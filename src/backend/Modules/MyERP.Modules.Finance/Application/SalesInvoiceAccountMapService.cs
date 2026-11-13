using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Finance.Domain;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;

namespace MyERP.Modules.Finance.Application;

/// <summary>Client input: <see cref="Version"/> 0 means "not configured yet"; otherwise the saved map version.</summary>
public sealed record SalesInvoiceAccountMapInput(int Version, string? ReceivableAccountCode, string? RevenueAccountCode,
    string? OutputTaxAccountCode);

/// <summary>A mapped MD account with its current label; <see cref="Name"/> is null when MD no longer has the code.</summary>
public sealed record SalesInvoiceMappedAccount(string Code, string? Name, bool IsActive, bool IsPostable);

/// <summary>
/// The company's SD invoice account map. <see cref="Configured"/> false (version 0, no accounts) is an explicit
/// "not set up" answer, not a 404: invoices of this company cannot be posted until a map is saved.
/// </summary>
public sealed record SalesInvoiceAccountMapRead(bool Configured, int Version,
    SalesInvoiceMappedAccount? ReceivableAccount, SalesInvoiceMappedAccount? RevenueAccount,
    SalesInvoiceMappedAccount? OutputTaxAccount, DateTime? CreatedAt, long? CreatedBy, DateTime? UpdatedAt,
    long? UpdatedBy)
{
    public static readonly SalesInvoiceAccountMapRead NotConfigured = new(false, 0, null, null, null, null, null, null, null);
}

public sealed record SalesInvoiceAccountMapWriteResult(SalesInvoiceAccountMapRead Map, bool Changed);

public static class SalesInvoiceAccountMapErrors
{
    /// <summary>409: the request version is not the saved one (0 while a row exists, or a stale number).</summary>
    public const string VersionConflict = "FI.SALES_MAP.VERSION_CONFLICT";
    /// <summary>422: at least one code is missing, inactive or not postable in MD; <c>accountErrors</c> names each field.</summary>
    public const string AccountInvalid = "FI.SALES_MAP.ACCOUNT_INVALID";
}

/// <summary>Pure maintenance rules (BR-FI-147).</summary>
public static class SalesInvoiceAccountMapRules
{
    public const string ReceivableField = "receivableAccountCode";
    public const string RevenueField = "revenueAccountCode";
    public const string OutputTaxField = "outputTaxAccountCode";

    /// <summary>Trims codes; blank, longer than MD's 30 characters or a negative version is a 400 naming every field.</summary>
    public static SalesInvoiceAccountMapInput Normalize(SalesInvoiceAccountMapInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (input.Version < 0) errors["version"] = ["Version must be 0 (not configured) or the saved version."];
        string Code(string field, string? value)
        {
            var code = value?.Trim() ?? "";
            if (code.Length == 0) errors[field] = ["Account code is required."];
            else if (code.Length > SalesInvoiceAccountMapping.CodeMaxLength)
                errors[field] = [$"Account code must not exceed {SalesInvoiceAccountMapping.CodeMaxLength} characters."];
            return code;
        }
        var normalized = new SalesInvoiceAccountMapInput(input.Version, Code(ReceivableField, input.ReceivableAccountCode),
            Code(RevenueField, input.RevenueAccountCode), Code(OutputTaxField, input.OutputTaxAccountCode));
        if (errors.Count > 0) throw new RequestValidationException(errors);
        return normalized;
    }

    /// <summary>Same reasons as <see cref="IChartOfAccounts.RequirePostableAsync"/>; null when the account can be posted.</summary>
    public static string? AccountReason(AccountInfo? account) => account switch
    {
        null => "MD.ACCOUNT.NOT_FOUND",
        { IsActive: false } => "MD.ACCOUNT.INACTIVE",
        { IsPostable: false } => "MD.ACCOUNT.NOT_POSTABLE",
        _ => null
    };

    /// <summary><paramref name="saved"/> is 0 when the company has no row.</summary>
    public static bool VersionMatches(int requested, int saved) => requested == saved;

    public static bool SameAccounts(SalesInvoiceAccountMapping saved, SalesInvoiceAccountMapInput input) =>
        string.Equals(saved.ReceivableAccountCode, input.ReceivableAccountCode, StringComparison.Ordinal)
        && string.Equals(saved.RevenueAccountCode, input.RevenueAccountCode, StringComparison.Ordinal)
        && string.Equals(saved.OutputTaxAccountCode, input.OutputTaxAccountCode, StringComparison.Ordinal);
}

