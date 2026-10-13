using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Import;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.MasterData.Application;

public sealed record ImportFieldError(string Field, string Code);
public sealed record PartnerImportRow(int RowNumber, string? Code, string? Name, string Status,
    IReadOnlyList<ImportFieldError> Errors);
public sealed record PartnerImportResult(IReadOnlyList<PartnerImportRow> Rows, int CreatedCount, int SkippedCount, int ErrorCount);

/// <summary>客戶匯入：主檔、預設聯絡人與預設送貨地址。</summary>
public sealed class CustomerImportService(MyErpDbContext db, ILogger<CustomerImportService>? logger = null)
    : PartnerImportService<Customer, CustomerCategory>(db, logger, "md_customer_import", "MD.CUSTOMER", "Customer", Columns)
{
    public static readonly string[] Columns = [.. BaseColumns, .. AddressColumns];

    protected override Customer NewPartner(long companyId, string code, string name, long? categoryId) =>
        new() { CompanyId = companyId, Code = code, Name = name, CategoryId = categoryId };

    protected override string CategoryLock => CompanyLock.CustomerCategory;

    private protected override PartnerChild NewContact(long companyId, long partnerId, ContactDetails contact) =>
        contact.ApplyTo(new CustomerContact { CompanyId = companyId, PartnerId = partnerId, Name = contact.Name, IsDefault = true });

    private protected override PartnerChild NewAddress(long companyId, long partnerId, AddressDetails address) =>
        address.ApplyTo(new CustomerAddress { CompanyId = companyId, PartnerId = partnerId, Address = address.Address, IsDefault = true });
}

/// <summary>供應商匯入：主檔與預設聯絡人；供應商沒有地址子表，地址欄不屬於範本（ADR-043）。</summary>
public sealed class SupplierImportService(MyErpDbContext db, ILogger<SupplierImportService>? logger = null)
    : PartnerImportService<Supplier, SupplierCategory>(db, logger, "md_supplier_import", "MD.SUPPLIER", "Supplier", Columns)
{
    public static readonly string[] Columns = BaseColumns;

    protected override Supplier NewPartner(long companyId, string code, string name, long? categoryId) =>
        new() { CompanyId = companyId, Code = code, Name = name, CategoryId = categoryId };

    protected override string CategoryLock => CompanyLock.SupplierCategory;

    private protected override PartnerChild NewContact(long companyId, long partnerId, ContactDetails contact) =>
        contact.ApplyTo(new SupplierContact { CompanyId = companyId, PartnerId = partnerId, Name = contact.Name, IsDefault = true });
}

