using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyERP.Modules.Sales;
using MyERP.Modules.Sales.Contracts;
using MyERP.Shared.Errors;
using MyERP.Shared.Security;

namespace MyERP.ArchitectureTests;

public sealed class SalesModuleTests
{
    [Fact]
    public void ModuleRegistersItsOwnActionPermissions()
    {
        var module = new SalesModule();
        module.Code.Should().Be(SalesModuleContracts.ModuleCode);
        var services = new ServiceCollection();
        module.AddServices(services, new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        var permissions = provider.GetServices<IPermissionDefinitionProvider>().Single().Permissions;
        permissions.Should().OnlyContain(x => x.Module == "SD");
        permissions.Select(x => x.Code).Should().Contain([
            "SAL.QUOTATION.VIEW", "SAL.QUOTATION.CREATE", "SAL.QUOTATION.EDIT",
            "SAL.ORDER.VIEW", "SAL.ORDER.CREATE", "SAL.ORDER.EDIT", "SAL.ORDER.SUBMIT",
            "SAL.DELIVERY.VIEW", "SAL.DELIVERY.CREATE", "SAL.DELIVERY.SUBMIT",
            "SAL.PRICING.VIEW", "SAL.INVOICE.ISSUE", "SAL.CREDIT.VIEW", "SAL.RETURN.CREATE"]);
        permissions.Select(x => x.Code).Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void CompanyContextFailsClosed(long? companyId)
    {
        var context = new SalesCompanyAccess(new StubCompany(companyId));
        context.Invoking(x => x.RequireCompanyId()).Should().Throw<ForbiddenException>()
            .Which.ErrorCode.Should().Be("COMMON.COMPANY.ACCESS_DENIED");
    }

    [Fact]
    public void OtherCompanyDocumentIsHidden()
    {
        var context = new SalesCompanyAccess(new StubCompany(17));
        context.EnsureVisible(17, 8);
        context.Invoking(x => x.EnsureVisible(18, 8)).Should().Throw<NotFoundException>()
            .Which.ErrorCode.Should().Be("SAL.DOCUMENT.NOT_FOUND");
    }

    private sealed record StubCompany(long? CompanyId) : ICurrentCompany;
}
