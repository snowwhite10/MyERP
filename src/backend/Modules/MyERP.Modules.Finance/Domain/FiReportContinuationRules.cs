namespace MyERP.Modules.Finance.Domain;

/// <summary>
/// Authority the caller resolved for the actor (from SYS through its contract): the granting role IDs and the partner
/// scope. Only used to recompute the ADR-097 digest; it is not an authorization decision.
/// </summary>
public sealed record FiReportActorScope(long ActorUserId, IReadOnlyCollection<long>? RoleIds,
    FiReportPartnerScope? PartnerScope);

/// <summary>
/// A continuation request: the token and the filters the client repeats (<see cref="PartnerId"/>, dates,
/// <see cref="KindFilter"/>, <see cref="Currency"/>, <see cref="PageSize"/>) plus the server-wired company and report
/// kind. The actor scope digest is never taken from the client; it is recomputed on every call.
/// </summary>
public sealed record FiReportContinuationRequest(
    string? Token,
    long CompanyId,
    FiReportKind Kind,
    long? PartnerId,
    DateOnly? AsOf,
    DateOnly? From,
    DateOnly? Through,
    OtherIncomeExpenseKind? KindFilter,
    string? Currency,
    int PageSize);

/// <summary>Either a page (with <see cref="NextToken"/> when more rows follow) or a problem — never both.</summary>
public sealed record FiReportContinuationResult<TPage>(TPage? Page, string? NextToken, FiReportProblem? Problem)
    where TPage : class
{
    public bool IsSuccess => Problem is null;
}

/// <summary>Manifest-verified CSV export (no BOM) with its row count, or a problem — never both, never partial.</summary>
public sealed record FiReportExportResult(string? Content, int RowCount, FiReportProblem? Problem)
{
    public bool IsSuccess => Problem is null;
}

/// <summary>
/// Pure FI report continuation coordinator (BR-FI-130／131, FI-RPT-006, ADR-098, WU-BI-001a8). One call chains the
/// existing rules in a fixed order so the WU-BI-001b endpoint only wires I/O:
/// <list type="number">
/// <item>recompute the actor scope digest (ADR-097) — invalid input is a 500 wiring fault;</item>
/// <item>validate the token against the repeated request binding (ADR-093) without expiry, so a rejected, tampered
/// or scope-changed token never causes a manifest load;</item>
/// <item>load the manifest named by the signed token and validate again with its expiry (gone／expired → 409);</item>
/// <item>check the stored manifest belongs to this binding and cutoff (otherwise 500), only then re-read and page it
/// (ADR-094; disagreement → 500, no partial page);</item>
/// <item>issue the next-page token only when the page has a next page.</item>
/// </list>
/// Every failure is mapped by <see cref="FiReportProblemRules"/> (ADR-096). The callbacks are the only I/O; exceptions
/// they throw propagate to the endpoint's unknown-outcome 500. Authorization (VIEW permission, partner filter within
/// scope, 401／403／404) is not decided here and stays with the endpoint and the FI read contract.
/// </summary>
public static class FiReportContinuationRules
{
    public static FiReportContinuationResult<ArApDetailPage> ContinueArAp(FiReportContinuationRequest? request,
        FiReportActorScope? actor, byte[]? key, DateTimeOffset now, Func<long, FiReportManifest?>? loadManifest,
        Func<FiReportManifest, ArApDetailReport?>? reread) =>
        Continue(request, actor, key, now, loadManifest, reread is null, arAp: true,
            (m, n) => FiReportManifestRules.PageArAp(m, reread!(m), n), p => p.HasNext);

    public static FiReportContinuationResult<OtherIncomeExpensePage> ContinueOtherIncomeExpense(
        FiReportContinuationRequest? request, FiReportActorScope? actor, byte[]? key, DateTimeOffset now,
        Func<long, FiReportManifest?>? loadManifest, Func<FiReportManifest, OtherIncomeExpenseReport?>? reread) =>
        Continue(request, actor, key, now, loadManifest, reread is null, arAp: false,
            (m, n) => FiReportManifestRules.PageOtherIncomeExpense(m, reread!(m), n), p => p.HasNext);

