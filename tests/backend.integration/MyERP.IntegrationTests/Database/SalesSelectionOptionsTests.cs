using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Domain;
using MyERP.Modules.MasterData.Infrastructure;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Paging;

namespace MyERP.IntegrationTests.Database;

public sealed class SalesSelectionOptionsTests(TestDatabase database)
{
    private MyErpDbContext Context(long companyId) => new(
        new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(new AuditStamper(
                new FixedClock(new DateTime(2026, 10, 24)), new FixedUser(7), new FixedCompany(companyId)))).Options,
        [new CustomerDbModel(), new UomDbModel(), new ItemDbModel(), new CurrencyDbModel(), new TaxDbModel()],
        new FixedCompany(companyId));

    [Fact]
    public async Task SalesChoicesExcludeUnapprovedInactiveDeletedAndOtherCompanyReferences()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = TestCompanies.Next();
        var otherCompanyId = TestCompanies.Next();
        await using var db = Context(companyId);
        await using var otherDb = Context(otherCompanyId);
        var uom = new UnitOfMeasure { CompanyId = companyId, Code = "EA", Name = "Each" };
        db.Add(uom);
        await db.SaveChangesAsync(token);
        db.AddRange(
            new Customer { CompanyId = companyId, Code = "C1", Name = "Ready", ReviewStatus = PartnerReviewStatus.Approved },
            new Customer { CompanyId = companyId, Code = "C2", Name = "Pending" },
            new Customer { CompanyId = companyId, Code = "C3", Name = "Inactive", IsActive = false, ReviewStatus = PartnerReviewStatus.Approved },
            new Customer { CompanyId = companyId, Code = "C4", Name = "Deleted", IsDeleted = true, ReviewStatus = PartnerReviewStatus.Approved },
            new Item { CompanyId = companyId, Code = "I1", Name = "Ready", BaseUomId = uom.Id },
            new Item { CompanyId = companyId, Code = "I2", Name = "Inactive", BaseUomId = uom.Id, IsActive = false },
            new Item { CompanyId = companyId, Code = "I3", Name = "Deleted", BaseUomId = uom.Id, IsDeleted = true });
        await db.SaveChangesAsync(token);
        otherDb.Add(new Customer { CompanyId = otherCompanyId, Code = "C5", Name = "Foreign", ReviewStatus = PartnerReviewStatus.Approved });
        await otherDb.SaveChangesAsync(token);
        var options = new SalesSelectionOptions(db);
        Assert.Equal("C1", Assert.Single((await options.CustomersAsync(new PagedRequest(), token)).Items).Code);
        Assert.Equal("I1", Assert.Single((await options.ItemsAsync(new PagedRequest(), token)).Items).Code);
    }

    [Fact]
    public async Task CommercialChoicesUseOutputDirectionAndInclusiveBusinessDate()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var output = new TaxCode { CompanyId = companyId, Code = "OUT", Name = "Output", TaxType = "Output" };
        var input = new TaxCode { CompanyId = companyId, Code = "IN", Name = "Input", TaxType = "Input" };
        db.AddRange(output, input, new Currency { CompanyId = companyId, Code = "TWD", Name = "Dollar", DecimalPlaces = 0 },
            new Currency { CompanyId = companyId, Code = "USD", Name = "Disabled", IsActive = false });
        await db.SaveChangesAsync(token);
        db.AddRange(new TaxRate { CompanyId = companyId, TaxCodeId = output.Id, EffectiveFrom = new DateOnly(2024, 2, 29),
                EffectiveTo = new DateOnly(2024, 3, 1), RatePercent = 5m },
            new TaxRate { CompanyId = companyId, TaxCodeId = input.Id, EffectiveFrom = new DateOnly(2024, 2, 29), RatePercent = 5m });
        await db.SaveChangesAsync(token);
        var options = new SalesSelectionOptions(db);
        var onEnd = await options.CommercialAsync(new DateOnly(2024, 3, 1), token);
        Assert.Equal("TWD", Assert.Single(onEnd.Currencies).Code);
        Assert.Equal("OUT", Assert.Single(onEnd.OutputTaxRates).Code);
        Assert.Empty((await options.CommercialAsync(new DateOnly(2024, 3, 2), token)).OutputTaxRates);
    }
}
