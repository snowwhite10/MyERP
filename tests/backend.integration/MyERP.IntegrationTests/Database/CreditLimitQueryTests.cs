using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Finance.Application;
using MyERP.Modules.Finance.Domain;
using MyERP.Modules.Finance.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;

namespace MyERP.IntegrationTests.Database;

/// <summary>WU-FI-001b2: company-bound list/detail of credit limits with filters, stable order and paging.</summary>
public sealed class CreditLimitQueryTests(TestDatabase database)
{
    private static readonly DateTime Now = new(2026, 11, 4, 9, 0, 0);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task FiltersByCustomerStatusAndIntersectingDatesWithStableOrder()
    {
        var company = TestCompanies.Next();
        var ids = await SeedAsync(company,
            Limit(10, "2026-01-01", "2026-06-30", 1m),
            Limit(10, "2026-07-01", "2026-12-31", 2m),
            Limit(11, "2026-07-01", "2026-12-31", 3m),
            Limit(10, "2025-01-01", "2025-12-31", 4m, CreditLimitStatus.Deactivated));
        await SeedAsync(TestCompanies.Next(), Limit(10, "2026-01-01", "2026-12-31", 5m));
        var service = Service(company);

        var all = await service.ListAsync(new CreditLimitQuery(), Ct);
        Assert.Equal(4, all.TotalCount);
        // Default -effectiveFrom then -id: two rows share 2026-07-01, so the later id comes first.
        Assert.Equal([ids[2], ids[1], ids[0], ids[3]], all.Items.Select(x => x.Id));

        var customer = await service.ListAsync(new CreditLimitQuery { CustomerId = 10, Sort = "effectiveFrom" }, Ct);
        Assert.Equal([ids[3], ids[0], ids[1]], customer.Items.Select(x => x.Id));

        var active = await service.ListAsync(new CreditLimitQuery { Status = CreditLimitStatus.Deactivated }, Ct);
        Assert.Equal([ids[3]], active.Items.Select(x => x.Id));

        // Inclusive intersection: a single day on the boundary returns only the period covering it.
        var onDay = await service.ListAsync(new CreditLimitQuery
        {
            From = new DateOnly(2026, 6, 30), To = new DateOnly(2026, 6, 30)
        }, Ct);
        Assert.Equal([ids[0]], onDay.Items.Select(x => x.Id));
        var openEnded = await service.ListAsync(new CreditLimitQuery { From = new DateOnly(2026, 7, 1) }, Ct);
        Assert.Equal(2, openEnded.TotalCount);
        var until = await service.ListAsync(new CreditLimitQuery { To = new DateOnly(2025, 12, 31) }, Ct);
        Assert.Equal([ids[3]], until.Items.Select(x => x.Id));

        var page2 = await service.ListAsync(new CreditLimitQuery { Page = 2, PageSize = 3, Sort = "amount" }, Ct);
        Assert.Equal(4, page2.TotalCount);
        Assert.Equal([ids[3]], page2.Items.Select(x => x.Id));
    }

    [Fact]
    public async Task DetailIsCompanyBoundAndIncludesHistory()
    {
        var company = TestCompanies.Next();
        var other = TestCompanies.Next();
        var ids = await SeedAsync(company, Limit(10, "2025-01-01", "2025-12-31", 4m, CreditLimitStatus.Deactivated));
        var otherIds = await SeedAsync(other, Limit(10, "2025-01-01", "2025-12-31", 4m));

        var detail = await Service(company).GetAsync(ids[0], Ct);
        Assert.Equal(CreditLimitStatus.Deactivated, detail.Status);
        Assert.Equal(7, detail.CreatedBy);

        var error = await Assert.ThrowsAsync<NotFoundException>(() => Service(company).GetAsync(otherIds[0], Ct));
        Assert.Equal(CreditLimitErrors.NotFound, error.ErrorCode);
    }

