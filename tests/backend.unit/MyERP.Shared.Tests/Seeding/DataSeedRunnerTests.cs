using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using MyERP.Shared.Infrastructure.Seeding;

namespace MyERP.Shared.Tests.Seeding;

public sealed class DataSeedRunnerTests
{
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Test")]
    public async Task RunAsync_OutsideDevelopment_ThrowsWithoutRunningSeeders(string environmentName)
    {
        var calls = new List<string>();
        var runner = CreateRunner(environmentName, new FakeSeeder("SYS", 100, calls));

        var act = () => runner.RunAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage($"*Development*{environmentName}*");
        calls.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_InDevelopment_RunsSeedersByOrderThenName()
    {
        var calls = new List<string>();
        var runner = CreateRunner(
            Environments.Development,
            new FakeSeeder("INV", 300, calls),
            new FakeSeeder("MD", 200, calls),
            new FakeSeeder("SYS", 100, calls),
            new FakeSeeder("CO", 200, calls));

        await runner.RunAsync(CancellationToken.None);

        calls.Should().Equal("SYS", "CO", "MD", "INV");
    }

    [Fact]
    public async Task RunDataSeedAsync_ResolvesRunnerFromScope()
    {
        var calls = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new FakeEnvironment(Environments.Development));
        services.AddLogging();
        services.AddScoped<DataSeedRunner>();
        services.AddScoped<IDataSeeder>(_ => new FakeSeeder("SYS", 100, calls));
        await using var provider = services.BuildServiceProvider();

        await provider.RunDataSeedAsync(TestContext.Current.CancellationToken);

        calls.Should().Equal("SYS");
    }

    [Theory]
    [InlineData(new[] { "--seed" }, true)]
    [InlineData(new[] { "--urls", "http://x", "--SEED" }, true)]
    [InlineData(new[] { "seed" }, false)]
    [InlineData(new string[0], false)]
    public void IsRequested_MatchesCommandLineSwitch(string[] args, bool expected) =>
        DataSeedRunner.IsRequested(args).Should().Be(expected);

    private static DataSeedRunner CreateRunner(string environmentName, params IDataSeeder[] seeders) =>
        new(seeders, new FakeEnvironment(environmentName), NullLogger<DataSeedRunner>.Instance);

    private sealed class FakeSeeder(string name, int order, List<string> calls) : IDataSeeder
    {
        public int Order => order;

        public string Name => name;

        public Task SeedAsync(CancellationToken cancellationToken)
        {
            calls.Add(name);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "MyERP.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
