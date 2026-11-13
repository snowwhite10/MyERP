using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using MyERP.Modules.Finance.Application;
using MyERP.Modules.Finance.Domain;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Finance.Infrastructure;

/// <summary>
/// WU-FI-004a3x1b2b1: SQL side of <see cref="IGlPostingService"/>. Every attempt (and every committed lookup) runs in
/// its own DI scope, so a failed attempt never leaves tracked rows or a broken connection for the next one; MD's
/// <see cref="IChartOfAccounts"/> / <see cref="ICurrencyLookup"/> resolved from that scope share its DbContext and
/// therefore its transaction. It only performs I/O: order, phases, retries and plan rebuilds belong to
/// <see cref="GlPostingAttemptRunner"/>, authority assembly to <see cref="GlPostingAuthorityRules"/>.
/// </summary>
public sealed class GlPostingTransactionPort(IServiceScopeFactory scopes) : IGlPostingTransactionPort
{
    // Only the state reader is kept: the runner owns (and disposes) each session.
    private Func<ConnectionState>? _latestState;

    public async Task<IGlPostingAttemptSession?> BeginAsync(GlPostingRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        // A failed Begin must not inherit the previous attempt's (possibly broken) connection state.
        _latestState = null;
        var scope = scopes.CreateAsyncScope();
        try
        {
            var db = scope.ServiceProvider.GetRequiredService<MyErpDbContext>();
            if (request.CompanyId <= 0 || db.CurrentCompanyId != request.CompanyId
                || request.Source?.CompanyId != request.CompanyId)
            {
                await scope.DisposeAsync();
                return null;
            }

            var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            var session = new GlPostingAttemptSession(scope.ServiceProvider, db, transaction, request, scope);
            _latestState = () => session.ConnectionState;
            return session;
        }
        catch
        {
            await scope.DisposeAsync();
            throw;
        }
    }

