using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.MasterData.Domain;
using MyERP.Modules.MasterData.Infrastructure;
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
using MyERP.Shared.Time;

namespace MyERP.IntegrationTests.Database;

public sealed class RfqDraftMutationTests(TestDatabase database)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateOnly Day = new(2028, 2, 29);

    [Fact]
    public async Task SendFreezesSupplierIdentityAndClosePreservesHistory()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var service = Service(db, companyId);
        var draft = await service.CreateAsync(Input(), Ct);
        var sent = await service.SendAsync(draft.Id, draft.Version, Ct);
        Assert.Equal(RfqStatus.Sent, sent.Status);
        Assert.NotNull(sent.SentAt);
        var invitation = Assert.Single(sent.Invitations);
        Assert.Equal("SUP21", invitation.SupplierCodeSnapshot);
        Assert.Equal("Supplier 21", invitation.SupplierNameSnapshot);
        Assert.Equal(sent.SentAt, invitation.SentAt);
        await Assert.ThrowsAsync<ConflictException>(() => service.SendAsync(draft.Id, draft.Version, Ct));
        var closed = await service.CloseAsync(sent.Id, sent.Version, Ct);
        Assert.Equal(RfqStatus.Closed, closed.Status);
        Assert.Equal(invitation.SupplierNameSnapshot, Assert.Single(closed.Invitations).SupplierNameSnapshot);
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.CloseAsync(closed.Id, closed.Version, Ct));
        await using var check = Context(companyId);
        Assert.Equal(RfqStatus.Closed, (await check.Set<Rfq>().SingleAsync(x => x.Id == draft.Id, Ct)).Status);
    }

    [Fact]
    public async Task SupplierLookupFailureRollsBackAllInvitationsAndRetryUsesSameVersion()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var draft = await Service(db, companyId).CreateAsync(Input() with { SupplierIds = [21, 22] }, Ct);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service(db, companyId, new FailingSuppliers()).SendAsync(draft.Id, draft.Version, Ct));
        await using (var check = Context(companyId))
        {
            var saved = await check.Set<Rfq>().Include(x => x.Invitations)
                .SingleAsync(x => x.Id == draft.Id, Ct);
            Assert.Equal(RfqStatus.Draft, saved.Status);
            Assert.Null(saved.SentAt);
            Assert.All(saved.Invitations, x =>
            {
                Assert.Null(x.SentAt);
                Assert.Null(x.SupplierCodeSnapshot);
                Assert.Null(x.SupplierNameSnapshot);
            });
        }
        var sent = await Service(db, companyId).SendAsync(draft.Id, draft.Version, Ct);
        Assert.Equal(2, sent.Invitations.Count);
    }

    [Fact]
    public async Task TwoConnectionsCannotSendSameDraftTwice()
    {
        var companyId = TestCompanies.Next();
        await using var setup = Context(companyId);
        var draft = await Service(setup, companyId).CreateAsync(Input(), Ct);
        await using var first = Context(companyId);
        await using var second = Context(companyId);
        static async Task<Exception?> Attempt(RfqDraftService service, long id, int version,
            CancellationToken token)
        {
            try { await service.SendAsync(id, version, token); return null; }
            catch (Exception error) { return error; }
        }
        var results = await Task.WhenAll(
            Attempt(Service(first, companyId), draft.Id, draft.Version, Ct),
            Attempt(Service(second, companyId), draft.Id, draft.Version, Ct));
        Assert.Single(results, x => x is null);
        Assert.Single(results, x => x is ConflictException);
        await using var check = Context(companyId);
        var saved = await check.Set<Rfq>().Include(x => x.Invitations).SingleAsync(x => x.Id == draft.Id, Ct);
        Assert.Equal(RfqStatus.Sent, saved.Status);
        Assert.NotNull(Assert.Single(saved.Invitations).SentAt);
    }

    [Fact]
    public async Task CreatesReplacesAndSoftDeletesWholeDraft()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var service = Service(db, companyId);
        var created = await service.CreateAsync(Input(), Ct);
        Assert.Equal("RFQ-001", created.DocumentNo);
        Assert.Equal(7, created.OwnerId);
        Assert.Equal(10, created.BranchId);
        Assert.Equal(1.123456m, Assert.Single(created.Lines).Quantity);
        Assert.Equal(21, Assert.Single(created.Invitations).SupplierId);
        var updated = await service.UpdateAsync(created.Id, created.Version,
            Input() with { Lines = [new(2, 12, null, Day.AddDays(3), 2m, null)],
                SupplierIds = [22] }, Ct);
        Assert.Equal(2, Assert.Single(updated.Lines).LineNo);
        Assert.Equal(22, Assert.Single(updated.Invitations).SupplierId);
        await Assert.ThrowsAsync<ConflictException>(() => service.UpdateAsync(created.Id, created.Version, Input(), Ct));
        await service.DeleteAsync(created.Id, updated.Version, Ct);
        await using var check = Context(companyId);
        var saved = await check.Set<Rfq>().IgnoreQueryFilters().Include(x => x.Lines)
            .Include(x => x.Invitations).SingleAsync(x => x.Id == created.Id, Ct);
        Assert.True(saved.IsDeleted);
        Assert.Single(saved.Lines);
        Assert.Single(saved.Invitations);
        Assert.Equal(0, await check.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM dbo.inv_stock_movement WHERE company_id = {0}",
            companyId).SingleAsync(Ct));
    }

    [Fact]
    public async Task InvalidLineSupplierAndSourceRollBackEntireDraft()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var service = Service(db, companyId);
        await Assert.ThrowsAsync<RequestValidationException>(() => service.CreateAsync(
            Input() with { SupplierIds = [21, 21] }, Ct));
        await Assert.ThrowsAsync<RequestValidationException>(() => service.CreateAsync(
            Input() with { Lines = [new(1, 11, null, Day, 1.0000001m, null)] }, Ct));
        await Assert.ThrowsAsync<NotFoundException>(() => service.CreateAsync(
            Input() with { Lines = [new(1, 11, long.MaxValue, Day, 1m, null)] }, Ct));
        Assert.Empty(await db.Set<Rfq>().ToListAsync(Ct));
    }

    [Fact]
    public async Task SourceMustBeApprovedSameItemAndWithinUnorderedQuantity()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var requisition = new Requisition { CompanyId = companyId, DocumentNo = "PUR-RFQ-SOURCE",
            BusinessDate = Day, RequesterId = 7, Status = RequisitionStatus.Approved };
        requisition.Lines.Add(new RequisitionLine { CompanyId = companyId, LineNo = 1,
            ItemId = 11, RequiredDate = Day.AddDays(1), Quantity = 5m, OrderedQuantity = 2m });
        db.Add(requisition);
        await db.SaveChangesAsync(Ct);
        var sourceId = Assert.Single(requisition.Lines).Id;
        var service = Service(db, companyId);
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreateAsync(Input() with
        { Lines = [new(1, 12, sourceId, Day.AddDays(1), 1m, null)] }, Ct));
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreateAsync(Input() with
        { Lines = [new(1, 11, sourceId, Day.AddDays(1), 4m, null)] }, Ct));
        db.Attach(requisition);
        requisition.Status = RequisitionStatus.Rejected;
        await db.SaveChangesAsync(Ct);
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreateAsync(Input() with
        { Lines = [new(1, 11, sourceId, Day.AddDays(1), 1m, null)] }, Ct));
        db.Attach(requisition);
        requisition.Status = RequisitionStatus.Approved;
        await db.SaveChangesAsync(Ct);
        var created = await service.CreateAsync(Input() with
        { Lines = [new(1, 11, sourceId, Day.AddDays(1), 3m, null)] }, Ct);
        Assert.Equal(sourceId, Assert.Single(created.Lines).RequisitionLineId);
        Assert.Equal(2m, Assert.Single(requisition.Lines).OrderedQuantity);
        Assert.Single(await db.Set<Rfq>().ToListAsync(Ct));
        requisition.Status = RequisitionStatus.Rejected;
        await db.SaveChangesAsync(Ct);
        var invalidAtSend = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.SendAsync(created.Id, created.Version, Ct));
        Assert.Equal("PUR.RFQ.INVALID_SOURCE", invalidAtSend.ErrorCode);
        db.Attach(requisition);
        requisition.Status = RequisitionStatus.Approved;
        requisition.Lines[0].OrderedQuantity = 3m;
        await db.SaveChangesAsync(Ct);
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.SendAsync(created.Id, created.Version, Ct));
        db.Attach(requisition);
        requisition.Lines[0].OrderedQuantity = 2m;
        await db.SaveChangesAsync(Ct);
        Assert.Equal(RfqStatus.Sent, (await service.SendAsync(created.Id, created.Version, Ct)).Status);
        await db.Entry(requisition.Lines[0]).ReloadAsync(Ct);
        Assert.Equal(2m, requisition.Lines[0].OrderedQuantity);
        Assert.Equal(0, await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM dbo.inv_stock_movement WHERE company_id = {0}",
            companyId).SingleAsync(Ct));
    }

    [Fact]
    public async Task RealMasterDataAndNumberingRejectInvalidReferencesAndRollBackIssuedNumber()
    {
        var companyId = await CreateCompanyAsync();
        await using var db = FullContext(companyId);
        var uom = new UnitOfMeasure { CompanyId = companyId, Code = "EA", Name = "Each", DecimalPlaces = 6 };
        db.Add(uom);
        await db.SaveChangesAsync(Ct);
        var item = new Item { CompanyId = companyId, Code = $"I{companyId}", Name = "Item", BaseUomId = uom.Id };
        var supplier = new Supplier { CompanyId = companyId, Code = $"S{companyId}", Name = "Supplier",
            ReviewStatus = PartnerReviewStatus.Approved };
        var rule = new NumberRule { CompanyId = companyId, DocumentType = RfqDraftService.DocumentType,
            Name = "RFQ", Prefix = "RFQ-", SequenceLength = 4,
            DateFormat = NumberDateFormat.None, ResetPeriod = NumberResetPeriod.Never };
        db.AddRange(item, supplier, rule);
        await db.SaveChangesAsync(Ct);
        var input = Input() with { Lines = [new(1, item.Id, null, Day, 1m, null)], SupplierIds = [supplier.Id] };
        var service = RealService(db, companyId);

        item.IsActive = false;
        await db.SaveChangesAsync(Ct);
        var inactiveItem = await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreateAsync(input, Ct));
        Assert.Equal("MD.ITEM.INACTIVE", inactiveItem.ErrorCode);
        await db.Set<Item>().Where(x => x.Id == item.Id)
            .ExecuteUpdateAsync(x => x.SetProperty(y => y.IsActive, true), Ct);
        await db.Set<Item>().Where(x => x.Id == item.Id)
            .ExecuteUpdateAsync(x => x.SetProperty(y => y.IsDeleted, true), Ct);
        await Assert.ThrowsAsync<NotFoundException>(() => service.CreateAsync(input, Ct));
        await db.Set<Item>().IgnoreQueryFilters().Where(x => x.Id == item.Id)
            .ExecuteUpdateAsync(x => x.SetProperty(y => y.IsDeleted, false), Ct);
        await db.Set<Supplier>().Where(x => x.Id == supplier.Id)
            .ExecuteUpdateAsync(x => x.SetProperty(y => y.IsActive, false), Ct);
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreateAsync(input, Ct));
        await db.Set<Supplier>().Where(x => x.Id == supplier.Id)
            .ExecuteUpdateAsync(x => x.SetProperty(y => y.IsActive, true)
                .SetProperty(y => y.ReviewStatus, PartnerReviewStatus.Pending), Ct);
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreateAsync(input, Ct));
        await db.Set<Supplier>().Where(x => x.Id == supplier.Id)
            .ExecuteUpdateAsync(x => x.SetProperty(y => y.ReviewStatus, PartnerReviewStatus.Approved)
                .SetProperty(y => y.IsDeleted, true), Ct);
        await Assert.ThrowsAsync<NotFoundException>(() => service.CreateAsync(input, Ct));
        await db.Set<Supplier>().IgnoreQueryFilters().Where(x => x.Id == supplier.Id)
            .ExecuteUpdateAsync(x => x.SetProperty(y => y.IsDeleted, false), Ct);

        var fault = new FailingNumbers(new NumberGenerator(db, new FixedClock(new DateTime(2026, 10, 21)), new FixedUser(7)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => RealService(db, companyId, fault).CreateAsync(input, Ct));
        Assert.Empty(await db.Set<Rfq>().ToListAsync(Ct));
        Assert.Empty(await db.Set<NumberSequence>().ToListAsync(Ct));
        var created = await service.CreateAsync(input, Ct);
        Assert.Equal("RFQ-0001", created.DocumentNo);
        Assert.Equal(item.Id, Assert.Single(created.Lines).ItemId);
        Assert.Equal(supplier.Id, Assert.Single(created.Invitations).SupplierId);
        Assert.Equal(1, (await db.Set<NumberSequence>().SingleAsync(Ct)).CurrentValue);

        await db.Set<Item>().Where(x => x.Id == item.Id)
            .ExecuteUpdateAsync(x => x.SetProperty(y => y.IsActive, false), Ct);
        var inactiveAtSend = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.SendAsync(created.Id, created.Version, Ct));
        Assert.Equal("MD.ITEM.INACTIVE", inactiveAtSend.ErrorCode);
        await db.Set<Item>().Where(x => x.Id == item.Id)
            .ExecuteUpdateAsync(x => x.SetProperty(y => y.IsActive, true), Ct);
        await db.Set<Item>().Where(x => x.Id == item.Id)
            .ExecuteUpdateAsync(x => x.SetProperty(y => y.IsDeleted, true), Ct);
        await Assert.ThrowsAsync<NotFoundException>(() => service.SendAsync(created.Id, created.Version, Ct));
        await db.Set<Item>().IgnoreQueryFilters().Where(x => x.Id == item.Id)
            .ExecuteUpdateAsync(x => x.SetProperty(y => y.IsDeleted, false), Ct);
        await db.Set<Supplier>().Where(x => x.Id == supplier.Id)
            .ExecuteUpdateAsync(x => x.SetProperty(y => y.IsActive, false), Ct);
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.SendAsync(created.Id, created.Version, Ct));
        await db.Set<Supplier>().Where(x => x.Id == supplier.Id)
            .ExecuteUpdateAsync(x => x.SetProperty(y => y.IsActive, true), Ct);
        await db.Set<Supplier>().Where(x => x.Id == supplier.Id)
            .ExecuteUpdateAsync(x => x.SetProperty(y => y.ReviewStatus, PartnerReviewStatus.Pending), Ct);
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.SendAsync(created.Id, created.Version, Ct));
        await db.Set<Supplier>().Where(x => x.Id == supplier.Id)
            .ExecuteUpdateAsync(x => x.SetProperty(y => y.ReviewStatus, PartnerReviewStatus.Approved)
                .SetProperty(y => y.IsDeleted, true), Ct);
        await Assert.ThrowsAsync<NotFoundException>(() => service.SendAsync(created.Id, created.Version, Ct));
        await db.Set<Supplier>().IgnoreQueryFilters().Where(x => x.Id == supplier.Id)
            .ExecuteUpdateAsync(x => x.SetProperty(y => y.IsDeleted, false), Ct);
        var sent = await service.SendAsync(created.Id, created.Version, Ct);
        Assert.Equal(supplier.Code, Assert.Single(sent.Invitations).SupplierCodeSnapshot);
        Assert.Equal(supplier.Name, Assert.Single(sent.Invitations).SupplierNameSnapshot);
        await db.Set<Supplier>().Where(x => x.Id == supplier.Id)
            .ExecuteUpdateAsync(x => x.SetProperty(y => y.Name, "Renamed"), Ct);
        var closed = await service.CloseAsync(sent.Id, sent.Version, Ct);
        Assert.Equal(supplier.Name, Assert.Single(closed.Invitations).SupplierNameSnapshot);
    }

    [Fact]
    public async Task SelfScopeHidesAnotherOwnerAndLockedStatesCannotChange()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var other = new Rfq { CompanyId = companyId, DocumentNo = $"RFQ-OTHER-{companyId}",
            BusinessDate = Day, ReplyDeadline = Day, OwnerId = 8 };
        var sent = new Rfq { CompanyId = companyId, DocumentNo = $"RFQ-SENT-{companyId}",
            BusinessDate = Day, ReplyDeadline = Day, OwnerId = 7, Status = RfqStatus.Sent,
            SentAt = new DateTime(2026, 10, 21) };
        db.AddRange(other, sent);
        await db.SaveChangesAsync(Ct);
        var service = Service(db, companyId);
        await Assert.ThrowsAsync<NotFoundException>(() => service.UpdateAsync(other.Id, other.Version, Input(), Ct));
        await Assert.ThrowsAsync<NotFoundException>(() => service.DeleteAsync(other.Id, other.Version, Ct));
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.UpdateAsync(sent.Id, sent.Version, Input(), Ct));
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.DeleteAsync(sent.Id, sent.Version, Ct));
    }

    private static RfqInput Input() => new(Day, Day.AddDays(2), " test ",
        [new(1, 11, null, Day.AddDays(1), 1.123456m, null)], [21]);

    private MyErpDbContext Context(long companyId)
    {
        var company = new FixedCompany(companyId);
        var stamper = new AuditStamper(new FixedClock(new DateTime(2026, 10, 21)), new FixedUser(7), company);
        return new MyErpDbContext(new DbContextOptionsBuilder<MyErpDbContext>()
            .UseMyErpSqlServer(database.ConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(stamper)).Options,
            [new RfqDbModel(), new RequisitionDbModel()], company);
    }

    private MyErpDbContext FullContext(long companyId)
    {
        var company = new FixedCompany(companyId);
        var stamper = new AuditStamper(new FixedClock(new DateTime(2026, 10, 21)), new FixedUser(7), company);
        return new MyErpDbContext(new DbContextOptionsBuilder<MyErpDbContext>()
            .UseMyErpSqlServer(database.ConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(stamper)).Options,
            [new RfqDbModel(), new RequisitionDbModel(), new UomDbModel(), new ItemDbModel(),
                new SupplierDbModel(), new PaymentSetupDbModel(), new SystemOrganizationDbModel()], company);
    }

    private async Task<long> CreateCompanyAsync()
    {
        var company = new FixedCompany(null);
        var stamp = new AuditStamper(new FixedClock(new DateTime(2026, 10, 21)), new FixedUser(7), company);
        await using var db = new MyErpDbContext(new DbContextOptionsBuilder<MyErpDbContext>()
            .UseMyErpSqlServer(database.ConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(stamp)).Options,
            [new SystemOrganizationDbModel()], company);
        var entity = new SystemCompany { Code = "R" + Guid.NewGuid().ToString("N")[..12], Name = "RFQ test" };
        db.Add(entity);
        await db.SaveChangesAsync(Ct);
        return entity.Id;
    }

    private static RfqDraftService RealService(MyErpDbContext db, long companyId, INumberGenerator? numbers = null) =>
        new(db, new RequisitionCompanyAccess(new FixedCompany(companyId)),
            new FixedAuthorization(new UserCompanyAuthorization(7, companyId, 10, 20, false,
                new Dictionary<string, DataScope> {
                    [PurchasingPermissions.RfqCreate] = DataScope.Self,
                    [PurchasingPermissions.RfqSend] = DataScope.Self,
                    [PurchasingPermissions.RfqClose] = DataScope.Self })),
            numbers ?? new NumberGenerator(db, new FixedClock(new DateTime(2026, 10, 21)), new FixedUser(7)),
            new RequisitionItemGuard(db), new SupplierService(db), new FixedClock(new DateTime(2026, 10, 21)));

    private sealed class FailingNumbers(INumberGenerator inner) : INumberGenerator
    {
        public async Task<string> NextAsync(string type, DateOnly date, CancellationToken token = default)
        {
            await inner.NextAsync(type, date, token);
            throw new InvalidOperationException("Injected failure after SYS sequence write.");
        }
    }

    private static RfqDraftService Service(MyErpDbContext db, long companyId,
        ISupplierLookup? supplierLookup = null) => new(db,
        new RequisitionCompanyAccess(new FixedCompany(companyId)),
        new FixedAuthorization(new UserCompanyAuthorization(7, companyId, 10, 20, false,
            new Dictionary<string, DataScope>
            {
                [PurchasingPermissions.RfqCreate] = DataScope.Self,
                [PurchasingPermissions.RfqEdit] = DataScope.Self,
                [PurchasingPermissions.RfqDelete] = DataScope.Self
                , [PurchasingPermissions.RfqSend] = DataScope.Self
                , [PurchasingPermissions.RfqClose] = DataScope.Self
        })), new FakeNumbers(), new FakeItems(), supplierLookup ?? new FakeSuppliers(),
        new FixedClock(new DateTime(2026, 10, 21)));

    private sealed class FailingSuppliers : ISupplierLookup
    {
        private int calls;
        public Task<SupplierIdentity> RequireApprovedAsync(long id, CancellationToken token = default)
        {
            if (++calls == 2) throw new InvalidOperationException("Injected supplier lookup failure.");
            return Task.FromResult(new SupplierIdentity(id, $"SUP{id}", $"Supplier {id}"));
        }
    }

    private sealed class FixedAuthorization(UserCompanyAuthorization value) : ICurrentAuthorization
    {
        public Task<UserCompanyAuthorization?> GetAsync(CancellationToken token) => Task.FromResult<UserCompanyAuthorization?>(value);
    }
    private sealed class FakeNumbers : INumberGenerator
    {
        public Task<string> NextAsync(string type, DateOnly date, CancellationToken token = default) => Task.FromResult("RFQ-001");
    }
    private sealed class FakeItems : IRequisitionItemGuard
    {
        public Task<T> RunAsync<T>(IReadOnlyCollection<long> ids, Func<Task<T>> action, CancellationToken token = default) => action();
    }
    private sealed class FakeSuppliers : ISupplierLookup
    {
        public Task<SupplierIdentity> RequireApprovedAsync(long id, CancellationToken token = default) =>
            Task.FromResult(new SupplierIdentity(id, $"SUP{id}", $"Supplier {id}"));
    }
}
