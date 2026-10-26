using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using MyERP.DocGen.Sources;

namespace MyERP.DocGen.Word;

public interface IMermaidRenderer
{
    byte[] RenderPng(string source);
}

/// <summary>用固定版本 mermaid-cli 與 repo 內 Playwright Chromium 產生 PNG。</summary>
public sealed class MermaidCliRenderer(RepositoryLayout layout) : IMermaidRenderer
{
    public byte[] RenderPng(string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        var e2e = Path.Combine(layout.Root, "tests", "e2e");
        var cli = Path.Combine(e2e, "node_modules", "@mermaid-js", "mermaid-cli", "src", "cli.js");
        var nodeVersion = File.ReadAllText(Path.Combine(layout.Root, ".nvmrc"), Encoding.UTF8).Trim().TrimStart('v');
        var node = Path.Combine(layout.Root, ".tools", $"node-v{nodeVersion}-win-x64", "node.exe");
        var browsers = Path.Combine(layout.Root, ".tools", "ms-playwright");
        var chrome = Directory.Exists(browsers)
            ? Directory.GetFiles(browsers, "chrome.exe", SearchOption.AllDirectories)
                .FirstOrDefault(path => path.Contains("chromium-", StringComparison.OrdinalIgnoreCase))
            : null;
        if (!File.Exists(cli) || !File.Exists(node) || chrome is null)
        {
            throw new InvalidOperationException("Mermaid rendering requires tests/e2e npm ci and Playwright Chromium (see tools/docgen/README.md).");
        }

        var lockFile = Path.Combine(e2e, "package-lock.json");
        var fingerprint = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(lockFile)))
            + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(cli)))
            + ":" + nodeVersion + ":" + Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(chrome)))
            + ":" + File.GetLastWriteTimeUtc(chrome).Ticks;
        var cache = new MermaidImageCache(Path.Combine(layout.Root, ".tools", "docgen-mermaid-cache"));
        return cache.GetOrRender(source, fingerprint, () => RenderUncached(source, e2e, cli, node, chrome));
    }

    private static byte[] RenderUncached(string source, string e2e, string cli, string node, string chrome)
    {
        var temporary = Path.Combine(Path.GetTempPath(), "myerp-mermaid-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            var input = Path.Combine(temporary, "diagram.mmd");
            var output = Path.Combine(temporary, "diagram.png");
            File.WriteAllText(input, source, new UTF8Encoding(false));
            var start = new ProcessStartInfo(node)
            {
                WorkingDirectory = e2e,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            start.Environment["PUPPETEER_EXECUTABLE_PATH"] = chrome;
            start.Environment["PUPPETEER_SKIP_DOWNLOAD"] = "1";
            foreach (var argument in new[] { cli, "--input", input, "--output", output, "--backgroundColor", "white" })
            {
                start.ArgumentList.Add(argument);
            }

            using var process = Process.Start(start) ?? throw new IOException("Could not start Mermaid renderer.");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(60_000))
            {
                process.Kill(entireProcessTree: true);
                throw new IOException("Mermaid rendering timed out after 60 seconds.");
            }
            Task.WaitAll(stdout, stderr);
            if (process.ExitCode != 0 || !File.Exists(output))
            {
                throw new InvalidDataException($"Mermaid rendering failed: {stderr.Result.Trim()}");
            }

            return File.ReadAllBytes(output);
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
    }
}
