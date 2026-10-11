using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.System.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Settings;

namespace MyERP.Modules.System.Application.Parameters;

/// <summary>
/// 參數與目前公司的值。<c>Version</c> 為 0 表示尚未修改過（使用預設值、資料庫沒有資料列）；
/// <c>Minimum</c>／<c>Maximum</c>／<c>MaxLength</c>／<c>Options</c> 讓前端產生對應的輸入欄位。
/// </summary>
public sealed record ParameterDetail(
    string Key, string Module, string Group, ParameterType Type, string Value, string DefaultValue, bool IsDefault,
    decimal? Minimum, decimal? Maximum, int MaxLength, IReadOnlyList<string> Options, int Version);

/// <summary>修改一個參數值，須帶讀取時的 <c>version</c>（未修改過為 0）。</summary>
public sealed record ParameterValueUpdate(string Key, string? Value, int Version);

/// <summary>一次保存多個參數（同一交易，全部成功或全部失敗）。</summary>
public sealed record ParameterBatchUpdate(IReadOnlyList<ParameterValueUpdate>? Items);

/// <summary>系統參數維護（REQ_F4118.17.1–3）。只處理目前公司的值；呼叫端以權限代碼保護。</summary>
public sealed class SystemParameterService(MyErpDbContext db, ParameterCatalog catalog)
{
    public const int MaxBatchSize = 200;

    public async Task<IReadOnlyList<ParameterDetail>> ListAsync(string? module, CancellationToken cancellationToken = default)
    {
        await ActiveCompanyIdAsync(cancellationToken);
        var definitions = catalog.All.Where(d => string.IsNullOrWhiteSpace(module) || string.Equals(d.Module, module.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        var stored = await db.Set<SystemParameter>().AsNoTracking().ToDictionaryAsync(p => p.ParamKey, StringComparer.Ordinal, cancellationToken);
        return definitions.Select(d => ToDetail(d, stored.GetValueOrDefault(d.Key))).ToList();
    }

    /// <summary>
    /// 驗證全部項目後才寫入；任何錯誤（未知鍵、重複鍵、值不合規則）以 400 一次列出，欄位為 <c>items[i].key</c>／<c>items[i].value</c>。
    /// 版本不符回 409。值與目前值相同的項目不寫入（不遞增版本、不產生稽核）。回傳修改後的完整清單。
    /// </summary>
    public async Task<IReadOnlyList<ParameterDetail>> UpdateAsync(ParameterBatchUpdate input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var companyId = await ActiveCompanyIdAsync(cancellationToken);
        var items = input.Items ?? [];
        if (items.Count is 0 or > MaxBatchSize)
            throw new RequestValidationException("items", $"Provide 1–{MaxBatchSize} parameters.");

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var changes = new List<(ParameterDefinition Definition, string Value, int Version)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var path = string.Create(CultureInfo.InvariantCulture, $"items[{i}]");
            var definition = catalog.Find(item?.Key?.Trim().ToUpperInvariant());
            if (item is null || definition is null)
            {
                errors[path + ".key"] = ["Unknown parameter."];
                continue;
            }

            if (!seen.Add(definition.Key))
            {
                errors[path + ".key"] = ["Each parameter can appear only once."];
                continue;
            }

            try
            {
                changes.Add((definition, definition.Normalize(item.Value, path + ".value"), item.Version));
            }
            catch (RequestValidationException ex)
            {
                foreach (var (field, messages) in ex.Errors) errors[field] = messages;
            }
        }

        if (errors.Count > 0) throw new RequestValidationException(errors);

        var keys = changes.Select(c => c.Definition.Key).ToArray();
        var stored = await db.Set<SystemParameter>().Where(p => keys.Contains(p.ParamKey)).ToDictionaryAsync(p => p.ParamKey, StringComparer.Ordinal, cancellationToken);
        foreach (var (definition, value, version) in changes)
        {
            if (stored.TryGetValue(definition.Key, out var row))
            {
                if (row.Version != version) throw new ConflictException();
                if (row.ParamValue == value) continue;
                row.ParamValue = value;
                db.Entry(row).Property(x => x.Version).OriginalValue = version;
            }
            else
            {
                if (version != 0) throw new ConflictException();
                if (value == definition.DefaultValue) continue;
                db.Add(new SystemParameter { CompanyId = companyId, ParamKey = definition.Key, ParamValue = value });
            }
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException();
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // 另一個請求同時建立了同一參數的資料列
            throw new ConflictException();
        }

        return await ListAsync(null, cancellationToken);
    }

    private static ParameterDetail ToDetail(ParameterDefinition definition, SystemParameter? row)
    {
        // 參數型別或範圍收窄後，舊值可能已不合規則：讀取時退回預設值，保存時再驗證
        var value = row is not null && IsValid(definition, row.ParamValue) ? row.ParamValue : definition.DefaultValue;
        return new ParameterDetail(
            definition.Key, definition.Module, definition.Group, definition.Type, value, definition.DefaultValue, value == definition.DefaultValue,
            definition.Minimum, definition.Maximum, definition.MaxLength, definition.Options, row?.Version ?? 0);
    }

    internal static bool IsValid(ParameterDefinition definition, string value)
    {
        try
        {
            return definition.Normalize(value) == value;
        }
        catch (RequestValidationException)
        {
            return false;
        }
    }

    private async Task<long> ActiveCompanyIdAsync(CancellationToken token)
    {
        var id = db.CurrentCompanyId;
        if (id is null || !await db.Set<SystemCompany>().AnyAsync(x => x.Id == id && x.IsActive, token))
            throw new ForbiddenException("SYS.COMPANY.UNAVAILABLE", "An active company context is required.");
        return id.Value;
    }
}

/// <summary><see cref="ISystemParameters"/> 實作：同一 scope 內第一次讀取時載入目前公司的全部參數值。</summary>
public sealed class SystemParameterReader(MyErpDbContext db, ParameterCatalog catalog) : ISystemParameters
{
    private Dictionary<string, string>? values;
    private long? loadedCompanyId;

