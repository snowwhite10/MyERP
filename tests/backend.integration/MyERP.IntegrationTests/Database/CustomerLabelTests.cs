using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Domain;
using MyERP.Modules.MasterData.Infrastructure;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.IntegrationTests.Database;

/// <summary>WU-FI-001d: MD batch customer labels for other modules' screens (FI-CREDIT-022/023, ADR-077).</summary>
public sealed class CustomerLabelTests(TestDatabase database)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task LabelsCurrentCompanyCustomersIncludingInactiveAndDeletedButNeverOtherCompanies()
    {
        var company = TestCompanies.Next();
        var other = TestCompanies.Next();
        var active = await SeedAsync(company, "Active", isActive: true, isDeleted: false);
        var inactive = await SeedAsync(company, "Inactive", isActive: false, isDeleted: false);
        var deleted = await SeedAsync(company, "Deleted", isActive: false, isDeleted: true);
        var foreign = await SeedAsync(other, "Foreign", isActive: true, isDeleted: false);

        await using var db = Context(company);
        var labels = await new CustomerService(db).GetLabelsAsync(
            [active.Id, inactive.Id, deleted.Id, foreign.Id, long.MaxValue, active.Id], Ct);

        Assert.Equal(3, labels.Count);
        Assert.Equal((active.Code, "Active", true, false),
            (labels[active.Id].Code, labels[active.Id].Name, labels[active.Id].IsActive, labels[active.Id].IsDeleted));
        Assert.Equal((inactive.Code, false, false),
            (labels[inactive.Id].Code, labels[inactive.Id].IsActive, labels[inactive.Id].IsDeleted));
        Assert.Equal((deleted.Code, "Deleted", true),
            (labels[deleted.Id].Code, labels[deleted.Id].Name, labels[deleted.Id].IsDeleted));
        Assert.False(labels.ContainsKey(foreign.Id));
        Assert.False(labels.ContainsKey(long.MaxValue));
        // Tracking stays empty: labels are read-only and must not leak entities into the caller's context.
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task EmptyOrMissingCompanyReturnsNoLabels()
    {
        await using (var db = Context(TestCompanies.Next()))
            Assert.Empty(await new CustomerService(db).GetLabelsAsync([], Ct));
        var seeded = await SeedAsync(TestCompanies.Next(), "No company", true, false);
        await using var none = Context(null);
        Assert.Empty(await new CustomerService(none).GetLabelsAsync([seeded.Id], Ct));
    }

    private async Task<Customer> SeedAsync(long company, string name, bool isActive, bool isDeleted)
    {
        await using var db = Context(company);
        var customer = new Customer
        {
            CompanyId = company, Code = "L" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), Name = name,
            IsActive = isActive, IsDeleted = isDeleted, ReviewStatus = PartnerReviewStatus.Approved
        };
        db.Add(customer);
        await db.SaveChangesAsync(Ct);
        return customer;
    }

    private MyErpDbContext Context(long? company) => new(
        new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString).Options,
        [new PaymentSetupDbModel(), new CustomerCategoryDbModel(), new CustomerDbModel()], new FixedCompanyOrNone(company));

    private sealed class FixedCompanyOrNone(long? id) : MyERP.Shared.Security.ICurrentCompany
    {
        public long? CompanyId => id;
    }
}
