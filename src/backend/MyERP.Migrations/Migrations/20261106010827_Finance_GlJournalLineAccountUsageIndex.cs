using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Finance_GlJournalLineAccountUsageIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_gl_journal_line_company_id_account_code",
                table: "gl_journal_line",
                columns: new[] { "company_id", "account_code" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_gl_journal_line_company_id_account_code",
                table: "gl_journal_line");
        }
    }
}