    private static FiReportContinuationResult<TPage> Continue<TPage>(FiReportContinuationRequest? request,
        FiReportActorScope? actor, byte[]? key, DateTimeOffset now, Func<long, FiReportManifest?>? loadManifest,
        bool rereadMissing, bool arAp, Func<FiReportManifest, int, FiReportManifestPageResult<TPage>> page,
        Func<TPage, bool> hasNext) where TPage : class
    {
        var missing = new List<string>();
        if (request is null) missing.Add("continuation.request:Required");
        if (actor is null) missing.Add("continuation.actor:Required");
        if (loadManifest is null) missing.Add("continuation.loadManifest:Required");
        if (rereadMissing) missing.Add("continuation.reread:Required");
        if (missing.Count > 0) return Internal<TPage>(missing);

        if (Enum.IsDefined(request!.Kind) && IsArAp(request.Kind) != arAp)
            return Internal<TPage>(["request.kind:NotAllowed"]);

        var opened = Open(request, actor!, key, now, loadManifest!);
        if (opened.Problem is not null) return Fail<TPage>(opened.Problem);
        var (manifest, binding, position) = (opened.Manifest!, opened.Binding!, opened.Position!);

        var result = page(manifest, position.PageNumber);
        if (result.Status != FiReportManifestStatus.Ok || result.Page is null)
            return Fail<TPage>(FiReportProblemRules.Map(result.Errors.Count > 0
                ? result.Errors
                : [new FiReportManifestFieldError("reread", "page", FiReportManifestError.Inconsistent)]));

        if (!hasNext(result.Page)) return new(result.Page, null, null);

        var next = FiReportCursorTokenRules.Issue(binding,
            new FiReportCursorPosition(position.CutoffSequence, position.ManifestId, position.PageNumber + 1), key);
        return next.Status == FiReportCursorIssueStatus.Issued
            ? new(result.Page, next.Token, null)
            : Fail<TPage>(FiReportProblemRules.Map(next));
    }

    /// <summary>
    /// First page (ADR-099, WU-BI-001a9): the request carries no token. Server-wired faults (null inputs, wrong report
    /// family, a token present, invalid actor scope, TTL outside 1–60 minutes, short key, default clock, company ≤ 0)
    /// → 500 and client filter errors → 400, all before <paramref name="read"/>. Then read → ADR-094 create (too many
    /// rows 422, disagreement 500) → <paramref name="saveManifest"/> (null／≤ 0 ID → 500, no page) → page 1 → a token
    /// for page 2 only when there is a next page. The manifest is saved even for a single page so export can reuse it.
    /// </summary>
    public static FiReportContinuationResult<ArApDetailPage> FirstArAp(FiReportContinuationRequest? request,
        FiReportActorScope? actor, byte[]? key, DateTimeOffset now, TimeSpan ttl,
        Func<FiReportCursorBinding, ArApDetailReport?>? read, Func<FiReportManifest, long?>? saveManifest) =>
        First(request, actor, key, now, ttl, read, saveManifest, arAp: true, FiReportManifestRules.CreateArAp,
            FiReportManifestRules.PageArAp, p => p.HasNext);

    public static FiReportContinuationResult<OtherIncomeExpensePage> FirstOtherIncomeExpense(
        FiReportContinuationRequest? request, FiReportActorScope? actor, byte[]? key, DateTimeOffset now, TimeSpan ttl,
        Func<FiReportCursorBinding, OtherIncomeExpenseReport?>? read, Func<FiReportManifest, long?>? saveManifest) =>
        First(request, actor, key, now, ttl, read, saveManifest, arAp: false,
            FiReportManifestRules.CreateOtherIncomeExpense, FiReportManifestRules.PageOtherIncomeExpense,
            p => p.HasNext);

