using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MyERP.Modules.Purchasing;
using MyERP.Modules.Purchasing.Application;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Modules.Purchasing.Infrastructure;
using MyERP.Modules.System.Application.Numbering;
using MyERP.Modules.System.Domain;
using MyERP.Modules.System.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Numbering;
using MyERP.Shared.Security;

namespace MyERP.IntegrationTests.Database;

/// <summary>WU-PUR-005e4：CONVERT 以真實 SYS 發號、真實 SQL 交易驗收（PUR-RFQ-060～066）。</summary>
public sealed class RfqConversionTransactionTests(TestDatabase database)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    // Leap day keeps the business date and the date-formatted number honest (PUR-RFQ-061).
    private static readonly DateTime Today = new(2028, 2, 29, 9, 30, 0);
    private const int MaxAwards = 1000;

    [Fact]
    public async Task TwoAwardsBecomeOneDraftWithRealSysNumberAndExactPrecision()
    {
        var companyId = await NewCompanyAsync();
        await using var db = NewDb(companyId);
        var seed = await SeedAsync(db, companyId,
            new AwardSpec(101, 30.12345678m, 1.234567m, 3.5m, 129.9991m, 6.4999m, 136.4990m),
            new AwardSpec(101, 30.12345678m, 0.000001m, 999999.999999m, 30.1235m, 1.5062m, 31.6297m));
        var before = await SnapshotAwardsAsync(db, seed.RfqId);

        var result = await Service(db, companyId).ConvertAsync(seed.RfqId,
            new RfqConversionInput(seed.Version, 101, [seed.AwardIds[1], seed.AwardIds[0]]), Ct);

        Assert.Equal("Draft", result.Status);
        Assert.Equal("PO20280229001", result.DocumentNo);
        Assert.Equal(2, result.LineCount);
        Assert.Equal(seed.AwardIds.Order(), result.AwardIds);
        Assert.True(result.RfqVersion > seed.Version);
        await using var verify = NewDb(companyId);
        var order = await verify.Set<PurchaseOrderSource>().AsNoTracking()
            .SingleAsync(x => x.RfqId == seed.RfqId, Ct);
        Assert.Equal(result.OrderId, order.Id);
        Assert.Equal(result.DocumentNo, order.DocumentNo);
        Assert.Equal(new DateOnly(2028, 2, 29), order.BusinessDate);
        Assert.Equal("USD", order.Currency);
        Assert.Equal("TWD", order.BaseCurrencyCode);
        Assert.Equal(30.12345678m, order.ExchangeRate);
        Assert.Equal(160.1226m, order.BaseNet);
        Assert.Equal(8.0061m, order.BaseTax);
        Assert.Equal(168.1287m, order.BaseGross);
        var lines = await verify.Set<PurchaseOrderSourceLine>().AsNoTracking()
            .Where(x => x.PurchaseOrderId == order.Id).OrderBy(x => x.LineNo).ToListAsync(Ct);
        // SQL decimal scale is preserved verbatim: no rounding of six-decimal prices or four-decimal amounts.
        Assert.Equal("1|1.234567|3.500000|136.4990;2|0.000001|999999.999999|31.6297",
            string.Join(';', lines.Select(x => string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"{x.LineNo}|{x.UnitPrice}|{x.Quantity}|{x.BaseGross}"))));
        var links = await verify.Set<PurchaseOrderAwardSource>().AsNoTracking()
            .Where(x => lines.Select(l => l.Id).Contains(x.PurchaseOrderLineId))
            .OrderBy(x => x.AwardId).Select(x => x.AwardId).ToListAsync(Ct);
        Assert.Equal(seed.AwardIds.Order(), links);
        Assert.Equal(1, (await verify.Set<NumberSequence>().AsNoTracking().SingleAsync(x => x.CompanyId == companyId, Ct)).CurrentValue);
        Assert.Equal(before, await SnapshotAwardsAsync(verify, seed.RfqId));
    }

    [Fact]
    public async Task MixedOrPartlyConvertedBatchesAreRejectedWholeWithoutConsumingNumbers()
    {
        var companyId = await NewCompanyAsync();
        await using var db = NewDb(companyId);
        var seed = await SeedAsync(db, companyId,
            new AwardSpec(101, 30m, 1m, 1m, 30m, 1.5m, 31.5m),
            new AwardSpec(101, 30m, 2m, 1m, 60m, 3m, 63m),
            new AwardSpec(102, 30m, 3m, 1m, 90m, 4.5m, 94.5m),
            new AwardSpec(101, 31m, 4m, 1m, 124m, 6.2m, 130.2m));
        var service = Service(db, companyId);
        var supplier = await Assert.ThrowsAsync<BusinessRuleException>(() => service.ConvertAsync(seed.RfqId,
            new RfqConversionInput(seed.Version, 101, [seed.AwardIds[0], seed.AwardIds[2]]), Ct));
        Assert.Equal("PUR.RFQ.SUPPLIER_MISMATCH", supplier.ErrorCode);
        var rate = await Assert.ThrowsAsync<BusinessRuleException>(() => service.ConvertAsync(seed.RfqId,
            new RfqConversionInput(seed.Version, 101, [seed.AwardIds[0], seed.AwardIds[3]]), Ct));
        Assert.Equal("PUR.RFQ.RATE_MISMATCH", rate.ErrorCode);
        await AssertNothingWrittenAsync(companyId, seed.RfqId, seed.Version, expectedSequence: null);

        var first = await service.ConvertAsync(seed.RfqId,
            new RfqConversionInput(seed.Version, 101, [seed.AwardIds[0]]), Ct);
        Assert.Equal("PO20280229001", first.DocumentNo);
        var partial = await Assert.ThrowsAsync<BusinessRuleException>(() => service.ConvertAsync(seed.RfqId,
            new RfqConversionInput(first.RfqVersion, 101, [seed.AwardIds[0], seed.AwardIds[1]]), Ct));
        Assert.Equal("PUR.RFQ.AWARD_ALREADY_CONVERTED", partial.ErrorCode);
        Assert.Contains(seed.AwardIds[0].ToString(System.Globalization.CultureInfo.InvariantCulture),
            partial.Message, StringComparison.Ordinal);
        await using (var verify = NewDb(companyId))
        {
            Assert.Equal(1, await verify.Set<PurchaseOrderSource>().CountAsync(x => x.RfqId == seed.RfqId, Ct));
            Assert.Equal(1, await verify.Set<PurchaseOrderAwardSource>().CountAsync(x => x.CompanyId == companyId, Ct));
            Assert.Equal(1, (await verify.Set<NumberSequence>().AsNoTracking().SingleAsync(x => x.CompanyId == companyId, Ct)).CurrentValue);
            Assert.Equal(first.RfqVersion, (await verify.Set<Rfq>().AsNoTracking()
                .SingleAsync(x => x.Id == seed.RfqId, Ct)).Version);
        }

        // Closed RFQs stay readable evidence but cannot spawn new orders (PUR-RFQ-065).
        var rfq = await db.Set<Rfq>().SingleAsync(x => x.Id == seed.RfqId, Ct);
        rfq.Status = RfqStatus.Closed;
        await db.SaveChangesAsync(Ct);
        var closed = await Assert.ThrowsAsync<BusinessRuleException>(() => service.ConvertAsync(seed.RfqId,
            new RfqConversionInput(rfq.Version, 101, [seed.AwardIds[1]]), Ct));
        Assert.Equal("PUR.RFQ.NOT_OPEN", closed.ErrorCode);
        await using var final = NewDb(companyId);
        Assert.Equal(1, (await final.Set<NumberSequence>().AsNoTracking().SingleAsync(x => x.CompanyId == companyId, Ct)).CurrentValue);
    }

    [Fact]
    public async Task FailureAfterSysNumberRollsBackOrderLinksVersionAndSequence()
    {
        var companyId = await NewCompanyAsync();
        Seeded seed;
        await using (var db = NewDb(companyId))
            seed = await SeedAsync(db, companyId,
                new AwardSpec(101, 30m, 1m, 2m, 60m, 3m, 63m),
                new AwardSpec(101, 30m, 2m, 2m, 120m, 6m, 126m));
        var input = new RfqConversionInput(seed.Version, 101, seed.AwardIds);

        foreach (var table in new[] { "pur_purchase_order_line", "pur_order_source" })
        {
            var fault = new FailInsert(table);
            await using (var failing = NewDb(companyId, fault))
            {
                var error = await Assert.ThrowsAsync<DbUpdateException>(() =>
                    Service(failing, companyId).ConvertAsync(seed.RfqId, input, Ct));
                Assert.IsType<InvalidOperationException>(error.InnerException);
            }
            Assert.True(fault.Fired);
            await AssertNothingWrittenAsync(companyId, seed.RfqId, seed.Version, expectedSequence: null);
        }

        await using var retry = NewDb(companyId);
        var result = await Service(retry, companyId).ConvertAsync(seed.RfqId, input, Ct);
        Assert.Equal("PO20280229001", result.DocumentNo);
        Assert.Equal(2, result.LineCount);
    }

    [Fact]
    public async Task SimultaneousConversionsOfTheSameVersionCreateOneOrderAndOneNumber()
    {
        var companyId = await NewCompanyAsync();
        Seeded seed;
        await using (var db = NewDb(companyId))
            seed = await SeedAsync(db, companyId, new AwardSpec(101, 30m, 1m, 2m, 60m, 3m, 63m));
        var input = new RfqConversionInput(seed.Version, 101, seed.AwardIds);
        await using var first = NewDb(companyId);
        await using var second = NewDb(companyId);
        var outcomes = await Task.WhenAll(
            Capture(() => Service(first, companyId).ConvertAsync(seed.RfqId, input, Ct)),
            Capture(() => Service(second, companyId).ConvertAsync(seed.RfqId, input, Ct)));
        Assert.Single(outcomes, x => x is null);
        Assert.Single(outcomes.OfType<ConflictException>());
        await using var verify = NewDb(companyId);
        Assert.Equal(1, await verify.Set<PurchaseOrderSource>().CountAsync(x => x.RfqId == seed.RfqId, Ct));
        Assert.Equal(1, await verify.Set<PurchaseOrderAwardSource>().CountAsync(x => x.CompanyId == companyId, Ct));
        Assert.Equal(1, (await verify.Set<NumberSequence>().AsNoTracking().SingleAsync(x => x.CompanyId == companyId, Ct)).CurrentValue);
    }

    [Fact]
    public async Task OtherCompaniesAndOutOfScopeUsersCannotSeeTheRfq()
    {
        var companyId = await NewCompanyAsync();
        Seeded seed;
        await using (var db = NewDb(companyId))
            seed = await SeedAsync(db, companyId, new AwardSpec(101, 30m, 1m, 2m, 60m, 3m, 63m),
                branchId: 501, departmentId: 601);
        var input = new RfqConversionInput(seed.Version, 101, seed.AwardIds);
        var otherCompanyId = await NewCompanyAsync();
        await using (var other = NewDb(otherCompanyId))
        {
            await Assert.ThrowsAsync<NotFoundException>(() => Service(other, otherCompanyId,
                DataScope.Company).ConvertAsync(seed.RfqId, input, Ct));
        }
        await using var same = NewDb(companyId);
        await Assert.ThrowsAsync<NotFoundException>(() => Service(same, companyId, DataScope.Branch,
            userId: 8, branchId: 502, departmentId: 601).ConvertAsync(seed.RfqId, input, Ct));
        await Assert.ThrowsAsync<NotFoundException>(() => Service(same, companyId, DataScope.Department,
            userId: 8, branchId: 501, departmentId: 602).ConvertAsync(seed.RfqId, input, Ct));
        await Assert.ThrowsAsync<NotFoundException>(() => Service(same, companyId, DataScope.Self,
            userId: 8, branchId: 501, departmentId: 601).ConvertAsync(seed.RfqId, input, Ct));
        await AssertNothingWrittenAsync(companyId, seed.RfqId, seed.Version, expectedSequence: null);
        await using (var verifyOther = NewDb(otherCompanyId))
            Assert.Empty(await verifyOther.Set<NumberSequence>().Where(x => x.CompanyId == otherCompanyId).ToListAsync(Ct));

        var result = await Service(same, companyId, DataScope.Department, userId: 8,
            branchId: 502, departmentId: 601).ConvertAsync(seed.RfqId, input, Ct);
        Assert.Equal("PO20280229001", result.DocumentNo);
    }

    [Fact]
    public async Task ThousandLineUpperBoundConvertsAndDraftOrDeletedRfqsDoNot()
    {
        var companyId = await NewCompanyAsync();
        await using var db = NewDb(companyId);
        var specs = Enumerable.Range(1, MaxAwards)
            .Select(i => new AwardSpec(101, 30m, i / 1000000m, 1m, 0.0001m, 0m, 0.0001m)).ToArray();
        var big = await SeedAsync(db, companyId, specs);
        var result = await Service(db, companyId).ConvertAsync(big.RfqId,
            new RfqConversionInput(big.Version, 101, big.AwardIds), Ct);
        Assert.Equal(MaxAwards, result.LineCount);
        await using (var verify = NewDb(companyId))
        {
            Assert.Equal(MaxAwards, await verify.Set<PurchaseOrderSourceLine>()
                .CountAsync(x => x.PurchaseOrderId == result.OrderId, Ct));
            Assert.Equal(0.1m, await verify.Set<PurchaseOrderSource>().Where(x => x.Id == result.OrderId)
                .Select(x => x.BaseGross).SingleAsync(Ct));
            Assert.Equal(1, (await verify.Set<NumberSequence>().AsNoTracking()
                .SingleAsync(x => x.CompanyId == companyId, Ct)).CurrentValue);
        }

        var draft = await SeedAsync(db, companyId, new AwardSpec(101, 30m, 1m, 1m, 30m, 1.5m, 31.5m));
        var draftRfq = await db.Set<Rfq>().SingleAsync(x => x.Id == draft.RfqId, Ct);
        draftRfq.Status = RfqStatus.Draft;
        draftRfq.SentAt = null;
        await db.SaveChangesAsync(Ct);
        var notOpen = await Assert.ThrowsAsync<BusinessRuleException>(() => Service(db, companyId)
            .ConvertAsync(draft.RfqId, new RfqConversionInput(draftRfq.Version, 101, draft.AwardIds), Ct));
        Assert.Equal("PUR.RFQ.NOT_OPEN", notOpen.ErrorCode);
        // A failed conversion clears the shared change tracker, so reload before the next edit.
        draftRfq = await db.Set<Rfq>().SingleAsync(x => x.Id == draft.RfqId, Ct);
        // Only Draft RFQs may be soft-deleted (ck_pur_rfq_deleted); the deleted row is invisible, not merely closed.
        draftRfq.IsDeleted = true;
        draftRfq.DeletedAt = Today;
        await db.SaveChangesAsync(Ct);
        await Assert.ThrowsAsync<NotFoundException>(() => Service(db, companyId)
            .ConvertAsync(draft.RfqId, new RfqConversionInput(draftRfq.Version, 101, draft.AwardIds), Ct));
        await using var final = NewDb(companyId);
        Assert.Equal(1, (await final.Set<NumberSequence>().AsNoTracking()
            .SingleAsync(x => x.CompanyId == companyId, Ct)).CurrentValue);
        Assert.Equal(0, await final.Set<PurchaseOrderSource>().IgnoreQueryFilters()
            .CountAsync(x => x.RfqId == draft.RfqId, Ct));
    }

    [Fact]
    public async Task AwardContextShowsTheOrderEachConvertedAwardBelongsTo()
    {
        // WU-PUR-005f3d: the 4.15 screen needs the saved order per award to explain "already converted".
        var companyId = await NewCompanyAsync();
        await using var db = NewDb(companyId);
        var seed = await SeedAsync(db, companyId,
            new AwardSpec(101, 30m, 1m, 1m, 30m, 1.5m, 31.5m),
            new AwardSpec(101, 30m, 2m, 1m, 60m, 3m, 63m));
        var result = await Service(db, companyId).ConvertAsync(seed.RfqId,
            new RfqConversionInput(seed.Version, 101, [seed.AwardIds[0]]), Ct);

        await using var read = NewDb(companyId);
        var auth = new FixedAuthorization(new UserCompanyAuthorization(7, companyId, null, null, false,
            new Dictionary<string, DataScope> { [PurchasingPermissions.RfqCompare] = DataScope.Self }));
        var context = await new RfqQueryService(read, new RequisitionCompanyAccess(new FixedCompany(companyId)), auth)
            .GetAwardContextAsync(seed.RfqId, Ct);

        Assert.Equal(result.RfqVersion, context.Version);
        var converted = context.Awards.Single(x => x.Id == seed.AwardIds[0]);
        Assert.Equal(result.OrderId, converted.PurchaseOrderId);
        Assert.Equal("PO20280229001", converted.PurchaseOrderNo);
        var open = context.Awards.Single(x => x.Id == seed.AwardIds[1]);
        Assert.Null(open.PurchaseOrderId);
        Assert.Null(open.PurchaseOrderNo);

        // Permanent conversion evidence must remain visible after the order is soft-deleted.
        await using var change = NewDb(companyId);
        var order = await change.Set<PurchaseOrderSource>().SingleAsync(x => x.Id == result.OrderId, Ct);
        order.IsDeleted = true;
        order.DeletedAt = Today;
        await change.SaveChangesAsync(Ct);
        var afterDelete = await new RfqQueryService(read,
            new RequisitionCompanyAccess(new FixedCompany(companyId)), auth).GetAwardContextAsync(seed.RfqId, Ct);
        var retained = afterDelete.Awards.Single(x => x.Id == seed.AwardIds[0]);
        Assert.Equal(result.OrderId, retained.PurchaseOrderId);
        Assert.Equal(result.DocumentNo, retained.PurchaseOrderNo);
    }

    private async Task AssertNothingWrittenAsync(long companyId, long rfqId, int version, long? expectedSequence)
    {
        await using var verify = NewDb(companyId);
        Assert.Equal(0, await verify.Set<PurchaseOrderSource>().CountAsync(x => x.RfqId == rfqId, Ct));
        Assert.Equal(0, await verify.Set<PurchaseOrderSourceLine>().CountAsync(x => x.CompanyId == companyId, Ct));
        Assert.Equal(0, await verify.Set<PurchaseOrderAwardSource>().CountAsync(x => x.CompanyId == companyId, Ct));
        Assert.Equal(expectedSequence, (await verify.Set<NumberSequence>().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId, Ct))?.CurrentValue);
        Assert.Equal(version, (await verify.Set<Rfq>().AsNoTracking()
            .SingleAsync(x => x.Id == rfqId, Ct)).Version);
    }

    private static async Task<List<string>> SnapshotAwardsAsync(MyErpDbContext db, long rfqId) =>
        (await db.Set<RfqAward>().AsNoTracking().Where(x => x.RfqId == rfqId).OrderBy(x => x.Id)
            .ToListAsync(Ct))
        .Select(x => $"{x.Id}|{x.SupplierId}|{x.Quantity}|{x.UnitPrice}|{x.ExchangeRate}|{x.BaseGross}|{x.Reason}")
        .ToList();

    private async Task<long> NewCompanyAsync()
    {
        await using var setup = NewDb(null);
        var company = new SystemCompany
        {
            Code = "PC" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
            Name = "RFQ conversion test"
        };
        setup.Add(company);
        await setup.SaveChangesAsync(Ct);
        await using var db = NewDb(company.Id);
        db.Add(PurchaseOrderRule(company.Id));
        await db.SaveChangesAsync(Ct);
        return company.Id;
    }

    private static NumberRule PurchaseOrderRule(long companyId) => new()
    {
        CompanyId = companyId, DocumentType = RfqConversionService.DocumentType, Name = "Purchase order",
        Prefix = "PO", DateFormat = NumberDateFormat.YearMonthDay, ResetPeriod = NumberResetPeriod.Never,
        SequenceLength = 3
    };

    private sealed record AwardSpec(long SupplierId, decimal Rate, decimal UnitPrice, decimal Quantity,
        decimal BaseNet, decimal BaseTax, decimal BaseGross);

    private sealed record Seeded(long RfqId, int Version, long[] AwardIds);

    private static async Task<Seeded> SeedAsync(MyErpDbContext db, long companyId, params AwardSpec[] specs) =>
        await SeedAsync(db, companyId, specs, null, null);

    private static Task<Seeded> SeedAsync(MyErpDbContext db, long companyId, AwardSpec spec,
        long? branchId, long? departmentId) => SeedAsync(db, companyId, [spec], branchId, departmentId);

    private static async Task<Seeded> SeedAsync(MyErpDbContext db, long companyId, AwardSpec[] specs,
        long? branchId, long? departmentId)
    {
        var rfq = new Rfq
        {
            CompanyId = companyId, DocumentNo = $"Q-CONV-{Guid.NewGuid():N}"[..20],
            BusinessDate = new DateOnly(2028, 2, 20), ReplyDeadline = new DateOnly(2028, 2, 28),
            OwnerId = 7, BranchId = branchId, DepartmentId = departmentId,
            Status = RfqStatus.Sent, SentAt = new DateTime(2028, 2, 20)
        };
        for (var i = 0; i < specs.Length; i++)
            rfq.Lines.Add(new RfqLine { CompanyId = companyId, LineNo = i + 1, ItemId = 11 + i,
                RequiredDate = new DateOnly(2028, 3, 31), Quantity = specs[i].Quantity });
        foreach (var supplierId in specs.Select(x => x.SupplierId).Distinct())
            rfq.Invitations.Add(new RfqInvitation { CompanyId = companyId, SupplierId = supplierId });
        db.Add(rfq);
        await db.SaveChangesAsync(Ct);
        var lines = rfq.Lines.OrderBy(x => x.LineNo).ToArray();
        var quotes = new Dictionary<long, SupplierQuotation>();
        foreach (var supplierId in specs.Select(x => x.SupplierId).Distinct())
        {
            var revision = new QuotationRevision { CompanyId = companyId, RevisionNo = 1,
                QuoteDate = new DateOnly(2028, 2, 21), ValidUntil = new DateOnly(2028, 3, 31) };
            for (var i = 0; i < specs.Length; i++)
                if (specs[i].SupplierId == supplierId)
                    revision.Lines.Add(new QuotationLine { CompanyId = companyId, RfqLineId = lines[i].Id,
                        UnitPrice = specs[i].UnitPrice, CurrencyCode = "USD", TaxRateId = 31 });
            var quote = new SupplierQuotation { CompanyId = companyId, RfqId = rfq.Id,
                SupplierId = supplierId, CurrentRevisionNo = 1, Status = SupplierQuotationStatus.Awarded };
            quote.Revisions.Add(revision);
            db.Add(quote);
            quotes[supplierId] = quote;
        }
        await db.SaveChangesAsync(Ct);
        var awards = specs.Select((spec, i) =>
        {
            var quote = quotes[spec.SupplierId];
            return new RfqAward
            {
                CompanyId = companyId, RfqId = rfq.Id, RfqLineId = lines[i].Id, QuotationId = quote.Id,
                QuotationLineId = quote.Revisions.Single().Lines.Single(x => x.RfqLineId == lines[i].Id).Id,
                SupplierId = spec.SupplierId, Reason = $"Award {i + 1}", Quantity = spec.Quantity,
                UnitPrice = spec.UnitPrice, CurrencyCode = "USD", BaseCurrencyCode = "TWD", TaxRateId = 31,
                TaxRatePercent = 5m, TaxRateFrom = new DateOnly(2028, 1, 1), ExchangeRate = spec.Rate,
                ExchangeRateId = 9, ExchangeRateFrom = new DateOnly(2028, 2, 1),
                ComparisonDate = new DateOnly(2028, 2, 22), BaseNet = spec.BaseNet,
                BaseTax = spec.BaseTax, BaseGross = spec.BaseGross
            };
        }).ToArray();
        db.AddRange(awards);
        await db.SaveChangesAsync(Ct);
        // Awarding advances the RFQ version in production; mirror that so the fixture is realistic.
        db.Entry(rfq).Property(x => x.Status).IsModified = true;
        await db.SaveChangesAsync(Ct);
        db.ChangeTracker.Clear();
        return new Seeded(rfq.Id, rfq.Version, awards.Select(x => x.Id).ToArray());
    }

    private static RfqConversionService Service(MyErpDbContext db, long companyId,
        DataScope scope = DataScope.Self, long userId = 7, long? branchId = null, long? departmentId = null)
    {
        var company = new FixedCompany(companyId);
        var authorization = new FixedAuthorization(new UserCompanyAuthorization(userId, companyId, branchId,
            departmentId, false, new Dictionary<string, DataScope> { [PurchasingPermissions.RfqConvert] = scope }));
        var clock = new FixedClock(Today);
        return new RfqConversionService(db, new RequisitionCompanyAccess(company), authorization,
            new NumberGenerator(db, clock, new FixedUser(userId)), clock);
    }

    private static async Task<Exception?> Capture(Func<Task> action)
    {
        try { await action(); return null; }
        catch (Exception ex) { return ex; }
    }

    private MyErpDbContext NewDb(long? companyId, DbCommandInterceptor? fault = null)
    {
        var company = new FixedCompany(companyId);
        var audit = new AuditSaveChangesInterceptor(new AuditStamper(new FixedClock(Today), new FixedUser(7), company));
        return new(new DbContextOptionsBuilder<MyErpDbContext>()
                .UseMyErpSqlServer(database.ConnectionString)
                .AddInterceptors(fault is null ? [audit] : [audit, fault]).Options,
            [new RfqDbModel(), new QuotationDbModel(), new RfqAwardDbModel(),
                new PurchaseInvoiceSourceDbModel(), new SystemOrganizationDbModel()], company);
    }

    private sealed class FailInsert(string table) : DbCommandInterceptor
    {
        public bool Fired { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            // EF emits INSERT ... OUTPUT or MERGE ... INSERT for batches; both name the table in brackets.
            if (command.CommandText.Contains("INSERT", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains($"[{table}]", StringComparison.OrdinalIgnoreCase))
            {
                Fired = true;
                throw new InvalidOperationException($"Injected {table} persistence failure.");
            }
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class FixedAuthorization(UserCompanyAuthorization value) : ICurrentAuthorization
    {
        public Task<UserCompanyAuthorization?> GetAsync(CancellationToken cancellationToken) =>
            Task.FromResult<UserCompanyAuthorization?>(value);
    }
}
