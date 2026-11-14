using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Finance.Domain;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;

namespace MyERP.Modules.Finance.Application;

/// <summary>Client input: <see cref="Version"/> 0 means "not configured yet"; otherwise the saved map version.</summary>
public sealed record PurchaseInvoiceAccountMapInput(int Version, string? PayableAccountCode, string? PurchaseAccountCode,
    string? InputTaxAccountCode);

/// <summary>
/// The company's PUR purchase invoice account map. <see cref="Configured"/> false (version 0, no accounts) is an explicit
/// "not set up" answer, not a 404: purchase invoices of this company cannot be posted until a map is saved.
/// Each account reuses <see cref="SalesInvoiceMappedAccount"/> (code, current MD name or null, active, postable).
/// </summary>
public sealed record PurchaseInvoiceAccountMapRead(bool Configured, int Version,
    SalesInvoiceMappedAccount? PayableAccount, SalesInvoiceMappedAccount? PurchaseAccount,
    SalesInvoiceMappedAccount? InputTaxAccount, DateTime? CreatedAt, long? CreatedBy, DateTime? UpdatedAt,
    long? UpdatedBy)
{
    public static readonly PurchaseInvoiceAccountMapRead NotConfigured = new(false, 0, null, null, null, null, null, null, null);
}

public sealed record PurchaseInvoiceAccountMapWriteResult(PurchaseInvoiceAccountMapRead Map, bool Changed);

public static class PurchaseInvoiceAccountMapErrors
{
    /// <summary>409: the request version is not the saved one (0 while a row exists, or a stale number).</summary>
    public const string VersionConflict = "FI.PURCHASE_MAP.VERSION_CONFLICT";
    /// <summary>422: at least one code is missing, inactive or not postable in MD; <c>accountErrors</c> names each field.</summary>
    public const string AccountInvalid = "FI.PURCHASE_MAP.ACCOUNT_INVALID";
}

/// <summary>Pure maintenance rules (BR-FI-150); account postability reuses <see cref="SalesInvoiceAccountMapRules.AccountReason"/>.</summary>
public static class PurchaseInvoiceAccountMapRules
{
    public const string PayableField = "payableAccountCode";
    public const string PurchaseField = "purchaseAccountCode";
    public const string InputTaxField = "inputTaxAccountCode";

    /// <summary>Trims codes; blank, longer than MD's 30 characters or a negative version is a 400 naming every field.</summary>
    public static PurchaseInvoiceAccountMapInput Normalize(PurchaseInvoiceAccountMapInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (input.Version < 0) errors["version"] = ["Version must be 0 (not configured) or the saved version."];
        string Code(string field, string? value)
        {
            var code = value?.Trim() ?? "";
            if (code.Length == 0) errors[field] = ["Account code is required."];
            else if (code.Length > PurchaseInvoiceAccountMapping.CodeMaxLength)
                errors[field] = [$"Account code must not exceed {PurchaseInvoiceAccountMapping.CodeMaxLength} characters."];
            return code;
        }
        var normalized = new PurchaseInvoiceAccountMapInput(input.Version, Code(PayableField, input.PayableAccountCode),
            Code(PurchaseField, input.PurchaseAccountCode), Code(InputTaxField, input.InputTaxAccountCode));
        if (errors.Count > 0) throw new RequestValidationException(errors);
        return normalized;
    }

    public static bool SameAccounts(PurchaseInvoiceAccountMapping saved, PurchaseInvoiceAccountMapInput input) =>
        string.Equals(saved.PayableAccountCode, input.PayableAccountCode, StringComparison.Ordinal)
        && string.Equals(saved.PurchaseAccountCode, input.PurchaseAccountCode, StringComparison.Ordinal)
        && string.Equals(saved.InputTaxAccountCode, input.InputTaxAccountCode, StringComparison.Ordinal);
}

