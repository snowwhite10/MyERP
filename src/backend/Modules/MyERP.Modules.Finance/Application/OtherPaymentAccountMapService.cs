using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Finance.Domain;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;

namespace MyERP.Modules.Finance.Application;

/// <summary>Client input for one category: <see cref="Version"/> 0 means "not configured yet"; otherwise the saved row version.</summary>
public sealed record OtherPaymentAccountMapInput(int Version, string? ExpenseAccountCode);

/// <summary>MD expense account of a mapping row with its current label; <see cref="IsUsable"/> is the posting rule (active, leaf, Expense/Cost).</summary>
public sealed record OtherPaymentMappedAccount(long Id, string? Code, string? Name, bool IsActive, bool IsPostable,
    bool IsUsable);

/// <summary>
/// One MD expense category and its FI expense account. <see cref="Configured"/> false (version 0, no account) is an explicit
/// "not set up" row: other payments of this category cannot be posted (422 FI.OTHER_PAYMENT_POSTING.MAPPING_MISSING).
/// </summary>
public sealed record OtherPaymentAccountMapRow(long ExpenseCategoryId, string? ExpenseCategoryCode,
    string? ExpenseCategoryName, bool ExpenseCategoryActive, bool Configured, int Version,
    OtherPaymentMappedAccount? ExpenseAccount, DateTime? CreatedAt, long? CreatedBy, DateTime? UpdatedAt, long? UpdatedBy);

public sealed record OtherPaymentAccountMapList(IReadOnlyList<OtherPaymentAccountMapRow> Items);

public sealed record OtherPaymentAccountMapWriteResult(OtherPaymentAccountMapRow Row, bool Changed);

public static class OtherPaymentAccountMapErrors
{
    /// <summary>409: the request version is not the saved row version (0 while a row exists, or a stale number).</summary>
    public const string VersionConflict = "FI.OTHER_PAYMENT_MAP.VERSION_CONFLICT";
    /// <summary>404: the category does not exist in the current company (deleted, other company or unknown id).</summary>
    public const string CategoryNotFound = "FI.OTHER_PAYMENT_MAP.CATEGORY_NOT_FOUND";
    /// <summary>422: the category is an income category; only expense categories are mapped.</summary>
    public const string CategoryInvalid = "FI.OTHER_PAYMENT_MAP.CATEGORY_INVALID";
    /// <summary>422: the account cannot be debited by an other payment; <c>reason</c> names why.</summary>
    public const string AccountInvalid = "FI.OTHER_PAYMENT_MAP.ACCOUNT_INVALID";
    /// <summary><c>reason</c> when the account is an asset/liability/equity/revenue account (AP control, unapplied, prepaid, bank…).</summary>
    public const string NotExpenseAccount = "FI.OTHER_PAYMENT_MAP.NOT_EXPENSE_ACCOUNT";
}

/// <summary>Pure maintenance rules (BR-FI-060); the account type rule is the posting rule <see cref="OtherPaymentPostingRules.IsExpenseAccount"/>.</summary>
public static class OtherPaymentAccountMapRules
{
    public const string AccountField = "expenseAccountCode";
    public const int CodeMaxLength = 30;

    /// <summary>Trims the code; blank, longer than MD's 30 characters or a negative version is a 400 naming every field.</summary>
    public static OtherPaymentAccountMapInput Normalize(OtherPaymentAccountMapInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (input.Version < 0) errors["version"] = ["Version must be 0 (not configured) or the saved version."];
        var code = input.ExpenseAccountCode?.Trim() ?? "";
        if (code.Length == 0) errors[AccountField] = ["Account code is required."];
        else if (code.Length > CodeMaxLength)
            errors[AccountField] = [$"Account code must not exceed {CodeMaxLength} characters."];
        if (errors.Count > 0) throw new RequestValidationException(errors);
        return input with { ExpenseAccountCode = code };
    }

    /// <summary>MD reason first (missing, inactive, has children), then the expense/cost type rule; null when usable.</summary>
    public static string? AccountReason(AccountInfo? account) =>
        SalesInvoiceAccountMapRules.AccountReason(account)
        ?? (OtherPaymentPostingRules.IsExpenseAccount(account) ? null : OtherPaymentAccountMapErrors.NotExpenseAccount);
}

