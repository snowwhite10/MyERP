using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Migrations.Migrations;

/// <summary>Enable versioned, consistent report reads without holding posting locks.</summary>
[DbContext(typeof(MyErpDbContext))]
[Migration("20261017211700_Inventory_StockFlowSnapshot")]
public sealed class Inventory_StockFlowSnapshot : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("DECLARE @sql nvarchar(max) = N'ALTER DATABASE ' + QUOTENAME(DB_NAME()) + N' SET ALLOW_SNAPSHOT_ISOLATION ON'; EXEC(@sql);", suppressTransaction: true);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("DECLARE @sql nvarchar(max) = N'ALTER DATABASE ' + QUOTENAME(DB_NAME()) + N' SET ALLOW_SNAPSHOT_ISOLATION OFF'; EXEC(@sql);", suppressTransaction: true);
}