/// <summary>
/// WU-FI-003a2c（ADR-118、REQ_F4116.9.5）：authorised read and create-or-update of the current company's PUR purchase
/// invoice account map, with the same contract as the SD map (ADR-110). The row version is the GL
/// <c>PUR/PurchaseInvoice</c> mapping version (ADR-116), so a save that changes no account returns changed=false and
/// leaves the version alone; a real change increments it through the audit interceptor and is audited as CREATE/UPDATE.
/// Every code must be postable in MD at save time (posting re-checks it). Concurrent creates lose on the unique company
/// index, concurrent updates on the version token; both with 409 <see cref="PurchaseInvoiceAccountMapErrors.VersionConflict"/>.
/// </summary>
public sealed class PurchaseInvoiceAccountMapService(MyErpDbContext db, ICurrentCompany company,
    ICurrentAuthorization authorization, IChartOfAccounts accounts)
{
    public const string ViewPermission = "GL.PURCHASE_MAP.VIEW";
    public const string EditPermission = "GL.PURCHASE_MAP.EDIT";
    private static readonly int[] DuplicateKeyErrors = [2601, 2627];

    public async Task<PurchaseInvoiceAccountMapRead> GetAsync(CancellationToken token)
    {
        var companyId = await FinanceAccess.RequireAsync(company, authorization, ViewPermission, token);
        var row = await db.Set<PurchaseInvoiceAccountMapping>().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId, token);
        return row is null ? PurchaseInvoiceAccountMapRead.NotConfigured : await ReadAsync(row, token);
    }

    public async Task<PurchaseInvoiceAccountMapWriteResult> SaveAsync(PurchaseInvoiceAccountMapInput request,
        CancellationToken token)
    {
        var companyId = await FinanceAccess.RequireAsync(company, authorization, EditPermission, token);
        var input = PurchaseInvoiceAccountMapRules.Normalize(request);
        var row = await db.Set<PurchaseInvoiceAccountMapping>().SingleOrDefaultAsync(x => x.CompanyId == companyId, token);
        if (!SalesInvoiceAccountMapRules.VersionMatches(input.Version, row?.Version ?? 0)) throw Conflict();
        if (row is not null && PurchaseInvoiceAccountMapRules.SameAccounts(row, input))
            return new(await ReadAsync(row, token), false);

        await RequirePostableAsync(input, token);
        if (row is null)
        {
            row = new PurchaseInvoiceAccountMapping { CompanyId = companyId };
            db.Add(row);
        }
        row.PayableAccountCode = input.PayableAccountCode!;
        row.PurchaseAccountCode = input.PurchaseAccountCode!;
        row.InputTaxAccountCode = input.InputTaxAccountCode!;
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

    private async Task RequirePostableAsync(PurchaseInvoiceAccountMapInput input, CancellationToken token)
    {
        var reasons = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (field, code) in new[]
                 {
                     (PurchaseInvoiceAccountMapRules.PayableField, input.PayableAccountCode!),
                     (PurchaseInvoiceAccountMapRules.PurchaseField, input.PurchaseAccountCode!),
                     (PurchaseInvoiceAccountMapRules.InputTaxField, input.InputTaxAccountCode!)
                 })
        {
            var reason = SalesInvoiceAccountMapRules.AccountReason(await accounts.FindAsync(code, token));
            if (reason is not null) reasons[field] = reason;
        }
        if (reasons.Count > 0)
            throw new BusinessRuleException(PurchaseInvoiceAccountMapErrors.AccountInvalid,
                "Every mapped account must exist, be active and have no child accounts.")
            {
                Extensions = new Dictionary<string, object?>(StringComparer.Ordinal) { ["accountErrors"] = reasons }
            };
    }

    private async Task<PurchaseInvoiceAccountMapRead> ReadAsync(PurchaseInvoiceAccountMapping row, CancellationToken token)
    {
        async Task<SalesInvoiceMappedAccount> Label(string code)
        {
            var account = await accounts.FindAsync(code, token);
            return new(code, account?.Name, account?.IsActive ?? false, account?.IsPostable ?? false);
        }
        return new(true, row.Version, await Label(row.PayableAccountCode), await Label(row.PurchaseAccountCode),
            await Label(row.InputTaxAccountCode), row.CreatedAt, row.CreatedBy, row.UpdatedAt, row.UpdatedBy);
    }

    private static ConflictException Conflict() => new(PurchaseInvoiceAccountMapErrors.VersionConflict,
        "The purchase invoice account map has been changed by another user. Please reload.");
}
