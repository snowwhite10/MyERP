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
            "SAL.PRICING.VIEW", "SAL.PRICING.CREATE", "SAL.PRICING.EDIT", "SAL.PRICING.DEACTIVATE",
            "SAL.PRICING.OVERRIDE", "SAL.INVOICE.ISSUE", "SAL.CREDIT.VIEW", "SAL.CREDIT.OVERRIDE", "SAL.RETURN.VIEW", "SAL.RETURN.CREATE",
            "SAL.RETURN.EDIT", "SAL.RETURN.DELETE", "SAL.RETURN.SUBMIT"]);
        permissions.Select(x => x.Code).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void ApprovalResultHandlersDoNotInjectServicesThatDependOnTheSysWorkflow()
    {
        // WU-SD-003b1: SYS ApprovalWorkflowService enumerates every IApprovalResultHandler, and MD CustomerService
        // depends on that workflow. A handler that injects MD lookups (directly or through the credit gate) forms a
        // DI cycle that hung every IApprovalWorkflowService resolution; such services must be resolved lazily.
        var forbidden = new[] { typeof(MyERP.Modules.Sales.Application.SalesOrderCreditCheck) };
        var handlers = typeof(SalesModule).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } &&
                typeof(MyERP.Shared.Approvals.IApprovalResultHandler).IsAssignableFrom(t))
            .ToList();
        handlers.Should().Contain(typeof(MyERP.Modules.Sales.Application.SalesOrderApprovalResultHandler));
        foreach (var handler in handlers)
            handler.GetConstructors().SelectMany(c => c.GetParameters()).Select(p => p.ParameterType)
                .Should().NotContain(t => forbidden.Contains(t) ||
                    t.Namespace == "MyERP.Modules.MasterData.Contracts", handler.Name);
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
