using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MyERP.Shared.Infrastructure.Seeding;

/// <summary>
/// 模組的種子程序（MASTER_PROMPT 第 12 節）。必須冪等：重複執行只補上缺少的資料，不覆蓋使用者已修改的內容。
/// 由 <see cref="DataSeedRunner"/> 依 <see cref="Order"/> 由小至大執行；被依賴的模組（例如 SYS）用較小的值。
/// </summary>
public interface IDataSeeder
{
    /// <summary>執行順序；SYS = 100，其餘模組依依賴關係往後排（每模組間隔 100）。</summary>
    int Order { get; }

    string Name { get; }

    Task SeedAsync(CancellationToken cancellationToken);
}

/// <summary>依序執行全部 <see cref="IDataSeeder"/>。只可在 Development 環境執行，其他環境拒絕（ADR-023）。</summary>
public sealed partial class DataSeedRunner(
    IEnumerable<IDataSeeder> seeders,
    IHostEnvironment environment,
    ILogger<DataSeedRunner> logger)
{
    /// <summary>主程式的命令列參數：<c>dotnet run --project src/backend/MyERP.Api -- --seed</c>。</summary>
    public const string CommandLineSwitch = "--seed";

    public static bool IsRequested(IEnumerable<string> args) =>
        args?.Contains(CommandLineSwitch, StringComparer.OrdinalIgnoreCase) ?? false;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
        {
            SeedRefused(logger, environment.EnvironmentName);
            throw new InvalidOperationException(
                $"Seed data can only be loaded in the Development environment (current: {environment.EnvironmentName}).");
        }

        foreach (var seeder in seeders.OrderBy(s => s.Order).ThenBy(s => s.Name, StringComparer.Ordinal))
        {
            SeedStarting(logger, seeder.Name);
            await seeder.SeedAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Seed data refused: environment {Environment} is not Development")]
    private static partial void SeedRefused(ILogger logger, string environment);

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeding {Seeder}")]
    private static partial void SeedStarting(ILogger logger, string seeder);
}

public static class DataSeedingExtensions
{
    /// <summary>在獨立的 DI scope 執行全部種子程序。</summary>
    public static async Task RunDataSeedAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        var scope = services.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            await scope.ServiceProvider.GetRequiredService<DataSeedRunner>().RunAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
