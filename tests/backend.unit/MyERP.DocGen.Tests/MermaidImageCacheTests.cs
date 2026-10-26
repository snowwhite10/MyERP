using MyERP.DocGen.Word;

namespace MyERP.DocGen.Tests;

public sealed class MermaidImageCacheTests
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+ip1sAAAAASUVORK5CYII=");

    [Fact]
    public void EmptySourceOrFingerprintIsRejectedBeforeRendering()
    {
        var cache = new MermaidImageCache(Path.Combine(Path.GetTempPath(), "myerp-mermaid-cache-test-" + Guid.NewGuid().ToString("N")));
        Action emptySource = () => cache.GetOrRender(" ", "v1", () => throw new IOException("rendered"));
        Action emptyFingerprint = () => cache.GetOrRender("graph A", " ", () => throw new IOException("rendered"));
        emptySource.Should().Throw<ArgumentException>();
        emptyFingerprint.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RepeatedSourceUsesCacheAndChangedSourceOrToolRendersAgain()
    {
        var directory = Path.Combine(Path.GetTempPath(), "myerp-mermaid-cache-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var calls = 0;
            byte[] Render() { calls++; return Png; }
            var cache = new MermaidImageCache(directory);
            cache.GetOrRender("erDiagram\n A ||--o{ B : has", "tool-v1", Render).Should().Equal(Png);
            cache.GetOrRender("erDiagram\n A ||--o{ B : has", "tool-v1", Render).Should().Equal(Png);
            calls.Should().Be(1);

            cache.GetOrRender("erDiagram\n A ||--o{ C : has", "tool-v1", Render);
            cache.GetOrRender("erDiagram\n A ||--o{ B : has", "tool-v2", Render);
            calls.Should().Be(3);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void CorruptEntryIsRenderedAgainAndFailedRenderIsNotCached()
    {
        var directory = Path.Combine(Path.GetTempPath(), "myerp-mermaid-cache-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var cache = new MermaidImageCache(directory);
            Action failure = () => cache.GetOrRender("graph A", "v1", () => throw new IOException("renderer failed"));
            failure.Should().Throw<IOException>();
            cache.GetOrRender("graph A", "v1", () => Png);
            File.WriteAllBytes(Directory.GetFiles(directory, "*.png").Single(), [1, 2, 3]);
            var calls = 0;
            cache.GetOrRender("graph A", "v1", () => { calls++; return Png; }).Should().Equal(Png);
            calls.Should().Be(1);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void TruncatedPngWithValidSignatureIsRenderedAgain()
    {
        var directory = Path.Combine(Path.GetTempPath(), "myerp-mermaid-cache-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var cache = new MermaidImageCache(directory);
            cache.GetOrRender("graph B", "v1", () => Png);
            File.WriteAllBytes(Directory.GetFiles(directory, "*.png").Single(), Png[..^12]);
            var calls = 0;
            cache.GetOrRender("graph B", "v1", () => { calls++; return Png; });
            calls.Should().Be(1);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void PngWithDamagedImageDataIsRenderedAgain()
    {
        var directory = Path.Combine(Path.GetTempPath(), "myerp-mermaid-cache-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var cache = new MermaidImageCache(directory);
            cache.GetOrRender("graph D", "v1", () => Png);
            var path = Directory.GetFiles(directory, "*.png").Single();
            var damaged = Png.ToArray();
            damaged[45] ^= 1;
            File.WriteAllBytes(path, damaged);
            var calls = 0;
            cache.GetOrRender("graph D", "v1", () => { calls++; return Png; });
            calls.Should().Be(1);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void InvalidRendererOutputIsNotCached()
    {
        var directory = Path.Combine(Path.GetTempPath(), "myerp-mermaid-cache-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var cache = new MermaidImageCache(directory);
            Action invalid = () => cache.GetOrRender("graph C", "v1", () => Png[..^12]);
            invalid.Should().Throw<InvalidDataException>();
            Directory.Exists(directory).Should().BeFalse();
            cache.GetOrRender("graph C", "v1", () => Png).Should().Equal(Png);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }
}
