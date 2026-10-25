using System.Diagnostics;
using System.Text;
using MyERP.DocGen.Sources;

namespace MyERP.DocGen.Generation;

public static class PrototypeScreenshots
{
    public static void Render(RepositoryLayout layout, string directory, int count)
    {
        if (count == 0) return;
        var e2e = Path.Combine(layout.Root, "tests", "e2e");
        var script = Path.Combine(e2e, "render-prototypes.mjs");
        var version = File.ReadAllText(Path.Combine(layout.Root, ".nvmrc"), Encoding.UTF8).Trim().TrimStart('v');
        var node = Path.Combine(layout.Root, ".tools", $"node-v{version}-win-x64", "node.exe");
        var browsers = Path.Combine(layout.Root, ".tools", "ms-playwright");
        if (!File.Exists(node) || !File.Exists(script) || !Directory.Exists(browsers) ||
            !Directory.Exists(Path.Combine(e2e, "node_modules", "playwright")))
        {
            throw new InvalidOperationException("Prototype screenshots require tests/e2e npm ci and Playwright Chromium (see tools/docgen/README.md).");
        }

        var start = new ProcessStartInfo(node)
        {
            WorkingDirectory = e2e,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.Environment["PLAYWRIGHT_BROWSERS_PATH"] = browsers;
        foreach (var argument in new[] { script, directory, count.ToString(System.Globalization.CultureInfo.InvariantCulture) })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Could not start prototype screenshot renderer.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(300_000))
        {
            process.Kill(entireProcessTree: true);
            throw new IOException("Prototype screenshot rendering timed out after 5 minutes.");
        }
        Task.WaitAll(stdout, stderr);
        if (process.ExitCode != 0) throw new InvalidDataException($"Prototype screenshot rendering failed: {stderr.Result.Trim()}");
    }
}
