using System;
using Microsoft.EntityFrameworkCore.Migrations;

#pragma warning disable CA1861 // EF Core scaffolded migration column arrays.

#nullable disable

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Purchasing_RfqAward : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "ak_pur_quotation_line_company_id_id",
                table: "pur_quotation_line",
                columns: new[] { "company_id", "id" });

            migrationBuilder.CreateTable(
                name: "pur_rfq_award",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    rfq_id = table.Column<long>(type: "bigint", nullable: false),
                    rfq_line_id = table.Column<long>(type: "bigint", nullable: false),
                    quotation_id = table.Column<long>(type: "bigint", nullable: false),
                    quotation_line_id = table.Column<long>(type: "bigint", nullable: false),
                    supplier_id = table.Column<long>(type: "bigint", nullable: false),
                    reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    quantity = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false),
                    unit_price = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false),
                    currency_code = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                    base_currency_code = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                    tax_rate_id = table.Column<long>(type: "bigint", nullable: false),
                    tax_rate_percent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    tax_rate_from = table.Column<DateOnly>(type: "date", nullable: false),
                    tax_rate_to = table.Column<DateOnly>(type: "date", nullable: true),
                    exchange_rate = table.Column<decimal>(type: "decimal(19,8)", precision: 19, scale: 8, nullable: false),
                    exchange_rate_id = table.Column<long>(type: "bigint", nullable: true),
                    exchange_rate_from = table.Column<DateOnly>(type: "date", nullable: false),
                    exchange_rate_to = table.Column<DateOnly>(type: "date", nullable: true),
                    comparison_date = table.Column<DateOnly>(type: "date", nullable: false),
                    base_net = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    base_tax = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    base_gross = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pur_rfq_award", x => x.id);
                    table.CheckConstraint("ck_pur_rfq_award_amount", "[base_net] >= 0 AND [base_tax] >= 0 AND [base_gross] = [base_net] + [base_tax]");
                    table.CheckConstraint("ck_pur_rfq_award_dates", "([tax_rate_to] IS NULL OR [tax_rate_to] >= [tax_rate_from]) AND ([exchange_rate_to] IS NULL OR [exchange_rate_to] >= [exchange_rate_from])");
                    table.CheckConstraint("ck_pur_rfq_award_price", "[unit_price] >= 0 AND [exchange_rate] > 0 AND [tax_rate_percent] >= 0");
                    table.CheckConstraint("ck_pur_rfq_award_quantity", "[quantity] > 0");
                    table.ForeignKey(
                        name: "fk_pur_rfq_award_pur_quotation_line_company_id_quotation_line_id",
                        columns: x => new { x.company_id, x.quotation_line_id },
                        principalTable: "pur_quotation_line",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pur_rfq_award_pur_rfq_line_company_id_rfq_line_id",
                        columns: x => new { x.company_id, x.rfq_line_id },
                        principalTable: "pur_rfq_line",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_pur_rfq_award_company_id_exchange_rate_id",
                table: "pur_rfq_award",
                columns: new[] { "company_id", "exchange_rate_id" });

            migrationBuilder.CreateIndex(
                name: "ix_pur_rfq_award_company_id_quotation_id",
                table: "pur_rfq_award",
                columns: new[] { "company_id", "quotation_id" });

            migrationBuilder.CreateIndex(
                name: "ix_pur_rfq_award_company_id_quotation_line_id",
                table: "pur_rfq_award",
                columns: new[] { "company_id", "quotation_line_id" });

            migrationBuilder.CreateIndex(
                name: "ix_pur_rfq_award_company_id_rfq_id_id",
                table: "pur_rfq_award",
                columns: new[] { "company_id", "rfq_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_pur_rfq_award_company_id_rfq_line_id",
                table: "pur_rfq_award",
                columns: new[] { "company_id", "rfq_line_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pur_rfq_award_company_id_supplier_id",
                table: "pur_rfq_award",
                columns: new[] { "company_id", "supplier_id" });

            migrationBuilder.CreateIndex(
                name: "ix_pur_rfq_award_company_id_tax_rate_id",
                table: "pur_rfq_award",
                columns: new[] { "company_id", "tax_rate_id" });

            migrationBuilder.Sql("""
                CREATE TRIGGER dbo.tr_pur_rfq_award_immutable
                ON dbo.pur_rfq_award AFTER UPDATE, DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51072, 'RFQ awards are immutable.', 1;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pur_rfq_award");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_pur_quotation_line_company_id_id",
                table: "pur_quotation_line");
        }
    }
}
