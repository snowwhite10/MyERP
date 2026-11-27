using System;
using Microsoft.EntityFrameworkCore.Migrations;
#pragma warning disable CA1861 // EF Core generates constant column arrays for migration operations.

#nullable disable

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Finance_OtherPaymentSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ap_other_payment_source",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    source_type = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    source_id = table.Column<long>(type: "bigint", nullable: false),
                    source_version = table.Column<int>(type: "int", nullable: false),
                    fingerprint = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                    gl_journal_id = table.Column<long>(type: "bigint", nullable: false),
                    business_date = table.Column<DateOnly>(type: "date", nullable: false),
                    expense_category_id = table.Column<long>(type: "bigint", nullable: false),
                    expense_account_id = table.Column<long>(type: "bigint", nullable: false),
                    mapping_version = table.Column<int>(type: "int", nullable: false),
                    bank_account_id = table.Column<long>(type: "bigint", nullable: false),
                    bank_reference = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    base_currency = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                    amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ap_other_payment_source", x => x.id);
                    table.UniqueConstraint("ak_ap_other_payment_source_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("ck_ap_other_payment_source_amount", "[amount] > 0");
                    table.CheckConstraint("ck_ap_other_payment_source_currency", "[base_currency] COLLATE Latin1_General_BIN LIKE '[A-Z][A-Z][A-Z]'");
                    table.CheckConstraint("ck_ap_other_payment_source_identity", "[company_id] > 0 AND [source_id] > 0 AND [source_version] > 0 AND [gl_journal_id] > 0 AND LEN(TRIM([fingerprint])) > 0");
                    table.CheckConstraint("ck_ap_other_payment_source_refs", "[expense_category_id] > 0 AND [expense_account_id] > 0 AND [mapping_version] > 0 AND [bank_account_id] > 0 AND LEN(TRIM([bank_reference])) > 0");
                    table.CheckConstraint("ck_ap_other_payment_source_type", "[source_type] = 'OtherPayment'");
                    table.ForeignKey(
                        name: "fk_ap_other_payment_source_ap_other_payment_doc_company_id_source_id",
                        columns: x => new { x.company_id, x.source_id },
                        principalTable: "ap_other_payment_doc",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ap_other_payment_source_gl_journal_company_id_gl_journal_id",
                        columns: x => new { x.company_id, x.gl_journal_id },
                        principalTable: "gl_journal",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "gl_other_payment_account_map",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    expense_category_id = table.Column<long>(type: "bigint", nullable: false),
                    expense_account_id = table.Column<long>(type: "bigint", nullable: false),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gl_other_payment_account_map", x => x.id);
                    table.CheckConstraint("ck_gl_other_payment_account_map_identity", "[company_id] > 0 AND [expense_category_id] > 0 AND [expense_account_id] > 0 AND [version] > 0");
                });

            migrationBuilder.CreateIndex(
                name: "ix_ap_other_payment_source_company_id_expense_category_id_business_date",
                table: "ap_other_payment_source",
                columns: new[] { "company_id", "expense_category_id", "business_date" })
                .Annotation("SqlServer:Include", new[] { "amount" });

            migrationBuilder.CreateIndex(
                name: "ix_ap_other_payment_source_company_id_gl_journal_id",
                table: "ap_other_payment_source",
                columns: new[] { "company_id", "gl_journal_id" });

            migrationBuilder.CreateIndex(
                name: "ix_ap_other_payment_source_company_id_source_id",
                table: "ap_other_payment_source",
                columns: new[] { "company_id", "source_id" });

            migrationBuilder.CreateIndex(
                name: "ux_ap_other_payment_source_company_id_bank_account_id_bank_reference",
                table: "ap_other_payment_source",
                columns: new[] { "company_id", "bank_account_id", "bank_reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_ap_other_payment_source_company_id_source_type_source_id",
                table: "ap_other_payment_source",
                columns: new[] { "company_id", "source_type", "source_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_ap_other_payment_source_gl_journal_id",
                table: "ap_other_payment_source",
                column: "gl_journal_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_gl_other_payment_account_map_company_id_expense_category_id",
                table: "gl_other_payment_account_map",
                columns: new[] { "company_id", "expense_category_id" },
                unique: true);

            migrationBuilder.Sql("""
                CREATE TRIGGER dbo.tr_ap_other_payment_source_immutable ON dbo.ap_other_payment_source
                AFTER INSERT, UPDATE, DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM deleted)
                        THROW 51039, 'Posted FI other payment sources are immutable.', 1;
                    IF EXISTS (
                        SELECT 1 FROM inserted i
                        LEFT JOIN dbo.ap_other_payment_doc d ON d.company_id = i.company_id AND d.id = i.source_id
                        WHERE d.id IS NULL OR d.is_deleted = 1
                           OR d.expense_category_id <> i.expense_category_id
                           OR d.bank_account_id <> i.bank_account_id
                           OR d.bank_reference COLLATE Latin1_General_BIN <> i.bank_reference COLLATE Latin1_General_BIN
                           OR d.business_date <> i.business_date
                           OR d.currency COLLATE Latin1_General_BIN <> i.base_currency COLLATE Latin1_General_BIN
                           OR d.amount <> i.amount)
                        THROW 51039, 'A posted FI other payment source must match its saved document.', 1;
                END
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER dbo.tr_ap_other_payment_doc_posted_reference ON dbo.ap_other_payment_doc
                AFTER INSERT, UPDATE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (
                        SELECT 1 FROM inserted i
                        JOIN dbo.ap_other_payment_source s
                          ON s.company_id = i.company_id AND s.bank_account_id = i.bank_account_id
                         AND s.bank_reference = i.bank_reference
                        WHERE i.is_deleted = 0 AND s.source_id <> i.id)
                        THROW 51039, 'The withdrawal reference is already used by a posted FI other payment.', 1;
                END
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER dbo.tr_gl_other_payment_account_map_guard ON dbo.gl_other_payment_account_map
                AFTER UPDATE, DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM deleted d WHERE NOT EXISTS (SELECT 1 FROM inserted i WHERE i.id = d.id))
                        THROW 51039, 'FI other payment account maps cannot be deleted.', 1;
                    IF EXISTS (SELECT 1 FROM deleted d JOIN inserted i ON i.id = d.id
                               WHERE i.company_id <> d.company_id OR i.expense_category_id <> d.expense_category_id
                                  OR i.version <= d.version)
                        THROW 51039, 'FI other payment account map updates must keep company and category and increase the version.', 1;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER dbo.tr_gl_other_payment_account_map_guard;");
            migrationBuilder.Sql("DROP TRIGGER dbo.tr_ap_other_payment_doc_posted_reference;");
            migrationBuilder.Sql("DROP TRIGGER dbo.tr_ap_other_payment_source_immutable;");            migrationBuilder.DropTable(
                name: "ap_other_payment_source");

            migrationBuilder.DropTable(
                name: "gl_other_payment_account_map");
        }
    }
}
