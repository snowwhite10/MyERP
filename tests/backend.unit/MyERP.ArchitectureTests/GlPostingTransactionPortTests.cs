using System.Net.Sockets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyERP.Modules.Finance;
using MyERP.Modules.Finance.Application;
using MyERP.Modules.Finance.Domain;
using MyERP.Modules.Finance.Infrastructure;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;

namespace MyERP.ArchitectureTests;

/// <summary>
/// WU-FI-004a3x1b2b1: offline guarantees of the GL posting SQL port (FI-GL-PORT-001–007) — DI composition, the
/// company scope checked before any SQL, error/connection delegation and mapping-version source selection (ADR-103).
/// Real SQL behaviour (locks, authority reads, commit, rollback, replay, concurrency) is WU-FI-004a3x1b2b2.
/// </summary>
public sealed class GlPostingTransactionPortTests
{
    // Port 1 is never listening: any accidental connection attempt fails fast instead of passing silently.
    private const string UnreachableSql = "Server=127.0.0.1,1;Database=none;Connect Timeout=1;Encrypt=False";

    private static GlPostingSource Source(long companyId) => new(companyId, "SD", "Invoice", 12, 1, "sha256:abc");

    private static GlPostingRequest Request(long companyId, long sourceCompanyId) =>
        new(companyId, Source(sourceCompanyId), new(2026, 11, 6), "TWD", null, 1);

    private sealed class Company(long? companyId) : ICurrentCompany
    {
        public long? CompanyId { get; } = companyId;
    }

    private sealed class Mapping(string module, string type) : IGlPostingMappingVersionSource
    {
        public string SourceModule { get; } = module;
        public string SourceType { get; } = type;
        public Task<int> ReadCurrentVersionAsync(long companyId, CancellationToken cancellationToken) => Task.FromResult(1);
    }

