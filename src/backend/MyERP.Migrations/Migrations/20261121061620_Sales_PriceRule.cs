using System;
using Microsoft.EntityFrameworkCore.Migrations;
#pragma warning disable CA1861 // EF Core generates constant column arrays for migration operations.

#nullable disable

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Sales_PriceRule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sal_price_rule",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    item_id = table.Column<long>(type: "bigint", nullable: false),
                    customer_id = table.Column<long>(type: "bigint", nullable: true),
                    currency_code = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: false),
                    unit_price = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false),
                    discount_ceiling = table.Column<decimal>(type: "decimal(7,4)", precision: 7, scale: 4, nullable: false),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sal_price_rule", x => x.id);
                    table.CheckConstraint("ck_sal_price_rule_ceiling", "[discount_ceiling] >= 0 AND [discount_ceiling] <= 100");
                    table.CheckConstraint("ck_sal_price_rule_currency", "LEN([currency_code]) = 3 AND [currency_code] COLLATE Latin1_General_BIN LIKE '[A-Z][A-Z][A-Z]'");
                    table.CheckConstraint("ck_sal_price_rule_dates", "[valid_to] >= [valid_from]");
                    table.CheckConstraint("ck_sal_price_rule_price", "[unit_price] >= 0");
                    table.CheckConstraint("ck_sal_price_rule_refs", "[item_id] > 0 AND ([customer_id] IS NULL OR [customer_id] > 0)");
                });

            migrationBuilder.CreateIndex(
                name: "ix_sal_price_rule_company_id_customer_id",
                table: "sal_price_rule",
                columns: new[] { "company_id", "customer_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sal_price_rule_company_id_item_id_currency_code_customer_id_valid_from_id",
                table: "sal_price_rule",
                columns: new[] { "company_id", "item_id", "currency_code", "customer_id", "valid_from", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_sal_price_rule_company_id_valid_from_id",
                table: "sal_price_rule",
                columns: new[] { "company_id", "valid_from", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sal_price_rule");
        }
    }
}
