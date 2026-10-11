using Microsoft.EntityFrameworkCore;
using MyERP.IntegrationTests.Database;
using MyERP.Modules.System.Application.Notifications;
using MyERP.Modules.System.Contracts;
using MyERP.Modules.System.Domain;
using MyERP.Modules.System.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.IntegrationTests.Database;

public sealed class NotificationPublisherTests(TestDatabase database)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PublishIsCompanyScopedAndIdempotent()
    {
        var companyA = await InsertCompanyAsync("NA");
        var companyB = await InsertCompanyAsync("NB");
        var user = await InsertUserAsync();
        await AssignAsync(user, companyA);
        var clock = new FixedClock(new DateTime(2026, 10, 11, 19, 0, 0));
        var message = new NotificationMessage(user, "approval", "Ready", "Please review", "/sys/approvals", "approval:42:step:1");

        await using (var db = Context(companyA))
        {
            var publisher = new NotificationPublisher(db, clock);
            var first = await publisher.PublishAsync(message, Ct);
            var second = await publisher.PublishAsync(message with { SourceKey = " " + message.SourceKey + " " }, Ct);
            Assert.Equal(first, second);
            var stored = Assert.Single(await db.Set<SystemNotification>().AsNoTracking().ToListAsync(Ct));
            Assert.Null(stored.ReadAt);
            Assert.Equal(clock.LocalNow.AddDays(90), stored.ExpiresAt);
        }

        await using (var db = Context(companyB))
        {
            var publisher = new NotificationPublisher(db, clock);
            await Assert.ThrowsAsync<ForbiddenException>(() => publisher.PublishAsync(message, Ct));
            Assert.Empty(await db.Set<SystemNotification>().AsNoTracking().ToListAsync(Ct));
        }
    }

    [Fact]
    public async Task RejectsUnsafePayloadAndUnknownRecipient()
    {
        var company = await InsertCompanyAsync("NC");
        await using var db = Context(company);
        var publisher = new NotificationPublisher(db, new FixedClock(new DateTime(2026, 10, 11)));
        await Assert.ThrowsAsync<ForbiddenException>(() => publisher.PublishAsync(new NotificationMessage(999999999, "approval", "Ready", "Review", "/sys/approvals", "x"), Ct));
        var user = await InsertUserAsync();
        await AssignAsync(user, company);
        await Assert.ThrowsAsync<RequestValidationException>(() => publisher.PublishAsync(new NotificationMessage(user, "approval", "Ready", "Review", "https://evil.example", "x"), Ct));
        await Assert.ThrowsAsync<RequestValidationException>(() => publisher.PublishAsync(new NotificationMessage(user, "approval", "Ready", "Review", "//evil.example", "x"), Ct));
        Assert.Empty(await db.Set<SystemNotification>().AsNoTracking().ToListAsync(Ct));
    }

    [Fact]
    public async Task ConcurrentReplayStoresOnlyOneNotification()
    {
        var company = await InsertCompanyAsync("ND");
        var user = await InsertUserAsync();
        await AssignAsync(user, company);
        var clock = new FixedClock(new DateTime(2026, 10, 11, 19, 0, 0));
        var message = new NotificationMessage(user, "approval", "Ready", "Please review", "/sys/approvals", "approval:concurrent:1");
        await using var firstDb = Context(company);
        await using var secondDb = Context(company);
        var results = await Task.WhenAll(
            new NotificationPublisher(firstDb, clock).PublishAsync(message, Ct),
            new NotificationPublisher(secondDb, clock).PublishAsync(message, Ct));
        Assert.Equal(results[0], results[1]);
        await using var verify = Context(company);
        Assert.Single(await verify.Set<SystemNotification>().AsNoTracking().ToListAsync(Ct));
    }

    private MyErpDbContext Context(long company) => new(
        new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString).Options,
        [new SystemOrganizationDbModel()], new FixedCompany(company));

    private async Task<long> InsertCompanyAsync(string prefix)
    {
        await using var db = Context(0);
        var code = prefix + Guid.NewGuid().ToString("N")[..10];
        var company = new SystemCompany { Code = code, Name = "Notification test", CreatedAt = new DateTime(2026, 10, 11), Version = 1 };
        db.Set<SystemCompany>().Add(company);
        await db.SaveChangesAsync(Ct);
        return company.Id;
    }

    private async Task<long> InsertUserAsync()
    {
        await using var db = Context(0);
        var name = "notify" + Guid.NewGuid().ToString("N")[..12];
        var user = new SystemUser { Username = name, NormalizedUsername = name, PasswordHash = "test", PasswordChangedAt = new DateTime(2026, 10, 11), CreatedAt = new DateTime(2026, 10, 11), Version = 1 };
        db.Set<SystemUser>().Add(user);
        await db.SaveChangesAsync(Ct);
        return user.Id;
    }

    private async Task AssignAsync(long user, long company)
    {
        await using var db = Context(company);
        db.Set<SystemUserCompany>().Add(new SystemUserCompany { UserId = user, CompanyId = company, CreatedAt = new DateTime(2026, 10, 11), Version = 1 });
        await db.SaveChangesAsync(Ct);
    }
}
