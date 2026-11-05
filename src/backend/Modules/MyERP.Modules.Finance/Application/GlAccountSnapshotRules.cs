using MyERP.Modules.MasterData.Contracts;

namespace MyERP.Modules.Finance.Application;

public enum GlAccountSnapshotStatus
{
    Allowed,
    InvalidInput,
    MappingChanged,
    Missing,
    WrongCompany,
    AccountChanged,
    Inactive,
    NotPostable,
    InvalidSnapshot
}

/// <summary>Scalar FI snapshot; no MD navigation or cross-module foreign key.</summary>
public sealed record GlAccountSnapshot(long CompanyId, long AccountId, string Code, string Name,
    int AccountVersion, int MappingVersion);

public sealed record GlAccountSnapshotResult(GlAccountSnapshotStatus Status, GlAccountSnapshot? Snapshot = null);

/// <summary>
/// Pure preflight over the current MD public-contract result and FI mapping version.
/// The caller must fetch both within the posting transaction and recheck before saving.
/// </summary>
public static class GlAccountSnapshotRules
{
    public static GlAccountSnapshotResult Validate(long companyId, long accountId, string? accountCode,
        int expectedMappingVersion, int currentMappingVersion, AccountInfo? account)
    {
        if (companyId <= 0 || accountId <= 0 || expectedMappingVersion <= 0 ||
            string.IsNullOrEmpty(accountCode) || !ValidCode(accountCode))
            return new(GlAccountSnapshotStatus.InvalidInput);
        if (currentMappingVersion <= 0 || expectedMappingVersion != currentMappingVersion)
            return new(GlAccountSnapshotStatus.MappingChanged);
        if (account is null)
            return new(GlAccountSnapshotStatus.Missing);
        if (account.CompanyId != companyId)
            return new(GlAccountSnapshotStatus.WrongCompany);
        if (account.Id != accountId || !string.Equals(account.Code, accountCode, StringComparison.Ordinal))
            return new(GlAccountSnapshotStatus.AccountChanged);
        if (!account.IsActive)
            return new(GlAccountSnapshotStatus.Inactive);
        if (!account.IsPostable)
            return new(GlAccountSnapshotStatus.NotPostable);
        if (account.Version <= 0 || string.IsNullOrWhiteSpace(account.Name) || account.Name.Length > 200)
            return new(GlAccountSnapshotStatus.InvalidSnapshot);
        return new(GlAccountSnapshotStatus.Allowed,
            new GlAccountSnapshot(companyId, account.Id, account.Code, account.Name,
                account.Version, currentMappingVersion));
    }

    private static bool ValidCode(string code) => code.Length is > 0 and <= 30 &&
        code.All(static c => c is >= '0' and <= '9');
}