/// <summary>
/// WU-FI-002a4b（ADR-110、REQ_F4116.8.5）：authorised read and create-or-update of the current company's SD invoice
/// account map. The row version is the GL mapping version (ADR-109), so a save that changes no account returns
/// changed=false and leaves the version alone; a real change increments it through the audit interceptor and is
/// audited as CREATE/UPDATE. Every code must be postable in MD at save time (posting re-checks it).
/// Concurrent creates are resolved by the unique company index, concurrent updates by the version token; both lose
/// with 409 <see cref="SalesInvoiceAccountMapErrors.VersionConflict"/>.
/// </summary>
public sealed class SalesInvoiceAccountMapService(MyErpDbContext db, ICurrentCompany company,
    ICurrentAuthorization authorization, IChartOfAccounts accounts)
{
    public const string ViewPermission = "GL.SALES_MAP.VIEW";
    public const string EditPermission = "GL.SALES_MAP.EDIT";
    private static readonly int[] DuplicateKeyErrors = [2601, 2627];

    public async Task<SalesInvoiceAccountMapRead> GetAsync(CancellationToken token)
    {
        var companyId = await FinanceAccess.RequireAsync(company, authorization, ViewPermission, token);
        var row = await db.Set<SalesInvoiceAccountMapping>().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId, token);
        return row is null ? SalesInvoiceAccountMapRead.NotConfigured : await ReadAsync(row, token);
    }

    public async Task<SalesInvoiceAccountMapWriteResult> SaveAsync(SalesInvoiceAccountMapInput request,
        CancellationToken token)
    {
        var companyId = await FinanceAccess.RequireAsync(company, authorization, EditPermission, token);
        var input = SalesInvoiceAccountMapRules.Normalize(request);
        var row = await db.Set<SalesInvoiceAccountMapping>().SingleOrDefaultAsync(x => x.CompanyId == companyId, token);
        if (!SalesInvoiceAccountMapRules.VersionMatches(input.Version, row?.Version ?? 0)) throw Conflict();
        if (row is not null && SalesInvoiceAccountMapRules.SameAccounts(row, input))
            return new(await ReadAsync(row, token), false);

        await RequirePostableAsync(input, token);
        if (row is null)
        {
            row = new SalesInvoiceAccountMapping { CompanyId = companyId };
            db.Add(row);
        }
        row.ReceivableAccountCode = input.ReceivableAccountCode!;
        row.RevenueAccountCode = input.RevenueAccountCode!;
        row.OutputTaxAccountCode = input.OutputTaxAccountCode!;
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
        return new(await ReadAsync(row, token), true);
    }

    private async Task RequirePostableAsync(SalesInvoiceAccountMapInput input, CancellationToken token)
    {
        var reasons = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (field, code) in Fields(input))
        {
            var reason = SalesInvoiceAccountMapRules.AccountReason(await accounts.FindAsync(code, token));
            if (reason is not null) reasons[field] = reason;
        }
        if (reasons.Count > 0)
            throw new BusinessRuleException(SalesInvoiceAccountMapErrors.AccountInvalid,
                "Every mapped account must exist, be active and have no child accounts.")
            {
                Extensions = new Dictionary<string, object?>(StringComparer.Ordinal) { ["accountErrors"] = reasons }
            };
    }

    private static IEnumerable<(string Field, string Code)> Fields(SalesInvoiceAccountMapInput input) =>
    [
        (SalesInvoiceAccountMapRules.ReceivableField, input.ReceivableAccountCode!),
        (SalesInvoiceAccountMapRules.RevenueField, input.RevenueAccountCode!),
        (SalesInvoiceAccountMapRules.OutputTaxField, input.OutputTaxAccountCode!)
    ];

    private async Task<SalesInvoiceAccountMapRead> ReadAsync(SalesInvoiceAccountMapping row, CancellationToken token)
    {
        async Task<SalesInvoiceMappedAccount> Label(string code)
        {
            var account = await accounts.FindAsync(code, token);
            return new(code, account?.Name, account?.IsActive ?? false, account?.IsPostable ?? false);
        }
        return new(true, row.Version, await Label(row.ReceivableAccountCode), await Label(row.RevenueAccountCode),
            await Label(row.OutputTaxAccountCode), row.CreatedAt, row.CreatedBy, row.UpdatedAt, row.UpdatedBy);
    }

    private static ConflictException Conflict() => new(SalesInvoiceAccountMapErrors.VersionConflict,
        "The sales invoice account map has been changed by another user. Please reload.");
}
