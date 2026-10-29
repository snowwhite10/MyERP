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
            "SAL.QUOTATION.VIEW", "SAL.QUOTATION.CREATE", "SAL.QUOTATION.EDIT", "SAL.QUOTATION.VOID",
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

    [Theory]
    [InlineData("SAL.ORDER.EDIT", DataScope.Self, DataScope.Self)]
    [InlineData("SAL.ORDER.VIEW", DataScope.Company, DataScope.Company)]
    public async Task ActionScopeUsesItsOwnPermission(string permission, DataScope grant, DataScope expected)
    {
        var access = new SalesActionAccess(new SalesCompanyAccess(new StubCompany(17)),
            new StubAuthorization(new UserCompanyAuthorization(9, 17, null, null, false,
                new Dictionary<string, DataScope> { [permission] = grant })));
        var scope = await access.RequireScopeAsync(permission, TestContext.Current.CancellationToken);
        scope.EffectiveScope.Should().Be(expected);
        scope.UserId.Should().Be(9);
    }

    [Fact]
    public async Task MissingOrWrongCompanyActionScopeFailsClosed()
    {
        var granted = new UserCompanyAuthorization(9, 18, null, null, false,
            new Dictionary<string, DataScope> { ["SAL.ORDER.EDIT"] = DataScope.Company });
        var access = new SalesActionAccess(new SalesCompanyAccess(new StubCompany(17)), new StubAuthorization(granted));
        var action = async () => await access.RequireScopeAsync("SAL.ORDER.EDIT", TestContext.Current.CancellationToken);
        (await action.Should().ThrowAsync<ForbiddenException>()).Which.ErrorCode.Should().Be("COMMON.PERMISSION.DENIED");
        var missing = new SalesActionAccess(new SalesCompanyAccess(new StubCompany(17)),
            new StubAuthorization(new UserCompanyAuthorization(9, 17, null, null, false,
                new Dictionary<string, DataScope> { ["SAL.ORDER.VIEW"] = DataScope.Company })));
        var missingAction = async () => await missing.RequireScopeAsync("SAL.ORDER.EDIT", TestContext.Current.CancellationToken);
        (await missingAction.Should().ThrowAsync<ForbiddenException>()).Which.ErrorCode.Should().Be("COMMON.PERMISSION.DENIED");
        var anonymous = new SalesActionAccess(new SalesCompanyAccess(new StubCompany(17)), new StubAuthorization(null));
        var anonymousAction = async () => await anonymous.RequireScopeAsync("SAL.ORDER.EDIT", TestContext.Current.CancellationToken);
        (await anonymousAction.Should().ThrowAsync<ForbiddenException>()).Which.ErrorCode.Should().Be("COMMON.PERMISSION.DENIED");
    }

    private sealed record StubAuthorization(UserCompanyAuthorization? Authorization) : ICurrentAuthorization
    {
        public Task<UserCompanyAuthorization?> GetAsync(CancellationToken token) => Task.FromResult(Authorization);
    }

    private sealed record StubCompany(long? CompanyId) : ICurrentCompany;
}