    public async Task<GlCommittedLookup?> LookupCommittedAsync(GlPostingSource source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MyErpDbContext>();
        if (source.CompanyId <= 0 || db.CurrentCompanyId != source.CompanyId)
            return null;

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var row = await GlPostingSourceRows.ReadAsync(db, source, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        if (row is null)
            return new(true, null);
        if (row.CompanyId != source.CompanyId || row.JournalId is not > 0)
            return null;
        return new(true, new(new(row.CompanyId, row.SourceModule, row.SourceType, row.SourceId, row.SourceVersion,
            row.Fingerprint), row.JournalId.Value));
    }

    public int? SqlErrorNumber(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return GlPostingWriter.SqlErrorNumber(exception);
    }

    /// <summary>
    /// The runner asks while the failed attempt's session is still open (before disposal), so its live connection
    /// state is used; without a session only the exception chain decides.
    /// </summary>
    public bool IsConnectionLost(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return GlPostingConnectionRules.IsConnectionLost(exception, _latestState?.Invoke() ?? ConnectionState.Open);
    }
}

/// <summary>Reads the committed <c>gl_source_key</c> row of a permanent source identity with its journal ID.</summary>
internal static class GlPostingSourceRows
{
    public static Task<GlCommittedSourceRow?> ReadAsync(MyErpDbContext db, GlPostingSource source,
        CancellationToken cancellationToken)
    {
        var companyId = source.CompanyId;
        var module = source.Module;
        var type = source.SourceType;
        var sourceId = source.SourceId;
        var journals = db.Set<GlJournal>().AsNoTracking();
        return db.Set<GlSourceKey>().AsNoTracking()
            .Where(k => k.CompanyId == companyId && k.SourceModule == module && k.SourceType == type
                && k.SourceId == sourceId)
            .Select(k => new GlCommittedSourceRow(k.CompanyId, k.SourceModule, k.SourceType, k.SourceId,
                k.SourceVersion, k.Fingerprint,
                journals.Where(j => j.CompanyId == k.CompanyId && j.SourceKeyId == k.Id)
                    .Select(j => (long?)j.Id).FirstOrDefault()))
            .SingleOrDefaultAsync(cancellationToken);
    }
}

/// <summary>
/// One ReadCommitted posting transaction. Lock and write are delegated to <see cref="GlPostingWriter"/> (which rolls
/// back on failure). With an owned scope (<see cref="GlPostingTransactionPort"/>) it owns the transaction: commit
/// commits and disposing without a commit rolls back. Without one (<see cref="GlJoinedPostingPort"/>, ADR-107) it
/// only joins the caller's transaction: commit is a no-op and disposal leaves the transaction and scope to the caller.
/// </summary>
internal sealed class GlPostingAttemptSession(IServiceProvider services, MyErpDbContext db,
    IDbContextTransaction transaction, GlPostingRequest request, AsyncServiceScope? ownedScope) : IGlPostingAttemptSession
{
    private readonly GlPostingWriter _writer = services.GetRequiredService<GlPostingWriter>();
    private bool _committed;
    private bool _disposed;
    private ConnectionState _finalState = ConnectionState.Open;

    /// <summary>Live connection state while open; the state captured at disposal afterwards.</summary>
    public ConnectionState ConnectionState => _disposed ? _finalState : CurrentState();

    public Task LockAsync(CancellationToken cancellationToken) =>
        _writer.LockAsync(request.CompanyId, request.Source, cancellationToken: cancellationToken);

    public async Task<GlPostingAuthority?> ReadAuthorityAsync(CancellationToken cancellationToken)
    {
        var companyId = request.CompanyId;
        var mapping = GlPostingMappingVersionRules.Select(
            services.GetServices<IGlPostingMappingVersionSource>(), request.Source);
        if (mapping is null)
            return null;

        var committed = await GlPostingSourceRows.ReadAsync(db, request.Source, cancellationToken);
        var baseCurrency = (await services.GetRequiredService<ICurrencyLookup>()
            .GetBaseCurrencyAsync(cancellationToken))?.Code;
        var periods = await db.Set<GlPeriod>().AsNoTracking()
            .Where(p => p.CompanyId == companyId)
            .OrderBy(p => p.StartDate).ThenBy(p => p.Id)
            .Select(p => new GlPeriodWindow(p.Id, p.CompanyId, p.StartDate, p.EndDate, p.Status == GlPeriodStatus.Open))
            .ToListAsync(cancellationToken);
        var mappingVersion = await mapping.ReadCurrentVersionAsync(companyId, cancellationToken);

        var chart = services.GetRequiredService<IChartOfAccounts>();
        var accounts = new Dictionary<string, AccountInfo?>(StringComparer.Ordinal);
        foreach (var code in GlPostingAuthorityRules.AccountCodesToRead(request.Draft))
            accounts[code] = await chart.FindAsync(code, cancellationToken);

        return GlPostingAuthorityRules.Assemble(companyId, committed, baseCurrency, periods, mappingVersion,
            request.Draft, accounts);
    }

    public async Task<long> WriteAsync(GlPostingWritePlan plan, CancellationToken cancellationToken) =>
        (await _writer.WriteAsync(plan, cancellationToken)).JournalId;

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        // Joined: durability belongs to the caller's commit (ADR-107).
        if (ownedScope is null)
            return;
        await transaction.CommitAsync(cancellationToken);
        _committed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _finalState = CurrentState();
        _disposed = true;
        if (ownedScope is not { } scope)
            return;
        try
        {
            if (!_committed && _finalState == ConnectionState.Open && transaction.GetDbTransaction().Connection is not null)
                await transaction.RollbackAsync(CancellationToken.None);
        }
        catch (Exception rollback) when (rollback is DbException or InvalidOperationException)
        {
            // A broken connection has already discarded the uncommitted work on the server.
        }
        finally
        {
            await transaction.DisposeAsync();
            await scope.DisposeAsync();
        }
    }

    private ConnectionState CurrentState()
    {
        try
        {
            return db.Database.GetDbConnection().State;
        }
        catch (ObjectDisposedException)
        {
            return ConnectionState.Closed;
        }
    }
}
