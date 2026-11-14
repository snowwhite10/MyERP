using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Finance_SalesCollectionAccountMap : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "gl_sales_collection_account_map",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    unapplied_cash_account_code = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    receivable_account_code = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gl_sales_collection_account_map", x => x.id);
                    table.CheckConstraint("ck_gl_sales_collection_account_map_codes", "LEN(TRIM([unapplied_cash_account_code])) > 0 AND LEN(TRIM([receivable_account_code])) > 0");
                    table.CheckConstraint("ck_gl_sales_collection_account_map_distinct", "UPPER(TRIM([unapplied_cash_account_code])) <> UPPER(TRIM([receivable_account_code]))");
                    table.CheckConstraint("ck_gl_sales_collection_account_map_identity", "[company_id] > 0 AND [version] > 0");
                });

            migrationBuilder.CreateIndex(
                name: "ux_gl_sales_collection_account_map_company_id",
                table: "gl_sales_collection_account_map",
                column: "company_id",
                unique: true);

            // 映射不可刪除；修改必須遞增版本且不可改公司（ADR-120，同 ADR-109／116）。刪後重建或版本回退會令舊版本號對應另一組科目。
            migrationBuilder.Sql("""
                CREATE TRIGGER dbo.tr_gl_sales_collection_account_map_guard ON dbo.gl_sales_collection_account_map
                AFTER UPDATE, DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM deleted d WHERE NOT EXISTS (SELECT 1 FROM inserted i WHERE i.id = d.id))
                        THROW 51030, 'FI sales collection account maps cannot be deleted.', 1;
                    IF EXISTS (SELECT 1 FROM deleted d JOIN inserted i ON i.id = d.id
                               WHERE i.company_id <> d.company_id OR i.version <= d.version)
                        THROW 51030, 'FI sales collection account map updates must keep the company and increase the version.', 1;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER dbo.tr_gl_sales_collection_account_map_guard;");
            migrationBuilder.DropTable(
                name: "gl_sales_collection_account_map");
        }
    }
}
