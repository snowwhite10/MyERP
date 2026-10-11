using MyERP.ArchitectureTests.Infrastructure;
using MyERP.Shared.Security;

namespace MyERP.ArchitectureTests;

public sealed class MasterDataModuleTests
{
    [Fact]
    public void MasterDataModuleIsLoadedWithContractsAndOnlyItsOwnPermissionPrefix()
    {
        var names = SolutionAssemblies.All.Select(a => a.GetName().Name).ToList();
        names.Should().Contain(["MyERP.Modules.MasterData", "MyERP.Modules.MasterData.Contracts"]);

        var assembly = SolutionAssemblies.ModuleImplementations.Single(a => a.GetName().Name == "MyERP.Modules.MasterData");
        var providers = assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IPermissionDefinitionProvider).IsAssignableFrom(t))
            .Select(t => (IPermissionDefinitionProvider)Activator.CreateInstance(t)!)
            .ToList();
        providers.Should().ContainSingle();
        providers[0].Permissions.Should().ContainSingle(p => p.Code == "MD.MASTER.VIEW" && p.Module == "MD");
        providers[0].Permissions.Should().AllSatisfy(p => p.Code.Should().StartWith("MD."));
    }
}
