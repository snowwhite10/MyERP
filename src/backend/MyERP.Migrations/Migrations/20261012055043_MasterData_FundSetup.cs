using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861 // EF migration generator emits literal index column arrays.

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class MasterData_FundSetup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "md_fund_account",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    fund_type = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    currency_id = table.Column<long>(type: "bigint", nullable: false),
                    bank_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    bank_account_no = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    ledger_account_id = table.Column<long>(type: "bigint", nullable: true),
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
                    table.PrimaryKey("pk_md_fund_account", x => x.id);
                    table.CheckConstraint("ck_md_fund_account_bank", "[fund_type] = 'Bank' OR ([bank_name] IS NULL AND [bank_account_no] IS NULL)");
                    table.CheckConstraint("ck_md_fund_account_custom_fields_json", "[custom_fields] IS NULL OR ISJSON([custom_fields]) = 1");
                    table.CheckConstraint("ck_md_fund_account_fund_type", "[fund_type] IN ('Cash', 'Bank')");
                    table.ForeignKey(
                        name: "fk_md_fund_account_md_account_company_id_ledger_account_id",
                        columns: x => new { x.company_id, x.ledger_account_id },
                        principalTable: "md_account",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "fk_md_fund_account_md_currency_company_id_currency_id",
                        columns: x => new { x.company_id, x.currency_id },
                        principalTable: "md_currency",
                        principalColumns: new[] { "company_id", "id" });
                });

            migrationBuilder.CreateTable(
                name: "md_income_expense_category",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    direction = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    ledger_account_id = table.Column<long>(type: "bigint", nullable: true),
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
                    table.PrimaryKey("pk_md_income_expense_category", x => x.id);
                    table.CheckConstraint("ck_md_income_expense_category_custom_fields_json", "[custom_fields] IS NULL OR ISJSON([custom_fields]) = 1");
                    table.CheckConstraint("ck_md_income_expense_category_direction", "[direction] IN ('Income', 'Expense')");
                    table.ForeignKey(
                        name: "fk_md_income_expense_category_md_account_company_id_ledger_account_id",
                        columns: x => new { x.company_id, x.ledger_account_id },
                        principalTable: "md_account",
                        principalColumns: new[] { "company_id", "id" });
                });

            migrationBuilder.CreateIndex(
                name: "ix_md_fund_account_company_id_currency_id",
                table: "md_fund_account",
                columns: new[] { "company_id", "currency_id" });

            migrationBuilder.CreateIndex(
                name: "ix_md_fund_account_company_id_ledger_account_id",
                table: "md_fund_account",
                columns: new[] { "company_id", "ledger_account_id" });

            migrationBuilder.CreateIndex(
                name: "ix_md_fund_account_company_id_name",
                table: "md_fund_account",
                columns: new[] { "company_id", "name" });

            migrationBuilder.CreateIndex(
                name: "ux_md_fund_account_company_id_code",
                table: "md_fund_account",
                columns: new[] { "company_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_md_income_expense_category_company_id_ledger_account_id",
                table: "md_income_expense_category",
                columns: new[] { "company_id", "ledger_account_id" });

            migrationBuilder.CreateIndex(
                name: "ix_md_income_expense_category_company_id_name",
                table: "md_income_expense_category",
                columns: new[] { "company_id", "name" });

            migrationBuilder.CreateIndex(
                name: "ux_md_income_expense_category_company_id_code",
                table: "md_income_expense_category",
                columns: new[] { "company_id", "code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "md_fund_account");

            migrationBuilder.DropTable(
                name: "md_income_expense_category");
        }
    }
}
