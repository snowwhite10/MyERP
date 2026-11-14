using System;
using Microsoft.EntityFrameworkCore.Migrations;

#pragma warning disable CA1861 // EF Core generates constant column arrays for migration operations.

#nullable disable

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Finance_PurchaseSourceFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ap_purchase_source",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    supplier_id = table.Column<long>(type: "bigint", nullable: false),
                    source_type = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    source_id = table.Column<long>(type: "bigint", nullable: false),
                    source_version = table.Column<int>(type: "int", nullable: false),
                    fingerprint = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                    gl_journal_id = table.Column<long>(type: "bigint", nullable: false),
                    business_date = table.Column<DateOnly>(type: "date", nullable: false),
                    currency = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                    base_currency = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                    exchange_rate = table.Column<decimal>(type: "decimal(19,8)", precision: 19, scale: 8, nullable: false),
                    net_amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    tax_amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    gross_amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    base_net_amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    base_tax_amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    base_gross_amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ap_purchase_source", x => x.id);
                    table.UniqueConstraint("ak_ap_purchase_source_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("ck_ap_purchase_source_amounts", "[net_amount] >= 0 AND [tax_amount] >= 0 AND [gross_amount] > 0 AND [net_amount] + [tax_amount] = [gross_amount] AND [base_net_amount] >= 0 AND [base_tax_amount] >= 0 AND [base_gross_amount] > 0 AND [base_net_amount] + [base_tax_amount] = [base_gross_amount]");
                    table.CheckConstraint("ck_ap_purchase_source_currency", "[currency] COLLATE Latin1_General_BIN LIKE '[A-Z][A-Z][A-Z]' AND [base_currency] COLLATE Latin1_General_BIN LIKE '[A-Z][A-Z][A-Z]' AND [exchange_rate] > 0");
                    table.CheckConstraint("ck_ap_purchase_source_identity", "[company_id] > 0 AND [supplier_id] > 0 AND [source_id] > 0 AND [source_version] > 0 AND [gl_journal_id] > 0 AND LEN(TRIM([fingerprint])) > 0");
                    table.CheckConstraint("ck_ap_purchase_source_type", "[source_type] IN ('PurchaseInvoice')");
                    table.ForeignKey(
                        name: "fk_ap_purchase_source_gl_journal_company_id_gl_journal_id",
                        columns: x => new { x.company_id, x.gl_journal_id },
                        principalTable: "gl_journal",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ap_posted_payable",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    supplier_id = table.Column<long>(type: "bigint", nullable: false),
                    purchase_source_id = table.Column<long>(type: "bigint", nullable: false),
                    posting_date = table.Column<DateOnly>(type: "date", nullable: false),
                    currency = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                    base_currency = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                    original_amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    open_amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    original_base_amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    open_base_amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ap_posted_payable", x => x.id);
                    table.CheckConstraint("ck_ap_posted_payable_amounts", "[original_amount] > 0 AND [original_base_amount] > 0 AND [open_amount] >= 0 AND [open_amount] <= [original_amount] AND [open_base_amount] >= 0 AND [open_base_amount] <= [original_base_amount] AND (([open_amount] = 0 AND [open_base_amount] = 0) OR ([open_amount] > 0 AND [open_base_amount] > 0))");
                    table.CheckConstraint("ck_ap_posted_payable_currency", "[currency] COLLATE Latin1_General_BIN LIKE '[A-Z][A-Z][A-Z]' AND [base_currency] COLLATE Latin1_General_BIN LIKE '[A-Z][A-Z][A-Z]'");
                    table.CheckConstraint("ck_ap_posted_payable_refs", "[company_id] > 0 AND [supplier_id] > 0 AND [purchase_source_id] > 0");
                    table.ForeignKey(
                        name: "fk_ap_posted_payable_ap_purchase_source_company_id_purchase_source_id",
                        columns: x => new { x.company_id, x.purchase_source_id },
                        principalTable: "ap_purchase_source",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ap_posted_payable_company_id_supplier_id_posting_date_id",
                table: "ap_posted_payable",
                columns: new[] { "company_id", "supplier_id", "posting_date", "id" })
                .Annotation("SqlServer:Include", new[] { "currency", "open_amount", "open_base_amount" });

            migrationBuilder.CreateIndex(
                name: "ux_ap_posted_payable_company_id_purchase_source_id",
                table: "ap_posted_payable",
                columns: new[] { "company_id", "purchase_source_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ap_purchase_source_company_id_gl_journal_id",
                table: "ap_purchase_source",
                columns: new[] { "company_id", "gl_journal_id" });

            migrationBuilder.CreateIndex(
                name: "ix_ap_purchase_source_company_id_supplier_id_business_date",
                table: "ap_purchase_source",
                columns: new[] { "company_id", "supplier_id", "business_date" });

            migrationBuilder.CreateIndex(
                name: "ux_ap_purchase_source_company_id_source_type_source_id",
                table: "ap_purchase_source",
                columns: new[] { "company_id", "source_type", "source_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_ap_purchase_source_gl_journal_id",
                table: "ap_purchase_source",
                column: "gl_journal_id",
                unique: true);

            // BR-FI-022：已過帳採購來源不可修改或刪除；更正須以新的補償來源。
            migrationBuilder.Sql("""
                CREATE TRIGGER dbo.tr_ap_purchase_source_immutable ON dbo.ap_purchase_source
                AFTER UPDATE, DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM deleted)
                        THROW 51026, 'Posted FI purchase sources are immutable.', 1;
                END
                """);
            // BR-FI-019／022：應付原始資料必須等於其來源且保存後不可改；之後只可減少未付金額；不可刪除。
            migrationBuilder.Sql("""
                CREATE TRIGGER dbo.tr_ap_posted_payable_origin ON dbo.ap_posted_payable
                AFTER INSERT, UPDATE, DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM deleted) AND NOT EXISTS (SELECT 1 FROM inserted)
                        THROW 51026, 'Posted FI payables cannot be deleted.', 1;
                    IF EXISTS (
                        SELECT 1 FROM deleted d JOIN inserted i ON i.id = d.id
                        WHERE i.company_id <> d.company_id OR i.supplier_id <> d.supplier_id
                           OR i.purchase_source_id <> d.purchase_source_id OR i.posting_date <> d.posting_date
                           OR i.currency COLLATE Latin1_General_BIN <> d.currency COLLATE Latin1_General_BIN
                           OR i.base_currency COLLATE Latin1_General_BIN <> d.base_currency COLLATE Latin1_General_BIN
                           OR i.original_amount <> d.original_amount OR i.original_base_amount <> d.original_base_amount)
                        THROW 51026, 'Posted FI payable origin is immutable.', 1;
                    IF EXISTS (
                        SELECT 1 FROM inserted i
                        LEFT JOIN dbo.ap_purchase_source s ON s.company_id = i.company_id AND s.id = i.purchase_source_id
                        WHERE s.id IS NULL OR s.supplier_id <> i.supplier_id OR s.business_date <> i.posting_date
                           OR s.currency COLLATE Latin1_General_BIN <> i.currency COLLATE Latin1_General_BIN
                           OR s.base_currency COLLATE Latin1_General_BIN <> i.base_currency COLLATE Latin1_General_BIN
                           OR s.gross_amount <> i.original_amount OR s.base_gross_amount <> i.original_base_amount)
                        THROW 51026, 'Posted FI payable must match its purchase source.', 1;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER dbo.tr_ap_posted_payable_origin;");
            migrationBuilder.Sql("DROP TRIGGER dbo.tr_ap_purchase_source_immutable;");

            migrationBuilder.DropTable(
                name: "ap_posted_payable");

            migrationBuilder.DropTable(
                name: "ap_purchase_source");
        }
    }
}
