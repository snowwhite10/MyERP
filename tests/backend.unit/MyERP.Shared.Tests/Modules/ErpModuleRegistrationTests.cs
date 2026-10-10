using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyERP.Shared.Infrastructure.Modules;

namespace MyERP.Shared.Tests.Modules;

public sealed class ErpModuleRegistrationTests
{
    private static readonly IConfiguration EmptyConfiguration = new ConfigurationBuilder().Build();

    [Fact]
    public void AddErpModules_RegistersModulesServicesAndApplicationParts()
    {
        var services = new ServiceCollection();
        var mvc = services.AddControllers();
        var module = new FakeModule("SD");

        mvc.AddErpModules(EmptyConfiguration, module);

        module.AddServicesCalls.Should().Be(1);
        using var provider = services.BuildServiceProvider();
        provider.GetServices<IErpModule>().Should().ContainSingle().Which.Should().BeSameAs(module);
        provider.GetRequiredService<FakeModuleService>().Should().NotBeNull();
        mvc.PartManager.ApplicationParts.OfType<AssemblyPart>()
            .Should().Contain(p => p.Assembly == typeof(FakeModule).Assembly);
    }

    [Fact]
    public void AddErpModules_UnknownModuleCode_Throws()
    {
        var mvc = new ServiceCollection().AddControllers();

        var act = () => mvc.AddErpModules(EmptyConfiguration, new FakeModule("XX"));

        act.Should().Throw<KeyNotFoundException>().WithMessage("*XX*");
    }

    [Fact]
    public void AddErpModules_DuplicateModule_Throws()
    {
        var mvc = new ServiceCollection().AddControllers();

        var act = () => mvc.AddErpModules(EmptyConfiguration, new FakeModule("SD"), new FakeModule("sd"));

        act.Should().Throw<InvalidOperationException>().WithMessage("*SD*registered more than once*");
    }

    [Fact]
    public void AddErpModules_SameAssemblyTwice_AddsApplicationPartOnce()
    {
        var mvc = new ServiceCollection().AddControllers();

        mvc.AddErpModules(EmptyConfiguration, new FakeModule("SD"), new FakeModule("MD"));

        mvc.PartManager.ApplicationParts.OfType<AssemblyPart>()
            .Count(p => p.Assembly == typeof(FakeModule).Assembly).Should().Be(1);
    }

    private sealed class FakeModuleService;

    private sealed class FakeModule(string code) : IErpModule
    {
        public string Code { get; } = code;

        public int AddServicesCalls { get; private set; }

        public void AddServices(IServiceCollection services, IConfiguration configuration)
        {
            AddServicesCalls++;
            services.AddSingleton<FakeModuleService>();
        }
    }
}
