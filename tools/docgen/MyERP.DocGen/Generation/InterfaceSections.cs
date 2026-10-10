using MyERP.DocGen.Sources;
using MyERP.DocGen.Word;

namespace MyERP.DocGen.Generation;

/// <summary>
/// SRS 4.4、4.5 與 SDS 4.2 共用的介面清單寫法：三處以同一份 <see cref="RequirementSet.OrderedInterfaces"/>
/// 產生，保證名稱、編號與次序一致（BlueWhale 原文兩處次序不同，MyERP 不得沿用）。
/// </summary>
public static class InterfaceSections
{
    /// <param name="prefix">章節前綴，例如 <c>4.4.1</c>、<c>4.5.1</c>、<c>4.2.1</c>。</param>
    /// <param name="body">每個介面標題之後的內容。</param>
    /// <param name="writer">Destination document.</param>
    /// <param name="requirements">Requirement source.</param>
    public static void Write(WordDocumentWriter writer, RequirementSet requirements, string prefix, Action<InterfaceDefinition> body)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(requirements);
        ArgumentNullException.ThrowIfNull(body);
        var interfaces = requirements.OrderedInterfaces.ToList();
        if (interfaces.Count == 0)
        {
            writer.Paragraph("[Pending] No user interfaces have been defined yet.", italic: true);
            return;
        }

        foreach (var module in interfaces.GroupBy(i => i.Module))
        {
            writer.Heading(4, $"{prefix}.{module.Key} {ModuleCatalog.NameOf(module.Key)} Module");
            foreach (var definition in module)
            {
                writer.Heading(5, $"{prefix}.{definition.Module}.{definition.Number} {definition.Name}");
                body(definition);
            }
        }
    }

    /// <summary>截圖佔位（原型 / 實際系統截圖由之後的 Playwright 工作單元產生）。</summary>
    public static void ScreenshotPlaceholder(WordDocumentWriter writer, InterfaceDefinition definition, string kind)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(definition);
        writer.Paragraph($"[Pending] {kind} screenshot of {definition.Name}.", italic: true, center: true);
    }
}
