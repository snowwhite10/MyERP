using MyERP.Modules.Finance.Domain;
using MyERP.Shared.Security;

namespace MyERP.Modules.Finance.Application;

/// <summary>
/// The only way FI-owned source adapters write the general ledger (MASTER_PROMPT §5, BR-FI-027/028). The outcome is
/// <see cref="GlPostingOutcomeKind.Posted"/> / <c>changed=true</c> only after this call's own commit.
/// </summary>
public interface IGlPostingService
{
    Task<GlPostingOutcome> PostAsync(GlPostingRequest? request, CancellationToken cancellationToken = default);
}

/// <summary>
/// WU-FI-004a3x1b2a: composes the posting path. A well-formed request for a company other than the current one (or
/// with no company context) is 404 <c>SOURCE_NOT_FOUND</c> before any transaction; everything else runs the single
/// <see cref="GlPostingAttemptRunner"/> order (input 400, locks, authority, decision, write, commit, retry budget)
/// over the FI infrastructure port. The service itself has no I/O and never decides outcomes.
/// </summary>
public sealed class GlPostingService : IGlPostingService
{
    private readonly IGlPostingTransactionPort _port;
    private readonly ICurrentCompany _company;

    public GlPostingService(IGlPostingTransactionPort port, ICurrentCompany company)
    {
        ArgumentNullException.ThrowIfNull(port);
        ArgumentNullException.ThrowIfNull(company);
        _port = port;
        _company = company;
    }

    public Task<GlPostingOutcome> PostAsync(GlPostingRequest? request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (IsWellFormed(request) && _company.CompanyId != request!.CompanyId)
        {
            return Task.FromResult(new GlPostingOutcome(GlPostingOutcomeKind.Rejected, false, 404,
                GlPostingErrors.SourceNotFound));
        }
        return GlPostingAttemptRunner.PostAsync(request, _port, cancellationToken: cancellationToken);
    }

    /// <summary>Malformed input keeps the runner's 400 regardless of company context (no scope leak either way).</summary>
    internal static bool IsWellFormed(GlPostingRequest? request) =>
        request is { CompanyId: > 0, Source: { } source }
        && GlSourceReplayRules.Decide(source, null).Status == GlSourceReplayStatus.New;
}

/// <summary>
/// WU-FI-002a2a (ADR-107): <see cref="IGlPostingService"/>'s posting path joined to the caller's open SQL transaction
/// on the same scoped DbContext, for FI source adapters that must keep GL, credit, source and receivable writes atomic
/// (BR-FI-013). It runs one attempt and never commits. Posted / <c>changed=true</c> means the rows are written and
/// become durable only when the caller commits. Rejected and Unchanged leave the caller's transaction open (the caller
/// decides); every Fault has already rolled back the caller's whole transaction. The caller's DbContext must have no
/// unsaved changes. Calling without an open transaction is a programming error (<see cref="InvalidOperationException"/>).
/// </summary>
public interface IGlJoinedPostingService
{
    Task<GlPostingOutcome> PostInCurrentTransactionAsync(GlPostingRequest? request,
        CancellationToken cancellationToken = default);
}