/// <summary>
/// 往來對象匯入（ADR-044）：預覽只讀；提交在公司鎖及單一交易內重新驗證與寫入。
/// 已存在的代碼略過（冪等），任何一列有錯即整批不寫入。
/// </summary>
public abstract class PartnerImportService<TPartner, TCategory>(MyErpDbContext db, ILogger? logger, string lockName,
    string errorPrefix, string label, IReadOnlyList<string> columns)
    where TPartner : BusinessPartner<TCategory>
    where TCategory : MasterCategory
{
    private static readonly Action<ILogger, string, int, int, long, Exception?> LogImported =
        LoggerMessage.Define<string, int, int, long>(LogLevel.Information, new EventId(4401, "PartnerImported"),
            "{Partner} import created {CreatedCount} and skipped {SkippedCount} rows for company {CompanyId}");
    protected static readonly string[] BaseColumns = ["code", "name", "categoryCode", "contactName", "contactMobile",
        "contactPhone", "contactEmail"];
    protected static readonly string[] AddressColumns = ["address", "postalCode", "recipient", "addressPhone"];
    private static readonly string[] ContactColumns = ["contactName", "contactMobile", "contactPhone", "contactEmail"];

    protected abstract TPartner NewPartner(long companyId, string code, string name, long? categoryId);

    /// <summary>分類資料表的 <see cref="CompanyLock"/> 鎖名；提交期間以共享鎖持有（MD-B03）。</summary>
    protected abstract string CategoryLock { get; }

    private protected abstract PartnerChild NewContact(long companyId, long partnerId, ContactDetails contact);

    /// <summary>只有範本含地址欄時才會呼叫。</summary>
    private protected virtual PartnerChild NewAddress(long companyId, long partnerId, AddressDetails address) =>
        throw new NotSupportedException($"{label} import has no address columns.");

    public async Task<PartnerImportResult> PreviewAsync(byte[] content, CancellationToken token = default) =>
        (await ValidateAsync(content, token)).Result;

    public async Task<PartnerImportResult> CommitAsync(byte[] content, CancellationToken token = default)
    {
        var companyId = RequireCompany();
        var result = await CompanyLock.RunAsync(db, lockName, companyId, () => CompanyLock.RunSharedAsync(db, [CategoryLock], companyId, async () =>
        {
            var plan = await ValidateAsync(content, token);
            if (plan.Result.ErrorCount > 0)
                throw new BusinessRuleException(errorPrefix + "_IMPORT.HAS_ERRORS", "Correct all import row errors before submitting.");

            foreach (var item in plan.Creates)
            {
                item.Partner = NewPartner(companyId, item.Code, item.Name, item.CategoryId);
                db.Add(item.Partner);
            }

            try
            {
                await db.SaveChangesAsync(token);
                foreach (var item in plan.Creates)
                {
                    if (item.Contact is { } contact) db.Add(NewContact(companyId, item.Partner!.Id, contact));
                    if (item.Address is { } address) db.Add(NewAddress(companyId, item.Partner!.Id, address));
                }
                await db.SaveChangesAsync(token);
            }
            catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
            {
                throw new ConflictException(errorPrefix + ".DUPLICATE_CODE", $"{label} code already exists in this company.");
            }

            return plan.Result;
        }, token), token);
        if (logger is not null) LogImported(logger, label, result.CreatedCount, result.SkippedCount, companyId, null);
        return result;
    }

    private async Task<ImportPlan> ValidateAsync(byte[] content, CancellationToken token)
    {
        var companyId = RequireCompany();
        var sheet = TabularFileReader.Read(content, 1000);
        var map = sheet.MapColumns(columns, ["code", "name"]);
        var importedCodes = DistinctCodes(sheet, map, "code");
        var categoryCodes = DistinctCodes(sheet, map, "categoryCode");
        var existing = (await db.Set<TPartner>().IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && importedCodes.Contains(x.Code))
            .Select(x => x.Code).ToListAsync(token)).ToHashSet(StringComparer.Ordinal);
        var categories = await db.Set<TCategory>().AsNoTracking().Where(x => x.IsActive && categoryCodes.Contains(x.Code))
            .ToDictionaryAsync(x => x.Code, x => x.Id, token);
        var hasAddress = columns.Contains(AddressColumns[0]);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var rows = new List<PartnerImportRow>();
        var creates = new List<CreateItem>();
        foreach (var row in sheet.Rows)
        {
            string? Cell(string key) => map.TryGetValue(key, out var index) ? row.Get(index)?.Trim() : null;
            var codeInput = Cell("code");
            var nameInput = Cell("name");
            var errors = new List<ImportFieldError>();
            string? code = null;
            try { code = MasterDataInput.Code(codeInput); }
            catch (RequestValidationException) { errors.Add(new("code", string.IsNullOrWhiteSpace(codeInput) ? "REQUIRED" : "INVALID")); }

            if (code is not null && !seen.Add(code)) errors.Add(new("code", "DUPLICATE_IN_FILE"));
            if (code is not null && existing.Contains(code) && errors.Count == 0)
            {
                rows.Add(new(row.RowNumber, code, nameInput, "Skip", []));
                continue;
            }

            string? name = null;
            try { name = MasterDataInput.Name(nameInput); }
            catch (RequestValidationException) { errors.Add(new("name", string.IsNullOrWhiteSpace(nameInput) ? "REQUIRED" : "INVALID")); }

            long? categoryId = null;
            var categoryCode = Cell("categoryCode");
            if (!string.IsNullOrWhiteSpace(categoryCode))
            {
                if (categories.TryGetValue(categoryCode.ToUpperInvariant(), out var found)) categoryId = found;
                else errors.Add(new("categoryCode", "NOT_FOUND"));
            }

            ContactDetails? contact = null;
            if (ContactColumns.Any(key => !string.IsNullOrWhiteSpace(Cell(key))))
                TryValidate(() => ContactDetails.Validate(new ContactInput(Cell("contactName"), Mobile: Cell("contactMobile"),
                    Phone: Cell("contactPhone"), Email: Cell("contactEmail"))), "contact", Cell, errors, out contact);

            AddressDetails? address = null;
            if (hasAddress && AddressColumns.Any(key => !string.IsNullOrWhiteSpace(Cell(key))))
                TryValidate(() => AddressDetails.Validate(new AddressInput(Cell("address"), Cell("postalCode"),
                    Cell("recipient"), Cell("addressPhone"))), "address", Cell, errors, out address);

            rows.Add(new(row.RowNumber, codeInput, nameInput, errors.Count == 0 ? "Create" : "Error", errors));
            if (errors.Count == 0) creates.Add(new CreateItem(code!, name!, categoryId, contact, address));
        }

        return new(new(rows, creates.Count, rows.Count(x => x.Status == "Skip"), rows.Count(x => x.Status == "Error")), creates);
    }

    private static string[] DistinctCodes(TabularSheet sheet, IReadOnlyDictionary<string, int> map, string key) =>
        map.TryGetValue(key, out var column)
            ? sheet.Rows.Select(row => row.Get(column)?.Trim().ToUpperInvariant())
                .Where(code => !string.IsNullOrEmpty(code)).Select(code => code!).Distinct(StringComparer.Ordinal).ToArray()
            : [];

    private static void TryValidate<T>(Func<T> validate, string prefix, Func<string, string?> cell,
        List<ImportFieldError> errors, out T? value) where T : class
    {
        try { value = validate(); }
        catch (RequestValidationException ex)
        {
            value = null;
            foreach (var field in ex.Errors.Keys)
            {
                var mapped = prefix == "contact" ? field switch
                {
                    "name" => "contactName", "mobile" => "contactMobile", "phone" => "contactPhone", "email" => "contactEmail", _ => field,
                } : field == "phone" ? "addressPhone" : field;
                errors.Add(new(mapped, string.IsNullOrWhiteSpace(cell(mapped)) ? "REQUIRED" : "INVALID"));
            }
        }
    }

    private long RequireCompany() => db.CurrentCompanyId is > 0 ? db.CurrentCompanyId.Value
        : throw new ForbiddenException("COMMON.COMPANY.ACCESS_DENIED", "Current company is required.");

    private sealed record ImportPlan(PartnerImportResult Result, List<CreateItem> Creates);
    private sealed record CreateItem(string Code, string Name, long? CategoryId, ContactDetails? Contact, AddressDetails? Address)
    {
        public TPartner? Partner { get; set; }
    }
}
