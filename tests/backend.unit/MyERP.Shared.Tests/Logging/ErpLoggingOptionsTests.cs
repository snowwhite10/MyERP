using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using MyERP.Shared.Infrastructure.Logging;
using Serilog.Core;
using Serilog.Events;

namespace MyERP.Shared.Tests.Logging;

public sealed class ErpLoggingOptionsTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    [Fact]
    public void FromConfiguration_Defaults()
    {
        var options = ErpLoggingOptions.FromConfiguration(Config());

        options.SlowRequestMilliseconds.Should().Be(1000);
        options.FileEnabled.Should().BeTrue();
        options.FilePath.Should().Be("logs/app-.log");
        options.RetainedDays.Should().Be(30);
        options.ConsoleEnabled.Should().BeNull();
        options.ApplicationInsightsConnectionString.Should().BeNull();
    }

    [Fact]
    public void FromConfiguration_ReadsAllSettings()
    {
        var options = ErpLoggingOptions.FromConfiguration(Config(
            (ErpLoggingOptions.SlowRequestMillisecondsKey, "250"),
            (ErpLoggingOptions.FileEnabledKey, "false"),
            (ErpLoggingOptions.FilePathKey, "D:/logs/api-.log"),
            (ErpLoggingOptions.RetainedDaysKey, "7"),
            (ErpLoggingOptions.ConsoleEnabledKey, "true"),
            (ErpLoggingOptions.ApplicationInsightsConnectionStringKey, " InstrumentationKey=abc ")));

        options.Should().BeEquivalentTo(new ErpLoggingOptions
        {
            SlowRequestMilliseconds = 250,
            FileEnabled = false,
            FilePath = "D:/logs/api-.log",
            RetainedDays = 7,
            ConsoleEnabled = true,
            ApplicationInsightsConnectionString = "InstrumentationKey=abc",
        });
    }

    [Fact]
    public void FromConfiguration_ZeroThreshold_IsAllowed()
    {
        ErpLoggingOptions.FromConfiguration(Config((ErpLoggingOptions.SlowRequestMillisecondsKey, "0")))
            .SlowRequestMilliseconds.Should().Be(0);
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("")]
    public void FromConfiguration_BlankConnectionString_FallsBackToEnvironmentKey(string blank)
    {
        var options = ErpLoggingOptions.FromConfiguration(Config(
            (ErpLoggingOptions.ApplicationInsightsConnectionStringKey, blank),
            (ErpLoggingOptions.ApplicationInsightsEnvironmentKey, "InstrumentationKey=env")));

        options.ApplicationInsightsConnectionString.Should().Be("InstrumentationKey=env");
    }

    [Theory]
    [InlineData(ErpLoggingOptions.SlowRequestMillisecondsKey, "-1")]
    [InlineData(ErpLoggingOptions.SlowRequestMillisecondsKey, "1s")]
    [InlineData(ErpLoggingOptions.RetainedDaysKey, "0")]
    [InlineData(ErpLoggingOptions.RetainedDaysKey, "abc")]
    [InlineData(ErpLoggingOptions.FileEnabledKey, "yes")]
    [InlineData(ErpLoggingOptions.ConsoleEnabledKey, "1")]
    public void FromConfiguration_InvalidValue_Throws(string key, string value)
    {
        var act = () => ErpLoggingOptions.FromConfiguration(Config((key, value)));

        act.Should().Throw<FormatException>().WithMessage($"*{key}*");
    }

    [Theory]
    [InlineData(0, 1000, LogEventLevel.Debug)]
    [InlineData(1000, 1000, LogEventLevel.Debug)]
    [InlineData(1000.01, 1000, LogEventLevel.Warning)]
    [InlineData(5000, 1000, LogEventLevel.Warning)]
    [InlineData(0.5, 0, LogEventLevel.Warning)]
    public void GetRequestLevel_WarnsOnlyAboveThreshold(double elapsed, int threshold, LogEventLevel expected)
    {
        ErpLoggingExtensions.GetRequestLevel(elapsed, threshold).Should().Be(expected);
    }

    [Fact]
    public void ResolveFilePath_RelativeIsUnderContentRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "myerp-root");

        ErpLoggingExtensions.ResolveFilePath("logs/app-.log", root)
            .Should().Be(Path.GetFullPath(Path.Combine(root, "logs", "app-.log")));
    }

    [Fact]
    public void ResolveFilePath_AbsoluteIsUnchanged()
    {
        var absolute = Path.Combine(Path.GetTempPath(), "x", "app-.log");

        ErpLoggingExtensions.ResolveFilePath(absolute, "C:/ignored").Should().Be(absolute);
    }

    [Fact]
    public void AddMyErpLogging_WithoutConnectionString_DoesNotRegisterApplicationInsights()
    {
        using var provider = Build(Config((ErpLoggingOptions.FileEnabledKey, "false")));

        provider.GetService<TelemetryConfiguration>().Should().BeNull();
        provider.GetServices<ILogEventEnricher>().Should().ContainSingle(e => e is ErpLogEnricher);
        provider.GetRequiredService<Serilog.ILogger>().Should().NotBeNull();
    }

    [Fact]
    public void AddMyErpLogging_WithConnectionString_RegistersApplicationInsights()
    {
        const string connectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=https://127.0.0.1:1/";
        using var provider = Build(Config(
            (ErpLoggingOptions.FileEnabledKey, "false"),
            (ErpLoggingOptions.ApplicationInsightsConnectionStringKey, connectionString)));

        provider.GetRequiredService<Serilog.ILogger>().Should().NotBeNull();
        provider.GetRequiredService<TelemetryConfiguration>().ConnectionString.Should().Be(connectionString);
    }

    [Fact]
    public void AddMyErpLogging_InvalidConfiguration_FailsAtRegistration()
    {
        var act = () => new ServiceCollection().AddMyErpLogging(
            Config((ErpLoggingOptions.SlowRequestMillisecondsKey, "-5")), new TestEnvironment());

        act.Should().Throw<FormatException>();
    }

    private static ServiceProvider Build(IConfiguration configuration) =>
        new ServiceCollection().AddMyErpLogging(configuration, new TestEnvironment()).BuildServiceProvider();

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;

        public string ApplicationName { get; set; } = "MyERP.Tests";

        public string ContentRootPath { get; set; } = Path.GetTempPath();

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
