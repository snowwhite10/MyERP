using System.Text.RegularExpressions;
using MyERP.Shared.Modules;

namespace MyERP.Shared.Tests.Modules;

public sealed partial class ErpModuleCatalogTests
{
    [GeneratedRegex("^[a-z]{2,3}$")]
    private static partial Regex PrefixPattern();

    [Fact]
    public void All_ContainsFifteenModulesFromMasterPrompt()
    {
        ErpModuleCatalog.All.Select(m => m.Code).Should().BeEquivalentTo(
            ["SYS", "MD", "INV", "PUR", "SD", "FI", "CO", "BI", "PP", "QM", "PM", "CRM", "HR", "PS", "SCM"]);
    }

    [Fact]
    public void Codes_AreUnique()
    {
        ErpModuleCatalog.All.Select(m => m.Code).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Prefixes_AreUniqueAcrossModulesAndLowercase()
    {
        var prefixes = ErpModuleCatalog.All.SelectMany(m => m.Prefixes).ToList();

        prefixes.Should().OnlyHaveUniqueItems();
        prefixes.Should().AllSatisfy(p => PrefixPattern().IsMatch(p).Should().BeTrue($"prefix '{p}' must be 2-3 lowercase letters"));
    }

    [Fact]
    public void EveryModule_HasAtLeastOnePrefixAndProjectName()
    {
        ErpModuleCatalog.All.Should().AllSatisfy(m =>
        {
            m.Prefixes.Should().NotBeEmpty();
            m.ProjectName.Should().StartWith("MyERP.Modules.");
            m.SrsModuleNumbers.Should().NotBeEmpty();
        });
    }

    [Fact]
    public void Finance_OwnsSixLedgerPrefixes()
    {
        ErpModuleCatalog.Get("FI").Prefixes.Should().Equal("gl", "ar", "ap", "fa", "cm", "tax");
    }

    [Theory]
    [InlineData("SD", "sal", "MyERP.Modules.Sales")]
    [InlineData("SYS", "sys", "MyERP.Modules.System")]
    [InlineData("INV", "inv", "MyERP.Modules.Inventory")]
    public void Get_ReturnsModuleWithExpectedPrefixAndProject(string code, string prefix, string project)
    {
        var module = ErpModuleCatalog.Get(code);

        module.Prefixes.Should().Contain(prefix);
        module.ProjectName.Should().Be(project);
    }

    [Fact]
    public void Get_IsCaseInsensitive()
    {
        ErpModuleCatalog.Get("sd").Code.Should().Be("SD");
    }

    [Fact]
    public void Get_UnknownCode_Throws()
    {
        var act = () => ErpModuleCatalog.Get("XX");

        act.Should().Throw<KeyNotFoundException>().WithMessage("*XX*");
    }

    [Theory]
    [InlineData("sal", "SD")]
    [InlineData("gl", "FI")]
    [InlineData("TAX", "FI")]
    public void FindByPrefix_ReturnsOwningModule(string prefix, string expectedCode)
    {
        ErpModuleCatalog.FindByPrefix(prefix)!.Code.Should().Be(expectedCode);
    }

    [Theory]
    [InlineData("zz")]
    [InlineData("")]
    public void FindByPrefix_Unknown_ReturnsNull(string prefix)
    {
        ErpModuleCatalog.FindByPrefix(prefix).Should().BeNull();
    }
}