    [Theory]
    [InlineData(false, "AR.CREDIT.VIEW")]   // no current company
    [InlineData(true, "AR.CREDIT.CREATE")]  // a different action does not imply VIEW
    public async Task MissingCompanyOrViewPermissionIsForbidden(bool hasCompany, string permission)
    {
        var company = TestCompanies.Next();
        var service = new CreditLimitQueryService(Context(company),
            new FixedCompanyOrNone(hasCompany ? company : null), Authorization(company, [permission]));

        await Assert.ThrowsAsync<ForbiddenException>(() => service.ListAsync(new CreditLimitQuery(), Ct));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.GetAsync(1, Ct));
    }

    [Fact]
    public async Task AuthorizationOfAnotherCompanyIsForbidden()
    {
        var company = TestCompanies.Next();
        var service = new CreditLimitQueryService(Context(company), new FixedCompanyOrNone(company),
            Authorization(company + 1, ["AR.CREDIT.VIEW"]));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.ListAsync(new CreditLimitQuery(), Ct));
    }

    public static TheoryData<CreditLimitQuery, string> InvalidQueries => new()
    {
        { new CreditLimitQuery { From = new DateOnly(2026, 2, 1), To = new DateOnly(2026, 1, 1) }, "to" },
        { new CreditLimitQuery { CustomerId = 0 }, "customerId" },
        { new CreditLimitQuery { Status = (CreditLimitStatus)42 }, "status" },
        { new CreditLimitQuery { Sort = "createdBy" }, "sort" },
        { new CreditLimitQuery { Sort = "amount,-effectiveFrom" }, "sort" },
        { new CreditLimitQuery { Keyword = "C10" }, "keyword" },
        { new CreditLimitQuery { PageSize = 0 }, "pageSize" }
    };

    [Theory]
    [MemberData(nameof(InvalidQueries))]
    public async Task InvalidFilterIsFieldValidationError(CreditLimitQuery query, string field)
    {
        var error = await Assert.ThrowsAsync<RequestValidationException>(
            () => Service(TestCompanies.Next()).ListAsync(query, Ct));
        Assert.True(error.Errors.ContainsKey(field), string.Join(",", error.Errors.Keys));
    }

    private static CreditLimit Limit(long customer, string from, string to, decimal amount,
        CreditLimitStatus status = CreditLimitStatus.Active) => new()
        {
            CustomerId = customer, BaseCurrency = "TWD", Amount = amount, Status = status,
            EffectiveFrom = DateOnly.Parse(from, System.Globalization.CultureInfo.InvariantCulture),
            EffectiveTo = DateOnly.Parse(to, System.Globalization.CultureInfo.InvariantCulture)
        };

    private async Task<long[]> SeedAsync(long company, params CreditLimit[] limits)
    {
        await using var db = Context(company);
        // ar_credit_limit references the permanent company/customer key.
        foreach (var customer in limits.Select(x => x.CustomerId).Distinct())
            db.Add(new CreditKey { CompanyId = company, CustomerId = customer });
        await db.SaveChangesAsync(Ct);
        foreach (var limit in limits)
        {
            limit.CompanyId = company;
            db.Add(limit);
            await db.SaveChangesAsync(Ct); // one by one so ids follow argument order
        }
        return [.. limits.Select(x => x.Id)];
    }

    private MyErpDbContext Context(long company)
    {
        var current = new FixedCompany(company);
        var stamper = new AuditStamper(new FixedClock(Now), new FixedUser(7), current);
        return new MyErpDbContext(new DbContextOptionsBuilder<MyErpDbContext>()
            .UseMyErpSqlServer(database.ConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(stamper)).Options,
            [new CustomerCreditDbModel()], current);
    }

    // DataScope.Self on purpose: credit limits are company-wide configuration, so any VIEW grant sees them (ADR-075).
    private CreditLimitQueryService Service(long company) =>
        new(Context(company), new FixedCompanyOrNone(company), Authorization(company, ["AR.CREDIT.VIEW"]));

    private static StubAuthorization Authorization(long company, string[] permissions) =>
        new StubAuthorization(new UserCompanyAuthorization(7, company, null, null, false,
            permissions.ToDictionary(x => x, _ => DataScope.Self)));

    private sealed class StubAuthorization(UserCompanyAuthorization value) : ICurrentAuthorization
    {
        public Task<UserCompanyAuthorization?> GetAsync(CancellationToken token) =>
            Task.FromResult<UserCompanyAuthorization?>(value);
    }

    private sealed class FixedCompanyOrNone(long? id) : ICurrentCompany
    {
        public long? CompanyId => id;
    }
}
