using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Import;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.MasterData.Application;

public sealed record ImportFieldError(string Field, string Code);
public sealed record CustomerImportRow(int RowNumber, string? Code, string? Name, string Status,
    IReadOnlyList<ImportFieldError> Errors);
public sealed record CustomerImportResult(IReadOnlyList<CustomerImportRow> Rows, int CreatedCount, int SkippedCount, int ErrorCount);

/// <summary>客戶匯入：預覽只讀；提交在公司鎖及單一交易內重新驗證與寫入。</summary>
public sealed class CustomerImportService(MyErpDbContext db, ILogger<CustomerImportService>? logger = null)
{
    private static readonly Action<ILogger, int, int, long, Exception?> LogImported =
        LoggerMessage.Define<int, int, long>(LogLevel.Information, new EventId(4401, "CustomerImported"),
            "Customer import created {CreatedCount} and skipped {SkippedCount} rows for company {CompanyId}");
    public static readonly string[] Columns = ["code", "name", "categoryCode", "contactName", "contactMobile",
        "contactPhone", "contactEmail", "address", "postalCode", "recipient", "addressPhone"];

    public async Task<CustomerImportResult> PreviewAsync(byte[] content, CancellationToken token = default) =>
        (await ValidateAsync(content, token)).Result;

    public async Task<CustomerImportResult> CommitAsync(byte[] content, CancellationToken token = default)
    {
        var companyId = RequireCompany();
        var result = await CompanyLock.RunAsync(db, "md_customer_import", companyId, async () =>
        {
            var plan = await ValidateAsync(content, token);
            if (plan.Result.ErrorCount > 0)
                throw new BusinessRuleException("MD.CUSTOMER_IMPORT.HAS_ERRORS", "Correct all import row errors before submitting.");

            foreach (var item in plan.Creates)
            {
                var customer = new Customer
                {
                    CompanyId = companyId, Code = item.Code, Name = item.Name, CategoryId = item.CategoryId,
                };
                db.Add(customer);
                item.Customer = customer;
            }

            try
            {
                await db.SaveChangesAsync(token);
                foreach (var item in plan.Creates)
                {
                    if (item.Contact is { } contact)
                        db.Add(contact.ApplyTo(new CustomerContact
                        {
                            CompanyId = companyId, PartnerId = item.Customer!.Id, Name = contact.Name, IsDefault = true,
                        }));
                    if (item.Address is { } address)
                        db.Add(address.ApplyTo(new CustomerAddress
                        {
                            CompanyId = companyId, PartnerId = item.Customer!.Id, Address = address.Address, IsDefault = true,
                        }));
                }
                await db.SaveChangesAsync(token);
            }
            catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
            {
                throw new ConflictException("MD.CUSTOMER.DUPLICATE_CODE", "Customer code already exists in this company.");
            }

            return plan.Result;
        }, token);
        if (logger is not null) LogImported(logger, result.CreatedCount, result.SkippedCount, companyId, null);
        return result;
    }

    private async Task<ImportPlan> ValidateAsync(byte[] content, CancellationToken token)
    {
        var companyId = RequireCompany();
        var sheet = TabularFileReader.Read(content, 1000);
        var columns = sheet.MapColumns(Columns, ["code", "name"]);
        var importedCodes = sheet.Rows.Select(row => row.Get(columns["code"])?.Trim().ToUpperInvariant())
            .Where(code => !string.IsNullOrEmpty(code)).Select(code => code!).Distinct(StringComparer.Ordinal).ToArray();
        var categoryCodes = columns.TryGetValue("categoryCode", out var categoryColumn)
            ? sheet.Rows.Select(row => row.Get(categoryColumn)?.Trim().ToUpperInvariant())
                .Where(code => !string.IsNullOrEmpty(code)).Select(code => code!).Distinct(StringComparer.Ordinal).ToArray()
            : [];
        var existing = (await db.Set<Customer>().IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && importedCodes.Contains(x.Code))
            .Select(x => x.Code).ToListAsync(token)).ToHashSet(StringComparer.Ordinal);
        var categories = await db.Set<CustomerCategory>().AsNoTracking().Where(x => x.IsActive && categoryCodes.Contains(x.Code))
            .ToDictionaryAsync(x => x.Code, x => x.Id, token);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var rows = new List<CustomerImportRow>();
        var creates = new List<CreateItem>();
        foreach (var row in sheet.Rows)
        {
            string? Cell(string key) => columns.TryGetValue(key, out var index) ? row.Get(index)?.Trim() : null;
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
            var contactFields = new[] { "contactName", "contactMobile", "contactPhone", "contactEmail" };
            if (contactFields.Any(key => !string.IsNullOrWhiteSpace(Cell(key))))
                TryValidate(() => ContactDetails.Validate(new ContactInput(Cell("contactName"), Mobile: Cell("contactMobile"),
                    Phone: Cell("contactPhone"), Email: Cell("contactEmail"))), "contact", Cell, errors, out contact);

            AddressDetails? address = null;
            var addressFields = new[] { "address", "postalCode", "recipient", "addressPhone" };
            if (addressFields.Any(key => !string.IsNullOrWhiteSpace(Cell(key))))
                TryValidate(() => AddressDetails.Validate(new AddressInput(Cell("address"), Cell("postalCode"),
                    Cell("recipient"), Cell("addressPhone"))), "address", Cell, errors, out address);

            rows.Add(new(row.RowNumber, codeInput, nameInput, errors.Count == 0 ? "Create" : "Error", errors));
            if (errors.Count == 0) creates.Add(new CreateItem(code!, name!, categoryId, contact, address));
        }

        return new(new(rows, creates.Count, rows.Count(x => x.Status == "Skip"), rows.Count(x => x.Status == "Error")), creates);
    }

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

    private sealed record ImportPlan(CustomerImportResult Result, List<CreateItem> Creates);
    private sealed record CreateItem(string Code, string Name, long? CategoryId, ContactDetails? Contact, AddressDetails? Address)
    {
        public Customer? Customer { get; set; }
    }
}