    private static FiReportContinuationResult<TPage> First<TReport, TPage>(FiReportContinuationRequest? request,
        FiReportActorScope? actor, byte[]? key, DateTimeOffset now, TimeSpan ttl,
        Func<FiReportCursorBinding, TReport?>? read, Func<FiReportManifest, long?>? saveManifest, bool arAp,
        Func<FiReportCursorBinding?, TReport?, DateTimeOffset, TimeSpan, FiReportManifestResult> create,
        Func<FiReportManifest?, TReport?, int, FiReportManifestPageResult<TPage>> page, Func<TPage, bool> hasNext)
        where TReport : class where TPage : class
    {
        var missing = new List<string>();
        if (request is null) missing.Add("first.request:Required");
        if (actor is null) missing.Add("first.actor:Required");
        if (read is null) missing.Add("first.read:Required");
        if (saveManifest is null) missing.Add("first.saveManifest:Required");
        if (missing.Count > 0) return Internal<TPage>(missing);

        var wiring = new List<string>();
        if (Enum.IsDefined(request!.Kind) && IsArAp(request.Kind) != arAp) wiring.Add("request.kind:NotAllowed");
        if (request.Token is not null) wiring.Add("request.token:NotAllowed");
        if (ttl < FiReportManifestRules.MinTtl || ttl > FiReportManifestRules.MaxTtl)
            wiring.Add("request.ttl:OutOfRange");
        if (wiring.Count > 0) return Internal<TPage>(wiring);

        var scope = FiReportActorScopeRules.Digest(actor!.ActorUserId, request.CompanyId, request.Kind, actor.RoleIds,
            actor.PartnerScope);
        if (!scope.IsValid)
            return Internal<TPage>(scope.Errors.Select(e => $"actorScope.{e.Field}:{e.Error}").ToList());

        if (key is not { Length: >= FiReportCursorTokenRules.MinKeyLength })
            return Fail<TPage>(FiReportProblemRules.Map(new FiReportCursorIssueResult(null,
                [new FiReportCursorFieldError("key", "key", FiReportCursorError.KeyInvalid)])));

        var binding = new FiReportCursorBinding(request.CompanyId, scope.Digest, request.Kind, request.PartnerId,
            request.AsOf, request.From, request.Through, request.KindFilter, request.Currency, request.PageSize);
        var input = new List<FiReportCursorFieldError>();
        if (now == default) input.Add(new FiReportCursorFieldError("clock", "now", FiReportCursorError.Required));
        FiReportCursorTokenRules.ValidateBinding(binding, input);
        if (input.Count > 0) return Fail<TPage>(FiReportProblemRules.Map(new FiReportCursorValidationResult(null, input)));

        var report = read!(binding);
        var created = create(binding, report, now, ttl);
        if (created.Status != FiReportManifestStatus.Ok || created.Manifest is null)
            return Fail<TPage>(FiReportProblemRules.Map(created.Errors));
        var manifest = created.Manifest;

        var id = saveManifest!(manifest);
        if (id is not > 0)
            return Internal<TPage>([id is null ? "save.manifestId:Required" : "save.manifestId:OutOfRange"]);

        var first = page(manifest, report, 1);
        if (first.Status != FiReportManifestStatus.Ok || first.Page is null)
            return Fail<TPage>(FiReportProblemRules.Map(first.Errors.Count > 0
                ? first.Errors
                : [new FiReportManifestFieldError("report", "page", FiReportManifestError.Inconsistent)]));

        if (!hasNext(first.Page)) return new(first.Page, null, null);

        var next = FiReportCursorTokenRules.Issue(binding,
            new FiReportCursorPosition(manifest.CutoffSequence, id.Value, 2), key);
        return next.Status == FiReportCursorIssueStatus.Issued
            ? new(first.Page, next.Token, null)
            : Fail<TPage>(FiReportProblemRules.Map(next));
    }

    /// <summary>
    /// Single entry (ADR-100, WU-BI-001a10): the endpoint wires all four callbacks once and calls this for every page.
    /// A missing request, actor or any callback is a 500 wiring fault on either path, before any I/O. Then a null
    /// <see cref="FiReportContinuationRequest.Token"/> → <see cref="FirstArAp"/> (never loads or re-reads); any other
    /// value, including empty or blank → <see cref="ContinueArAp"/> (never reads or saves; blank is 400 token Required).
    /// </summary>
    public static FiReportContinuationResult<ArApDetailPage> ReadArAp(FiReportContinuationRequest? request,
        FiReportActorScope? actor, byte[]? key, DateTimeOffset now, TimeSpan ttl,
        Func<FiReportCursorBinding, ArApDetailReport?>? read, Func<FiReportManifest, long?>? saveManifest,
        Func<long, FiReportManifest?>? loadManifest, Func<FiReportManifest, ArApDetailReport?>? reread) =>
        Dispatch<ArApDetailPage>(request, actor, read is null, saveManifest is null, loadManifest is null,
                reread is null)
            ?? (request!.Token is null
                ? FirstArAp(request, actor, key, now, ttl, read, saveManifest)
                : ContinueArAp(request, actor, key, now, loadManifest, reread));

