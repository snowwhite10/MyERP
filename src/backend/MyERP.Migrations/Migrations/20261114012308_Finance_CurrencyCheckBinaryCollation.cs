using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyERP.Migrations.Migrations
{
    /// <summary>
    /// WU-FI-001f（ADR-114、BR-FI-148）：三個舊幣別檢查改用 Latin1_General_BIN，預設不分大小寫定序下不再接受小寫。
    /// AddCheckConstraint 預設 WITH CHECK：既有列如有不合格幣別，Up 失敗並整個回滾，不會靜默放寬或改資料。
    /// </summary>
    public partial class Finance_CurrencyCheckBinaryCollation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_gl_journal_currency",
                table: "gl_journal");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ar_posted_receivable_currency",
                table: "ar_posted_receivable");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ar_credit_limit_currency",
                table: "ar_credit_limit");

            migrationBuilder.AddCheckConstraint(
                name: "ck_gl_journal_currency",
                table: "gl_journal",
                sql: "[base_currency] COLLATE Latin1_General_BIN LIKE '[A-Z][A-Z][A-Z]'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ar_posted_receivable_currency",
                table: "ar_posted_receivable",
                sql: "[base_currency] COLLATE Latin1_General_BIN LIKE '[A-Z][A-Z][A-Z]'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ar_credit_limit_currency",
                table: "ar_credit_limit",
                sql: "[base_currency] COLLATE Latin1_General_BIN LIKE '[A-Z][A-Z][A-Z]'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_gl_journal_currency",
                table: "gl_journal");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ar_posted_receivable_currency",
                table: "ar_posted_receivable");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ar_credit_limit_currency",
                table: "ar_credit_limit");

            migrationBuilder.AddCheckConstraint(
                name: "ck_gl_journal_currency",
                table: "gl_journal",
                sql: "[base_currency] LIKE '[A-Z][A-Z][A-Z]'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ar_posted_receivable_currency",
                table: "ar_posted_receivable",
                sql: "[base_currency] LIKE '[A-Z][A-Z][A-Z]'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ar_credit_limit_currency",
                table: "ar_credit_limit",
                sql: "[base_currency] LIKE '[A-Z][A-Z][A-Z]'");
        }
    }
}
