using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Finance_GlJournalLineAccountSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // No Posted writer exists before this migration. If any legacy line existed, the
            // empty/zero defaults violate the snapshot check and the migration fails instead of
            // fabricating account history (BR-FI-027, ADR-089).
            migrationBuilder.AddColumn<string>(
                name: "account_name",
                table: "gl_journal_line",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "account_version",
                table: "gl_journal_line",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "mapping_version",
                table: "gl_journal_line",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddCheckConstraint(
                name: "ck_gl_journal_line_account_snapshot",
                table: "gl_journal_line",
                sql: "LEN(TRIM([account_name])) > 0 AND [account_version] > 0 AND [mapping_version] > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_gl_journal_line_account_snapshot",
                table: "gl_journal_line");

            migrationBuilder.DropColumn(
                name: "account_name",
                table: "gl_journal_line");

            migrationBuilder.DropColumn(
                name: "account_version",
                table: "gl_journal_line");

            migrationBuilder.DropColumn(
                name: "mapping_version",
                table: "gl_journal_line");
        }
    }
}
