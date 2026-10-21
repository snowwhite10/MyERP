using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyERP.ArchitectureTests.Infrastructure;
using MyERP.Modules.Purchasing;
using MyERP.Modules.Purchasing.Application;
using MyERP.Modules.Purchasing.Contracts;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Security;

namespace MyERP.ArchitectureTests;

public sealed class PurchasingModuleTests
{
    [Fact]
    public void PurchaseModuleRegistersIsolatedContractsAndPurchasingPermissions()
    {
        var names = SolutionAssemblies.All.Select(a => a.GetName().Name).ToList();
        names.Should().Contain(["MyERP.Modules.Purchasing", "MyERP.Modules.Purchasing.Contracts"]);
        var module = new PurchasingModule();
        module.Code.Should().Be(PurchasingModuleContracts.ModuleCode);
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentCompany>(new StubCompany(17));
        module.AddServices(services, new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<RequisitionCompanyAccess>().RequireCompanyId().Should().Be(17);

        var permissions = provider.GetServices<IPermissionDefinitionProvider>().Single();
        permissions.Permissions.Select(p => p.Code).Should().BeEquivalentTo([
            "PUR.REQUISITION.VIEW", "PUR.REQUISITION.CREATE", "PUR.REQUISITION.EDIT",
            "PUR.REQUISITION.DELETE", "PUR.REQUISITION.SUBMIT", "PUR.REQUISITION.EXPORT",
            "PUR.RFQ.VIEW", "PUR.RFQ.CREATE", "PUR.RFQ.EDIT", "PUR.RFQ.DELETE",
            "PUR.RFQ.SEND", "PUR.RFQ.CLOSE", "PUR.QUOTATION.VIEW", "PUR.QUOTATION.QUOTE"]);
        var catalog = new PermissionCatalog([permissions]);
        permissions.Permissions.Should().OnlyContain(p => p.Module == "PUR" && catalog.Contains(p.Code));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void MissingOrInvalidCompanyFailsClosed(long? companyId)
    {
        var access = new RequisitionCompanyAccess(new StubCompany(companyId));
        access.Invoking(x => x.RequireCompanyId()).Should().Throw<ForbiddenException>()
            .Which.ErrorCode.Should().Be("COMMON.COMPANY.ACCESS_DENIED");
    }

    [Fact]
    public void AnotherCompanyDocumentIsHiddenAsNotFound()
    {
        var access = new RequisitionCompanyAccess(new StubCompany(17));
        access.EnsureVisible(17, 45);
        access.Invoking(x => x.EnsureVisible(18, 45)).Should().Throw<NotFoundException>()
            .Which.ErrorCode.Should().Be("PUR.REQUISITION.NOT_FOUND");
    }

    private sealed record StubCompany(long? CompanyId) : ICurrentCompany;
}