/// <summary>
/// WU-FI-008b3b（REQ_F4116.4.2、BR-FI-060）：authorised maintenance of <c>gl_other_payment_account_map</c>. The list has one
/// row per MD expense category of the current company (inactive included, income excluded, ordered by code); unconfigured
/// categories are version 0. A save creates (version 0) or updates (saved version) one category's row; the row version is
/// the GL <c>FI/OtherPayment</c> mapping version (ADR-211), so a save naming the same account returns changed=false and
/// keeps the version. Posted other payments keep the account and version they were posted with. Concurrent creates lose on
/// the unique company+category index, concurrent updates on the version token; both 409.
/// </summary>
public sealed class OtherPaymentAccountMapService(MyErpDbContext db, ICurrentCompany company,
    ICurrentAuthorization authorization, IChartOfAccounts accounts, IIncomeExpenseCategoryLookup categories)
{
    public const string ViewPermission = "GL.OTHER_PAYMENT_MAP.VIEW";
    public const string EditPermission = "GL.OTHER_PAYMENT_MAP.EDIT";
    private static readonly int[] DuplicateKeyErrors = [2601, 2627];

    public async Task<OtherPaymentAccountMapList> ListAsync(CancellationToken token)
    {
        var companyId = await FinanceAccess.RequireAsync(company, authorization, ViewPermission, token);
        var expenseCategories = await categories.ListAsync(IncomeExpenseDirection.Expense, token);
        var rows = await db.Set<OtherPaymentAccountMapping>().AsNoTracking().Where(x => x.CompanyId == companyId)
            .ToDictionaryAsync(x => x.ExpenseCategoryId, token);
        var items = new List<OtherPaymentAccountMapRow>(expenseCategories.Count);
        foreach (var category in expenseCategories)
            items.Add(await ToRowAsync(category.Id, category.Code, category.Name, category.IsActive,
                rows.GetValueOrDefault(category.Id), token));
        return new(items);
    }

    public async Task<OtherPaymentAccountMapWriteResult> SaveAsync(long expenseCategoryId,
        OtherPaymentAccountMapInput request, CancellationToken token)
    {
        var companyId = await FinanceAccess.RequireAsync(company, authorization, EditPermission, token);
        var input = OtherPaymentAccountMapRules.Normalize(request);
        var category = await categories.FindByIdAsync(expenseCategoryId, token)
            ?? throw new NotFoundException(OtherPaymentAccountMapErrors.CategoryNotFound, "Expense category",
                expenseCategoryId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (category.Direction != IncomeExpenseDirection.Expense)
            throw new BusinessRuleException(OtherPaymentAccountMapErrors.CategoryInvalid,
                "Only expense categories can be mapped to an other payment expense account.");

        var row = await db.Set<OtherPaymentAccountMapping>()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.ExpenseCategoryId == expenseCategoryId, token);
        if (input.Version != (row?.Version ?? 0)) throw Conflict();
        var account = await accounts.FindAsync(input.ExpenseAccountCode!, token);
        var reason = OtherPaymentAccountMapRules.AccountReason(account);
        if (reason is not null)
            throw new BusinessRuleException(OtherPaymentAccountMapErrors.AccountInvalid,
                "The account must exist, be active, have no child accounts and be an expense or cost account.")
            {
                Extensions = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["reason"] = reason, ["field"] = OtherPaymentAccountMapRules.AccountField
                }
            };
        if (row is not null && row.ExpenseAccountId == account!.Id)
            return new(await ToRowAsync(expenseCategoryId, category.Code, category.Name, category.IsActive, row, token),
                false);

        if (row is null)
        {
            row = new OtherPaymentAccountMapping { CompanyId = companyId, ExpenseCategoryId = expenseCategoryId };
            db.Add(row);
        }
        row.ExpenseAccountId = account!.Id;
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
        return new(await ToRowAsync(expenseCategoryId, category.Code, category.Name, category.IsActive, row, token), true);
    }

    private async Task<OtherPaymentAccountMapRow> ToRowAsync(long categoryId, string code, string name, bool active,
        OtherPaymentAccountMapping? row, CancellationToken token)
    {
        if (row is null) return new(categoryId, code, name, active, false, 0, null, null, null, null, null);
        var account = await accounts.FindByIdAsync(row.ExpenseAccountId, token);
        var mapped = new OtherPaymentMappedAccount(row.ExpenseAccountId, account?.Code, account?.Name,
            account?.IsActive ?? false, account?.IsPostable ?? false, OtherPaymentAccountMapRules.AccountReason(account) is null);
        return new(categoryId, code, name, active, true, row.Version, mapped, row.CreatedAt, row.CreatedBy, row.UpdatedAt,
            row.UpdatedBy);
    }

    private static ConflictException Conflict() => new(OtherPaymentAccountMapErrors.VersionConflict,
        "The other payment account map has been changed by another user. Please reload.");
}
