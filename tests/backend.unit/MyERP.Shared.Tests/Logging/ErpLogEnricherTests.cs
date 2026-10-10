using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using MyERP.Shared.Infrastructure.Logging;
using MyERP.Shared.Security;
using Serilog.Core;
using Serilog.Events;
using Serilog.Parsing;

namespace MyERP.Shared.Tests.Logging;

public sealed class ErpLogEnricherTests
{
    [Theory]
    [InlineData("/api/v1/sys/users", "SYS")]
    [InlineData("/api/v1/sal/orders/12", "SD")]
    [InlineData("/API/V1/GL/vouchers", "FI")]
    [InlineData("/api/v2/ar/receipts", "FI")]
    [InlineData("/api/v1/inv", "INV")]
    public void ResolveModule_FromRoutePrefix(string path, string expected)
    {
        ErpLogEnricher.ResolveModule(new PathString(path), sourceContext: null).Should().Be(expected);
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/api/v1/unknown/x")]
    [InlineData("/api/x1/sys/users")]
    [InlineData("/api/v/sys/users")]
    [InlineData("/api/v1")]
    [InlineData("/other/v1/sys")]
    [InlineData("")]
    public void ResolveModule_NonModuleRoute_IsNull(string path)
    {
        ErpLogEnricher.ResolveModule(new PathString(path), sourceContext: null).Should().BeNull();
    }

    [Theory]
    [InlineData("MyERP.Modules.Sales.Application.OrderService", "SD")]
    [InlineData("MyERP.Modules.System", "SYS")]
    [InlineData("MyERP.Modules.System.Contracts.Events", "SYS")]
    [InlineData("MyERP.Modules.Finance.Domain.Voucher", "FI")]
    public void ResolveModule_FromSourceContext(string sourceContext, string expected)
    {
        ErpLogEnricher.ResolveModule(PathString.Empty, sourceContext).Should().Be(expected);
    }

    [Theory]
    [InlineData("MyERP.Modules.SystemX.Foo")]
    [InlineData("MyERP.Shared.Infrastructure.Web.ErpExceptionHandler")]
    [InlineData("Microsoft.AspNetCore.Hosting")]
    public void ResolveModule_NonModuleSourceContext_IsNull(string sourceContext)
    {
        ErpLogEnricher.ResolveModule(PathString.Empty, sourceContext).Should().BeNull();
    }

    [Fact]
    public void ResolveModule_RouteWinsOverSourceContext()
    {
        ErpLogEnricher.ResolveModule(new PathString("/api/v1/pur/orders"), "MyERP.Modules.Sales.X").Should().Be("PUR");
    }

    [Fact]
    public void Enrich_HttpRequest_AddsTraceUserCompanyAndModule()
    {
        var context = ContextWith(userId: 42, companyId: 7);
        context.Request.Path = "/api/v1/sal/orders";
        context.TraceIdentifier = "req-1";

        var logEvent = Enrich(context);

        Scalar(logEvent, ErpLogEnricher.TraceIdProperty).Should().NotBeNull();
        Scalar(logEvent, ErpLogEnricher.UserIdProperty).Should().Be(42L);
        Scalar(logEvent, ErpLogEnricher.CompanyIdProperty).Should().Be(7L);
        Scalar(logEvent, ErpLogEnricher.ModuleProperty).Should().Be("SD");
    }

    [Fact]
    public void Enrich_UsesActivityTraceId_WhenPresent()
    {
        using var activity = new Activity("test").SetIdFormat(ActivityIdFormat.W3C).Start();
        var context = ContextWith(userId: null, companyId: null);
        context.TraceIdentifier = "req-1";

        var logEvent = Enrich(context);

        Scalar(logEvent, ErpLogEnricher.TraceIdProperty).Should().Be(activity.TraceId.ToHexString());
    }

    [Fact]
    public void Enrich_NoActivity_FallsBackToRequestIdentifier()
    {
        var previous = Activity.Current;
        Activity.Current = null;
        try
        {
            var context = ContextWith(userId: null, companyId: null);
            context.TraceIdentifier = "req-1";

            Scalar(Enrich(context), ErpLogEnricher.TraceIdProperty).Should().Be("req-1");
        }
        finally
        {
            Activity.Current = previous;
        }
    }

    [Fact]
    public void Enrich_AnonymousWithoutCompany_OmitsUserAndCompany()
    {
        var logEvent = Enrich(ContextWith(userId: null, companyId: null));

        logEvent.Properties.Should().NotContainKey(ErpLogEnricher.UserIdProperty);
        logEvent.Properties.Should().NotContainKey(ErpLogEnricher.CompanyIdProperty);
        logEvent.Properties.Should().NotContainKey(ErpLogEnricher.ModuleProperty);
    }

    [Fact]
    public void Enrich_NoHttpContext_UsesSourceContextModuleOnly()
    {
        var logEvent = NewEvent();
        logEvent.AddOrUpdateProperty(new LogEventProperty(Constants.SourceContextPropertyName, new ScalarValue("MyERP.Modules.Inventory.Jobs.Valuation")));

        new ErpLogEnricher(new HttpContextAccessor()).Enrich(logEvent, new PropertyFactory());

        Scalar(logEvent, ErpLogEnricher.ModuleProperty).Should().Be("INV");
        logEvent.Properties.Should().NotContainKey(ErpLogEnricher.UserIdProperty);
        logEvent.Properties.Should().NotContainKey(ErpLogEnricher.CompanyIdProperty);
    }

    [Fact]
    public void Enrich_DisposedRequestScope_OmitsUserAndCompany()
    {
        var provider = new Mock<IServiceProvider>();
        provider.Setup(p => p.GetService(It.IsAny<Type>())).Throws(new ObjectDisposedException("scope"));
        var context = new DefaultHttpContext { RequestServices = provider.Object };

        var logEvent = Enrich(context);

        logEvent.Properties.Should().NotContainKey(ErpLogEnricher.UserIdProperty);
        logEvent.Properties.Should().NotContainKey(ErpLogEnricher.CompanyIdProperty);
    }

    [Fact]
    public void Enrich_ExistingProperty_IsNotOverwritten()
    {
        var logEvent = NewEvent();
        logEvent.AddOrUpdateProperty(new LogEventProperty(ErpLogEnricher.UserIdProperty, new ScalarValue(99L)));

        new ErpLogEnricher(new HttpContextAccessor { HttpContext = ContextWith(userId: 42, companyId: null) })
            .Enrich(logEvent, new PropertyFactory());

        Scalar(logEvent, ErpLogEnricher.UserIdProperty).Should().Be(99L);
    }

    private static DefaultHttpContext ContextWith(long? userId, long? companyId)
    {
        var user = new Mock<ICurrentUser>();
        user.SetupGet(u => u.UserId).Returns(userId);
        var company = new Mock<ICurrentCompany>();
        company.SetupGet(c => c.CompanyId).Returns(companyId);
        var services = new ServiceCollection()
            .AddSingleton(user.Object)
            .AddSingleton(company.Object)
            .BuildServiceProvider();
        return new DefaultHttpContext { RequestServices = services };
    }

    private static LogEvent Enrich(HttpContext context)
    {
        var logEvent = NewEvent();
        new ErpLogEnricher(new HttpContextAccessor { HttpContext = context }).Enrich(logEvent, new PropertyFactory());
        return logEvent;
    }

    private static LogEvent NewEvent() =>
        new(DateTimeOffset.MinValue, LogEventLevel.Information, null, new MessageTemplate("test", []), []);

    private static object? Scalar(LogEvent logEvent, string name) =>
        logEvent.Properties.TryGetValue(name, out var value) ? ((ScalarValue)value).Value : null;

    private sealed class PropertyFactory : ILogEventPropertyFactory
    {
        public LogEventProperty CreateProperty(string name, object? value, bool destructureObjects = false) =>
            new(name, new ScalarValue(value));
    }
}
