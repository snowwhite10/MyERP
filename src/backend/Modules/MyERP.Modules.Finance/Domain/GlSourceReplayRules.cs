namespace MyERP.Modules.Finance.Domain;

/// <summary>Canonical identity and saved-source evidence supplied by an FI-owned adapter.</summary>
public sealed record GlPostingSource(
    long CompanyId, string Module, string SourceType, long SourceId, long SourceVersion, string Fingerprint);

/// <summary>A committed FI source row. The SQL posting path must enforce this assumption.</summary>
public sealed record GlCommittedSource(GlPostingSource Source, long JournalId);

public enum GlSourceReplayStatus { New, Unchanged, Conflict, IdentityMismatch, InvalidSource, InvalidSavedReceipt }

public sealed record GlSourceReplayDecision(GlSourceReplayStatus Status, long? JournalId);

/// <summary>
/// Pure replay preflight. The caller must read a committed source under the permanent-key SQL lock.
/// This rule neither proves a transaction committed nor reserves a source key.
/// </summary>
public static class GlSourceReplayRules
{
    public static GlSourceReplayDecision Decide(GlPostingSource candidate, GlCommittedSource? saved)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (!IsValid(candidate))
            return new(GlSourceReplayStatus.InvalidSource, null);
        if (saved is null)
            return new(GlSourceReplayStatus.New, null);
        if (saved.JournalId <= 0 || !IsValid(saved.Source))
            return new(GlSourceReplayStatus.InvalidSavedReceipt, null);
        if (candidate.CompanyId != saved.Source.CompanyId || candidate.SourceId != saved.Source.SourceId
            || !string.Equals(candidate.Module, saved.Source.Module, StringComparison.Ordinal)
            || !string.Equals(candidate.SourceType, saved.Source.SourceType, StringComparison.Ordinal))
            return new(GlSourceReplayStatus.IdentityMismatch, null);
        if (candidate.SourceVersion != saved.Source.SourceVersion
            || !string.Equals(candidate.Fingerprint, saved.Source.Fingerprint, StringComparison.Ordinal))
            return new(GlSourceReplayStatus.Conflict, null);
        return new(GlSourceReplayStatus.Unchanged, saved.JournalId);
    }

    private static bool IsValid(GlPostingSource source) =>
        source.CompanyId > 0 && source.SourceId > 0 && source.SourceVersion > 0
        && IsCanonicalText(source.Module)
        && IsCanonicalText(source.SourceType)
        && IsCanonicalText(source.Fingerprint);

    private static bool IsCanonicalText(string value) =>
        !string.IsNullOrWhiteSpace(value) && value == value.Trim();
}
