using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Finance.Domain;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;

namespace MyERP.Modules.Finance.Application;

/// <summary>Client input: <see cref="Version"/> 0 means "not configured yet"; otherwise the saved map version.</summary>
public sealed record SalesCollectionAccountMapInput(int Version, string? UnappliedCashAccountCode,
    string? ReceivableAccountCode);

/// <summary>
/// The company's sales collection account map. <see cref="Configured"/> false (version 0, no accounts) is an explicit
/// "not set up" answer, not a 404: collections of this company cannot be posted until a map is saved.
/// <see cref="SalesInvoiceReceivableAccountCode"/> is the SD invoice map's AR code (null when that map is not configured);
/// <see cref="Warnings"/> holds <see cref="SalesCollectionAccountMapErrors.ReceivableDiffersFromSalesMap"/> when a configured
/// map names another AR control account (allowed, ADR-120).
/// </summary>
public sealed record SalesCollectionAccountMapRead(bool Configured, int Version,
    SalesInvoiceMappedAccount? UnappliedCashAccount, SalesInvoiceMappedAccount? ReceivableAccount,
    string? SalesInvoiceReceivableAccountCode, IReadOnlyList<string> Warnings, DateTime? CreatedAt, long? CreatedBy,
    DateTime? UpdatedAt, long? UpdatedBy);

public sealed record SalesCollectionAccountMapWriteResult(SalesCollectionAccountMapRead Map, bool Changed);

public static class SalesCollectionAccountMapErrors
{
    /// <summary>409: the request version is not the saved one (0 while a row exists, or a stale number).</summary>
    public const string VersionConflict = "FI.COLLECTION_MAP.VERSION_CONFLICT";
    /// <summary>422: a code is missing, inactive or not postable in MD, or both codes are equal; <c>accountErrors</c> names each field.</summary>
    public const string AccountInvalid = "FI.COLLECTION_MAP.ACCOUNT_INVALID";
    /// <summary><c>accountErrors</c> reason on the AR field when it equals the unapplied-cash code (trimmed, case-insensitive).</summary>
    public const string SameAccount = "FI.COLLECTION_MAP.SAME_ACCOUNT";
    /// <summary>Read warning: the AR control code differs from the SD invoice map's receivable code.</summary>
    public const string ReceivableDiffersFromSalesMap = "FI.COLLECTION_MAP.RECEIVABLE_DIFFERS_FROM_SALES_MAP";
}

/// <summary>Pure maintenance rules (BR-FI-154); account postability reuses <see cref="SalesInvoiceAccountMapRules.AccountReason"/>.</summary>
public static class SalesCollectionAccountMapRules
{
    public const string UnappliedCashField = "unappliedCashAccountCode";
    public const string ReceivableField = "receivableAccountCode";

    /// <summary>Trims codes; blank, longer than MD's 30 characters or a negative version is a 400 naming every field.</summary>
    public static SalesCollectionAccountMapInput Normalize(SalesCollectionAccountMapInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (input.Version < 0) errors["version"] = ["Version must be 0 (not configured) or the saved version."];
        string Code(string field, string? value)
        {
            var code = value?.Trim() ?? "";
            if (code.Length == 0) errors[field] = ["Account code is required."];
            else if (code.Length > SalesCollectionAccountMapping.CodeMaxLength)
                errors[field] = [$"Account code must not exceed {SalesCollectionAccountMapping.CodeMaxLength} characters."];
            return code;
        }
        var normalized = new SalesCollectionAccountMapInput(input.Version,
            Code(UnappliedCashField, input.UnappliedCashAccountCode), Code(ReceivableField, input.ReceivableAccountCode));
        if (errors.Count > 0) throw new RequestValidationException(errors);
        return normalized;
    }

    /// <summary>Same rule as SQL check <c>ck_gl_sales_collection_account_map_distinct</c> (trimmed, case-insensitive).</summary>
    public static bool AccountsCollide(SalesCollectionAccountMapInput input) => string.Equals(
        input.UnappliedCashAccountCode?.Trim(), input.ReceivableAccountCode?.Trim(), StringComparison.OrdinalIgnoreCase);

    public static bool SameAccounts(SalesCollectionAccountMapping saved, SalesCollectionAccountMapInput input) =>
        string.Equals(saved.UnappliedCashAccountCode, input.UnappliedCashAccountCode, StringComparison.Ordinal)
        && string.Equals(saved.ReceivableAccountCode, input.ReceivableAccountCode, StringComparison.Ordinal);

