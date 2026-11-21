using System;
using Microsoft.EntityFrameworkCore.Migrations;
#pragma warning disable CA1861 // EF Core generates constant column arrays for migration operations.

#nullable disable

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Sales_QuotationLinePricing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "override_at",
                table: "sal_quotation_line",
                type: "datetime2(6)",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "override_by",
                table: "sal_quotation_line",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "override_reason",
                table: "sal_quotation_line",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pricing_mode",
                table: "sal_quotation_line",
                type: "varchar(30)",
                unicode: false,
                maxLength: 30,
                nullable: false,
                defaultValue: "Legacy");

            migrationBuilder.AddColumn<long>(
                name: "pricing_rule_id",
                table: "sal_quotation_line",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "pricing_rule_version",
                table: "sal_quotation_line",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "rule_discount_ceiling",
                table: "sal_quotation_line",
                type: "decimal(7,4)",
                precision: 7,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "rule_unit_price",
                table: "sal_quotation_line",
                type: "decimal(19,6)",
                precision: 19,
                scale: 6,
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "ak_sal_price_rule_company_id_id",
                table: "sal_price_rule",
                columns: new[] { "company_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_sal_quotation_line_company_id_pricing_rule_id",
                table: "sal_quotation_line",
                columns: new[] { "company_id", "pricing_rule_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_sal_quotation_line_pricing",
                table: "sal_quotation_line",
                sql: "([pricing_mode] = 'Legacy' AND [pricing_rule_id] IS NULL AND [pricing_rule_version] IS NULL AND [rule_unit_price] IS NULL AND [rule_discount_ceiling] IS NULL AND [override_reason] IS NULL AND [override_by] IS NULL AND [override_at] IS NULL) OR ([pricing_mode] = 'Rule' AND [pricing_rule_id] IS NOT NULL AND [pricing_rule_id] > 0 AND [pricing_rule_version] IS NOT NULL AND [pricing_rule_version] > 0 AND [rule_unit_price] IS NOT NULL AND [rule_discount_ceiling] IS NOT NULL AND [unit_price] = [rule_unit_price] AND [discount_rate] <= [rule_discount_ceiling] AND [override_reason] IS NULL AND [override_by] IS NULL AND [override_at] IS NULL) OR ([pricing_mode] = 'Override' AND [override_reason] IS NOT NULL AND LEN([override_reason]) > 0 AND [override_by] IS NOT NULL AND [override_by] > 0 AND [override_at] IS NOT NULL AND (([pricing_rule_id] IS NULL AND [pricing_rule_version] IS NULL AND [rule_unit_price] IS NULL AND [rule_discount_ceiling] IS NULL) OR ([pricing_rule_id] IS NOT NULL AND [pricing_rule_id] > 0 AND [pricing_rule_version] IS NOT NULL AND [pricing_rule_version] > 0 AND [rule_unit_price] IS NOT NULL AND [rule_discount_ceiling] IS NOT NULL)))");

            migrationBuilder.AddForeignKey(
                name: "fk_sal_quotation_line_sal_price_rule",
                table: "sal_quotation_line",
                columns: new[] { "company_id", "pricing_rule_id" },
                principalTable: "sal_price_rule",
                principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_sal_quotation_line_sal_price_rule",
                table: "sal_quotation_line");

            migrationBuilder.DropIndex(
                name: "ix_sal_quotation_line_company_id_pricing_rule_id",
                table: "sal_quotation_line");

            migrationBuilder.DropCheckConstraint(
                name: "ck_sal_quotation_line_pricing",
                table: "sal_quotation_line");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_sal_price_rule_company_id_id",
                table: "sal_price_rule");

            migrationBuilder.DropColumn(
                name: "override_at",
                table: "sal_quotation_line");

            migrationBuilder.DropColumn(
                name: "override_by",
                table: "sal_quotation_line");

            migrationBuilder.DropColumn(
                name: "override_reason",
                table: "sal_quotation_line");

            migrationBuilder.DropColumn(
                name: "pricing_mode",
                table: "sal_quotation_line");

            migrationBuilder.DropColumn(
                name: "pricing_rule_id",
                table: "sal_quotation_line");

            migrationBuilder.DropColumn(
                name: "pricing_rule_version",
                table: "sal_quotation_line");

            migrationBuilder.DropColumn(
                name: "rule_discount_ceiling",
                table: "sal_quotation_line");

            migrationBuilder.DropColumn(
                name: "rule_unit_price",
                table: "sal_quotation_line");
        }
    }
}
