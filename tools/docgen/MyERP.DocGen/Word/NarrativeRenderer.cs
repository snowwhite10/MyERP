using MyERP.DocGen.Markdown;
using MyERP.DocGen.Sources;

namespace MyERP.DocGen.Word;

/// <summary>
/// 把 05-doc-sources 的敘述性 Markdown 寫入 Word。來源檔內的標題不產生 Word 標題
/// （章節編號由 docgen 控制），改為粗體段落（BlueWhale 4.6.5 即以普通段落作小標題）。
/// 來源檔不存在時寫入佔位段落，並記錄於 <see cref="MissingSources"/>。
/// </summary>
public sealed class NarrativeRenderer(RepositoryLayout layout)
{
    private readonly List<string> _missing = [];

    public IReadOnlyList<string> MissingSources => _missing;

    /// <param name="relativePath">相對於 docs/05-doc-sources 的路徑，例如 <c>srs/ch1-1-purpose.md</c>。</param>
    /// <param name="writer">Destination document.</param>
    public void Render(WordDocumentWriter writer, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(writer);
        var path = Path.Combine(layout.DocSourcesDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var display = "docs/05-doc-sources/" + relativePath;
        if (!File.Exists(path))
        {
            _missing.Add(display);
            writer.Paragraph($"[Pending] This section will be generated from {display}.", italic: true);
            return;
        }

        RenderBlocks(writer, MarkdownDocument.Load(path).Blocks, new MermaidCliRenderer(layout));
    }

    public static void RenderBlocks(WordDocumentWriter writer, IEnumerable<MarkdownBlock> blocks, IMermaidRenderer? mermaid = null)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(blocks);
        foreach (var block in blocks)
        {
            switch (block)
            {
                case MarkdownHeading heading:
                    writer.Paragraph(MarkdownInline.ToPlainText(heading.Text), bold: true);
                    break;
                case MarkdownParagraph paragraph:
                    var content = MarkdownInline.ToPlainText(paragraph.Text);
                    if (content.StartsWith("Figure ", StringComparison.Ordinal)) writer.Caption(content);
                    else writer.Paragraph(content);
                    break;
                case MarkdownList list:
                    writer.Bullets(list.Items.Select(MarkdownInline.ToPlainText), list.Ordered);
                    break;
                case MarkdownTable table:
                    RequirementTables.Markdown(writer, table);
                    writer.Blank();
                    break;
                case MarkdownCode code when code.Language.Equals("mermaid", StringComparison.OrdinalIgnoreCase):
                    if (string.IsNullOrWhiteSpace(code.Text))
                    {
                        throw new InvalidDataException($"Mermaid diagram at line {code.Line} is empty.");
                    }
                    writer.Image((mermaid ?? throw new InvalidOperationException("Mermaid renderer is required.")).RenderPng(code.Text));
                    break;
                case MarkdownCode code:
                    writer.Paragraph(code.Text);
                    break;
            }
        }
    }
}
