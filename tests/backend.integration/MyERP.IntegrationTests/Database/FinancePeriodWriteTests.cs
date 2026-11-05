using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Finance.Domain;
using MyERP.Modules.Finance.Infrastructure;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.IntegrationTests.Database;

public sealed class FinancePeriodWriteTests(TestDatabase database)
{
    private static readonly FixedClock Clock = new(new DateTime(2026, 1, 1));
    private static readonly FixedUser User = new(7);

    private MyErpDbContext Context(long companyId)
    {
        var company = new FixedCompany(companyId);
        var stamper = new AuditStamper(Clock, User, company);
        return new MyErpDbContext(
            new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString)
                .AddInterceptors(new AuditSaveChangesInterceptor(stamper)).Options,
            [new GlPeriodDbModel()], company);
    }

    [Fact]
    public async Task CreateRejectsSharedEndpointAndAcceptsAdjacentDay()
    {
        var companyId = Random.Shared.NextInt64(88000000, 89000000);
        var token = TestContext.Current.CancellationToken;
        await using var first = Context(companyId);
        await using var second = Context(companyId);
        var left = await new GlPeriodWriteService(first, Clock, User).CreateAsync(companyId,
            new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), true, token);
        Assert.Equal(1, left.Version);
        var conflict = await Assert.ThrowsAsync<GlPeriodWriteException>(() =>
            new GlPeriodWriteService(second, Clock, User).CreateAsync(companyId,
                new DateOnly(2026, 1, 31), new DateOnly(2026, 2, 28), false, token));
        Assert.Equal(GlPeriodWriteStatus.Overlap, conflict.Status);
        var right = await new GlPeriodWriteService(second, Clock, User).CreateAsync(companyId,
            new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28), false, token);
        Assert.Equal(GlPeriodStatus.Closed, right.Status);
        Assert.Equal(2, await second.Set<GlPeriod>().CountAsync(token));
    }

    [Fact]
    public async Task RejectedCreateWritesNothingAndCannotUseOtherCompany()
    {
        var companyId = Random.Shared.NextInt64(89000000, 90000000);
        var token = TestContext.Current.CancellationToken;
        await using var db = Context(companyId);
        var service = new GlPeriodWriteService(db, Clock, User);
        var range = await Assert.ThrowsAsync<GlPeriodWriteException>(() => service.CreateAsync(companyId,
            new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 1), true, token));
        Assert.Equal(GlPeriodWriteStatus.InvalidRange, range.Status);
        var company = await Assert.ThrowsAsync<GlPeriodWriteException>(() => service.CreateAsync(companyId + 1,
            new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 2), true, token));
        Assert.Equal(GlPeriodWriteStatus.IdentityMismatch, company.Status);
        Assert.Equal(0, await db.Set<GlPeriod>().CountAsync(token));
    }

    [Fact]
    public async Task SimultaneousCreatesSerializeByCompany()
    {
        var companyId = Random.Shared.NextInt64(90000000, 91000000);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<GlPeriodWriteStatus> Create(long company)
        {
            await gate.Task;
            await using var db = Context(company);
            try
            {
                await new GlPeriodWriteService(db, Clock, User).CreateAsync(company,
                    new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), true,
                    TestContext.Current.CancellationToken);
                return GlPeriodWriteStatus.Allowed;
            }
            catch (GlPeriodWriteException error)
            {
                return error.Status;
            }
        }

        var sameA = Create(companyId);
        var sameB = Create(companyId);
        gate.SetResult();
        var sameResults = await Task.WhenAll(sameA, sameB)
            .WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
        Assert.Equal([GlPeriodWriteStatus.Allowed, GlPeriodWriteStatus.Overlap],
            sameResults.OrderBy(status => status).ToArray());
        await using var verificationDb = Context(companyId);
        Assert.Equal(1, await verificationDb.Set<GlPeriod>().CountAsync(TestContext.Current.CancellationToken));

        // Hold company A's period lock open; company B must still create without waiting for it.
        var token = TestContext.Current.CancellationToken;
        var heldCompanyId = companyId + 1;
        var otherCompanyId = companyId + 2;
        await using var holder = Context(heldCompanyId);
        await using var held = await holder.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, token);
        var resource = $"gl_period:{heldCompanyId}";
        await holder.Database.ExecuteSqlInterpolatedAsync(
            $"EXEC sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 0;",
            token);
        var other = await Create(otherCompanyId).WaitAsync(TimeSpan.FromSeconds(5), token);
        Assert.Equal(GlPeriodWriteStatus.Allowed, other);
        await held.RollbackAsync(token);
        await using var otherDb = Context(otherCompanyId);
        Assert.Equal(1, await otherDb.Set<GlPeriod>().CountAsync(token));
        Assert.Equal(GlPeriodWriteStatus.Allowed, await Create(heldCompanyId).WaitAsync(TimeSpan.FromSeconds(20), token));
    }
}
