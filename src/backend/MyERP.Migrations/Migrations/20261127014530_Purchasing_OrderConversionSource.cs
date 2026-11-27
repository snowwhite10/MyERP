using System;
using Microsoft.EntityFrameworkCore.Migrations;

#pragma warning disable CA1861 // EF Core scaffolded migration column arrays.

#nullable disable

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Purchasing_OrderConversionSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "base_gross",
                table: "pur_purchase_order_line",
                type: "decimal(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "base_net",
                table: "pur_purchase_order_line",
                type: "decimal(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "base_tax",
                table: "pur_purchase_order_line",
                type: "decimal(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "exchange_rate",
                table: "pur_purchase_order_line",
                type: "decimal(19,8)",
                precision: 19,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "requisition_line_id",
                table: "pur_purchase_order_line",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "tax_rate_id",
                table: "pur_purchase_order_line",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "base_gross",
                table: "pur_purchase_order",
                type: "decimal(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "base_net",
                table: "pur_purchase_order",
                type: "decimal(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "base_tax",
                table: "pur_purchase_order",
                type: "decimal(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "branch_id",
                table: "pur_purchase_order",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "business_date",
                table: "pur_purchase_order",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "department_id",
                table: "pur_purchase_order",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "document_no",
                table: "pur_purchase_order",
                type: "varchar(50)",
                unicode: false,
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "owner_id",
                table: "pur_purchase_order",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "rfq_id",
                table: "pur_purchase_order",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "ak_pur_rfq_award_company_id_id",
                table: "pur_rfq_award",
                columns: new[] { "company_id", "id" });

            migrationBuilder.CreateTable(
                name: "pur_order_source",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    purchase_order_line_id = table.Column<long>(type: "bigint", nullable: false),
                    award_id = table.Column<long>(type: "bigint", nullable: false),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pur_order_source", x => x.id);
                    table.CheckConstraint("ck_pur_order_source_refs", "[purchase_order_line_id] > 0 AND [award_id] > 0");
                    table.ForeignKey(
                        name: "fk_pur_order_source_pur_purchase_order_line_company_id_purchase_order_line_id",
                        columns: x => new { x.company_id, x.purchase_order_line_id },
                        principalTable: "pur_purchase_order_line",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pur_order_source_pur_rfq_award_company_id_award_id",
                        columns: x => new { x.company_id, x.award_id },
                        principalTable: "pur_rfq_award",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_pur_purchase_order_line_company_id_requisition_line_id",
                table: "pur_purchase_order_line",
                columns: new[] { "company_id", "requisition_line_id" });

            migrationBuilder.CreateIndex(
                name: "ix_pur_purchase_order_line_company_id_tax_rate_id",
                table: "pur_purchase_order_line",
                columns: new[] { "company_id", "tax_rate_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_pur_order_line_conversion_amount",
                table: "pur_purchase_order_line",
                sql: "([base_net] IS NULL AND [base_tax] IS NULL AND [base_gross] IS NULL) OR ([base_net] >= 0 AND [base_tax] >= 0 AND [base_gross] = [base_net] + [base_tax])");

            migrationBuilder.CreateIndex(
                name: "ix_pur_purchase_order_company_id_document_no",
                table: "pur_purchase_order",
                columns: new[] { "company_id", "document_no" },
                unique: true,
                filter: "[document_no] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_pur_purchase_order_company_id_rfq_id",
                table: "pur_purchase_order",
                columns: new[] { "company_id", "rfq_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_pur_order_conversion_amount",
                table: "pur_purchase_order",
                sql: "([base_net] IS NULL AND [base_tax] IS NULL AND [base_gross] IS NULL) OR ([base_net] >= 0 AND [base_tax] >= 0 AND [base_gross] = [base_net] + [base_tax])");

            migrationBuilder.CreateIndex(
                name: "ix_pur_order_source_company_id_award_id",
                table: "pur_order_source",
                columns: new[] { "company_id", "award_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pur_order_source_company_id_purchase_order_line_id",
                table: "pur_order_source",
                columns: new[] { "company_id", "purchase_order_line_id" },
                unique: true);

            migrationBuilder.Sql("""
                CREATE TRIGGER dbo.tr_pur_order_source_immutable
                ON dbo.pur_order_source AFTER UPDATE, DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51036, 'PUR order source evidence is immutable.', 1;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER dbo.tr_pur_order_source_immutable;");
            migrationBuilder.DropTable(
                name: "pur_order_source");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_pur_rfq_award_company_id_id",
                table: "pur_rfq_award");

            migrationBuilder.DropIndex(
                name: "ix_pur_purchase_order_line_company_id_requisition_line_id",
                table: "pur_purchase_order_line");

            migrationBuilder.DropIndex(
                name: "ix_pur_purchase_order_line_company_id_tax_rate_id",
                table: "pur_purchase_order_line");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pur_order_line_conversion_amount",
                table: "pur_purchase_order_line");

            migrationBuilder.DropIndex(
                name: "ix_pur_purchase_order_company_id_document_no",
                table: "pur_purchase_order");

            migrationBuilder.DropIndex(
                name: "ix_pur_purchase_order_company_id_rfq_id",
                table: "pur_purchase_order");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pur_order_conversion_amount",
                table: "pur_purchase_order");

            migrationBuilder.DropColumn(
                name: "base_gross",
                table: "pur_purchase_order_line");

            migrationBuilder.DropColumn(
                name: "base_net",
                table: "pur_purchase_order_line");

            migrationBuilder.DropColumn(
                name: "base_tax",
                table: "pur_purchase_order_line");

            migrationBuilder.DropColumn(
                name: "exchange_rate",
                table: "pur_purchase_order_line");

            migrationBuilder.DropColumn(
                name: "requisition_line_id",
                table: "pur_purchase_order_line");

            migrationBuilder.DropColumn(
                name: "tax_rate_id",
                table: "pur_purchase_order_line");

            migrationBuilder.DropColumn(
                name: "base_gross",
                table: "pur_purchase_order");

            migrationBuilder.DropColumn(
                name: "base_net",
                table: "pur_purchase_order");

            migrationBuilder.DropColumn(
                name: "base_tax",
                table: "pur_purchase_order");

            migrationBuilder.DropColumn(
                name: "branch_id",
                table: "pur_purchase_order");

            migrationBuilder.DropColumn(
                name: "business_date",
                table: "pur_purchase_order");

            migrationBuilder.DropColumn(
                name: "department_id",
                table: "pur_purchase_order");

            migrationBuilder.DropColumn(
                name: "document_no",
                table: "pur_purchase_order");

            migrationBuilder.DropColumn(
                name: "owner_id",
                table: "pur_purchase_order");

            migrationBuilder.DropColumn(
                name: "rfq_id",
                table: "pur_purchase_order");
        }
    }
}