    public static FiReportContinuationResult<OtherIncomeExpensePage> ReadOtherIncomeExpense(
        FiReportContinuationRequest? request, FiReportActorScope? actor, byte[]? key, DateTimeOffset now, TimeSpan ttl,
        Func<FiReportCursorBinding, OtherIncomeExpenseReport?>? read, Func<FiReportManifest, long?>? saveManifest,
        Func<long, FiReportManifest?>? loadManifest, Func<FiReportManifest, OtherIncomeExpenseReport?>? reread) =>
        Dispatch<OtherIncomeExpensePage>(request, actor, read is null, saveManifest is null, loadManifest is null,
                reread is null)
            ?? (request!.Token is null
                ? FirstOtherIncomeExpense(request, actor, key, now, ttl, read, saveManifest)
                : ContinueOtherIncomeExpense(request, actor, key, now, loadManifest, reread));

    /// <summary>Path-independent wiring check: returns a 500 listing every missing input, or null to dispatch.</summary>
    private static FiReportContinuationResult<TPage>? Dispatch<TPage>(FiReportContinuationRequest? request,
        FiReportActorScope? actor, bool readMissing, bool saveMissing, bool loadMissing, bool rereadMissing)
        where TPage : class
    {
        var missing = new List<string>();
        if (request is null) missing.Add("read.request:Required");
        if (actor is null) missing.Add("read.actor:Required");
        if (readMissing) missing.Add("read.read:Required");
        if (saveMissing) missing.Add("read.saveManifest:Required");
        if (loadMissing) missing.Add("read.loadManifest:Required");
        if (rereadMissing) missing.Add("read.reread:Required");
        return missing.Count > 0 ? Internal<TPage>(missing) : null;
    }

    /// <summary>A token-verified, unexpired stored manifest of this binding and cutoff — or the problem to return.</summary>
    private sealed record Opened(FiReportManifest? Manifest, FiReportCursorBinding? Binding,
        FiReportCursorPosition? Position, FiReportProblem? Problem);

    /// <summary>
    /// Shared continuation／export front half (ADR-098／101): recompute the actor digest (invalid → 500), validate the
    /// token without expiry (failure → nothing loaded), load, validate again with the manifest expiry (gone／expired →
    /// 409), then require the stored binding and cutoff to match (otherwise 500, nothing re-read).
    /// </summary>
    private static Opened Open(FiReportContinuationRequest request, FiReportActorScope actor, byte[]? key,
        DateTimeOffset now, Func<long, FiReportManifest?> loadManifest)
    {
        var scope = FiReportActorScopeRules.Digest(actor.ActorUserId, request.CompanyId, request.Kind, actor.RoleIds,
            actor.PartnerScope);
        if (!scope.IsValid)
            return Closed(InternalProblem(scope.Errors.Select(e => $"actorScope.{e.Field}:{e.Error}").ToList()));

        var binding = new FiReportCursorBinding(request.CompanyId, scope.Digest, request.Kind, request.PartnerId,
            request.AsOf, request.From, request.Through, request.KindFilter, request.Currency, request.PageSize);

        // 1st pass: signature and binding only (no expiry known yet), so nothing is loaded for a bad token.
        var signed = FiReportCursorTokenRules.Validate(request.Token, binding, key, now, DateTimeOffset.MaxValue);
        if (signed.Status != FiReportCursorValidationStatus.Valid) return Closed(FiReportProblemRules.Map(signed));
        var position = signed.Position!;

        var manifest = loadManifest(position.ManifestId);
        var live = FiReportCursorTokenRules.Validate(request.Token, binding, key, now, manifest?.ExpiresAt);
        if (live.Status != FiReportCursorValidationStatus.Valid) return Closed(FiReportProblemRules.Map(live));

        var stored = new List<FiReportManifestFieldError>();
        if (manifest!.Binding != binding) stored.Add(new("manifest", "binding", FiReportManifestError.Inconsistent));
        if (manifest.CutoffSequence != position.CutoffSequence)
            stored.Add(new("manifest", "cutoffSequence", FiReportManifestError.Inconsistent));
        return stored.Count > 0 ? Closed(FiReportProblemRules.Map(stored)) : new(manifest, binding, position, null);
    }

    private static Opened Closed(FiReportProblem? problem) => new(null, null, null, Problem(problem));

