using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MyERP.Modules.Finance.Application;
using MyERP.Modules.Finance.Domain;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;

namespace MyERP.Modules.Finance.Infrastructure;

/// <summary>
/// WU-FI-002a2a (ADR-107): joins the scoped <see cref="MyErpDbContext"/>'s current transaction and runs
/// <see cref="GlPostingAttemptRunner.PostJoinedAsync"/> once. A Fault or an exception from the posting path rolls the
/// caller's transaction back (if the writer has not already), so no caller can commit half of a failed posting.
/// Rejected/Unchanged/Posted leave the transaction to the caller.
/// </summary>
internal sealed class GlJoinedPostingService(MyErpDbContext db, IServiceProvider services, ICurrentCompany company)
    : IGlJoinedPostingService
{
    public async Task<GlPostingOutcome> PostInCurrentTransactionAsync(GlPostingRequest? request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var transaction = db.Database.CurrentTransaction
            ?? throw new InvalidOperationException("Joined GL posting requires the caller's open SQL transaction.");
        if (GlPostingService.IsWellFormed(request) && company.CompanyId != request!.CompanyId)
            return new(GlPostingOutcomeKind.Rejected, false, 404, GlPostingErrors.SourceNotFound);

        GlPostingOutcome outcome;
        try
        {
            outcome = await GlPostingAttemptRunner.PostJoinedAsync(request,
                new GlJoinedPostingPort(services, db, transaction), cancellationToken);
        }
        catch
        {
            await RollBackQuietlyAsync();
            throw;
        }

        if (outcome.Kind == GlPostingOutcomeKind.Fault)
            await RollBackQuietlyAsync();
        return outcome;
    }

    private async Task RollBackQuietlyAsync()
    {
        if (db.Database.CurrentTransaction is not { } current)
            return;
        try
        {
            await current.RollbackAsync(CancellationToken.None);
        }
        catch (Exception rollback) when (rollback is DbException or InvalidOperationException)
        {
            // A broken connection has already discarded the caller's uncommitted work on the server.
        }
    }
}

/// <summary>
/// Port over the caller's transaction (ADR-107): every session joins it and never commits, rolls back or disposes it.
/// The committed lookup is not part of joined posting (a failure is reported, never resolved from committed state).
/// </summary>
internal sealed class GlJoinedPostingPort(IServiceProvider services, MyErpDbContext db, IDbContextTransaction transaction)
    : IGlPostingTransactionPort
{
    public Task<IGlPostingAttemptSession?> BeginAsync(GlPostingRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.CompanyId <= 0 || db.CurrentCompanyId != request.CompanyId
            || request.Source?.CompanyId != request.CompanyId || db.Database.CurrentTransaction != transaction)
            return Task.FromResult<IGlPostingAttemptSession?>(null);
        return Task.FromResult<IGlPostingAttemptSession?>(
            new GlPostingAttemptSession(services, db, transaction, request, ownedScope: null));
    }

    public Task<GlCommittedLookup?> LookupCommittedAsync(GlPostingSource source, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Joined GL posting never resolves a failure from committed state.");

    public int? SqlErrorNumber(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return GlPostingWriter.SqlErrorNumber(exception);
    }

    public bool IsConnectionLost(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ConnectionState state;
        try
        {
            state = db.Database.GetDbConnection().State;
        }
        catch (ObjectDisposedException)
        {
            state = ConnectionState.Closed;
        }
        return GlPostingConnectionRules.IsConnectionLost(exception, state);
    }
}
