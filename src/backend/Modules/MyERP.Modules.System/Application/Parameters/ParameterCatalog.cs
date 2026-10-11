using MyERP.Modules.System.Contracts;
using MyERP.Shared.Modules;
using MyERP.Shared.Settings;

namespace MyERP.Modules.System.Application.Parameters;

/// <summary>彙整各模組宣告的系統參數；建立時驗證鍵前綴屬於宣告模組、鍵不重複（ADR-026）。</summary>
public sealed class ParameterCatalog
{
    private readonly Dictionary<string, ParameterDefinition> byKey;

    public ParameterCatalog(IEnumerable<IParameterDefinitionProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        byKey = new Dictionary<string, ParameterDefinition>(StringComparer.Ordinal);
        foreach (var definition in providers.SelectMany(p => p.Parameters))
        {
            var module = ErpModuleCatalog.Get(definition.Module);
            var prefix = definition.Key[..definition.Key.IndexOf('.', StringComparison.Ordinal)];
            if (!module.Prefixes.Contains(prefix, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Parameter '{definition.Key}' does not use a prefix of module '{module.Code}'.");
            if (!byKey.TryAdd(definition.Key, definition))
                throw new InvalidOperationException($"Parameter '{definition.Key}' is declared more than once.");
        }

        All = [.. byKey.Values.OrderBy(p => p.Module, StringComparer.Ordinal).ThenBy(p => p.Group, StringComparer.Ordinal).ThenBy(p => p.Key, StringComparer.Ordinal)];
    }

    /// <summary>依模組、分組、鍵排序。</summary>
    public IReadOnlyList<ParameterDefinition> All { get; }

    public ParameterDefinition? Find(string? key) => key is not null && byKey.TryGetValue(key, out var definition) ? definition : null;
}

/// <summary>SYS 模組自己的參數。其他模組（例如 INV 的負庫存、MD 的自訂欄位名稱）在各自模組宣告。</summary>
public sealed class SystemParameterDefinitions : IParameterDefinitionProvider
{
    public const string QuantityDecimals = "SYS.QUANTITY_DECIMALS";
    public const string UnitPriceDecimals = "SYS.UNIT_PRICE_DECIMALS";

    /// <summary>列印設定分組（ADR-027）：由 /sys/print-settings 維護，經 <see cref="IPrintSettingsReader"/> 讀取。</summary>
    public const string PrintGroup = "print";
    public const string PrintCompanyName = "SYS.PRINT_COMPANY_NAME";
    public const string PrintAddress = "SYS.PRINT_ADDRESS";
    public const string PrintPostalCode = "SYS.PRINT_POSTAL_CODE";
    public const string PrintPhone = "SYS.PRINT_PHONE";
    public const string PrintFax = "SYS.PRINT_FAX";
    public const string PrintPurchaseOrderFooter = "SYS.PRINT_PURCHASE_ORDER_FOOTER";
    public const string PrintSalesDeliveryFooter = "SYS.PRINT_SALES_DELIVERY_FOOTER";
    public const string PrintShowLogo = "SYS.PRINT_SHOW_LOGO";
    public const string PrintShowStamp = "SYS.PRINT_SHOW_STAMP";

    private const string Module = SystemModuleContracts.ModuleCode;

    public IReadOnlyList<ParameterDefinition> Parameters { get; } =
    [
        // 資料庫數量欄位為 DECIMAL(19,4)、單價為 DECIMAL(19,6)，參數只能在此精度內收窄輸入與顯示的位數
        ParameterDefinition.WholeNumber(QuantityDecimals, Module, "precision", 2, 0, 4),
        ParameterDefinition.WholeNumber(UnitPriceDecimals, Module, "precision", 4, 0, 6),
        // 列印抬頭：公司名稱空白時列印公司主檔名稱
        ParameterDefinition.Text(PrintCompanyName, Module, PrintGroup, "", 100),
        ParameterDefinition.Text(PrintAddress, Module, PrintGroup, "", 200),
        ParameterDefinition.Text(PrintPostalCode, Module, PrintGroup, "", 10),
        ParameterDefinition.Text(PrintPhone, Module, PrintGroup, "", 30),
        ParameterDefinition.Text(PrintFax, Module, PrintGroup, "", 30),
        ParameterDefinition.Text(PrintPurchaseOrderFooter, Module, PrintGroup, "", 500),
        ParameterDefinition.Text(PrintSalesDeliveryFooter, Module, PrintGroup, "", 500),
        ParameterDefinition.Boolean(PrintShowLogo, Module, PrintGroup, false),
        ParameterDefinition.Boolean(PrintShowStamp, Module, PrintGroup, false),
    ];
}
