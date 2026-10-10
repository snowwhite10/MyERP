using MyERP.Shared.Errors;

namespace MyERP.Shared.Paging;

/// <summary>單一排序欄位；<see cref="Field"/> 為白名單中的標準名稱（DTO 屬性名，camelCase）。</summary>
public sealed record SortField(string Field, bool Descending);

/// <summary>
/// 解析 <c>sort=-docDate,docNo</c>。欄位必須在白名單內（不分大小寫比對、回傳標準名稱），
/// 否則 400；由各資源把標準名稱對應到實體欄位，**不得**動態拼接 SQL。
/// </summary>
public static class SortSpec
{
    public static IReadOnlyList<SortField> Parse(string? sort, IReadOnlyCollection<string> allowedFields, string? defaultSort = null)
    {
        ArgumentNullException.ThrowIfNull(allowedFields);

        var text = string.IsNullOrWhiteSpace(sort) ? defaultSort : sort;
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var result = new List<SortField>();
        foreach (var raw in text.Split(','))
        {
            var part = raw.Trim();
            var descending = part.StartsWith('-');
            var name = descending ? part[1..] : part;
            var field = allowedFields.FirstOrDefault(f => string.Equals(f, name, StringComparison.OrdinalIgnoreCase));
            if (field is null)
            {
                throw Invalid($"Sort field '{name}' is not allowed. Allowed: {string.Join(", ", allowedFields)}.");
            }

            if (result.Exists(r => r.Field == field))
            {
                throw Invalid($"Sort field '{field}' is specified more than once.");
            }

            result.Add(new SortField(field, descending));
        }

        return result;
    }

    private static RequestValidationException Invalid(string message) => new("sort", message);
}
