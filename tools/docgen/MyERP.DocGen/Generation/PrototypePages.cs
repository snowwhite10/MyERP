using System.Text;
using System.Text.Encodings.Web;
using MyERP.DocGen.Sources;

namespace MyERP.DocGen.Generation;

public sealed record PrototypePage(string InterfaceKey, string FileName);

/// <summary>Standalone SRS 4.5 wireframes. The ordered SRS 4.4 interface list is the sole source.</summary>
public static class PrototypePages
{
    private static readonly UTF8Encoding Utf8 = new(false);

    public static IReadOnlyList<PrototypePage> Generate(RequirementSet requirements, string directory)
    {
        ArgumentNullException.ThrowIfNull(requirements);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory.CreateDirectory(directory);
        var pages = new List<PrototypePage>();
        foreach (var definition in requirements.OrderedInterfaces)
        {
            var fileName = $"4.5.1.{definition.Module}.{definition.Number}.html";
            var html = RenderPage(definition, requirements.ItemsOf(definition));
            File.WriteAllText(Path.Combine(directory, fileName), html, Utf8);
            pages.Add(new PrototypePage(definition.Key, fileName));
        }

        var currentFiles = pages.Select(page => page.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var oldFile in Directory.EnumerateFiles(directory, "4.5.1.*.html"))
        {
            if (!currentFiles.Contains(Path.GetFileName(oldFile))) File.Delete(oldFile);
        }

        File.WriteAllText(Path.Combine(directory, "index.html"), RenderIndex(requirements.OrderedInterfaces, pages), Utf8);
        return pages;
    }

    private static string RenderPage(InterfaceDefinition definition, IEnumerable<InterfaceItem> sourceItems)
    {
        var items = sourceItems.ToList();
        var html = new StringBuilder();
        Start(html, $"4.5.1.{definition.Key} {definition.Name}");
        html.Append("<a class=\"skip\" href=\"#main\">Skip to content</a><main id=\"main\" class=\"page\"><p class=\"eyebrow\">SRS 4.5 · User Portal · ")
            .Append(E(definition.Key)).Append("</p><h1>").Append(E(definition.Name))
            .Append("</h1><p class=\"note\">Wireframe generated from SRS 4.4 interface items. Content and controls are illustrative.</p>")
            .Append("<section class=\"panel\" aria-label=\"Interface items\">");
        if (items.Count == 0)
        {
            html.Append("<p class=\"empty\">No interface items are specified in SRS 4.4.</p>");
        }
        else
        {
            foreach (var item in items)
            {
                html.Append("<div class=\"item\" data-requirement-id=\"").Append(E(item.Id)).Append("\"><div>")
                    .Append("<strong>").Append(E(item.Item)).Append("</strong><small>").Append(E(item.Id)).Append("</small></div>")
                    .Append("<p>").Append(E(item.Purpose)).Append("</p></div>");
            }
        }
        html.Append("</section></main></body></html>");
        return html.ToString();
    }

    private static string RenderIndex(IEnumerable<InterfaceDefinition> definitions, IReadOnlyList<PrototypePage> pages)
    {
        var html = new StringBuilder();
        Start(html, "SRS 4.5 User Interface Prototypes");
        html.Append("<a class=\"skip\" href=\"#main\">Skip to content</a><main id=\"main\" class=\"page\"><p class=\"eyebrow\">SRS 4.5 · User Portal</p><h1>Interface Prototypes</h1>")
            .Append("<p class=\"note\">The list follows the SRS 4.4 interface sequence.</p><nav aria-label=\"Interface prototypes\"><ol>");
        foreach (var pair in definitions.Zip(pages))
        {
            html.Append("<li><a href=\"").Append(E(pair.Second.FileName)).Append("\">4.5.1.")
                .Append(E(pair.First.Key)).Append(' ').Append(E(pair.First.Name)).Append("</a></li>");
        }
        html.Append("</ol></nav></main></body></html>");
        return html.ToString();
    }

    private static void Start(StringBuilder html, string title)
    {
        html.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>")
            .Append(E(title)).Append("</title><style>")
            .Append("*{box-sizing:border-box}html{scroll-behavior:smooth}body{margin:0;background:#f4f7fb;color:#202b3a;font:16px/1.5 Arial,sans-serif}.page{max-width:1060px;margin:48px auto;padding:0 32px;overflow-wrap:anywhere}.skip{position:absolute;left:-10000px;top:8px;background:#fff;padding:8px}.skip:focus{left:8px;z-index:1}.eyebrow{color:#315a85;font-weight:700;letter-spacing:.05em}h1{font-size:32px;margin:8px 0 12px;text-wrap:balance}.note{color:#526176;margin-bottom:28px}.panel{background:#fff;border:1px solid #c9d5e2;border-radius:8px;padding:24px}.item{display:grid;grid-template-columns:minmax(200px,1fr) 2fr;gap:24px;padding:16px 0;border-bottom:1px solid #e2e8f0}.item:last-child{border-bottom:0}.item strong,.item small{display:block}.item small{color:#5c6b80;margin-top:4px}.item p{margin:0}.empty{color:#526176}li{margin:8px 0}a{color:#1d568b}a:hover{color:#123d66;text-decoration-thickness:2px}a:focus-visible{outline:3px solid #e6a23c;outline-offset:3px}@media(max-width:650px){.page{margin:24px auto;padding:0 16px}.item{display:block}.item p{margin-top:8px}}@media(prefers-color-scheme:dark){html{color-scheme:dark}body{background:#17212d;color:#f1f5f9}.eyebrow,a{color:#91c7ff}a:hover{color:#c8e4ff}.note,.empty,.item small{color:#bdcbd9}.panel{background:#243244;border-color:#45617d}.item{border-color:#45617d}.skip{background:#243244}}@media(prefers-reduced-motion:reduce){html{scroll-behavior:auto}}")
            .Append("</style></head><body>");
    }

    private static string E(string value) => HtmlEncoder.Default.Encode(value);
}
