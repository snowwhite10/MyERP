using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyERP.Shared.Infrastructure;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;
using MyERP.Shared.Time;

namespace MyERP.Shared.Tests.Web;

public sealed class CoreServiceRegistrationTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    [Fact]
    public void AddMyErpCore_RegistersClockWithConfiguredOffset()
    {
        using var provider = new ServiceCollection()
            .AddMyErpCore(Config(("App:TimeZoneOffset", "+09:00")))
            .BuildServiceProvider();

        var clock = provider.GetRequiredService<IClock>();

        clock.Offset.Should().Be(TimeSpan.FromHours(9));
        provider.GetRequiredService<IClock>().Should().BeSameAs(clock);
    }

    [Fact]
    public void AddMyErpCore_MissingOffset_DefaultsToUtc8()
    {
        using var provider = new ServiceCollection().AddMyErpCore(Config()).BuildServiceProvider();

        provider.GetRequiredService<IClock>().Offset.Should().Be(TimeSpan.FromHours(8));
    }

    [Fact]
    public void AddMyErpCore_InvalidOffset_FailsAtStartup()
    {
        var act = () => new ServiceCollection().AddMyErpCore(Config(("App:TimeZoneOffset", "Taipei")));

        act.Should().Throw<FormatException>().WithMessage("*App:TimeZoneOffset*");
    }

    [Fact]
    public void AddMyErpCore_RegistersScopedCurrentUserAndCompany()
    {
        using var provider = new ServiceCollection().AddMyErpCore(Config()).BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<ICurrentUser>().UserId.Should().BeNull();
        scope.ServiceProvider.GetRequiredService<ICurrentCompany>().CompanyId.Should().BeNull();
    }

    [Fact]
    public void AddMyErpPersistence_RegistersContextWithInterceptor_WithoutConnecting()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddMyErpCore(Config(("ConnectionStrings:MyErp", "Server=invalid.myerp.test;Database=x;Integrated Security=true")))
            .AddSingleton<IModuleDbModel, Persistence.TestModuleDbModel>();
        services.AddMyErpPersistence(Config(("ConnectionStrings:MyErp", "Server=invalid.myerp.test;Database=x;Integrated Security=true")));
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<MyErpDbContext>();

        db.Model.FindEntityType(typeof(Persistence.TestOrder)).Should().NotBeNull();
        db.GetService<Microsoft.EntityFrameworkCore.Infrastructure.IDbContextOptions>()
            .Extensions.OfType<Microsoft.EntityFrameworkCore.Infrastructure.CoreOptionsExtension>().Single()
            .Interceptors.Should().ContainSingle(i => i is AuditSaveChangesInterceptor);
    }

    [Fact]
    public void AddMyErpPersistence_MissingConnectionString_FailsWhenContextIsResolved()
    {
        var services = new ServiceCollection().AddLogging().AddMyErpCore(Config());
        services.AddMyErpPersistence(Config());
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var act = () => scope.ServiceProvider.GetRequiredService<MyErpDbContext>().Model;

        act.Should().Throw<InvalidOperationException>().WithMessage("*ConnectionStrings:MyErp*");
    }
}
