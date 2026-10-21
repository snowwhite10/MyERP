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

public sealed class RfqAwardTests(TestDatabase database)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AwardSavesSnapshotAndNeverReplacesTheSelectedLine()
    {
        var companyId = TestCompanies.Next();
        var company = new FixedCompany(companyId);
        var clock = new FixedClock(new DateTime(2026, 10, 21));
        var stamp = new AuditStamper(clock, new FixedUser(7), company);
        await using var db = NewDb(company, stamp);
        var rfq = new Rfq { CompanyId = companyId, DocumentNo = $"Q-AWARD-{companyId}",
            BusinessDate = new DateOnly(2026, 10, 20), ReplyDeadline = new DateOnly(2026, 10, 22),
            OwnerId = 7, Status = RfqStatus.Sent, SentAt = clock.LocalNow };
        rfq.Lines.Add(new RfqLine { CompanyId = companyId, LineNo = 1, ItemId = 11,
            RequiredDate = new DateOnly(2026, 11, 1), Quantity = 2m });
        rfq.Lines.Add(new RfqLine { CompanyId = companyId, LineNo = 2, ItemId = 12,
            RequiredDate = new DateOnly(2026, 11, 1), Quantity = 1m });
        rfq.Invitations.Add(new RfqInvitation { CompanyId = companyId, SupplierId = 101 });
        db.Add(rfq);
        await db.SaveChangesAsync(Ct);
        var quote = new SupplierQuotation { CompanyId = companyId, RfqId = rfq.Id,
            SupplierId = 101, CurrentRevisionNo = 1 };
        quote.Revisions.Add(new QuotationRevision { CompanyId = companyId, RevisionNo = 1,
            QuoteDate = new DateOnly(2026, 10, 20), ValidUntil = new DateOnly(2026, 10, 22),
            Lines = { new QuotationLine { CompanyId = companyId, RfqLineId = rfq.Lines.Single(x => x.LineNo == 1).Id,
                UnitPrice = 1.235m, CurrencyCode = "USD", TaxRateId = 31 },
                new QuotationLine { CompanyId = companyId, RfqLineId = rfq.Lines.Single(x => x.LineNo == 2).Id,
                    UnitPrice = 2m, CurrencyCode = "USD", TaxRateId = 31 } } });
        db.Add(quote);
        await db.SaveChangesAsync(Ct);
        var currency = new Mock<ICurrencyLookup>();
        currency.Setup(x => x.GetBaseCurrencyAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CurrencyInfo("TWD", "Taiwan Dollar", null, 0, true, true));
        currency.Setup(x => x.GetPrecisionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CurrencyPrecision.Default);
        currency.Setup(x => x.GetRateSnapshotAsync("USD", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExchangeRateSnapshot(30m, new DateOnly(2026, 10, 1), null, 9));
        var tax = new Mock<ITaxRateLookup>();
        tax.Setup(x => x.GetRateSnapshotAsync(31, TaxDirection.Input, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TaxRateSnapshot(31, 5m, new DateOnly(2026, 10, 1), null));
        var authorization = new FixedAuthorization(new UserCompanyAuthorization(7, companyId, null, null, false,
            new Dictionary<string, DataScope> { [PurchasingPermissions.RfqAward] = DataScope.Self }));
        var service = new RfqAwardService(db, new RequisitionCompanyAccess(company), authorization,
            clock, currency.Object, tax.Object);
        var lineId = rfq.Lines.Single(x => x.LineNo == 1).Id;
        var firstVersion = rfq.Version;
        await Assert.ThrowsAsync<RequestValidationException>(() => service.AwardAsync(rfq.Id,
            new RfqAwardInput(firstVersion, lineId, quote.Id, " "), Ct));
        await Assert.ThrowsAsync<RequestValidationException>(() => service.AwardAsync(rfq.Id,
            new RfqAwardInput(firstVersion, lineId, quote.Id, new string('x', 501)), Ct));
        await Assert.ThrowsAsync<RequestValidationException>(() => service.AwardAsync(rfq.Id,
            new RfqAwardInput(firstVersion, 0, quote.Id, "Reason"), Ct));
        await Assert.ThrowsAsync<NotFoundException>(() => service.AwardAsync(rfq.Id,
            new RfqAwardInput(firstVersion, long.MaxValue, quote.Id, "Reason"), Ct));
        await Assert.ThrowsAsync<NotFoundException>(() => service.AwardAsync(rfq.Id,
            new RfqAwardInput(firstVersion, lineId, long.MaxValue, "Reason"), Ct));
        var noPermission = new RfqAwardService(db, new RequisitionCompanyAccess(company),
            new FixedAuthorization(new UserCompanyAuthorization(7, companyId, null, null, false,
                new Dictionary<string, DataScope>())), clock, currency.Object, tax.Object);
        await Assert.ThrowsAsync<ForbiddenException>(() => noPermission.AwardAsync(rfq.Id,
            new RfqAwardInput(firstVersion, lineId, quote.Id, "Reason"), Ct));
        var outsider = new RfqAwardService(db, new RequisitionCompanyAccess(company),
            new FixedAuthorization(new UserCompanyAuthorization(8, companyId, null, null, false,
                new Dictionary<string, DataScope> { [PurchasingPermissions.RfqAward] = DataScope.Self })),
            clock, currency.Object, tax.Object);
        await Assert.ThrowsAsync<NotFoundException>(() => outsider.AwardAsync(rfq.Id,
            new RfqAwardInput(firstVersion, lineId, quote.Id, "Reason"), Ct));
        var expired = new RfqAwardService(db, new RequisitionCompanyAccess(company), authorization,
            new FixedClock(new DateTime(2026, 10, 23)), currency.Object, tax.Object);
        var expiry = await Assert.ThrowsAsync<BusinessRuleException>(() => expired.AwardAsync(rfq.Id,
            new RfqAwardInput(firstVersion, lineId, quote.Id, "Reason"), Ct));
        Assert.Equal("PUR.QUOTATION.EXPIRED", expiry.ErrorCode);
        currency.Setup(x => x.GetRateSnapshotAsync("USD", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BusinessRuleException("MD.EXCHANGE_RATE.NOT_FOUND", "Missing rate."));
        var missing = await Assert.ThrowsAsync<BusinessRuleException>(() => service.AwardAsync(rfq.Id,
            new RfqAwardInput(firstVersion, lineId, quote.Id, "Lowest evaluated cost"), Ct));
        Assert.Equal("MD.EXCHANGE_RATE.NOT_FOUND", missing.ErrorCode);
        Assert.Empty(await db.Set<RfqAward>().Where(x => x.RfqId == rfq.Id).ToListAsync(Ct));
        currency.Setup(x => x.GetRateSnapshotAsync("USD", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExchangeRateSnapshot(30m, new DateOnly(2026, 10, 1), null, 9));
        tax.Setup(x => x.GetRateSnapshotAsync(31, TaxDirection.Input, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BusinessRuleException("MD.TAX_RATE.NOT_FOUND", "Missing tax."));
        var missingTax = await Assert.ThrowsAsync<BusinessRuleException>(() => service.AwardAsync(rfq.Id,
            new RfqAwardInput(firstVersion, lineId, quote.Id, "Reason"), Ct));
        Assert.Equal("MD.TAX_RATE.NOT_FOUND", missingTax.ErrorCode);
        tax.Setup(x => x.GetRateSnapshotAsync(31, TaxDirection.Input, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TaxRateSnapshot(31, 5m, new DateOnly(2026, 10, 1), null));
        var withdrawnQuote = await db.Set<SupplierQuotation>().SingleAsync(x => x.Id == quote.Id, Ct);
        withdrawnQuote.Status = SupplierQuotationStatus.Withdrawn;
        await db.SaveChangesAsync(Ct);
        var withdrawn = await Assert.ThrowsAsync<BusinessRuleException>(() => service.AwardAsync(rfq.Id,
            new RfqAwardInput(firstVersion, lineId, quote.Id, "Reason"), Ct));
        Assert.Equal("PUR.QUOTATION.WITHDRAWN", withdrawn.ErrorCode);
        withdrawnQuote = await db.Set<SupplierQuotation>().SingleAsync(x => x.Id == quote.Id, Ct);
        withdrawnQuote.Status = SupplierQuotationStatus.Active;
        await db.SaveChangesAsync(Ct);
        var result = await service.AwardAsync(rfq.Id, new RfqAwardInput(rfq.Version, lineId,
            quote.Id, "Lowest evaluated cost"), Ct);
        Assert.Equal(78m, result.BaseGross);
        Assert.Equal(30m, result.ExchangeRate);
        Assert.Equal(5m, result.TaxRatePercent);
        Assert.Equal(9, result.ExchangeRateId);
        Assert.Equal(SupplierQuotationStatus.Awarded, (await db.Set<SupplierQuotation>()
            .AsNoTracking().SingleAsync(x => x.Id == quote.Id, Ct)).Status);
        await Assert.ThrowsAsync<ConflictException>(() => service.AwardAsync(rfq.Id,
            new RfqAwardInput(firstVersion, lineId, quote.Id, "Stale"), Ct));
        var duplicate = await Assert.ThrowsAsync<BusinessRuleException>(() => service.AwardAsync(rfq.Id,
            new RfqAwardInput(result.RfqVersion, lineId, quote.Id, "Replace"), Ct));
        Assert.Equal("PUR.RFQ.LINE_ALREADY_AWARDED", duplicate.ErrorCode);
        Assert.Single(await db.Set<RfqAward>().Where(x => x.RfqLineId == lineId).ToListAsync(Ct));

        await using var otherDb = NewDb(company, stamp);
        var otherService = new RfqAwardService(otherDb, new RequisitionCompanyAccess(company),
            authorization, clock, currency.Object, tax.Object);
        var secondLineId = rfq.Lines.Single(x => x.LineNo == 2).Id;
        var secondInput = new RfqAwardInput(result.RfqVersion, secondLineId, quote.Id, "Next line");
        var simultaneous = await Task.WhenAll(
            Capture(() => service.AwardAsync(rfq.Id, secondInput, Ct)),
            Capture(() => otherService.AwardAsync(rfq.Id, secondInput, Ct)));
        Assert.Single(simultaneous, x => x is null);
        Assert.Single(simultaneous.OfType<ConflictException>());
        Assert.Equal(2, await db.Set<RfqAward>().CountAsync(x => x.RfqId == rfq.Id, Ct));

        await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE dbo.pur_rfq_award SET reason = N'Overwritten' WHERE id = {result.Id}", Ct));
        var saved = await db.Set<RfqAward>().AsNoTracking().SingleAsync(x => x.Id == result.Id, Ct);
        Assert.Equal("Lowest evaluated cost", saved.Reason);
        await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM dbo.pur_rfq_award WHERE id = {result.Id}", Ct));
        Assert.Equal("Lowest evaluated cost", (await db.Set<RfqAward>()
            .AsNoTracking().SingleAsync(x => x.Id == result.Id, Ct)).Reason);
    }

    private static async Task<Exception?> Capture(Func<Task> action)
    {
        try { await action(); return null; }
        catch (Exception ex) { return ex; }
    }

    private MyErpDbContext NewDb(FixedCompany company, AuditStamper stamp) =>
        new(new DbContextOptionsBuilder<MyErpDbContext>()
            .UseMyErpSqlServer(database.ConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(stamp)).Options,
            [new RfqDbModel(), new QuotationDbModel(), new RfqAwardDbModel()], company);

    private sealed class FixedAuthorization(UserCompanyAuthorization value) : ICurrentAuthorization
    {
        public Task<UserCompanyAuthorization?> GetAsync(CancellationToken cancellationToken) => Task.FromResult<UserCompanyAuthorization?>(value);
    }
}
