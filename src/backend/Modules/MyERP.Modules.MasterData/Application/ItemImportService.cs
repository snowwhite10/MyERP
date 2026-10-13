using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Import;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.MasterData.Application;

public sealed record ItemImportRow(int RowNumber, string? Code, string? Name, string Status,
    IReadOnlyList<ImportFieldError> Errors);
public sealed record ItemImportResult(IReadOnlyList<ItemImportRow> Rows, int CreatedCount, int SkippedCount, int ErrorCount);

/// <summary>物料主檔匯入；不建立庫存或期初餘額。預覽唯讀，提交時在公司鎖內重新驗證。</summary>
public sealed class ItemImportService(MyErpDbContext db, ILogger<ItemImportService>? logger = null)
{
    public static readonly string[] Columns = ["code", "name", "specification", "baseUomCode", "brandCode", "categoryCode"];
    private static readonly Action<ILogger, int, int, long, Exception?> LogImported =
        LoggerMessage.Define<int, int, long>(LogLevel.Information, new EventId(4402, "ItemImported"),
            "Item import created {CreatedCount} and skipped {SkippedCount} rows for company {CompanyId}");

    public async Task<ItemImportResult> PreviewAsync(byte[] content, CancellationToken token = default) =>
        (await ValidateAsync(content, token)).Result;

    public async Task<ItemImportResult> CommitAsync(byte[] content, CancellationToken token = default)
    {
        var companyId = RequireCompany();
        // 匯入排他鎖內再以共享鎖持有單位、品牌、分類，避免引用剛刪除的資料（MD-B03）。
        var result = await CompanyLock.RunAsync(db, "md_item_import", companyId, () => CompanyLock.RunSharedAsync(db, ItemService.ReferencedTables, companyId, async () =>
        {
            var plan = await ValidateAsync(content, token);
            if (plan.Result.ErrorCount > 0)
                throw new BusinessRuleException("MD.ITEM_IMPORT.HAS_ERRORS", "Correct all import row errors before submitting.");
            foreach (var item in plan.Creates)
                db.Add(new Item { CompanyId = companyId, Code = item.Code, Name = item.Name,
                    Specification = item.Specification, BaseUomId = item.BaseUomId,
                    BrandId = item.BrandId, CategoryId = item.CategoryId });
            try { await db.SaveChangesAsync(token); }
            catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
            {
                throw new ConflictException("MD.ITEM.DUPLICATE_CODE", "Item code already exists in this company.");
            }
            return plan.Result;
        }, token), token);
        if (logger is not null) LogImported(logger, result.CreatedCount, result.SkippedCount, companyId, null);
        return result;
    }

    private async Task<ImportPlan> ValidateAsync(byte[] content, CancellationToken token)
    {
        var companyId = RequireCompany();
        var sheet = TabularFileReader.Read(content, 1000);
        var map = sheet.MapColumns(Columns, ["code", "name", "baseUomCode"]);
        string[] Codes(string key) => map.TryGetValue(key, out var index)
            ? [.. sheet.Rows.Select(row => row.Get(index)?.Trim().ToUpperInvariant())
                .Where(code => !string.IsNullOrEmpty(code)).Select(code => code!).Distinct(StringComparer.Ordinal)]
            : [];
        var itemCodes = Codes("code");
        var uomCodes = Codes("baseUomCode");
        var brandCodes = Codes("brandCode");
        var categoryCodes = Codes("categoryCode");
        var existing = (await db.Set<Item>().IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && itemCodes.Contains(x.Code))
            .Select(x => x.Code).ToListAsync(token)).ToHashSet(StringComparer.Ordinal);
        var uoms = await db.Set<UnitOfMeasure>().AsNoTracking().Where(x => x.IsActive && uomCodes.Contains(x.Code))
            .ToDictionaryAsync(x => x.Code, x => x.Id, token);
        var brands = await db.Set<ItemBrand>().AsNoTracking().Where(x => x.IsActive && brandCodes.Contains(x.Code))
            .ToDictionaryAsync(x => x.Code, x => x.Id, token);
        var categories = await db.Set<ItemCategory>().AsNoTracking().Where(x => x.IsActive && categoryCodes.Contains(x.Code))
            .ToDictionaryAsync(x => x.Code, x => x.Id, token);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var rows = new List<ItemImportRow>();
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
            string? specification = null;
            try { specification = MasterDataInput.OptionalText(Cell("specification"), "specification", 500); }
            catch (RequestValidationException) { errors.Add(new("specification", "INVALID")); }
            long? Reference(string key, IReadOnlyDictionary<string, long> values, bool required = false)
            {
                var input = Cell(key);
                if (string.IsNullOrWhiteSpace(input))
                {
                    if (required) errors.Add(new(key, "REQUIRED"));
                    return null;
                }
                if (values.TryGetValue(input.ToUpperInvariant(), out var id)) return id;
                errors.Add(new(key, "NOT_FOUND"));
                return null;
            }
            var uomId = Reference("baseUomCode", uoms, true);
            var brandId = Reference("brandCode", brands);
            var categoryId = Reference("categoryCode", categories);
            rows.Add(new(row.RowNumber, codeInput, nameInput, errors.Count == 0 ? "Create" : "Error", errors));
            if (errors.Count == 0) creates.Add(new CreateItem(code!, name!, specification, uomId!.Value, brandId, categoryId));
        }
        return new(new(rows, creates.Count, rows.Count(x => x.Status == "Skip"), rows.Count(x => x.Status == "Error")), creates);
    }

    private long RequireCompany() => db.CurrentCompanyId is > 0 ? db.CurrentCompanyId.Value
        : throw new ForbiddenException("COMMON.COMPANY.ACCESS_DENIED", "Current company is required.");

    private sealed record ImportPlan(ItemImportResult Result, List<CreateItem> Creates);
    private sealed record CreateItem(string Code, string Name, string? Specification, long BaseUomId,
        long? BrandId, long? CategoryId);
}
