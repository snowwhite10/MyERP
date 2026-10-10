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
                   SELECT 1 FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
                   JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE k
                     ON k.CONSTRAINT_NAME = tc.CONSTRAINT_NAME AND k.TABLE_SCHEMA = tc.TABLE_SCHEMA AND k.TABLE_NAME = tc.TABLE_NAME
                   WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY' AND tc.TABLE_SCHEMA = c.TABLE_SCHEMA
                     AND tc.TABLE_NAME = c.TABLE_NAME AND k.COLUMN_NAME = c.COLUMN_NAME) THEN 1 ELSE 0 END AS BIT),
               CAST(CASE WHEN EXISTS (
                   SELECT 1 FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
                   JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE k
                     ON k.CONSTRAINT_NAME = tc.CONSTRAINT_NAME AND k.TABLE_SCHEMA = tc.TABLE_SCHEMA AND k.TABLE_NAME = tc.TABLE_NAME
                   WHERE tc.CONSTRAINT_TYPE = 'FOREIGN KEY' AND tc.TABLE_SCHEMA = c.TABLE_SCHEMA
                     AND tc.TABLE_NAME = c.TABLE_NAME AND k.COLUMN_NAME = c.COLUMN_NAME) THEN 1 ELSE 0 END AS BIT)
        FROM INFORMATION_SCHEMA.COLUMNS c
        JOIN INFORMATION_SCHEMA.TABLES t ON t.TABLE_SCHEMA = c.TABLE_SCHEMA AND t.TABLE_NAME = c.TABLE_NAME
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
