using Microsoft.EntityFrameworkCore.Migrations;
using MyERP.Shared.Infrastructure.Persistence;

#nullable disable

namespace MyERP.Migrations.Migrations
{
    /// <summary>
    /// 資料庫基線（WU-P0-008，ADR-006）：
    /// 1) 定序 —— 由 Migrate 建庫時 EF 以模型的 UseCollation 產生 <c>CREATE DATABASE ... COLLATE</c>；
    ///    資料庫若由他人預先建立且定序不同，於此 ALTER（不可在交易內，且需無其他連線；有相依物件時失敗，應改為重建資料庫）。
    /// 2) 範例 SP <c>usp_sys_database_info</c>，驗證 SP 部署機制。
    /// </summary>
    public partial class Core_Baseline : Migration
    {
        private const string EnsureCollationSql =
            "IF CONVERT(NVARCHAR(128), DATABASEPROPERTYEX(DB_NAME(), 'Collation')) <> N'" + MyErpDbContext.DatabaseCollation + "'\n" +
            "BEGIN\n" +
            "    DECLARE @sql NVARCHAR(MAX) = N'ALTER DATABASE ' + QUOTENAME(DB_NAME()) + N' COLLATE " + MyErpDbContext.DatabaseCollation + "';\n" +
            "    EXEC (@sql);\n" +
            "END";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EnsureCollationSql, suppressTransaction: true);
            migrationBuilder.CreateOrAlterProcedure("sys/usp_sys_database_info");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 定序不還原（建庫屬性）；只移除本 migration 建立的物件
            migrationBuilder.DropProcedure("sys/usp_sys_database_info");
        }
    }
}
