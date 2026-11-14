using System;
using Microsoft.EntityFrameworkCore.Migrations;

#pragma warning disable CA1861 // EF Core generates constant column arrays for migration operations.

#nullable disable

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Finance_CollectionSourceFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ar_collection_receipt",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    customer_id = table.Column<long>(type: "bigint", nullable: false),
                    source_type = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    source_id = table.Column<long>(type: "bigint", nullable: false),
                    source_version = table.Column<int>(type: "int", nullable: false),
                    fingerprint = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                    gl_journal_id = table.Column<long>(type: "bigint", nullable: false),
                    business_date = table.Column<DateOnly>(type: "date", nullable: false),
                    bank_account_id = table.Column<long>(type: "bigint", nullable: false),
                    bank_reference = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    base_currency = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                    amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    unapplied_amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ar_collection_receipt", x => x.id);
                    table.UniqueConstraint("ak_ar_collection_receipt_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("ck_ar_collection_receipt_amounts", "[amount] > 0 AND [unapplied_amount] >= 0 AND [unapplied_amount] <= [amount]");
                    table.CheckConstraint("ck_ar_collection_receipt_bank", "[bank_account_id] > 0 AND LEN(TRIM([bank_reference])) > 0");
                    table.CheckConstraint("ck_ar_collection_receipt_currency", "[base_currency] COLLATE Latin1_General_BIN LIKE '[A-Z][A-Z][A-Z]'");
                    table.CheckConstraint("ck_ar_collection_receipt_identity", "[company_id] > 0 AND [customer_id] > 0 AND [source_id] > 0 AND [source_version] > 0 AND [gl_journal_id] > 0 AND LEN(TRIM([fingerprint])) > 0");
                    table.CheckConstraint("ck_ar_collection_receipt_type", "[source_type] = 'SalesCollection'");
                    table.ForeignKey(
                        name: "fk_ar_collection_receipt_ar_credit_key_company_id_customer_id",
                        columns: x => new { x.company_id, x.customer_id },
                        principalTable: "ar_credit_key",
                        principalColumns: new[] { "company_id", "customer_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ar_collection_receipt_gl_journal_company_id_gl_journal_id",
                        columns: x => new { x.company_id, x.gl_journal_id },
                        principalTable: "gl_journal",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ar_collection_settlement",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    customer_id = table.Column<long>(type: "bigint", nullable: false),
                    source_type = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    source_id = table.Column<long>(type: "bigint", nullable: false),
                    source_version = table.Column<int>(type: "int", nullable: false),
                    fingerprint = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                    gl_journal_id = table.Column<long>(type: "bigint", nullable: false),
                    receipt_id = table.Column<long>(type: "bigint", nullable: false),
                    business_date = table.Column<DateOnly>(type: "date", nullable: false),
                    base_currency = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                    total_amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ar_collection_settlement", x => x.id);
                    table.UniqueConstraint("ak_ar_collection_settlement_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("ck_ar_collection_settlement_amount", "[total_amount] > 0");
                    table.CheckConstraint("ck_ar_collection_settlement_currency", "[base_currency] COLLATE Latin1_General_BIN LIKE '[A-Z][A-Z][A-Z]'");
                    table.CheckConstraint("ck_ar_collection_settlement_identity", "[company_id] > 0 AND [customer_id] > 0 AND [source_id] > 0 AND [source_version] > 0 AND [gl_journal_id] > 0 AND LEN(TRIM([fingerprint])) > 0 AND [receipt_id] > 0");
                    table.CheckConstraint("ck_ar_collection_settlement_type", "[source_type] = 'CollectionWriteOff'");
                    table.ForeignKey(
                        name: "fk_ar_collection_settlement_ar_collection_receipt_company_id_receipt_id",
                        columns: x => new { x.company_id, x.receipt_id },
                        principalTable: "ar_collection_receipt",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ar_collection_settlement_ar_credit_key_company_id_customer_id",
                        columns: x => new { x.company_id, x.customer_id },
                        principalTable: "ar_credit_key",
                        principalColumns: new[] { "company_id", "customer_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ar_collection_settlement_gl_journal_company_id_gl_journal_id",
                        columns: x => new { x.company_id, x.gl_journal_id },
                        principalTable: "gl_journal",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ar_collection_allocation",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    settlement_id = table.Column<long>(type: "bigint", nullable: false),
                    receipt_id = table.Column<long>(type: "bigint", nullable: false),
                    receivable_id = table.Column<long>(type: "bigint", nullable: false),
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
                    table.PrimaryKey("pk_ar_collection_allocation", x => x.id);
                    table.CheckConstraint("ck_ar_collection_allocation_amount", "[amount] > 0");
                    table.CheckConstraint("ck_ar_collection_allocation_refs", "[company_id] > 0 AND [settlement_id] > 0 AND [receipt_id] > 0 AND [receivable_id] > 0");
                    table.ForeignKey(
                        name: "fk_ar_collection_allocation_ar_collection_receipt_company_id_receipt_id",
                        columns: x => new { x.company_id, x.receipt_id },
                        principalTable: "ar_collection_receipt",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ar_collection_allocation_ar_collection_settlement_company_id_settlement_id",
                        columns: x => new { x.company_id, x.settlement_id },
                        principalTable: "ar_collection_settlement",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ar_collection_allocation_ar_posted_receivable_company_id_receivable_id",
                        columns: x => new { x.company_id, x.receivable_id },
                        principalTable: "ar_posted_receivable",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ar_collection_allocation_company_id_receipt_id",
                table: "ar_collection_allocation",
                columns: new[] { "company_id", "receipt_id" })
                .Annotation("SqlServer:Include", new[] { "amount" });

            migrationBuilder.CreateIndex(
                name: "ix_ar_collection_allocation_company_id_receivable_id",
                table: "ar_collection_allocation",
                columns: new[] { "company_id", "receivable_id" })
                .Annotation("SqlServer:Include", new[] { "amount" });

            migrationBuilder.CreateIndex(
                name: "ux_ar_collection_allocation_company_id_settlement_id_receivable_id",
                table: "ar_collection_allocation",
                columns: new[] { "company_id", "settlement_id", "receivable_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ar_collection_receipt_company_id_customer_id_business_date",
                table: "ar_collection_receipt",
                columns: new[] { "company_id", "customer_id", "business_date" })
                .Annotation("SqlServer:Include", new[] { "unapplied_amount" });

            migrationBuilder.CreateIndex(
                name: "ix_ar_collection_receipt_company_id_gl_journal_id",
                table: "ar_collection_receipt",
                columns: new[] { "company_id", "gl_journal_id" });

            migrationBuilder.CreateIndex(
                name: "ux_ar_collection_receipt_company_id_bank_account_id_bank_reference",
                table: "ar_collection_receipt",
                columns: new[] { "company_id", "bank_account_id", "bank_reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_ar_collection_receipt_company_id_source_type_source_id",
                table: "ar_collection_receipt",
                columns: new[] { "company_id", "source_type", "source_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_ar_collection_receipt_gl_journal_id",
                table: "ar_collection_receipt",
                column: "gl_journal_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ar_collection_settlement_company_id_customer_id",
                table: "ar_collection_settlement",
                columns: new[] { "company_id", "customer_id" });

            migrationBuilder.CreateIndex(
                name: "ix_ar_collection_settlement_company_id_gl_journal_id",
                table: "ar_collection_settlement",
                columns: new[] { "company_id", "gl_journal_id" });

            migrationBuilder.CreateIndex(
                name: "ix_ar_collection_settlement_company_id_receipt_id",
                table: "ar_collection_settlement",
                columns: new[] { "company_id", "receipt_id" });

            migrationBuilder.CreateIndex(
                name: "ux_ar_collection_settlement_company_id_source_type_source_id",
                table: "ar_collection_settlement",
                columns: new[] { "company_id", "source_type", "source_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_ar_collection_settlement_gl_journal_id",
                table: "ar_collection_settlement",
                column: "gl_journal_id",
                unique: true);

            // BR-FI-033／034：收款過帳時未核銷＝金額；之後來源、銀行參考與金額不可改，未核銷只可減少；不可刪除。
            migrationBuilder.Sql("""
                CREATE TRIGGER dbo.tr_ar_collection_receipt_origin ON dbo.ar_collection_receipt
                AFTER INSERT, UPDATE, DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM deleted) AND NOT EXISTS (SELECT 1 FROM inserted)
                        THROW 51029, 'Posted FI collection receipts cannot be deleted.', 1;
                    IF NOT EXISTS (SELECT 1 FROM deleted)
                       AND EXISTS (SELECT 1 FROM inserted WHERE unapplied_amount <> amount)
                        THROW 51029, 'A posted FI collection receipt starts fully unapplied.', 1;
                    IF EXISTS (
                        SELECT 1 FROM deleted d JOIN inserted i ON i.id = d.id
                        WHERE i.company_id <> d.company_id OR i.customer_id <> d.customer_id
                           OR i.source_type <> d.source_type OR i.source_id <> d.source_id
                           OR i.source_version <> d.source_version
                           OR i.fingerprint COLLATE Latin1_General_BIN <> d.fingerprint COLLATE Latin1_General_BIN
                           OR i.gl_journal_id <> d.gl_journal_id OR i.business_date <> d.business_date
                           OR i.bank_account_id <> d.bank_account_id
                           OR i.bank_reference COLLATE Latin1_General_BIN <> d.bank_reference COLLATE Latin1_General_BIN
                           OR i.base_currency COLLATE Latin1_General_BIN <> d.base_currency COLLATE Latin1_General_BIN
                           OR i.amount <> d.amount OR i.unapplied_amount > d.unapplied_amount)
                        THROW 51029, 'Posted FI collection receipt origin is immutable and unapplied cash only decreases.', 1;
                END
                """);
            // BR-FI-035／037：核銷的客戶與幣別必須等於其收款；保存後不可修改或刪除。
            migrationBuilder.Sql("""
                CREATE TRIGGER dbo.tr_ar_collection_settlement_immutable ON dbo.ar_collection_settlement
                AFTER INSERT, UPDATE, DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM deleted)
                        THROW 51029, 'Posted FI collection settlements are immutable.', 1;
                    IF EXISTS (
                        SELECT 1 FROM inserted i
                        LEFT JOIN dbo.ar_collection_receipt r ON r.company_id = i.company_id AND r.id = i.receipt_id
                        WHERE r.id IS NULL OR r.customer_id <> i.customer_id
                           OR r.base_currency COLLATE Latin1_General_BIN <> i.base_currency COLLATE Latin1_General_BIN)
                        THROW 51029, 'A FI collection settlement must match its receipt customer and currency.', 1;
                END
                """);
            // BR-FI-035／037／040：分配的收款等於核銷的收款；應收必須是同客戶、同幣別的發票應收；保存後不可修改或刪除。
            migrationBuilder.Sql("""
                CREATE TRIGGER dbo.tr_ar_collection_allocation_immutable ON dbo.ar_collection_allocation
                AFTER INSERT, UPDATE, DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM deleted)
                        THROW 51029, 'Posted FI collection allocations are immutable.', 1;
                    IF EXISTS (
                        SELECT 1 FROM inserted i
                        LEFT JOIN dbo.ar_collection_settlement s ON s.company_id = i.company_id AND s.id = i.settlement_id
                        LEFT JOIN dbo.ar_posted_receivable r ON r.company_id = i.company_id AND r.id = i.receivable_id
                        WHERE s.id IS NULL OR r.id IS NULL OR s.receipt_id <> i.receipt_id
                           OR r.customer_id <> s.customer_id OR r.source_type <> 'Invoice'
                           OR r.base_currency COLLATE Latin1_General_BIN <> s.base_currency COLLATE Latin1_General_BIN)
                        THROW 51029, 'A FI collection allocation must settle an invoice receivable of its settlement customer and currency.', 1;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER dbo.tr_ar_collection_allocation_immutable;");
            migrationBuilder.Sql("DROP TRIGGER dbo.tr_ar_collection_settlement_immutable;");
            migrationBuilder.Sql("DROP TRIGGER dbo.tr_ar_collection_receipt_origin;");

            migrationBuilder.DropTable(
                name: "ar_collection_allocation");

            migrationBuilder.DropTable(
                name: "ar_collection_settlement");

            migrationBuilder.DropTable(
                name: "ar_collection_receipt");
        }
    }
}