    /// <summary>True when the SD invoice map exists and names another AR code (MD codes are case-insensitive).</summary>
    public static bool ReceivableDiffers(string collectionReceivable, string? salesInvoiceReceivable) =>
        salesInvoiceReceivable is not null
        && !string.Equals(collectionReceivable, salesInvoiceReceivable, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// WU-FI-005a2d（ADR-123、REQ_F4116.1.8）：authorised read and create-or-update of the current company's sales collection
/// account map, with the same contract as the SD and PUR maps (ADR-110／118). The row version is the GL
/// <c>FI/SalesCollection</c> and <c>FI/CollectionWriteOff</c> mapping version (ADR-120), so a save that changes no account
/// returns changed=false and leaves the version alone; a real change increments it and is audited as CREATE/UPDATE. Both
/// codes must differ and be postable in MD at save time (posting re-checks them). Concurrent creates lose on the unique
/// company index, concurrent updates on the version token; both with 409 <see cref="SalesCollectionAccountMapErrors.VersionConflict"/>.
/// </summary>
public sealed class SalesCollectionAccountMapService(MyErpDbContext db, ICurrentCompany company,
    ICurrentAuthorization authorization, IChartOfAccounts accounts)
{
    public const string ViewPermission = "GL.COLLECTION_MAP.VIEW";
    public const string EditPermission = "GL.COLLECTION_MAP.EDIT";
    private static readonly int[] DuplicateKeyErrors = [2601, 2627];

    public async Task<SalesCollectionAccountMapRead> GetAsync(CancellationToken token)
    {
        var companyId = await FinanceAccess.RequireAsync(company, authorization, ViewPermission, token);
        var row = await db.Set<SalesCollectionAccountMapping>().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId, token);
        return await ReadAsync(companyId, row, token);
    }

    public async Task<SalesCollectionAccountMapWriteResult> SaveAsync(SalesCollectionAccountMapInput request,
        CancellationToken token)
    {
        var companyId = await FinanceAccess.RequireAsync(company, authorization, EditPermission, token);
        var input = SalesCollectionAccountMapRules.Normalize(request);
        var row = await db.Set<SalesCollectionAccountMapping>().SingleOrDefaultAsync(x => x.CompanyId == companyId, token);
        if (!SalesInvoiceAccountMapRules.VersionMatches(input.Version, row?.Version ?? 0)) throw Conflict();
        if (row is not null && SalesCollectionAccountMapRules.SameAccounts(row, input))
            return new(await ReadAsync(companyId, row, token), false);

        await RequireValidAsync(input, token);
        if (row is null)
        {
            row = new SalesCollectionAccountMapping { CompanyId = companyId };
            db.Add(row);
        }
        row.UnappliedCashAccountCode = input.UnappliedCashAccountCode!;
        row.ReceivableAccountCode = input.ReceivableAccountCode!;
        try
        {
            await db.SaveChangesAsync(token);
        }
        catch (DbUpdateException ex) when (ex is DbUpdateConcurrencyException
            || ex.InnerException is SqlException { Number: var n } && DuplicateKeyErrors.Contains(n))
        {
            db.ChangeTracker.Clear();
            throw Conflict();
        }
        return new(await ReadAsync(companyId, row, token), true);
    }

    /// <summary>MD reasons per field, plus <see cref="SalesCollectionAccountMapErrors.SameAccount"/> on the AR field.</summary>
    private async Task RequireValidAsync(SalesCollectionAccountMapInput input, CancellationToken token)
    {
        var reasons = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (field, code) in new[]
                 {
                     (SalesCollectionAccountMapRules.UnappliedCashField, input.UnappliedCashAccountCode!),
                     (SalesCollectionAccountMapRules.ReceivableField, input.ReceivableAccountCode!)
                 })
        {
            var reason = SalesInvoiceAccountMapRules.AccountReason(await accounts.FindAsync(code, token));
            if (reason is not null) reasons[field] = reason;
        }
        if (SalesCollectionAccountMapRules.AccountsCollide(input))
            reasons.TryAdd(SalesCollectionAccountMapRules.ReceivableField, SalesCollectionAccountMapErrors.SameAccount);
        if (reasons.Count > 0)
            throw new BusinessRuleException(SalesCollectionAccountMapErrors.AccountInvalid,
                "Both mapped accounts must exist, be active, have no child accounts and differ from each other.")
            {
                Extensions = new Dictionary<string, object?>(StringComparer.Ordinal) { ["accountErrors"] = reasons }
            };
    }

    private async Task<SalesCollectionAccountMapRead> ReadAsync(long companyId, SalesCollectionAccountMapping? row,
        CancellationToken token)
    {
        var salesReceivable = await db.Set<SalesInvoiceAccountMapping>().AsNoTracking()
            .Where(x => x.CompanyId == companyId).Select(x => x.ReceivableAccountCode).SingleOrDefaultAsync(token);
        if (row is null) return new(false, 0, null, null, salesReceivable, [], null, null, null, null);

        async Task<SalesInvoiceMappedAccount> Label(string code)
        {
            var account = await accounts.FindAsync(code, token);
            return new(code, account?.Name, account?.IsActive ?? false, account?.IsPostable ?? false);
        }
        string[] warnings = SalesCollectionAccountMapRules.ReceivableDiffers(row.ReceivableAccountCode, salesReceivable)
            ? [SalesCollectionAccountMapErrors.ReceivableDiffersFromSalesMap]
            : [];
        return new(true, row.Version, await Label(row.UnappliedCashAccountCode), await Label(row.ReceivableAccountCode),
            salesReceivable, warnings, row.CreatedAt, row.CreatedBy, row.UpdatedAt, row.UpdatedBy);
    }

    private static ConflictException Conflict() => new(SalesCollectionAccountMapErrors.VersionConflict,
        "The sales collection account map has been changed by another user. Please reload.");
}
