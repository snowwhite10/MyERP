using Microsoft.EntityFrameworkCore;
using Moq;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.Purchasing;
using MyERP.Modules.Purchasing.Application;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Modules.Purchasing.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Monetary;
using MyERP.Shared.Security;

namespace MyERP.IntegrationTests.Database;

public sealed class QuotationComparisonTests(TestDatabase database)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ComparesSavedLinesAndPreservesUnavailableReasons()
    {
        var companyId = TestCompanies.Next();
        var company = new FixedCompany(companyId);
        var stamp = new AuditStamper(new FixedClock(new DateTime(2026, 10, 21)), new FixedUser(7), company);
        await using var db = new MyErpDbContext(new DbContextOptionsBuilder<MyErpDbContext>()
            .UseMyErpSqlServer(database.ConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(stamp)).Options,
            [new RfqDbModel(), new QuotationDbModel()], company);
        var rfq = new Rfq { CompanyId = companyId, DocumentNo = $"Q-CMP-{companyId}",
            BusinessDate = new DateOnly(2026, 10, 20), ReplyDeadline = new DateOnly(2026, 10, 22),
            OwnerId = 7, Status = RfqStatus.Sent, SentAt = new DateTime(2026, 10, 21) };
        rfq.Lines.Add(new RfqLine { CompanyId = companyId, LineNo = 1, ItemId = 11,
            RequiredDate = new DateOnly(2026, 11, 1), Quantity = 2m });
        foreach (var supplierId in new[] { 101L, 102L, 103L, 104L, 105L, 106L, 107L, 108L })
            rfq.Invitations.Add(new RfqInvitation { CompanyId = companyId, SupplierId = supplierId });
        db.Add(rfq);
        await db.SaveChangesAsync(Ct);
        foreach (var (supplierId, currency, status, quoteDate, validUntil, taxRateId) in new[] {
            (101L, "USD", SupplierQuotationStatus.Active, new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 22), 31L),
            (102L, "TWD", SupplierQuotationStatus.Active, new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 22), 31L),
            (103L, "EUR", SupplierQuotationStatus.Active, new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 22), 31L),
            (104L, "USD", SupplierQuotationStatus.Withdrawn, new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 22), 31L),
            (105L, "USD", SupplierQuotationStatus.Active, new DateOnly(2026, 10, 19), new DateOnly(2026, 10, 20), 31L),
            (106L, "USD", SupplierQuotationStatus.Active, new DateOnly(2026, 10, 22), new DateOnly(2026, 10, 23), 31L),
            (107L, "USD", SupplierQuotationStatus.Active, new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 22), 32L),
            (108L, "TWD", SupplierQuotationStatus.Awarded, new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 22), 31L) })
        {
            var quote = new SupplierQuotation { CompanyId = companyId, RfqId = rfq.Id,
                SupplierId = supplierId, CurrentRevisionNo = 1, Status = status };
            quote.Revisions.Add(new QuotationRevision { CompanyId = companyId, RevisionNo = 1,
                QuoteDate = quoteDate, ValidUntil = validUntil,
                Lines = { new QuotationLine { CompanyId = companyId, RfqLineId = rfq.Lines.Single().Id,
                    UnitPrice = 1.235m, CurrencyCode = currency, TaxRateId = taxRateId } } });
            db.Add(quote);
        }
        await db.SaveChangesAsync(Ct);
        var currencyLookup = new Mock<ICurrencyLookup>();
        currencyLookup.Setup(x => x.GetBaseCurrencyAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CurrencyInfo("TWD", "Taiwan Dollar", null, 0, true, true));
        currencyLookup.Setup(x => x.GetPrecisionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CurrencyPrecision.Default);
        currencyLookup.Setup(x => x.GetRateSnapshotAsync("USD", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExchangeRateSnapshot(30m, new DateOnly(2026, 10, 1), null, 9));
        currencyLookup.Setup(x => x.GetRateSnapshotAsync("TWD", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExchangeRateSnapshot(1m, new DateOnly(2026, 10, 21), null, null));
        currencyLookup.Setup(x => x.GetRateSnapshotAsync("EUR", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BusinessRuleException("MD.EXCHANGE_RATE.NOT_FOUND", "Missing rate."));
        var taxLookup = new Mock<ITaxRateLookup>();
        taxLookup.Setup(x => x.GetRateSnapshotAsync(31, TaxDirection.Input,
            It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TaxRateSnapshot(31, 5m, new DateOnly(2026, 10, 1), null));
        taxLookup.Setup(x => x.GetRateSnapshotAsync(32, TaxDirection.Input,
            It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BusinessRuleException("MD.TAX_RATE.NOT_FOUND", "Missing tax rate."));
        var service = new QuotationComparisonService(db, new RequisitionCompanyAccess(company),
            new FixedAuthorization(new UserCompanyAuthorization(7, companyId, null, null, false,
                new Dictionary<string, DataScope> { [PurchasingPermissions.RfqCompare] = DataScope.Self })),
            new FixedClock(new DateTime(2026, 10, 21)), currencyLookup.Object, taxLookup.Object);
        var result = await service.GetAsync(rfq.Id, Ct);
        var offers = Assert.Single(result.Lines).Offers;
        Assert.Equal([102L, 108L, 101L, 103L, 104L, 105L, 106L, 107L], offers.Select(x => x.SupplierId));
        Assert.Equal(2m, offers[0].BaseGross);
        Assert.Equal(2m, offers[1].BaseGross);
        Assert.Equal(SupplierQuotationStatus.Awarded, offers[1].Status);
        Assert.Equal(78m, offers[2].BaseGross);
        Assert.Equal(9L, offers[2].ExchangeRateId);
        Assert.Equal("MD.EXCHANGE_RATE.NOT_FOUND", offers[3].UnavailableReason);
        Assert.Equal("PUR.QUOTATION.WITHDRAWN", offers[4].UnavailableReason);
        Assert.Equal("PUR.QUOTATION.EXPIRED", offers[5].UnavailableReason);
        Assert.Equal("PUR.QUOTATION.NOT_YET_VALID", offers[6].UnavailableReason);
        Assert.Equal("MD.TAX_RATE.NOT_FOUND", offers[7].UnavailableReason);
        Assert.False(offers[3].IsComparable);
        Assert.All(offers.Skip(3), offer => Assert.Null(offer.BaseGross));
        currencyLookup.Verify(x => x.GetRateSnapshotAsync("TWD", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Once);
        currencyLookup.Verify(x => x.GetRateSnapshotAsync("USD", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Once);
        currencyLookup.Setup(x => x.GetRateSnapshotAsync("USD", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("MD unavailable"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetAsync(rfq.Id, Ct));

        var outsider = new QuotationComparisonService(db, new RequisitionCompanyAccess(company),
            new FixedAuthorization(new UserCompanyAuthorization(8, companyId, null, null, false,
                new Dictionary<string, DataScope> { [PurchasingPermissions.RfqCompare] = DataScope.Self })),
            new FixedClock(new DateTime(2026, 10, 21)), currencyLookup.Object, taxLookup.Object);
        await Assert.ThrowsAsync<NotFoundException>(() => outsider.GetAsync(rfq.Id, Ct));
        var noPermission = new QuotationComparisonService(db, new RequisitionCompanyAccess(company),
            new FixedAuthorization(new UserCompanyAuthorization(7, companyId, null, null, false,
                new Dictionary<string, DataScope>())),
            new FixedClock(new DateTime(2026, 10, 21)), currencyLookup.Object, taxLookup.Object);
        await Assert.ThrowsAsync<ForbiddenException>(() => noPermission.GetAsync(rfq.Id, Ct));

        var emptyRfq = new Rfq { CompanyId = companyId, DocumentNo = $"Q-CMP-EMPTY-{companyId}",
            BusinessDate = new DateOnly(2026, 10, 20), ReplyDeadline = new DateOnly(2026, 10, 22),
            OwnerId = 7, Status = RfqStatus.Sent, SentAt = new DateTime(2026, 10, 21) };
        emptyRfq.Lines.Add(new RfqLine { CompanyId = companyId, LineNo = 1, ItemId = 11,
            RequiredDate = new DateOnly(2026, 11, 1), Quantity = 1m });
        emptyRfq.Invitations.Add(new RfqInvitation { CompanyId = companyId, SupplierId = 201 });
        db.Add(emptyRfq);
        await db.SaveChangesAsync(Ct);
        Assert.Empty(Assert.Single((await service.GetAsync(emptyRfq.Id, Ct)).Lines).Offers);

        rfq.Status = RfqStatus.Draft;
        rfq.SentAt = null;
        await db.SaveChangesAsync(Ct);
        var draft = await Assert.ThrowsAsync<BusinessRuleException>(() => service.GetAsync(rfq.Id, Ct));
        Assert.Equal("PUR.RFQ.NOT_SENT", draft.ErrorCode);
    }

    private sealed class FixedAuthorization(UserCompanyAuthorization value) : ICurrentAuthorization
    {
        public Task<UserCompanyAuthorization?> GetAsync(CancellationToken cancellationToken) => Task.FromResult<UserCompanyAuthorization?>(value);
    }
}
