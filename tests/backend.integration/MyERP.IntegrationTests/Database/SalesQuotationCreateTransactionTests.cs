using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Application;
using Moq;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.MasterData.Domain;
using MyERP.Modules.MasterData.Infrastructure;
using MyERP.Modules.Sales;
using MyERP.Modules.Sales.Application;
using MyERP.Modules.Sales.Domain;
using MyERP.Modules.Sales.Infrastructure;
using MyERP.Modules.System.Application.Numbering;
using MyERP.Modules.System.Domain;
using MyERP.Modules.System.Infrastructure;
using MyERP.Shared.Approvals;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Monetary;
using MyERP.Shared.Security;

namespace MyERP.IntegrationTests.Database;

public sealed class SalesQuotationCreateTransactionTests(TestDatabase database)
{
    private static readonly DateOnly Day = new(2028, 2, 29);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CreateUsesRealMdReferencesAndRollsBackCrossCompanyLine()
    {
        long companyId;
        long otherCompanyId;
        await using (var setup = Context(null))
        {
            var company = new SystemCompany { Code = "QR" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
                Name = "Real quotation MD" };
            var other = new SystemCompany { Code = "QX" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
                Name = "Other quotation MD" };
            setup.AddRange(company, other);
            await setup.SaveChangesAsync(Ct);
            companyId = company.Id;
            otherCompanyId = other.Id;
        }

        await using var db = Context(companyId);
        var prefix = "QR" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        db.Add(new NumberRule { CompanyId = companyId, DocumentType = "SAL.QUOTATION", Name = "Quote",
            Prefix = prefix, DateFormat = NumberDateFormat.None, ResetPeriod = NumberResetPeriod.Never,
            SequenceLength = 3 });
        var customer = new Customer { CompanyId = companyId, Code = "C" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
            Name = "Real customer", ReviewStatus = PartnerReviewStatus.Approved };
        var uom = new UnitOfMeasure { CompanyId = companyId, Code = "U" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
            Name = "Unit" };
        var currency = new Currency { CompanyId = companyId, Code = "TWD", Name = "Taiwan dollar",
            DecimalPlaces = 0, IsBase = true };
        var taxCode = new TaxCode { CompanyId = companyId, Code = "OUT", Name = "Output", TaxType = "Output" };
        db.AddRange(customer, uom, currency, taxCode);
        await db.SaveChangesAsync(Ct);
        var item = new Item { CompanyId = companyId, Code = "I" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
            Name = "Real item", BaseUomId = uom.Id };
        var taxRate = new TaxRate { CompanyId = companyId, TaxCodeId = taxCode.Id,
            EffectiveFrom = Day, RatePercent = 5m };
        db.AddRange(item, taxRate);
        await db.SaveChangesAsync(Ct);

        long foreignItemId;
        long foreignCustomerId;
        long foreignTaxRateId;
        await using (var foreign = Context(otherCompanyId))
        {
            var foreignUom = new UnitOfMeasure { CompanyId = otherCompanyId,
                Code = "U" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), Name = "Unit" };
            var foreignCustomer = new Customer { CompanyId = otherCompanyId,
                Code = "C" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
                Name = "Foreign customer", ReviewStatus = PartnerReviewStatus.Approved };
            var foreignTaxCode = new TaxCode { CompanyId = otherCompanyId,
                Code = "OUT", Name = "Output", TaxType = "Output" };
            foreign.AddRange(foreignUom, foreignCustomer, foreignTaxCode);
            await foreign.SaveChangesAsync(Ct);
            var foreignItem = new Item { CompanyId = otherCompanyId,
                Code = "I" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
                Name = "Foreign item", BaseUomId = foreignUom.Id };
            var foreignTaxRate = new TaxRate { CompanyId = otherCompanyId,
                TaxCodeId = foreignTaxCode.Id, EffectiveFrom = Day, RatePercent = 5m };
            foreign.AddRange(foreignItem, foreignTaxRate);
            await foreign.SaveChangesAsync(Ct);
            foreignItemId = foreignItem.Id;
            foreignCustomerId = foreignCustomer.Id;
            foreignTaxRateId = foreignTaxRate.Id;
        }

