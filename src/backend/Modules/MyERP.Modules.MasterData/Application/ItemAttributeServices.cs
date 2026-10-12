using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.MasterData.Application;

/// <summary>
/// 物料屬性定義 CRUD（REQ_F4118.45.1–4、ADR-045）；列表預設按排序號。被物料使用的屬性不可刪除，只可停用；
/// 刪除與物料屬性整批取代共用公司級 applock，避免物料剛設定的屬性被刪除。
/// </summary>
public sealed class ItemAttributeService(MyErpDbContext db)
    : MasterCategoryService<ItemAttributeDefinition>(db, "MD.ITEM_ATTRIBUTE", "Item attribute")
{
    internal const string LockName = "md_item_attribute";
    private static readonly string[] Sorts = ["sortOrder", "code", "name"];

    public Task<ItemAttributeDefinition> CreateAsync(string? code, string? name, int sortOrder, CancellationToken token = default)
    {
        CheckSortOrder(sortOrder);
        return CreateCoreAsync(code, name, x => x.SortOrder = sortOrder, token);
    }

    public Task<ItemAttributeDefinition> UpdateAsync(long id, string? name, int sortOrder, bool isActive, int version, CancellationToken token = default)
    {
        CheckSortOrder(sortOrder);
        return UpdateCoreAsync(id, name, isActive, version, x => x.SortOrder = sortOrder, token);
    }

    public override Task DeleteAsync(long id, int version, CancellationToken token = default) =>
        CompanyLock.RunAsync(Db, LockName, RequireCompany(), async () =>
        {
            await base.DeleteAsync(id, version, token);
            return true;
        }, token);

    /// <summary>各屬性的使用物料數（列表顯示用）；沒有物料使用的屬性不在結果內。</summary>
    public async Task<IReadOnlyDictionary<long, int>> ItemCountsAsync(IReadOnlyCollection<long> attributeIds, CancellationToken token = default)
    {
        RequireCompany();
        if (attributeIds.Count == 0) return new Dictionary<long, int>();
        return await Db.Set<ItemAttributeValue>().AsNoTracking()
            .Where(x => attributeIds.Contains(x.AttributeId))
            .GroupBy(x => x.AttributeId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, token);
    }

    protected override ItemAttributeDefinition New(long companyId, string code, string name) => new() { CompanyId = companyId, Code = code, Name = name };

    protected override IReadOnlyCollection<string> SortFields => Sorts;

    protected override string DefaultSortField => "sortOrder";

    protected override IQueryable<ItemAttributeDefinition>? OrderBy(IQueryable<ItemAttributeDefinition> query, string field, bool descending) =>
        field != "sortOrder" ? null
        : descending ? query.OrderByDescending(x => x.SortOrder).ThenBy(x => x.Code).ThenBy(x => x.Id)
        : query.OrderBy(x => x.SortOrder).ThenBy(x => x.Code).ThenBy(x => x.Id);

    protected override async Task EnsureNotInUseAsync(long id, CancellationToken token)
    {
        if (await Db.Set<ItemAttributeValue>().AnyAsync(x => x.AttributeId == id, token))
            throw new ConflictException(ErrorPrefix + ".IN_USE", "Attribute is used by items; deactivate it instead.");
    }

    private static void CheckSortOrder(int value)
    {
        if (value is < 0 or > ItemAttributeDefinition.MaxSortOrder)
            throw new RequestValidationException("sortOrder", $"Use 0–{ItemAttributeDefinition.MaxSortOrder}.");
    }
}

/// <summary>整批取代物料屬性時的一個屬性值。</summary>
public sealed record ItemAttributeValueInput(long AttributeId, string? Value);

/// <summary>物料的屬性值，附屬性代碼、名稱、排序號與啟用狀態。</summary>
public sealed record ItemAttributeValueView(long AttributeId, string AttributeCode, string AttributeName, int SortOrder,
    bool IsAttributeActive, string Value);

/// <summary>
/// 物料屬性值（REQ_F4118.45.5–6、ADR-045）：以完整集合整批取代，每物料最多 <see cref="MaxAttributesPerItem"/> 個；
/// 新設定的屬性必須啟用，原有值的屬性停用後仍可保留。取代在公司級 applock 內進行，與屬性刪除互斥。
/// </summary>
public sealed class ItemAttributeValueService(MyErpDbContext db)
{
    public const int MaxAttributesPerItem = 50;

    /// <summary>按屬性排序號、代碼列出物料的屬性值（含已停用屬性）。</summary>
    public async Task<IReadOnlyList<ItemAttributeValueView>> ListAsync(long itemId, CancellationToken token = default)
    {
        RequireCompany();
        await RequireItemAsync(itemId, token);
        return await QueryAsync(itemId, token);
    }

