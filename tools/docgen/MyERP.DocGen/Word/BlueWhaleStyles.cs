using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace MyERP.DocGen.Word;

/// <summary>
/// BlueWhale SRS / SDS 版面（reference-notes.md 2.3，已核對參考 docx 的 styles.xml）：
/// Arial 11pt、行距 1.5；Heading 1–5 = 20 / 17 / 14 / 12 / 11pt，皆非粗體，H3 灰色 #434343。
/// MyERP 兩份文件統一 A4、邊界 1 inch、頁首頁尾距 0.5 inch。
/// </summary>
public static class BlueWhaleStyles
{
    public const string FontName = "Arial";
    public const int BodyHalfPoints = 22;
    public const int LineSpacing = 360;

    public const uint A4Width = 11906;
    public const uint A4Height = 16838;
    public const int Margin = 1440;
    public const uint HeaderFooterDistance = 720;

    /// <summary>可用版面寬度（twips）= A4 寬 − 左右邊界。</summary>
    public const int TextWidth = (int)A4Width - (2 * Margin);

    public const string CaptionStyleId = "Caption";
    public const string TitleStyleId = "Title";
    public const string SubtitleStyleId = "Subtitle";

    /// <summary>Heading 1–6：(字級 half-points, 段前, 段後, 顏色, 左右對齊)。</summary>
    private static readonly (int Size, int Before, int After, string? Color, bool Justify)[] Headings =
    [
        (40, 400, 120, null, false),
        (34, 360, 120, null, true),
        (28, 320, 80, "434343", false),
        (24, 280, 80, null, true),
        (22, 240, 80, null, false),
        (22, 240, 80, null, false),
    ];

    public static string HeadingStyleId(int level) => $"Heading{Math.Clamp(level, 1, 6)}";

    public static Styles Create()
    {
        var styles = new Styles(
            new DocDefaults(
                new RunPropertiesDefault(new RunPropertiesBaseStyle(
                    new RunFonts { Ascii = FontName, HighAnsi = FontName, EastAsia = FontName, ComplexScript = FontName },
                    new FontSize { Val = BodyHalfPoints.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                    new FontSizeComplexScript { Val = BodyHalfPoints.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                    new Languages { Val = "en" })),
                new ParagraphPropertiesDefault(new ParagraphPropertiesBaseStyle(
                    new SpacingBetweenLines { Line = LineSpacing.ToString(System.Globalization.CultureInfo.InvariantCulture), LineRule = LineSpacingRuleValues.Auto }))));

        styles.Append(new Style(new StyleName { Val = "Normal" }, new PrimaryStyle())
        {
            Type = StyleValues.Paragraph,
            StyleId = "Normal",
            Default = true,
        });

        for (var level = 1; level <= Headings.Length; level++)
        {
            styles.Append(HeadingStyle(level));
        }

        styles.Append(ParagraphStyle(TitleStyleId, "Title", 52, 0, 60, null, italic: false, center: true));
        styles.Append(ParagraphStyle(SubtitleStyleId, "Subtitle", 30, 0, 320, "666666", italic: false, center: true));
        styles.Append(ParagraphStyle(CaptionStyleId, "caption", BodyHalfPoints, 120, 240, null, italic: true, center: true));
        styles.Append(new Style(new StyleName { Val = "Table Grid" }, new UIPriority { Val = 59 })
        {
            Type = StyleValues.Table,
            StyleId = "TableGrid",
        });
        return styles;
    }

    private static Style HeadingStyle(int level)
    {
        var (size, before, after, color, justify) = Headings[level - 1];
        var paragraph = new StyleParagraphProperties(
            new KeepNext(),
            new KeepLines(),
            new SpacingBetweenLines { Before = Twips(before), After = Twips(after), LineRule = LineSpacingRuleValues.Auto });
        if (justify)
        {
            paragraph.Append(new Justification { Val = JustificationValues.Both });
        }

        paragraph.Append(new OutlineLevel { Val = level - 1 });

        var run = new StyleRunProperties(new Bold { Val = false }, new BoldComplexScript { Val = false });
        if (color is not null)
        {
            run.Append(new Color { Val = color });
        }

        run.Append(new FontSize { Val = Twips(size) }, new FontSizeComplexScript { Val = Twips(size) });

        return new Style(
            new StyleName { Val = $"heading {level}" },
            new BasedOn { Val = "Normal" },
            new NextParagraphStyle { Val = "Normal" },
            new UIPriority { Val = 9 },
            new PrimaryStyle(),
            paragraph,
            run)
        {
            Type = StyleValues.Paragraph,
            StyleId = HeadingStyleId(level),
        };
    }

    private static Style ParagraphStyle(string id, string name, int size, int before, int after, string? color, bool italic, bool center)
    {
        var paragraph = new StyleParagraphProperties(
            new SpacingBetweenLines { Before = Twips(before), After = Twips(after), LineRule = LineSpacingRuleValues.Auto });
        if (center)
        {
            paragraph.Append(new Justification { Val = JustificationValues.Center });
        }

        var run = new StyleRunProperties();
        if (italic)
        {
            run.Append(new Italic(), new ItalicComplexScript());
        }

        if (color is not null)
        {
            run.Append(new Color { Val = color });
        }

        run.Append(new FontSize { Val = Twips(size) }, new FontSizeComplexScript { Val = Twips(size) });

        return new Style(
            new StyleName { Val = name },
            new BasedOn { Val = "Normal" },
            new NextParagraphStyle { Val = "Normal" },
            new PrimaryStyle(),
            paragraph,
            run)
        {
            Type = StyleValues.Paragraph,
            StyleId = id,
        };
    }

    private static string Twips(int value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
