using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyERP.Modules.Finance;
using MyERP.Modules.Finance.Application;
using MyERP.Modules.Finance.Contracts;
using MyERP.Modules.Finance.Infrastructure;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.System.Application.Seeding;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Modules;
using MyERP.Shared.Security;

namespace MyERP.ArchitectureTests;

public sealed class FinanceModuleTests
{
    private static readonly string[] CreditPermissions =
        ["AR.CREDIT.VIEW", "AR.CREDIT.CREATE", "AR.CREDIT.EDIT", "AR.CREDIT.DEACTIVATE"];
    private static readonly string[] GlReadPermissions = ["GL.PERIOD.VIEW", "GL.JOURNAL.VIEW", "GL.SALES_MAP.VIEW",
        "GL.PURCHASE_MAP.VIEW", "GL.COLLECTION_MAP.VIEW"];
    private static readonly string[] GlWritePermissions = ["GL.PERIOD.CREATE", "GL.PERIOD.EDIT",
        "GL.PERIOD.CLOSE", "GL.PERIOD.REOPEN", "GL.PERIOD.DELETE", "GL.SALES_MAP.EDIT", "GL.PURCHASE_MAP.EDIT",
        "GL.COLLECTION_MAP.EDIT"];

    [Fact]
    public void ModuleOwnsCreditContractAndArTables()
    {
        var module = new FinanceModule();
        module.Code.Should().Be(FinanceModuleContracts.ModuleCode);
        ErpModuleCatalog.All.Should().Contain(x => x.Code == module.Code);

        var services = new ServiceCollection();
        module.AddServices(services, new ConfigurationBuilder().Build());
        services.Should().ContainSingle(x => x.ServiceType == typeof(ICustomerCreditService)
            && x.Lifetime == ServiceLifetime.Scoped);
        services.Where(x => x.ServiceType == typeof(IModuleDbModel))
            .Select(x => x.ImplementationType)
            .Should().BeEquivalentTo([typeof(CustomerCreditDbModel), typeof(GlPeriodDbModel), typeof(GlJournalDbModel),
                typeof(SalesSourceDbModel), typeof(SalesInvoiceAccountMapDbModel), typeof(PurchaseSourceDbModel),
                typeof(PurchaseInvoiceAccountMapDbModel), typeof(CollectionDbModel), typeof(SalesCollectionAccountMapDbModel),
                typeof(CollectionReceiptDocumentDbModel), typeof(CollectionSettlementDocumentDbModel)]);
        services.Should().ContainSingle(x => x.ServiceType == typeof(CreditLimitQueryService)
            && x.Lifetime == ServiceLifetime.Scoped);
        services.Should().ContainSingle(x => x.ServiceType == typeof(CreditLimitCommandService)
            && x.Lifetime == ServiceLifetime.Scoped);
        services.Should().ContainSingle(x => x.ServiceType == typeof(GlReadService)
            && x.Lifetime == ServiceLifetime.Scoped);
        services.Should().ContainSingle(x => x.ServiceType == typeof(GlPeriodCommandService)
            && x.Lifetime == ServiceLifetime.Scoped);
        services.Should().ContainSingle(x => x.ServiceType == typeof(SalesInvoiceAccountMapService)
            && x.Lifetime == ServiceLifetime.Scoped);
        services.Should().ContainSingle(x => x.ServiceType == typeof(PurchaseInvoiceAccountMapService)
            && x.Lifetime == ServiceLifetime.Scoped);
        services.Should().ContainSingle(x => x.ServiceType == typeof(SalesCollectionAccountMapService)
            && x.Lifetime == ServiceLifetime.Scoped);
    }

    [Fact]
    public void ModuleReportsPostedAccountUsageToMasterData()
    {
        // FI-GL-USAGE-006: MD asks every registered IAccountUsage; FI contributes exactly one scoped implementation.
        var services = new ServiceCollection();
        new FinanceModule().AddServices(services, new ConfigurationBuilder().Build());
        services.Should().ContainSingle(x => x.ServiceType == typeof(IAccountUsage))
            .Which.Should().Match<ServiceDescriptor>(x => x.ImplementationType == typeof(FinanceAccountUsage)
                && x.Lifetime == ServiceLifetime.Scoped);
    }

    [Fact]
    public void ModuleReportsPostedCollectionFundAccountUsageToMasterData()
    {
        // FI-FUND-USAGE-004 (WU-FI-005a2e, ADR-124): MD asks every registered IFundAccountUsage before deleting a fund
        // account; FI contributes exactly one scoped implementation backed by posted collection receipts.
        var services = new ServiceCollection();
        new FinanceModule().AddServices(services, new ConfigurationBuilder().Build());
        services.Should().ContainSingle(x => x.ServiceType == typeof(IFundAccountUsage))
            .Which.Should().Match<ServiceDescriptor>(x => x.ImplementationType == typeof(FinanceFundAccountUsage)
                && x.Lifetime == ServiceLifetime.Scoped);
    }

    [Fact]
    public void ModuleRegistersSeparateCreditPermissionsUnderTheArPrefix()
    {
        // FI-CREDIT-012: VIEW/CREATE/EDIT/DEACTIVATE are separate catalogue entries owned by FI.
        var services = new ServiceCollection();
        new FinanceModule().AddServices(services, new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        var permissions = provider.GetServices<IPermissionDefinitionProvider>().Single().Permissions;

        permissions.Should().OnlyContain(x => x.Module == "FI");
        permissions.Select(x => x.Code).Should().BeEquivalentTo([.. CreditPermissions, .. GlReadPermissions,
            .. GlWritePermissions]);
        permissions.Should().OnlyContain(x => PermissionCode.IsValid(x.Code));
    }

    [Fact]
    public void FinanceSeedRoleMaintainsLimitsAndManagerOnlyViewsThem()
    {
        var permissions = new FinancePermissions().Permissions;
        string[] Granted(string role) =>
            [.. permissions.Where(SystemSeedData.Roles.Single(r => r.Code == role).Grants).Select(p => p.Code)];

        Granted("FINANCE").Should().BeEquivalentTo([.. CreditPermissions, .. GlReadPermissions,
            .. GlWritePermissions]);
        Granted("MANAGER").Should().BeEquivalentTo(["AR.CREDIT.VIEW", .. GlReadPermissions]);
        Granted("VIEWER").Should().BeEquivalentTo(["AR.CREDIT.VIEW", .. GlReadPermissions]);
        Granted("SALES").Should().BeEmpty();
    }
}