    /// <summary>以 <paramref name="values"/> 取代物料全部屬性值：未列出的刪除，值改變的更新，新屬性新增；空集合清除全部。</summary>
    public async Task<IReadOnlyList<ItemAttributeValueView>> ReplaceAsync(
        long itemId, IReadOnlyList<ItemAttributeValueInput>? values, CancellationToken token = default)
    {
        var companyId = RequireCompany();
        var requested = Normalize(values);
        await RequireItemAsync(itemId, token);
        return await CompanyLock.RunAsync(db, ItemAttributeService.LockName, companyId, async () =>
        {
            var existing = await db.Set<ItemAttributeValue>().Where(x => x.ItemId == itemId).ToListAsync(token);
            var ids = requested.Select(x => x.AttributeId).ToList();
            var active = await db.Set<ItemAttributeDefinition>().AsNoTracking().Where(x => ids.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.IsActive, token);
            for (var i = 0; i < requested.Count; i++)
            {
                var id = requested[i].AttributeId;
                if (!active.TryGetValue(id, out var isActive) || !isActive && existing.All(x => x.AttributeId != id))
                    throw new RequestValidationException($"attributes[{i}].attributeId", "Attribute must be active in the current company.");
            }

            db.RemoveRange(existing.Where(x => !ids.Contains(x.AttributeId)));
            foreach (var (attributeId, value) in requested)
            {
                var current = existing.SingleOrDefault(x => x.AttributeId == attributeId);
                if (current is null)
                    db.Add(new ItemAttributeValue { CompanyId = companyId, ItemId = itemId, AttributeId = attributeId, Value = value });
                else if (current.Value != value)
                    current.Value = value;
            }

            try { await db.SaveChangesAsync(token); }
            catch (DbUpdateConcurrencyException) { throw new ConflictException(); }
            return await QueryAsync(itemId, token);
        }, token);
    }

    private Task<List<ItemAttributeValueView>> QueryAsync(long itemId, CancellationToken token) =>
        db.Set<ItemAttributeValue>().AsNoTracking().Where(x => x.ItemId == itemId)
            .Join(db.Set<ItemAttributeDefinition>(), v => v.AttributeId, a => a.Id, (v, a) => new { v.Value, Attribute = a })
            .OrderBy(x => x.Attribute.SortOrder).ThenBy(x => x.Attribute.Code).ThenBy(x => x.Attribute.Id)
            .Select(x => new ItemAttributeValueView(x.Attribute.Id, x.Attribute.Code, x.Attribute.Name, x.Attribute.SortOrder,
                x.Attribute.IsActive, x.Value))
            .ToListAsync(token);

    /// <summary>檢查集合大小、重複屬性與值（去除前後空白、必填、最多 200 字）；錯誤鍵帶索引，例如 <c>attributes[1].value</c>。</summary>
    private static List<(long AttributeId, string Value)> Normalize(IReadOnlyList<ItemAttributeValueInput>? values)
    {
        if (values is null) throw new RequestValidationException("attributes", "Attributes are required; send an empty list to clear them.");
        if (values.Count > MaxAttributesPerItem)
            throw new RequestValidationException("attributes", $"An item can have at most {MaxAttributesPerItem} attributes.");
        var result = new List<(long, string)>(values.Count);
        var seen = new HashSet<long>();
        for (var i = 0; i < values.Count; i++)
        {
            var input = values[i] ?? throw new RequestValidationException($"attributes[{i}]", "Attribute is required.");
            if (input.AttributeId <= 0)
                throw new RequestValidationException($"attributes[{i}].attributeId", "Attribute is required.");
            if (!seen.Add(input.AttributeId))
                throw new RequestValidationException($"attributes[{i}].attributeId", "Choose each attribute once.");
            var value = input.Value?.Trim();
            if (string.IsNullOrEmpty(value) || value.Length > ItemAttributeValue.MaxValueLength)
                throw new RequestValidationException($"attributes[{i}].value", $"Required; maximum {ItemAttributeValue.MaxValueLength} characters.");
            result.Add((input.AttributeId, value));
        }
        return result;
    }

    private async Task RequireItemAsync(long itemId, CancellationToken token)
    {
        if (!await db.Set<Item>().AnyAsync(x => x.Id == itemId, token))
            throw new NotFoundException("MD.ITEM.NOT_FOUND", "Item", itemId);
    }

    private long RequireCompany() => db.CurrentCompanyId is > 0 ? db.CurrentCompanyId.Value
        : throw new ForbiddenException("COMMON.COMPANY.ACCESS_DENIED", "Current company is required.");
}
