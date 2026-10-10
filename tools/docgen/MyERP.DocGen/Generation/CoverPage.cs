using System.Globalization;
using MyERP.DocGen.Word;

namespace MyERP.DocGen.Generation;

/// <summary>
/// SRS / SDS 共用封面（MASTER_PROMPT 15.5）：Contact No.、Project Name、Editor、Date 與簽署表。
/// 佔位符保留給人手填寫，不虛構真實人名或公司名。
/// </summary>
public static class CoverPage
{
    public static void Write(WordDocumentWriter writer, string title, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(writer);
        for (var i = 0; i < 6; i++)
        {
            writer.Blank();
        }

        writer.Paragraph(title, styleId: BlueWhaleStyles.TitleStyleId);
        writer.Blank();
        writer.Paragraph("Contact No.: <Contact No.>", center: true);
        writer.Paragraph("Project Name: MyERP", center: true);
        writer.Paragraph("Editor: <Editor>", center: true);
        writer.Paragraph("Date: " + FormatDate(date), center: true);
        writer.Blank();
        writer.Blank();
        RequirementTables.SignOff(writer);
    }

    public static string FormatDate(DateOnly date) => date.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);
}
