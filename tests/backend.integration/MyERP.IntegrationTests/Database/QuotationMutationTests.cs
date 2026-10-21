using Microsoft.EntityFrameworkCore;
using Moq;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.Purchasing;
using MyERP.Modules.Purchasing.Application;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Modules.Purchasing.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;

namespace MyERP.IntegrationTests.Database;

public sealed class QuotationMutationTests(TestDatabase database)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateOnly Day = new(2028, 2, 29);

    [Fact]
    public async Task FirstQuoteAndRevisionPreserveHistoryAndRejectInvalidWholeDocument()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var rfq = await SeedAsync(db, companyId);
        var service = Service(db, companyId);
        var lineId = rfq.Lines.Single().Id;
        var input = Input(lineId);
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreateAsync(rfq.Id,
            input with { SupplierId = 22 }, Ct));
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreateAsync(rfq.Id,
            input with { Lines = [new QuotationLineInput(lineId + 999, 1m, "TWD", 31)] }, Ct));
        Assert.Empty(await db.Set<SupplierQuotation>().Where(x => x.RfqId == rfq.Id).ToListAsync(Ct));
        var first = await service.CreateAsync(rfq.Id, input, Ct);
        Assert.Equal((1, 1.123456m), (first.CurrentRevisionNo, first.Revisions.Single().Lines.Single().UnitPrice));
        await Assert.ThrowsAsync<ConflictException>(() => service.CreateAsync(rfq.Id, input, Ct));
        var revised = await service.ReviseAsync(rfq.Id, first.Id, first.Version,
            input with { Lines = [new QuotationLineInput(lineId, 0m, "TWD", 31)] }, Ct);
        Assert.Equal(2, revised.CurrentRevisionNo);
        Assert.Equal([1.123456m, 0m], revised.Revisions.Select(x => x.Lines.Single().UnitPrice));
        await Assert.ThrowsAsync<ConflictException>(() => service.ReviseAsync(rfq.Id, first.Id,
            first.Version, input, Ct));
        await using var check = Context(companyId);
        var saved = await check.Set<SupplierQuotation>().Include(x => x.Revisions).ThenInclude(x => x.Lines)
            .SingleAsync(x => x.Id == first.Id, Ct);
        Assert.Equal(2, saved.Revisions.Count);
        Assert.Equal(1.123456m, saved.Revisions.Single(x => x.RevisionNo == 1).Lines.Single().UnitPrice);
    }

    [Fact]
    public async Task InputValidationAndClosedRfqLeaveNoRows()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var rfq = await SeedAsync(db, companyId);
        var service = Service(db, companyId);
        var input = Input(rfq.Lines.Single().Id);
        foreach (var invalid in new[] { input with { Lines = [] }, input with { ValidUntil = Day.AddDays(-1) },
            input with { Lines = [new QuotationLineInput(input.Lines![0].RfqLineId, -1m, "TWD", 31)] },
            input with { Lines = [new QuotationLineInput(input.Lines![0].RfqLineId, 1.1234567m, "TWD", 31)] } })
            await Assert.ThrowsAsync<RequestValidationException>(() => service.CreateAsync(rfq.Id, invalid, Ct));
        rfq.Status = RfqStatus.Closed;
        await db.SaveChangesAsync(Ct);
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreateAsync(rfq.Id, input, Ct));
        Assert.Empty(await db.Set<SupplierQuotation>().Where(x => x.RfqId == rfq.Id).ToListAsync(Ct));
    }

    [Fact]
    public async Task TwoConnectionsCannotReviseSameVersionTwice()
    {
        var companyId = TestCompanies.Next();
        await using var setup = Context(companyId);
        var rfq = await SeedAsync(setup, companyId);
        var input = Input(rfq.Lines.Single().Id);
        var first = await Service(setup, companyId).CreateAsync(rfq.Id, input, Ct);
        await using var left = Context(companyId);
        await using var right = Context(companyId);
        static async Task<Exception?> TryRevise(QuotationMutationService service, long rfqId,
            long quotationId, int version, QuotationInput input, CancellationToken token)
        {
            try { await service.ReviseAsync(rfqId, quotationId, version, input, token); return null; }
            catch (Exception error) { return error; }
        }
        var results = await Task.WhenAll(
            TryRevise(Service(left, companyId), rfq.Id, first.Id, first.Version, input, Ct),
            TryRevise(Service(right, companyId), rfq.Id, first.Id, first.Version, input, Ct));
        Assert.Single(results, x => x is null);
        Assert.Single(results, x => x is ConflictException);
        await using var check = Context(companyId);
        var saved = await check.Set<SupplierQuotation>().Include(x => x.Revisions)
            .SingleAsync(x => x.Id == first.Id, Ct);
        Assert.Equal(2, saved.CurrentRevisionNo);
        Assert.Equal(2, saved.Revisions.Count);
    }

    [Fact]
    public async Task InvalidMasterDataRollsBackAndSameRequestCanRetry()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var rfq = await SeedAsync(db, companyId);
        var input = Input(rfq.Lines.Single().Id);
        var inactive = new Mock<ICurrencyLookup>();
        inactive.Setup(x => x.FindCurrencyAsync("TWD", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CurrencyInfo("TWD", "Taiwan dollar", null, 0, true, false));
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Service(db, companyId, inactive.Object).CreateAsync(rfq.Id, input, Ct));
        Assert.Empty(await db.Set<SupplierQuotation>().Where(x => x.RfqId == rfq.Id).ToListAsync(Ct));
        Assert.Equal(1, (await Service(db, companyId).CreateAsync(rfq.Id, input, Ct)).CurrentRevisionNo);
    }

    private MyErpDbContext Context(long companyId)
    {
        var company = new FixedCompany(companyId);
        var stamp = new AuditStamper(new FixedClock(new DateTime(2026, 10, 21)), new FixedUser(7), company);
        return new MyErpDbContext(new DbContextOptionsBuilder<MyErpDbContext>()
            .UseMyErpSqlServer(database.ConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(stamp)).Options,
            [new RfqDbModel(), new QuotationDbModel()], company);
    }

    private static async Task<Rfq> SeedAsync(MyErpDbContext db, long companyId)
    {
        var rfq = new Rfq { CompanyId = companyId, DocumentNo = $"Q-MUT-{companyId}",
            BusinessDate = Day, ReplyDeadline = Day.AddDays(1), OwnerId = 7,
            Status = RfqStatus.Sent, SentAt = new DateTime(2026, 10, 21) };
        rfq.Lines.Add(new RfqLine { CompanyId = companyId, LineNo = 1, ItemId = 11,
            RequiredDate = Day.AddDays(2), Quantity = 1m });
        rfq.Invitations.Add(new RfqInvitation { CompanyId = companyId, SupplierId = 21,
            SupplierCodeSnapshot = "SUP21", SupplierNameSnapshot = "Supplier 21",
            SentAt = new DateTime(2026, 10, 21) });
        db.Add(rfq);
        await db.SaveChangesAsync(Ct);
        return rfq;
    }

    private static QuotationInput Input(long lineId) => new(21, Day, Day.AddDays(10), null,
        [new QuotationLineInput(lineId, 1.123456m, "TWD", 31)]);

    private static QuotationMutationService Service(MyErpDbContext db, long companyId,
        ICurrencyLookup? currencyLookup = null)
    {
        var currencies = new Mock<ICurrencyLookup>();
        currencies.Setup(x => x.FindCurrencyAsync("TWD", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CurrencyInfo("TWD", "Taiwan dollar", null, 0, true, true));
        var taxes = new Mock<ITaxRateLookup>();
        taxes.Setup(x => x.RequireRateAsync(31, TaxDirection.Input, It.IsAny<DateOnly>(),
            It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return new QuotationMutationService(db, new RequisitionCompanyAccess(new FixedCompany(companyId)),
            new FixedAuthorization(new UserCompanyAuthorization(7, companyId, 10, 20, false,
                new Dictionary<string, DataScope> { [PurchasingPermissions.QuotationQuote] = DataScope.Self })),
            currencyLookup ?? currencies.Object, taxes.Object, new FixedClock(new DateTime(2026, 10, 21)));
    }

    private sealed class FixedAuthorization(UserCompanyAuthorization value) : ICurrentAuthorization
    {
        public Task<UserCompanyAuthorization?> GetAsync(CancellationToken token) =>
            Task.FromResult<UserCompanyAuthorization?>(value);
    }
}