    public async Task<string> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var definition = catalog.Find(key) ?? throw new ArgumentException($"Parameter '{key}' is not declared.", nameof(key));
        var companyId = db.CurrentCompanyId ?? throw new ForbiddenException("SYS.COMPANY.UNAVAILABLE", "An active company context is required.");
        if (values is null || loadedCompanyId != companyId)
        {
            values = await db.Set<SystemParameter>().AsNoTracking()
                .ToDictionaryAsync(p => p.ParamKey, p => p.ParamValue, StringComparer.Ordinal, cancellationToken);
            loadedCompanyId = companyId;
        }

        return values.TryGetValue(definition.Key, out var value) && SystemParameterService.IsValid(definition, value) ? value : definition.DefaultValue;
    }

    public async Task<bool> GetBooleanAsync(string key, CancellationToken cancellationToken = default) =>
        bool.Parse(await GetAsync(Expect(key, ParameterType.Boolean), cancellationToken));

    public async Task<int> GetIntegerAsync(string key, CancellationToken cancellationToken = default) =>
        int.Parse(await GetAsync(Expect(key, ParameterType.WholeNumber), cancellationToken), CultureInfo.InvariantCulture);

    public async Task<decimal> GetDecimalAsync(string key, CancellationToken cancellationToken = default) =>
        decimal.Parse(await GetAsync(Expect(key, ParameterType.Number), cancellationToken), CultureInfo.InvariantCulture);

    private string Expect(string key, ParameterType type) =>
        catalog.Find(key) is { } definition && definition.Type != type
            ? throw new ArgumentException($"Parameter '{key}' is {definition.Type}, not {type}.", nameof(key))
            : key;
}