    /// <summary>
    /// Frozen export (ADR-101, WU-BI-001a11): the request must carry a first-page or continuation token. Wiring faults
    /// (null request／actor／callbacks; headers null, with a null entry or a wrong column count; wrong report family)
    /// → 500 before any I/O. Then the same front half as <see cref="ContinueArAp"/> (bad token 400 and nothing loaded,
    /// gone／expired 409 and nothing re-read, stored binding／cutoff mismatch 500 and nothing re-read) →
    /// <paramref name="reread"/> → ADR-095 <see cref="FiReportManifestRules.ExportArAp"/>: every member regardless of
    /// the bound page size or the token's page; any disagreement 500 with no content. EXPORT permission and the audit
    /// row stay with the endpoint.
    /// </summary>
    public static FiReportExportResult ExportArAp(FiReportContinuationRequest? request, FiReportActorScope? actor,
        byte[]? key, DateTimeOffset now, IReadOnlyList<string?>? headers, Func<long, FiReportManifest?>? loadManifest,
        Func<FiReportManifest, ArApDetailReport?>? reread) =>
        Export(request, actor, key, now, headers, loadManifest, reread is null, arAp: true,
            ArApDetailAsOfRules.ExportColumnCount, m => FiReportManifestRules.ExportArAp(m, reread!(m), headers));

    public static FiReportExportResult ExportOtherIncomeExpense(FiReportContinuationRequest? request,
        FiReportActorScope? actor, byte[]? key, DateTimeOffset now, IReadOnlyList<string?>? headers,
        Func<long, FiReportManifest?>? loadManifest, Func<FiReportManifest, OtherIncomeExpenseReport?>? reread) =>
        Export(request, actor, key, now, headers, loadManifest, reread is null, arAp: false,
            OtherIncomeExpenseRules.ExportColumnCount,
            m => FiReportManifestRules.ExportOtherIncomeExpense(m, reread!(m), headers));

    private static FiReportExportResult Export(FiReportContinuationRequest? request, FiReportActorScope? actor,
        byte[]? key, DateTimeOffset now, IReadOnlyList<string?>? headers, Func<long, FiReportManifest?>? loadManifest,
        bool rereadMissing, bool arAp, int columnCount, Func<FiReportManifest, FiReportManifestExportResult> export)
    {
        var missing = new List<string>();
        if (request is null) missing.Add("export.request:Required");
        if (actor is null) missing.Add("export.actor:Required");
        if (loadManifest is null) missing.Add("export.loadManifest:Required");
        if (rereadMissing) missing.Add("export.reread:Required");
        if (headers is null || headers.Any(h => h is null)) missing.Add("export.headers:Required");
        else if (headers.Count != columnCount) missing.Add("export.headers:OutOfRange");
        if (missing.Count > 0) return ExportFail(InternalProblem(missing));

        if (Enum.IsDefined(request!.Kind) && IsArAp(request.Kind) != arAp)
            return ExportFail(InternalProblem(["request.kind:NotAllowed"]));

        var opened = Open(request, actor!, key, now, loadManifest!);
        if (opened.Problem is not null) return ExportFail(opened.Problem);

        var result = export(opened.Manifest!);
        if (result.Status != FiReportManifestStatus.Ok || result.Content is null)
            return ExportFail(Problem(FiReportProblemRules.Map(result.Errors.Count > 0
                ? result.Errors
                : [new FiReportManifestFieldError("reread", "export", FiReportManifestError.Inconsistent)])));
        return new(result.Content, result.RowCount, null);
    }

    private static FiReportExportResult ExportFail(FiReportProblem problem) => new(null, 0, problem);

    private static bool IsArAp(FiReportKind kind) => kind is FiReportKind.ArDetail or FiReportKind.ApDetail;

    private static FiReportProblem Problem(FiReportProblem? problem) =>
        problem ?? InternalProblem(["continuation.problem:Required"]);

    private static FiReportProblem InternalProblem(IReadOnlyList<string> details) =>
        new(500, FiReportProblemRules.InternalError, new Dictionary<string, string[]>(), details);

    private static FiReportContinuationResult<TPage> Fail<TPage>(FiReportProblem? problem) where TPage : class =>
        new(null, null, Problem(problem));

    private static FiReportContinuationResult<TPage> Internal<TPage>(IReadOnlyList<string> details)
        where TPage : class =>
        new(null, null, InternalProblem(details));
}