    /// <summary>FinanceModule plus an offline DbContext; <paramref name="contexts"/> collects every scope's context.</summary>
    private static ServiceProvider Provider(long? companyId, List<MyErpDbContext>? contexts = null)
    {
        var services = new ServiceCollection();
        new FinanceModule().AddServices(services, new ConfigurationBuilder().Build());
        services.AddScoped<ICurrentCompany>(_ => new Company(companyId));
        services.AddScoped(sp =>
        {
            var db = new MyErpDbContext(new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(UnreachableSql).Options,
                sp.GetServices<IModuleDbModel>(), sp.GetRequiredService<ICurrentCompany>());
            contexts?.Add(db);
            return db;
        });
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    [Fact]
    public async Task ModuleRegistersScopedPortAndPostingServiceThatResolveTogether()
    {
        // FI-GL-PORT-001
        var services = new ServiceCollection();
        new FinanceModule().AddServices(services, new ConfigurationBuilder().Build());
        services.Should().ContainSingle(x => x.ServiceType == typeof(IGlPostingTransactionPort))
            .Which.Should().Match<ServiceDescriptor>(x => x.ImplementationType == typeof(GlPostingTransactionPort)
                && x.Lifetime == ServiceLifetime.Scoped);
        services.Should().ContainSingle(x => x.ServiceType == typeof(IGlPostingService))
            .Which.Should().Match<ServiceDescriptor>(x => x.ImplementationType == typeof(GlPostingService)
                && x.Lifetime == ServiceLifetime.Scoped);
        // No business adapter maps any source yet (later FI source units add theirs). The only mapping source is the
        // Development demo seed (ADR-105), which returns version 0 outside Development and so fails closed.
        services.Should().ContainSingle(x => x.ServiceType == typeof(IGlPostingMappingVersionSource))
            .Which.Should().Match<ServiceDescriptor>(x =>
                x.ImplementationType == typeof(MyERP.Modules.Finance.Infrastructure.Seeding.GlDemoSeedMapping)
                && x.Lifetime == ServiceLifetime.Scoped);

        await using var provider = Provider(3);
        await using var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IGlPostingService>().Should().BeOfType<GlPostingService>();
        scope.ServiceProvider.GetRequiredService<IGlPostingTransactionPort>().Should().BeOfType<GlPostingTransactionPort>();
    }

    [Theory]
    [InlineData(3L, 4L, 4L)] // request for another company than the attempt scope
    [InlineData(3L, 3L, 4L)] // source of another company than the request
    [InlineData(null, 3L, 3L)] // no company context
    [InlineData(3L, 0L, 0L)] // non-positive company
    public async Task BeginReturnsNullForOutOfScopeRequestsWithoutSqlAndReleasesTheAttemptScope(
        long? current, long requestCompany, long sourceCompany)
    {
        // FI-GL-PORT-002: checked before BeginTransaction, so the unreachable server is never contacted.
        var contexts = new List<MyErpDbContext>();
        await using var provider = Provider(current, contexts);
        await using var scope = provider.CreateAsyncScope();
        var port = scope.ServiceProvider.GetRequiredService<IGlPostingTransactionPort>();

        var session = await port.BeginAsync(Request(requestCompany, sourceCompany), TestContext.Current.CancellationToken);

        session.Should().BeNull();
        contexts.Should().ContainSingle();
        // The attempt's own scope (and its DbContext) is disposed, not left for the request scope.
        FluentActions.Invoking(() => contexts[0].Model).Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public async Task BeginAndLookupRejectNullArgumentsAndHonourCancellationBeforeOpeningAScope()
    {
        // FI-GL-PORT-003
        var contexts = new List<MyErpDbContext>();
        await using var provider = Provider(3, contexts);
        await using var scope = provider.CreateAsyncScope();
        var port = scope.ServiceProvider.GetRequiredService<IGlPostingTransactionPort>();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await FluentActions.Awaiting(() => port.BeginAsync(null!, CancellationToken.None))
            .Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Awaiting(() => port.LookupCommittedAsync(null!, CancellationToken.None))
            .Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Awaiting(() => port.BeginAsync(Request(3, 3), cancelled.Token))
            .Should().ThrowAsync<OperationCanceledException>();
        await FluentActions.Awaiting(() => port.LookupCommittedAsync(Source(3), cancelled.Token))
            .Should().ThrowAsync<OperationCanceledException>();
        contexts.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(4L)]
    public async Task LookupOutsideTheCurrentCompanyIsMalformedWithoutSql(long? current)
    {
        // FI-GL-PORT-004: null lets the retry loop report 500 INVALID_DECISION instead of a foreign-company read.
        var contexts = new List<MyErpDbContext>();
        await using var provider = Provider(current, contexts);
        await using var scope = provider.CreateAsyncScope();
        var port = scope.ServiceProvider.GetRequiredService<IGlPostingTransactionPort>();

        var lookup = await port.LookupCommittedAsync(Source(3), TestContext.Current.CancellationToken);

        lookup.Should().BeNull();
        contexts.Should().ContainSingle();
        FluentActions.Invoking(() => contexts[0].Model).Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public async Task ErrorNumberAndConnectionLossDelegateToTheSharedRules()
    {
        // FI-GL-PORT-005: without a live session the connection state is unknown, so only the exception decides.
        await using var provider = Provider(3);
        await using var scope = provider.CreateAsyncScope();
        var port = scope.ServiceProvider.GetRequiredService<IGlPostingTransactionPort>();

        port.SqlErrorNumber(new InvalidOperationException("x", new TimeoutException())).Should().BeNull();
        port.IsConnectionLost(new InvalidOperationException("x")).Should().BeFalse();
        port.IsConnectionLost(new InvalidOperationException("x", new IOException("reset"))).Should().BeTrue();
        port.IsConnectionLost(new SocketException()).Should().BeTrue();
        FluentActions.Invoking(() => port.SqlErrorNumber(null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => port.IsConnectionLost(null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void MappingVersionSourceIsTheSingleOrdinalMatchOtherwiseNull()
    {
        // FI-GL-PORT-006 (ADR-103)
        var invoice = new Mapping("SD", "Invoice");
        var receipt = new Mapping("AR", "Collection");

        GlPostingMappingVersionRules.Select([receipt, null, invoice], Source(3)).Should().BeSameAs(invoice);
        GlPostingMappingVersionRules.Select([receipt], Source(3)).Should().BeNull();
        GlPostingMappingVersionRules.Select([], Source(3)).Should().BeNull();
        GlPostingMappingVersionRules.Select([new Mapping("sd", "Invoice"), new Mapping("SD", "invoice")], Source(3))
            .Should().BeNull();
        GlPostingMappingVersionRules.Select([invoice, new Mapping("SD", "Invoice")], Source(3)).Should().BeNull();
        GlPostingMappingVersionRules.Select(null, Source(3)).Should().BeNull();
        GlPostingMappingVersionRules.Select([invoice], null).Should().BeNull();
    }

    [Fact]
    public async Task PostingWithoutAnySqlStillAppliesCompanyScopeThroughTheRegisteredPort()
    {
        // FI-GL-PORT-007: the DI-composed service rejects a foreign-company request 404 before the port opens a scope.
        var contexts = new List<MyErpDbContext>();
        await using var provider = Provider(3, contexts);
        await using var scope = provider.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IGlPostingService>();

        var outcome = await service.PostAsync(Request(4, 4), TestContext.Current.CancellationToken);

        outcome.Should().Be(new GlPostingOutcome(GlPostingOutcomeKind.Rejected, false, 404, GlPostingErrors.SourceNotFound));
        contexts.Should().BeEmpty();
    }
}
