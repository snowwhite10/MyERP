using MyERP.Modules.System.Application.Parameters;
using MyERP.Shared.Settings;

namespace MyERP.IntegrationTests.Auth;

/// <summary>SYS-PARAM-001：參數目錄拒絕前綴不屬於宣告模組或重複的鍵（ADR-026）。</summary>
public sealed class ParameterCatalogTests
{
    [Fact]
    public void CatalogSortsByModuleGroupAndKey()
    {
        var catalog = new ParameterCatalog([new SystemParameterDefinitions(), new Provider(ParameterDefinition.Boolean("INV.ALLOW_NEGATIVE_STOCK", "INV", "stock", false))]);
        // 分組 precision 排在 print 之前，同分組按鍵排序
        Assert.Equal(["INV.ALLOW_NEGATIVE_STOCK", SystemParameterDefinitions.QuantityDecimals, SystemParameterDefinitions.UnitPriceDecimals],
            catalog.All.Take(3).Select(p => p.Key));
        Assert.Equal(catalog.All.Skip(3).Select(p => p.Key).Order(StringComparer.Ordinal), catalog.All.Skip(3).Select(p => p.Key));
        Assert.All(catalog.All.Skip(3), p => Assert.Equal(SystemParameterDefinitions.PrintGroup, p.Group));
        Assert.Same(catalog.All[1], catalog.Find(SystemParameterDefinitions.QuantityDecimals));
        Assert.Null(catalog.Find("SYS.UNKNOWN"));
        Assert.Null(catalog.Find(null));
    }

    [Fact]
    public void CatalogRejectsForeignPrefixAndDuplicates()
    {
        Assert.Throws<InvalidOperationException>(() => new ParameterCatalog([new Provider(ParameterDefinition.Boolean("INV.FLAG", "SYS", "general", false))]));
        Assert.Throws<InvalidOperationException>(() => new ParameterCatalog([new SystemParameterDefinitions(), new SystemParameterDefinitions()]));
        Assert.ThrowsAny<Exception>(() => new ParameterCatalog([new Provider(ParameterDefinition.Boolean("XYZ.FLAG", "XYZ", "general", false))]));
    }

    private sealed class Provider(params ParameterDefinition[] parameters) : IParameterDefinitionProvider
    {
        public IReadOnlyList<ParameterDefinition> Parameters { get; } = parameters;
    }
}
