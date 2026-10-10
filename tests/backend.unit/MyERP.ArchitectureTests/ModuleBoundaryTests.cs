using MyERP.ArchitectureTests.Infrastructure;

namespace MyERP.ArchitectureTests;

/// <summary>模組化單體的邊界與分層（MASTER_PROMPT 第 5 節、ADR-003、architecture.md 2.2）。</summary>
public sealed class ModuleBoundaryTests
{
    private static readonly string[] DomainForbiddenNamespaces =
    [
        "Microsoft.EntityFrameworkCore",
        "Microsoft.AspNetCore",
        "Microsoft.Data.SqlClient",
        "Microsoft.Extensions.DependencyInjection",
    ];

    [Fact]
    public void Discovery_FindsCoreAndTemplateModuleAssemblies()
    {
        // 防止掃描不到任何組件而令其他規則「空集合通過」
        SolutionAssemblies.All.Select(a => a.GetName().Name).Should().Contain(
            ["MyERP.Api", "MyERP.Shared", "MyERP.Shared.Infrastructure", "MyERP.Migrations",
             "MyERP.Modules.System", "MyERP.Modules.System.Contracts"]);
    }

    [Fact]
    public void ModuleImplementations_DoNotReferenceOtherModuleImplementations()
    {
        ArchitectureRules.FindForbiddenAssemblyReferences(
                SolutionAssemblies.ModuleImplementations,
                (_, reference) => SolutionAssemblies.IsModuleImplementation(reference))
            .Should().BeEmpty("modules may only depend on other modules' Contracts");
    }

    [Fact]
    public void ModuleContracts_DoNotReferenceImplementationsOrInfrastructure()
    {
        ArchitectureRules.FindForbiddenAssemblyReferences(
                SolutionAssemblies.ModuleContracts,
                (_, reference) => SolutionAssemblies.IsModuleImplementation(reference)
                    || reference.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
                    || reference == "MyERP.Shared.Infrastructure")
            .Should().BeEmpty("Contracts expose interfaces and DTOs only");
    }

    [Fact]
    public void SharedKernel_DoesNotReferenceEfCoreOrOtherMyErpAssemblies()
    {
        var shared = SolutionAssemblies.All.Single(a => a.GetName().Name == "MyERP.Shared");

        ArchitectureRules.FindForbiddenAssemblyReferences(
                [shared],
                (_, reference) => reference.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
                    || reference.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)
                    || reference.StartsWith("MyERP.", StringComparison.Ordinal))
            .Should().BeEmpty();
    }

    [Fact]
    public void NonApiAssemblies_DoNotReferenceApiOrMigrations()
    {
        ArchitectureRules.FindForbiddenAssemblyReferences(
                SolutionAssemblies.All.Where(a => a.GetName().Name is not ("MyERP.Api" or "MyERP.Migrations")),
                (_, reference) => reference is "MyERP.Api" or "MyERP.Migrations")
            .Should().BeEmpty();
    }

    [Fact]
    public void ModuleTypes_LiveUnderTheirModuleRootNamespace()
    {
        var violations =
            from assembly in SolutionAssemblies.ModuleImplementations.Concat(SolutionAssemblies.ModuleContracts)
            let name = assembly.GetName().Name!
            from type in TypeInspector.GetLoadableTypes(assembly)
            where type.Namespace?.StartsWith("MyERP", StringComparison.Ordinal) == true
                  && !ArchitectureRules.IsInNamespace(type, name)
            select $"{type.FullName} is not under {name}";

        violations.Should().BeEmpty();
    }

    [Fact]
    public void DomainLayer_DoesNotDependOnInfrastructureFrameworksOrOtherLayers()
    {
        var violations = SolutionAssemblies.ModuleImplementations.SelectMany(assembly =>
        {
            var domainNamespace = SolutionAssemblies.ModuleRoot(assembly.GetName().Name!) + ".Domain";
            var domainTypes = TypeInspector.GetLoadableTypes(assembly)
                .Where(t => ArchitectureRules.IsInNamespace(t, domainNamespace));

            return ArchitectureRules.FindForbiddenTypeReferences(domainTypes, (_, r) => IsForbiddenForDomain(r, domainNamespace));
        });

        violations.Should().BeEmpty("Domain may only depend on MyERP.Shared and its own Domain namespace");
    }

    private static bool IsForbiddenForDomain(Type referenced, string domainNamespace)
    {
        if (DomainForbiddenNamespaces.Any(ns => ArchitectureRules.IsInNamespace(referenced, ns)))
        {
            return true;
        }

        if (!ArchitectureRules.IsInNamespace(referenced, "MyERP"))
        {
            return false;
        }

        var allowed = ArchitectureRules.IsInNamespace(referenced, domainNamespace)
            || (ArchitectureRules.IsInNamespace(referenced, "MyERP.Shared")
                && !ArchitectureRules.IsInNamespace(referenced, "MyERP.Shared.Infrastructure"));
        return !allowed;
    }
}
