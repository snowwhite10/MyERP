using System;
using Microsoft.EntityFrameworkCore.Migrations;

#pragma warning disable CA1861 // EF Core generated index column arrays

#nullable disable

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class MasterData_PaymentSetup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "md_payment_term",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    due_type = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    days = table.Column<int>(type: "int", nullable: true),
                    closing_day = table.Column<int>(type: "int", nullable: true),
                    months = table.Column<int>(type: "int", nullable: true),
                    payment_day = table.Column<int>(type: "int", nullable: true),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    code = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    custom_fields = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_md_payment_term", x => x.id);
                    table.CheckConstraint("ck_md_payment_term_custom_fields_json", "[custom_fields] IS NULL OR ISJSON([custom_fields]) = 1");
                    table.CheckConstraint("ck_md_payment_term_due_type", "[due_type] IN ('NetDays', 'ClosingDays', 'ClosingMonthDay')");
                    table.CheckConstraint("ck_md_payment_term_ranges", "([days] IS NULL OR [days] BETWEEN 0 AND 365) AND ([closing_day] IS NULL OR [closing_day] BETWEEN 1 AND 31) AND ([months] IS NULL OR [months] BETWEEN 0 AND 12) AND ([payment_day] IS NULL OR [payment_day] BETWEEN 1 AND 31)");
                    table.CheckConstraint("ck_md_payment_term_rule", "([due_type] = 'NetDays' AND [days] IS NOT NULL AND [closing_day] IS NULL AND [months] IS NULL AND [payment_day] IS NULL) OR ([due_type] = 'ClosingDays' AND [days] IS NOT NULL AND [closing_day] IS NOT NULL AND [months] IS NULL AND [payment_day] IS NULL) OR ([due_type] = 'ClosingMonthDay' AND [days] IS NULL AND [closing_day] IS NOT NULL AND [months] IS NOT NULL AND [payment_day] IS NOT NULL)");
                });

            migrationBuilder.CreateTable(
                name: "md_settlement_method",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    code = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    custom_fields = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_md_settlement_method", x => x.id);
                    table.CheckConstraint("ck_md_settlement_method_custom_fields_json", "[custom_fields] IS NULL OR ISJSON([custom_fields]) = 1");
                });

            migrationBuilder.CreateIndex(
                name: "ix_md_payment_term_company_id_name",
                table: "md_payment_term",
                columns: new[] { "company_id", "name" });

            migrationBuilder.CreateIndex(
                name: "ux_md_payment_term_company_id_code",
                table: "md_payment_term",
                columns: new[] { "company_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_md_settlement_method_company_id_name",
                table: "md_settlement_method",
                columns: new[] { "company_id", "name" });

            migrationBuilder.CreateIndex(
                name: "ux_md_settlement_method_company_id_code",
                table: "md_settlement_method",
                columns: new[] { "company_id", "code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "md_payment_term");

            migrationBuilder.DropTable(
                name: "md_settlement_method");
        }
    }
}
