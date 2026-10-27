using Microsoft.Data.SqlClient;

namespace MyERP.DocGen.Schema;

/// <summary>
/// 從 SQL Server 讀取 dbo 下的使用者資料表結構（MASTER_PROMPT 15.3：SDS 4.1 必須由實際資料庫產生）。
/// 排除 EF Core 的 <c>__EFMigrationsHistory</c>。
/// </summary>
public sealed class SqlServerSchemaSource(string connectionString) : ISchemaSource
{
    internal const string Query = """
        SELECT c.TABLE_NAME, c.COLUMN_NAME, c.DATA_TYPE, c.CHARACTER_MAXIMUM_LENGTH,
               CAST(c.NUMERIC_PRECISION AS INT), c.NUMERIC_SCALE, c.DATETIME_PRECISION, c.IS_NULLABLE,
               CAST(CASE WHEN EXISTS (
                   SELECT 1 FROM sys.indexes i
                   JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                   WHERE i.object_id = sc.object_id AND i.is_primary_key = 1
                     AND ic.column_id = sc.column_id) THEN 1 ELSE 0 END AS BIT),
               CAST(CASE WHEN EXISTS (
                   SELECT 1 FROM sys.foreign_key_columns fk
                   WHERE fk.parent_object_id = sc.object_id AND fk.parent_column_id = sc.column_id
                   ) THEN 1 ELSE 0 END AS BIT)
        FROM INFORMATION_SCHEMA.COLUMNS c
        JOIN INFORMATION_SCHEMA.TABLES t ON t.TABLE_SCHEMA = c.TABLE_SCHEMA AND t.TABLE_NAME = c.TABLE_NAME
        JOIN sys.tables st ON st.name = c.TABLE_NAME AND SCHEMA_NAME(st.schema_id) = c.TABLE_SCHEMA
        JOIN sys.columns sc ON sc.object_id = st.object_id AND sc.name = c.COLUMN_NAME
        WHERE t.TABLE_TYPE = 'BASE TABLE' AND t.TABLE_SCHEMA = 'dbo' AND t.TABLE_NAME <> '__EFMigrationsHistory'
        ORDER BY c.TABLE_NAME, c.ORDINAL_POSITION;
        """;

    public async Task<DatabaseSchema> ReadAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(Query, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        var tables = new List<TableSchema>();
        string? currentTable = null;
        var columns = new List<ColumnSchema>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var table = reader.GetString(0);
            if (currentTable is not null && table != currentTable)
            {
                tables.Add(new TableSchema(currentTable, columns));
                columns = [];
            }

            currentTable = table;
            columns.Add(new ColumnSchema(
                reader.GetString(1),
                reader.GetString(2),
                NullableInt(reader, 3),
                NullableInt(reader, 4),
                NullableInt(reader, 5),
                NullableInt(reader, 6),
                reader.GetString(7) == "YES",
                reader.GetBoolean(8),
                reader.GetBoolean(9)));
        }

        if (currentTable is not null)
        {
            tables.Add(new TableSchema(currentTable, columns));
        }

        return new DatabaseSchema(true, $"{connection.DataSource}/{connection.Database}", tables);
    }

    // INFORMATION_SCHEMA 的數值欄位型別不一（int / tinyint / smallint），統一轉 int。
    private static int? NullableInt(SqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : Convert.ToInt32(reader.GetValue(ordinal), System.Globalization.CultureInfo.InvariantCulture);
}