        var service = RealMdService(db, companyId);
        SalesQuotationInput Input(long itemId) => new(customer.Id, Day, Day.AddDays(30), null,
            [new SalesQuotationLineInput(1, itemId, 2m, 10m, 0m, "TWD", taxRate.Id)]);
        await Assert.ThrowsAsync<MyERP.Shared.Errors.NotFoundException>(() => service.CreateAsync(Input(foreignItemId), Ct));
        await Assert.ThrowsAsync<MyERP.Shared.Errors.NotFoundException>(() =>
            service.CreateAsync(Input(item.Id) with { CustomerId = foreignCustomerId }, Ct));
        await Assert.ThrowsAsync<MyERP.Shared.Errors.NotFoundException>(() =>
            service.CreateAsync(Input(item.Id) with { Lines =
                [new SalesQuotationLineInput(1, item.Id, 2m, 10m, 0m, "TWD", foreignTaxRateId)] }, Ct));
        await Assert.ThrowsAsync<MyERP.Shared.Errors.NotFoundException>(() =>
            service.CreateAsync(Input(item.Id) with { Lines =
                [new SalesQuotationLineInput(1, item.Id, 2m, 10m, 0m, "USD", taxRate.Id)] }, Ct));
        await Assert.ThrowsAsync<MyERP.Shared.Errors.RequestValidationException>(() =>
            service.CreateAsync(Input(item.Id) with { Lines =
                [new SalesQuotationLineInput(1, item.Id, 2m, 10m, 0m, "TWD", taxRate.Id),
                    new SalesQuotationLineInput(2, item.Id, 1m, 10m, 0m, "USD", taxRate.Id)] }, Ct));
        Assert.False(await db.Set<SalesQuotation>().AnyAsync(x => x.DocumentNo.StartsWith(prefix), Ct));

        var created = await service.CreateAsync(Input(item.Id), Ct);
        Assert.Equal(prefix + "001", created.DocumentNo);
        Assert.Equal("Real customer", created.CustomerName);
        Assert.Equal("Real item", Assert.Single(created.Lines).ItemName);
        Assert.Equal(5m, created.Lines[0].TaxRatePercent);
        Assert.Equal(21m, created.OriginalGross);
        db.ChangeTracker.Clear();
        var saved = await db.Set<SalesQuotation>().Include(x => x.Lines)
            .SingleAsync(x => x.Id == created.Id, Ct);
        Assert.Equal("Real customer", saved.CustomerName);
        Assert.Equal("Real item", Assert.Single(saved.Lines).ItemName);

