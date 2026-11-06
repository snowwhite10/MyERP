using MyERP.Modules.Finance.Domain;
using MyERP.Modules.MasterData.Contracts;

namespace MyERP.Modules.Finance.Application;

/// <summary>
/// Raw committed <c>gl_source_key</c> row joined to its journal, as read inside the posting transaction.
/// <see cref="JournalId"/> is null when no journal row was found for the key.
/// </summary>
public sealed record GlCommittedSourceRow(long CompanyId, string SourceModule, string SourceType, long SourceId,
    int SourceVersion, string Fingerprint, long? JournalId);

/// <summary>
/// WU-FI-004a3x1b1: pure assembly of <see cref="GlPostingAuthority"/> from the rows the SQL port reads after both
/// posting locks. It never decides posting: closed periods, other currencies, newer mapping versions and missing
/// accounts pass through for <see cref="GlPostingDecisionRules"/>. A read that cannot be trusted (foreign-company
/// row, key without journal, unread or mismatched account) returns null, which the attempt runner reports as
/// 500 <c>INVALID_DECISION</c> without writing.
/// </summary>
public static class GlPostingAuthorityRules
{
    /// <summary>Distinct draft account codes (ordinal, sorted) to read once each through MD's public contract.</summary>
    public static IReadOnlyList<string> AccountCodesToRead(GlJournalLineDraftResult? draft)
    {
        if (draft?.Lines is not { Count: > 0 } lines)
            return [];
        return lines.Select(l => l.AccountCode).Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray();
    }

    public static GlPostingAuthority? Assemble(long companyId, GlCommittedSourceRow? committed,
        string? companyBaseCurrency, IReadOnlyList<GlPeriodWindow>? periods, int currentMappingVersion,
        GlJournalLineDraftResult? draft, IReadOnlyDictionary<string, AccountInfo?>? accountsByCode)
    {
        if (companyId <= 0 || periods is null || periods.Any(p => p is null || p.CompanyId != companyId))
            return null;

        GlCommittedSource? saved = null;
        if (committed is not null)
        {
            if (committed.CompanyId != companyId || committed.JournalId is not > 0)
                return null;
            saved = new(new(committed.CompanyId, committed.SourceModule, committed.SourceType, committed.SourceId,
                committed.SourceVersion, committed.Fingerprint), committed.JournalId.Value);
        }

        var accounts = CurrentAccounts(companyId, draft, accountsByCode);
        return accounts is null
            ? null
            : new(saved, companyBaseCurrency, periods, currentMappingVersion, accounts);
    }

    private static AccountInfo?[]? CurrentAccounts(long companyId, GlJournalLineDraftResult? draft,
        IReadOnlyDictionary<string, AccountInfo?>? accountsByCode)
    {
        if (draft?.Lines is not { Count: > 0 } lines)
            return [];
        if (accountsByCode is null)
            return null;

        // Exact code matching regardless of the caller's dictionary comparer (MD codes are case-sensitive).
        var byCode = new Dictionary<string, AccountInfo?>(StringComparer.Ordinal);
        foreach (var (code, account) in accountsByCode)
            byCode[code] = account;

        var result = new AccountInfo?[lines.Count];
        for (var index = 0; index < lines.Count; index++)
        {
            var code = lines[index].AccountCode;
            if (code is null || !byCode.TryGetValue(code, out var account))
                return null;
            if (account is not null
                && (account.CompanyId != companyId || !string.Equals(account.Code, code, StringComparison.Ordinal)))
                return null;
            result[index] = account;
        }
        return result;
    }
}
