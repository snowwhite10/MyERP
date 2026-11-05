using System;
using Microsoft.EntityFrameworkCore.Migrations;

#pragma warning disable CA1861 // EF Core generates constant column arrays for migration operations.

#nullable disable

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Finance_GlJournalFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "ak_gl_period_company_id_id",
                table: "gl_period",
                columns: new[] { "company_id", "id" });

            migrationBuilder.CreateTable(
                name: "gl_source_key",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    source_module = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    source_type = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    source_id = table.Column<long>(type: "bigint", nullable: false),
                    source_version = table.Column<int>(type: "int", nullable: false),
                    fingerprint = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gl_source_key", x => x.id);
                    table.UniqueConstraint("ak_gl_source_key_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("ck_gl_source_key_identity", "[company_id] > 0 AND [source_id] > 0 AND [source_version] > 0 AND LEN(TRIM([source_module])) > 0 AND LEN(TRIM([source_type])) > 0 AND LEN(TRIM([fingerprint])) > 0");
                });

            migrationBuilder.CreateTable(
                name: "gl_journal",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    period_id = table.Column<long>(type: "bigint", nullable: false),
                    source_key_id = table.Column<long>(type: "bigint", nullable: false),
                    business_date = table.Column<DateOnly>(type: "date", nullable: false),
                    base_currency = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                    status = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    total_debit = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    total_credit = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gl_journal", x => x.id);
                    table.UniqueConstraint("ak_gl_journal_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("ck_gl_journal_balance", "[total_debit] > 0 AND [total_debit] = [total_credit]");
                    table.CheckConstraint("ck_gl_journal_currency", "[base_currency] LIKE '[A-Z][A-Z][A-Z]'");
                    table.CheckConstraint("ck_gl_journal_refs", "[company_id] > 0 AND [period_id] > 0 AND [source_key_id] > 0");
                    table.CheckConstraint("ck_gl_journal_status", "[status] = 'Posted'");
                    table.ForeignKey(
                        name: "fk_gl_journal_gl_period_company_id_period_id",
                        columns: x => new { x.company_id, x.period_id },
                        principalTable: "gl_period",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_gl_journal_gl_source_key_company_id_source_key_id",
                        columns: x => new { x.company_id, x.source_key_id },
                        principalTable: "gl_source_key",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "gl_journal_line",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    journal_id = table.Column<long>(type: "bigint", nullable: false),
                    sequence = table.Column<int>(type: "int", nullable: false),
                    account_id = table.Column<long>(type: "bigint", nullable: false),
                    account_code = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    debit = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    credit = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gl_journal_line", x => x.id);
                    table.CheckConstraint("ck_gl_journal_line_refs", "[company_id] > 0 AND [journal_id] > 0 AND [account_id] > 0 AND [sequence] > 0 AND LEN(TRIM([account_code])) > 0");
                    table.CheckConstraint("ck_gl_journal_line_side", "([debit] > 0 AND [credit] = 0) OR ([debit] = 0 AND [credit] > 0)");
                    table.ForeignKey(
                        name: "fk_gl_journal_line_gl_journal_company_id_journal_id",
                        columns: x => new { x.company_id, x.journal_id },
                        principalTable: "gl_journal",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_gl_journal_company_id_source_key_id",
                table: "gl_journal",
                columns: new[] { "company_id", "source_key_id" });

            migrationBuilder.CreateIndex(
                name: "ix_gl_journal_company_period_business_date_status",
                table: "gl_journal",
                columns: new[] { "company_id", "period_id", "business_date", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_gl_journal_source_key_id",
                table: "gl_journal",
                column: "source_key_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_gl_journal_line_company_id_journal_id",
                table: "gl_journal_line",
                columns: new[] { "company_id", "journal_id" });

            migrationBuilder.CreateIndex(
                name: "ux_gl_journal_line_journal_id_sequence",
                table: "gl_journal_line",
                columns: new[] { "journal_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_gl_source_key_company_module_type_source",
                table: "gl_source_key",
                columns: new[] { "company_id", "source_module", "source_type", "source_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "gl_journal_line");

            migrationBuilder.DropTable(
                name: "gl_journal");

            migrationBuilder.DropTable(
                name: "gl_source_key");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_gl_period_company_id_id",
                table: "gl_period");
        }
    }
}
