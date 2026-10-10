using Microsoft.EntityFrameworkCore.Migrations;

namespace MyERP.Migrations;

/// <summary>在 migration 中部署 SP（db-conventions.md 第 7 節）。修改 SP 必須新增 migration，<c>Down</c> 還原上一版或 DROP。</summary>
public static class ProcedureMigrationBuilderExtensions
{
    /// <summary>執行 <c>database/procedures/{path}.sql</c>（內容須為 <c>CREATE OR ALTER PROCEDURE</c>）。</summary>
    public static MigrationBuilder CreateOrAlterProcedure(this MigrationBuilder migrationBuilder, string path)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.Sql(ProcedureScripts.Read(path));
        return migrationBuilder;
    }

    /// <summary>刪除 SP；<paramref name="path"/> 同 <see cref="CreateOrAlterProcedure"/>，SP 名稱取檔名（只允許英數與底線）。</summary>
    public static MigrationBuilder DropProcedure(this MigrationBuilder migrationBuilder, string path)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var name = path.Replace('\\', '/').Split('/')[^1];
        if (name.Length == 0 || !name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
        {
            throw new ArgumentException($"Invalid stored procedure name '{name}'.", nameof(path));
        }

        migrationBuilder.Sql($"DROP PROCEDURE IF EXISTS dbo.{name};");
        return migrationBuilder;
    }
}
