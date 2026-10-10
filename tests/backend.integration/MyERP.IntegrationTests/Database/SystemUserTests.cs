using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyERP.Modules.System.Application;
using MyERP.Modules.System.Domain;
using MyERP.Modules.System.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Time;

namespace MyERP.IntegrationTests.Database;

public sealed class SystemUserTests(TestDatabase database)
{
    [Fact]
    public async Task PasswordPolicyAndHasherRejectWeakPasswordAndStoreOnlyHash()
    {
        var clock = new UserTestClock();
        var options = new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString).Options;
        await using var db = new MyErpDbContext(options, [new SystemOrganizationDbModel()], new FixedCompany(null));
        var service = new SystemUserService(db, new PasswordHasher<SystemUser>(), clock, new PasswordPolicy());
        await Assert.ThrowsAsync<RequestValidationException>(() => service.CreateAsync("tester" + Guid.NewGuid().ToString("N"), "weak", TestContext.Current.CancellationToken));
        var username = "tester" + Guid.NewGuid().ToString("N");
        var user = await service.CreateAsync(username, "Strong pass 2026!", TestContext.Current.CancellationToken);
        Assert.NotEqual("Strong pass 2026!", user.PasswordHash);
        Assert.Equal(PasswordVerificationResult.Success, new PasswordHasher<SystemUser>().VerifyHashedPassword(user, user.PasswordHash, "Strong pass 2026!"));
        Assert.Equal(1, await db.Set<SystemUser>().CountAsync(x => x.Username == username, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FiveFailuresLockForFifteenMinutesAndSuccessClearsCounter()
    {
        var clock = new UserTestClock();
        var options = new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString).Options;
        await using var db = new MyErpDbContext(options, [new SystemOrganizationDbModel()], new FixedCompany(null));
        var service = new SystemUserService(db, new PasswordHasher<SystemUser>(), clock, new PasswordPolicy());
        var user = await service.CreateAsync("tester" + Guid.NewGuid().ToString("N"), "Strong pass 2026!", TestContext.Current.CancellationToken);
        for (var i = 0; i < 5; i++)
        {
            Assert.False(await service.VerifyPasswordAsync(user.Id, "wrong", TestContext.Current.CancellationToken));
        }
        Assert.False(await service.VerifyPasswordAsync(user.Id, "Strong pass 2026!", TestContext.Current.CancellationToken));
        Assert.Equal(clock.LocalNow.AddMinutes(15), user.LockoutEnd);
        clock.Advance(TimeSpan.FromMinutes(15));
        Assert.True(await service.VerifyPasswordAsync(user.Id, "Strong pass 2026!", TestContext.Current.CancellationToken));
        Assert.Equal(0, user.FailedLoginCount);
        Assert.Null(user.LockoutEnd);
    }

    [Fact]
    public async Task ExpiredLockStartsNewFailureWindowAndPolicyCanDisablePasswordExpiry()
    {
        var clock = new UserTestClock();
        var options = new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString).Options;
        await using var db = new MyErpDbContext(options, [new SystemOrganizationDbModel()], new FixedCompany(null));
        var service = new SystemUserService(db, new PasswordHasher<SystemUser>(), clock, new PasswordPolicy { ExpirationDays = 1 });
        var user = await service.CreateAsync("tester" + Guid.NewGuid().ToString("N"), "Strong pass 2026!", TestContext.Current.CancellationToken);
        for (var i = 0; i < 5; i++)
        {
            await service.VerifyPasswordAsync(user.Id, "wrong", TestContext.Current.CancellationToken);
        }
        clock.Advance(TimeSpan.FromMinutes(15));
        Assert.False(await service.VerifyPasswordAsync(user.Id, "wrong", TestContext.Current.CancellationToken));
        Assert.Equal(1, user.FailedLoginCount);
        Assert.Null(user.LockoutEnd);
        clock.Advance(TimeSpan.FromDays(1));
        Assert.True(service.IsPasswordExpired(user));
        Assert.False(new SystemUserService(db, new PasswordHasher<SystemUser>(), clock, new PasswordPolicy { ExpirationDays = 0 }).IsPasswordExpired(user));
    }

    [Fact]
    public async Task UsernameRemainsUniqueAfterSoftDeletionAndConcurrentFailuresAreCounted()
    {
        var clock = new UserTestClock();
        var options = new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString).Options;
        var username = "tester" + Guid.NewGuid().ToString("N");
        await using var setup = new MyErpDbContext(options, [new SystemOrganizationDbModel()], new FixedCompany(null));
        var service = new SystemUserService(setup, new PasswordHasher<SystemUser>(), clock, new PasswordPolicy());
        var user = await service.CreateAsync(username, "Strong pass 2026!", TestContext.Current.CancellationToken);
        user.IsDeleted = true;
        await setup.SaveChangesAsync(TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<ConflictException>(() => service.CreateAsync(username.ToUpperInvariant(), "Strong pass 2026!", TestContext.Current.CancellationToken));
        user.IsDeleted = false;
        await setup.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using var firstDb = new MyErpDbContext(options, [new SystemOrganizationDbModel()], new FixedCompany(null));
        await using var secondDb = new MyErpDbContext(options, [new SystemOrganizationDbModel()], new FixedCompany(null));
        var first = new SystemUserService(firstDb, new PasswordHasher<SystemUser>(), clock, new PasswordPolicy());
        var second = new SystemUserService(secondDb, new PasswordHasher<SystemUser>(), clock, new PasswordPolicy());
        await Task.WhenAll(
            first.VerifyPasswordAsync(user.Id, "wrong", TestContext.Current.CancellationToken),
            second.VerifyPasswordAsync(user.Id, "wrong", TestContext.Current.CancellationToken));
        await setup.Entry(user).ReloadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, user.FailedLoginCount);
    }

    [Fact]
    public void InvalidPasswordPolicyConfigurationIsRejectedAtStartup()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Security:PasswordPolicy:MinimumLength"] = "4" })
            .Build();
        Assert.Throws<InvalidOperationException>(() =>
        {
            new MyERP.Modules.System.SystemModule().AddServices(new ServiceCollection(), configuration);
        });
        Assert.Throws<InvalidOperationException>(() => new PasswordPolicy { MaximumLength = 10 }.ValidateConfiguration());
        Assert.Throws<InvalidOperationException>(() => new PasswordPolicy { ExpirationDays = -1 }.ValidateConfiguration());
    }

    private sealed class UserTestClock : IClock
    {
        public DateTimeOffset Now { get; private set; } = new(2026, 10, 10, 18, 0, 0, TimeSpan.FromHours(8));
        public DateTime LocalNow => Now.DateTime;
        public DateOnly Today => DateOnly.FromDateTime(LocalNow);
        public TimeSpan Offset => TimeSpan.FromHours(8);
        public void Advance(TimeSpan duration) => Now = Now.Add(duration);
    }
}
