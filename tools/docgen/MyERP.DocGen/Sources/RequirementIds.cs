using System.Globalization;
using System.Text.RegularExpressions;

namespace MyERP.DocGen.Sources;

/// <summary>需求 ID 解析（MASTER_PROMPT 15.2）。</summary>
public static partial class RequirementIds
{
    /// <summary><c>REQ_F411{模組}.{子功能}.{序號}</c>。</summary>
    public static bool TryParseFunctional(string id, out int module, out int subFunction, out int sequence)
    {
        var match = FunctionalPattern().Match(id ?? "");
        module = subFunction = sequence = 0;
        if (!match.Success)
        {
            return false;
        }

        module = ToInt(match.Groups[1].Value);
        subFunction = ToInt(match.Groups[2].Value);
        sequence = ToInt(match.Groups[3].Value);
        return module > 0 && subFunction > 0 && sequence > 0;
    }

    /// <summary><c>REQ_DS43.{序號}</c>。</summary>
    public static bool TryParseDataStructure(string id, out int sequence)
    {
        var match = DataStructurePattern().Match(id ?? "");
        sequence = match.Success ? ToInt(match.Groups[1].Value) : 0;
        return sequence > 0;
    }

    /// <summary><c>REQ_IO_A{模組}.{介面}.{項目}</c>。</summary>
    public static bool TryParseInterfaceItem(string id, out int module, out int interfaceNo, out int item)
    {
        var match = InterfaceItemPattern().Match(id ?? "");
        module = interfaceNo = item = 0;
        if (!match.Success)
        {
            return false;
        }

        module = ToInt(match.Groups[1].Value);
        interfaceNo = ToInt(match.Groups[2].Value);
        item = ToInt(match.Groups[3].Value);
        return module > 0 && interfaceNo > 0 && item > 0;
    }

    /// <summary><c>REQ_NF42{小節 1–5}.{序號}</c>。</summary>
    public static bool TryParseNonFunctional(string id, out int section, out int sequence)
    {
        var match = NonFunctionalPattern().Match(id ?? "");
        section = sequence = 0;
        if (!match.Success)
        {
            return false;
        }

        section = ToInt(match.Groups[1].Value);
        sequence = ToInt(match.Groups[2].Value);
        return sequence > 0;
    }

    /// <summary>介面編號 <c>{模組}.{介面}</c>，例如 <c>2.4</c>。</summary>
    public static bool TryParseInterfaceNo(string text, out int module, out int interfaceNo)
    {
        var match = InterfaceNoPattern().Match(text ?? "");
        module = interfaceNo = 0;
        if (!match.Success)
        {
            return false;
        }

        module = ToInt(match.Groups[1].Value);
        interfaceNo = ToInt(match.Groups[2].Value);
        return module > 0 && interfaceNo > 0;
    }

    private static int ToInt(string text) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : 0;

    [GeneratedRegex(@"^REQ_F411(\d{1,3})\.(\d{1,3})\.(\d{1,3})$")]
    private static partial Regex FunctionalPattern();

    [GeneratedRegex(@"^REQ_DS43\.(\d{1,4})$")]
    private static partial Regex DataStructurePattern();

    [GeneratedRegex(@"^REQ_IO_A(\d{1,3})\.(\d{1,3})\.(\d{1,3})$")]
    private static partial Regex InterfaceItemPattern();

    [GeneratedRegex(@"^REQ_NF42([1-5])\.(\d{1,3})$")]
    private static partial Regex NonFunctionalPattern();

    [GeneratedRegex(@"^(\d{1,3})\.(\d{1,3})$")]
    private static partial Regex InterfaceNoPattern();
}