        // A draft keeps its commercial snapshot, but submission must use current MD eligibility.
        var workflow = new Mock<IApprovalWorkflowService>();
        workflow.Setup(x => x.SubmitAsync(It.IsAny<ApprovalSubmission>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ApprovalSubmission request, CancellationToken _) =>
                new ApprovalState(812, request.DocumentType, request.DocumentId,
                    ApprovalStatus.Pending, 1, 1));
        db.ChangeTracker.Clear();
        var auth = new Mock<ICurrentAuthorization>();
        auth.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
            new UserCompanyAuthorization(7, companyId, null, null, true,
                new Dictionary<string, DataScope>()));
        var companyAccess = new SalesCompanyAccess(new FixedCompany(companyId));
        var currencyLookup = new CurrencyLookup(db);
        var submit = new SalesQuotationSubmissionService(db,
            new SalesActionAccess(companyAccess, auth.Object), companyAccess,
            new CustomerService(db), new SalesItemLookup(db), currencyLookup,
            new TaxRateLookup(db, currencyLookup), workflow.Object,
            new FixedClock(new DateTime(2026, 10, 29)));

        async Task AssertRejectedAsync(Func<Task<int>> change, Func<Task<int>> restore)
        {
            await change();
            db.ChangeTracker.Clear();
            await Assert.ThrowsAnyAsync<ErpException>(() => submit.SubmitAsync(created.Id, 1, Ct));
            workflow.Verify(x => x.SubmitAsync(It.IsAny<ApprovalSubmission>(),
                It.IsAny<CancellationToken>()), Times.Never);
            db.ChangeTracker.Clear();
            var unchanged = await db.Set<SalesQuotation>().SingleAsync(x => x.Id == created.Id, Ct);
            Assert.Equal(SalesQuotationStatus.Draft, unchanged.Status);
            Assert.Null(unchanged.ApprovalInstanceId);
            Assert.Equal(1, unchanged.Version);
            db.ChangeTracker.Clear();
            await restore();
            db.ChangeTracker.Clear();
        }

        await AssertRejectedAsync(
            () => db.Set<Customer>().Where(x => x.Id == customer.Id).ExecuteUpdateAsync(
                x => x.SetProperty(p => p.ReviewStatus, PartnerReviewStatus.Rejected), Ct),
            () => db.Set<Customer>().Where(x => x.Id == customer.Id).ExecuteUpdateAsync(
                x => x.SetProperty(p => p.ReviewStatus, PartnerReviewStatus.Approved), Ct));
        await AssertRejectedAsync(
            () => db.Set<Item>().Where(x => x.Id == item.Id).ExecuteUpdateAsync(
                x => x.SetProperty(p => p.IsActive, false), Ct),
            () => db.Set<Item>().Where(x => x.Id == item.Id).ExecuteUpdateAsync(
                x => x.SetProperty(p => p.IsActive, true), Ct));
        await AssertRejectedAsync(
            () => db.Set<Currency>().Where(x => x.Id == currency.Id).ExecuteUpdateAsync(
                x => x.SetProperty(p => p.IsActive, false), Ct),
            () => db.Set<Currency>().Where(x => x.Id == currency.Id).ExecuteUpdateAsync(
                x => x.SetProperty(p => p.IsActive, true), Ct));
        await AssertRejectedAsync(
            () => db.Set<TaxCode>().Where(x => x.Id == taxCode.Id).ExecuteUpdateAsync(
                x => x.SetProperty(p => p.IsActive, false), Ct),
            () => db.Set<TaxCode>().Where(x => x.Id == taxCode.Id).ExecuteUpdateAsync(
                x => x.SetProperty(p => p.IsActive, true), Ct));

        var submitted = await submit.SubmitAsync(created.Id, 1, Ct);
        Assert.Equal(SalesQuotationStatus.Pending, submitted.Quotation.Status);
        workflow.Verify(x => x.SubmitAsync(It.IsAny<ApprovalSubmission>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateUsesSysNumberAndMdSnapshotsInOneTransaction()
    {
        long companyId;
        await using (var setup = Context(null))
        {
            var company = new SystemCompany { Code = "QT" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
                Name = "Quotation company" };
            setup.Add(company);
            await setup.SaveChangesAsync(Ct);
            companyId = company.Id;
        }
        await using var db = Context(companyId);
        var prefix = "QT" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        db.Add(new NumberRule { CompanyId = companyId, DocumentType = "SAL.QUOTATION", Name = "Quote",
            Prefix = prefix, DateFormat = NumberDateFormat.None, ResetPeriod = NumberResetPeriod.Never,
            SequenceLength = 3 });
        await db.SaveChangesAsync(Ct);

        var service = Service(db, companyId, true);
        var input = new SalesQuotationInput(21, Day, Day.AddDays(30), " first ",
            [new SalesQuotationLineInput(1, 31, 2m, 10m, 0m, "TWD", 41)]);
        var invalidCurrency = input with
        {
            Lines = [new SalesQuotationLineInput(1, 31, 2m, 10m, 0m, "USD", 41)]
        };
        await Assert.ThrowsAsync<MyERP.Shared.Errors.NotFoundException>(() =>
            service.CreateAsync(invalidCurrency, Ct));
        Assert.False(await db.Set<SalesQuotation>().AnyAsync(x => x.DocumentNo.StartsWith(prefix), Ct));
        var quote = await service.CreateAsync(input, Ct);
        Assert.Equal(prefix + "001", quote.DocumentNo);
        Assert.Equal(21m, quote.OriginalGross);
        Assert.Equal(1, quote.Version);
        Assert.Equal("first", quote.Remark);
        Assert.Equal(7, quote.OwnerUserId);
        db.ChangeTracker.Clear();
        var saved = await db.Set<SalesQuotation>().Include(x => x.Lines)
            .SingleAsync(x => x.Id == quote.Id, Ct);
        Assert.Equal("Customer", saved.CustomerName);
        Assert.Equal("Item", Assert.Single(saved.Lines).ItemName);
        Assert.Equal(5m, saved.Lines[0].TaxRatePercent);
        Assert.Equal(21m, saved.Lines[0].OriginalGross);

        await Assert.ThrowsAsync<MyERP.Shared.Errors.ForbiddenException>(() =>
            Service(db, companyId, false).CreateAsync(input, Ct));
        Assert.Equal(1, await db.Set<SalesQuotation>()
            .CountAsync(x => x.DocumentNo.StartsWith(prefix), Ct));
    }

    private MyErpDbContext Context(long? companyId) => new(
        new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(new AuditStamper(
                new FixedClock(new DateTime(2026, 10, 29)), new FixedUser(7), new FixedCompany(companyId)))).Options,
        [new SalesQuotationDbModel(), new SystemOrganizationDbModel(), new UomDbModel(), new ItemDbModel(),
            new CustomerDbModel(), new PaymentSetupDbModel(), new CurrencyDbModel(), new TaxDbModel()],
        new FixedCompany(companyId));

    private static SalesQuotationCreateService RealMdService(MyErpDbContext db, long companyId)
    {
        var auth = new Mock<ICurrentAuthorization>();
        auth.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
            new UserCompanyAuthorization(7, companyId, null, null, true,
                new Dictionary<string, DataScope>()));
        var company = new SalesCompanyAccess(new FixedCompany(companyId));
        var currencies = new CurrencyLookup(db);
        return new SalesQuotationCreateService(db, new SalesActionAccess(company, auth.Object), company,
            new NumberGenerator(db, new FixedClock(new DateTime(2026, 10, 29)), new FixedUser(7)),
            new SalesQuotationSnapshotBuilder(new CustomerService(db), new SalesItemLookup(db),
                currencies, new TaxRateLookup(db, currencies)),
            new SalesQuotationDraftStore(db, new FixedClock(new DateTime(2026, 10, 29))));
    }

    private static SalesQuotationCreateService Service(MyErpDbContext db, long companyId, bool allow)
    {
        var auth = new Mock<ICurrentAuthorization>();
        auth.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
            new UserCompanyAuthorization(7, companyId, null, null, allow,
                new Dictionary<string, DataScope>()));
        var customer = new Mock<ICustomerLookup>();
        customer.Setup(x => x.RequireApprovedAsync(21, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CustomerIdentity(21, "C21", "Customer"));
        var item = new Mock<ISalesItemLookup>();
        item.Setup(x => x.RequireActiveAsync(31, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SalesItemIdentity(31, "I31", "Item"));
        var currency = new Mock<ICurrencyLookup>();
        currency.Setup(x => x.FindCurrencyAsync("TWD", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CurrencyInfo("TWD", "Dollar", null, 0, true, true));
        currency.Setup(x => x.GetBaseCurrencyAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CurrencyInfo("TWD", "Dollar", null, 0, true, true));
        currency.Setup(x => x.GetRateSnapshotAsync("TWD", Day, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExchangeRateSnapshot(1m, Day, null, null));
        currency.Setup(x => x.GetPrecisionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CurrencyPrecision.Default);
        var tax = new Mock<ITaxRateLookup>();
        tax.Setup(x => x.GetRateSnapshotAsync(41, TaxDirection.Output, Day, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TaxRateSnapshot(41, 5m, Day, null));
        var company = new SalesCompanyAccess(new FixedCompany(companyId));
        return new SalesQuotationCreateService(db, new SalesActionAccess(company, auth.Object), company,
            new NumberGenerator(db, new FixedClock(new DateTime(2026, 10, 29)), new FixedUser(7)),
            new SalesQuotationSnapshotBuilder(customer.Object, item.Object, currency.Object, tax.Object),
            new SalesQuotationDraftStore(db, new FixedClock(new DateTime(2026, 10, 29))));
    }
}
