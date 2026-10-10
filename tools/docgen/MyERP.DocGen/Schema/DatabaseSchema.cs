using System.Globalization;

namespace MyERP.DocGen.Schema;

/// <summary>INFORMATION_SCHEMA 讀出的欄位（SDS 4.1 欄位表來源）。</summary>
public sealed record ColumnSchema(
    string Name,
    string DataType,
    int? MaxLength,
    int? NumericPrecision,
    int? NumericScale,
    int? DateTimePrecision,
    bool IsNullable,
    bool IsPrimaryKey,
    bool IsForeignKey)
{
    /// <summary>SDS 4.1「Size」欄：字串為長度（MAX）、decimal 為 (p,s)、datetime2 等為 (精度)，其餘 <c>-</c>。</summary>
    public string SizeText => DataType.ToUpperInvariant() switch
    {
        "CHAR" or "VARCHAR" or "NCHAR" or "NVARCHAR" or "BINARY" or "VARBINARY" =>
            MaxLength is -1 ? "MAX" : MaxLength?.ToString(CultureInfo.InvariantCulture) ?? "-",
        "DECIMAL" or "NUMERIC" =>
            NumericPrecision is { } p ? string.Create(CultureInfo.InvariantCulture, $"({p},{NumericScale ?? 0})") : "-",
        "DATETIME2" or "DATETIMEOFFSET" or "TIME" =>
            DateTimePrecision is { } d ? string.Create(CultureInfo.InvariantCulture, $"({d})") : "-",
        _ => "-",
    };

    /// <summary>SDS 4.1「PK/FK/IS NULL/NOT NULL」欄，例如 <c>PK, NOT NULL</c>、<c>FK, IS NULL</c>。</summary>
    public string ConstraintText
    {
        get
        {
            var parts = new List<string>(3);
            if (IsPrimaryKey)
            {
                parts.Add("PK");
            }

            if (IsForeignKey)
            {
                parts.Add("FK");
            }

            parts.Add(IsNullable ? "IS NULL" : "NOT NULL");
            return string.Join(", ", parts);
        }
    }
}

/// <summary>一張資料表（依欄位次序）。</summary>
public sealed record TableSchema(string Name, IReadOnlyList<ColumnSchema> Columns)
{
    public IEnumerable<string> PrimaryKey => Columns.Where(c => c.IsPrimaryKey).Select(c => c.Name);
}

/// <summary>資料庫結構快照；<see cref="IsAvailable"/> 為 false 時代表沒有連線，SDS 4.1 只輸出佔位說明。</summary>
public sealed record DatabaseSchema(bool IsAvailable, string Source, IReadOnlyList<TableSchema> Tables)
{
    public static DatabaseSchema Unavailable(string reason) => new(false, reason, []);
}

/// <summary>資料庫結構來源（正式為 SQL Server INFORMATION_SCHEMA；測試可替換）。</summary>
public interface ISchemaSource
{
    Task<DatabaseSchema> ReadAsync(CancellationToken cancellationToken);
}
