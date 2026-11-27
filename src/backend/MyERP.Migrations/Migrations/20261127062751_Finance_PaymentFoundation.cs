using System;
using Microsoft.EntityFrameworkCore.Migrations;

#pragma warning disable CA1861 // EF Core generates constant column arrays for migration operations.

#nullable disable

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Finance_PaymentFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "ak_ap_posted_payable_company_id_id",
                table: "ap_posted_payable",
                columns: new[] { "company_id", "id" });

            migrationBuilder.CreateTable(
                name: "ap_payment_source",
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
                    table.PrimaryKey("pk_ap_payment_source", x => x.id);
                    table.UniqueConstraint("ak_ap_payment_source_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("ck_ap_payment_source_amounts", "[amount] > 0 AND [unapplied_amount] >= 0 AND [unapplied_amount] <= [amount]");
                    table.CheckConstraint("ck_ap_payment_source_bank", "[bank_account_id] > 0 AND LEN(TRIM([bank_reference])) > 0");
                    table.CheckConstraint("ck_ap_payment_source_currency", "[base_currency] COLLATE Latin1_General_BIN LIKE '[A-Z][A-Z][A-Z]'");
                    table.CheckConstraint("ck_ap_payment_source_identity", "[company_id] > 0 AND [supplier_id] > 0 AND [source_id] > 0 AND [source_version] > 0 AND [gl_journal_id] > 0 AND LEN(TRIM([fingerprint])) > 0");
                    table.CheckConstraint("ck_ap_payment_source_type", "[source_type] = 'PurchasePayment'");
                    table.ForeignKey(
                        name: "fk_ap_payment_source_gl_journal_company_id_gl_journal_id",
                        columns: x => new { x.company_id, x.gl_journal_id },
                        principalTable: "gl_journal",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ap_payment_settlement",
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
                    payment_id = table.Column<long>(type: "bigint", nullable: false),
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
                    table.PrimaryKey("pk_ap_payment_settlement", x => x.id);
                    table.UniqueConstraint("ak_ap_payment_settlement_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("ck_ap_payment_settlement_amount", "[total_amount] > 0");
                    table.CheckConstraint("ck_ap_payment_settlement_currency", "[base_currency] COLLATE Latin1_General_BIN LIKE '[A-Z][A-Z][A-Z]'");
                    table.CheckConstraint("ck_ap_payment_settlement_identity", "[company_id] > 0 AND [supplier_id] > 0 AND [source_id] > 0 AND [source_version] > 0 AND [gl_journal_id] > 0 AND LEN(TRIM([fingerprint])) > 0 AND [payment_id] > 0");
                    table.CheckConstraint("ck_ap_payment_settlement_type", "[source_type] = 'PaymentWriteOff'");
                    table.ForeignKey(
                        name: "fk_ap_payment_settlement_ap_payment_source_company_id_payment_id",
                        columns: x => new { x.company_id, x.payment_id },
                        principalTable: "ap_payment_source",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ap_payment_settlement_gl_journal_company_id_gl_journal_id",
                        columns: x => new { x.company_id, x.gl_journal_id },
                        principalTable: "gl_journal",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ap_payment_allocation",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    settlement_id = table.Column<long>(type: "bigint", nullable: false),
                    payment_id = table.Column<long>(type: "bigint", nullable: false),
                    payable_id = table.Column<long>(type: "bigint", nullable: false),
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
                    table.PrimaryKey("pk_ap_payment_allocation", x => x.id);
                    table.CheckConstraint("ck_ap_payment_allocation_amount", "[amount] > 0");
                    table.CheckConstraint("ck_ap_payment_allocation_refs", "[company_id] > 0 AND [settlement_id] > 0 AND [payment_id] > 0 AND [payable_id] > 0");
                    table.ForeignKey(
                        name: "fk_ap_payment_allocation_ap_payment_settlement_company_id_settlement_id",
                        columns: x => new { x.company_id, x.settlement_id },
                        principalTable: "ap_payment_settlement",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ap_payment_allocation_ap_payment_source_company_id_payment_id",
                        columns: x => new { x.company_id, x.payment_id },
                        principalTable: "ap_payment_source",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ap_payment_allocation_ap_posted_payable_company_id_payable_id",
                        columns: x => new { x.company_id, x.payable_id },
                        principalTable: "ap_posted_payable",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ap_payment_allocation_company_id_payable_id",
                table: "ap_payment_allocation",
                columns: new[] { "company_id", "payable_id" })
                .Annotation("SqlServer:Include", new[] { "amount" });

            migrationBuilder.CreateIndex(
                name: "ix_ap_payment_allocation_company_id_payment_id",
                table: "ap_payment_allocation",
                columns: new[] { "company_id", "payment_id" })
                .Annotation("SqlServer:Include", new[] { "amount" });

            migrationBuilder.CreateIndex(
                name: "ux_ap_payment_allocation_company_id_settlement_id_payable_id",
                table: "ap_payment_allocation",
                columns: new[] { "company_id", "settlement_id", "payable_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ap_payment_settlement_company_id_gl_journal_id",
                table: "ap_payment_settlement",
                columns: new[] { "company_id", "gl_journal_id" });

            migrationBuilder.CreateIndex(
                name: "ix_ap_payment_settlement_company_id_payment_id",
                table: "ap_payment_settlement",
                columns: new[] { "company_id", "payment_id" });

            migrationBuilder.CreateIndex(
                name: "ix_ap_payment_settlement_company_id_supplier_id",
                table: "ap_payment_settlement",
                columns: new[] { "company_id", "supplier_id" });

            migrationBuilder.CreateIndex(
                name: "ux_ap_payment_settlement_company_id_source_type_source_id",
                table: "ap_payment_settlement",
                columns: new[] { "company_id", "source_type", "source_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_ap_payment_settlement_gl_journal_id",
                table: "ap_payment_settlement",
                column: "gl_journal_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ap_payment_source_company_id_gl_journal_id",
                table: "ap_payment_source",
                columns: new[] { "company_id", "gl_journal_id" });

            migrationBuilder.CreateIndex(
                name: "ix_ap_payment_source_company_id_supplier_id_business_date",
                table: "ap_payment_source",
                columns: new[] { "company_id", "supplier_id", "business_date" })
                .Annotation("SqlServer:Include", new[] { "unapplied_amount" });

            migrationBuilder.CreateIndex(
                name: "ux_ap_payment_source_company_id_bank_account_id_bank_reference",
                table: "ap_payment_source",
                columns: new[] { "company_id", "bank_account_id", "bank_reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_ap_payment_source_company_id_source_type_source_id",
                table: "ap_payment_source",
                columns: new[] { "company_id", "source_type", "source_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_ap_payment_source_gl_journal_id",
                table: "ap_payment_source",
                column: "gl_journal_id",
                unique: true);

            migrationBuilder.Sql("""
                CREATE TRIGGER dbo.tr_ap_payment_source_origin ON dbo.ap_payment_source
                AFTER INSERT, UPDATE, DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM deleted) AND NOT EXISTS (SELECT 1 FROM inserted)
                        THROW 51032, 'Posted FI payments cannot be deleted.', 1;
                    IF NOT EXISTS (SELECT 1 FROM deleted)
                       AND EXISTS (SELECT 1 FROM inserted WHERE unapplied_amount <> amount)
                        THROW 51032, 'A posted FI payment starts fully unapplied.', 1;
                    IF EXISTS (
                        SELECT 1 FROM deleted d JOIN inserted i ON i.id = d.id
                        WHERE i.company_id <> d.company_id OR i.supplier_id <> d.supplier_id
                           OR i.source_type <> d.source_type OR i.source_id <> d.source_id
                           OR i.source_version <> d.source_version
                           OR i.fingerprint COLLATE Latin1_General_BIN <> d.fingerprint COLLATE Latin1_General_BIN
                           OR i.gl_journal_id <> d.gl_journal_id OR i.business_date <> d.business_date
                           OR i.bank_account_id <> d.bank_account_id
                           OR i.bank_reference COLLATE Latin1_General_BIN <> d.bank_reference COLLATE Latin1_General_BIN
                           OR i.base_currency COLLATE Latin1_General_BIN <> d.base_currency COLLATE Latin1_General_BIN
                           OR i.amount <> d.amount OR i.unapplied_amount > d.unapplied_amount)
                        THROW 51032, 'Posted FI payment origin is immutable; unapplied may only decrease.', 1;
                END
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER dbo.tr_ap_payment_settlement_immutable ON dbo.ap_payment_settlement
                AFTER INSERT, UPDATE, DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM deleted)
                        THROW 51032, 'Posted FI payment settlements are immutable.', 1;
                    IF EXISTS (
                        SELECT 1 FROM inserted i
                        LEFT JOIN dbo.ap_payment_source p ON p.company_id = i.company_id AND p.id = i.payment_id
                        WHERE p.id IS NULL OR p.supplier_id <> i.supplier_id
                           OR p.base_currency COLLATE Latin1_General_BIN <> i.base_currency COLLATE Latin1_General_BIN)
                        THROW 51032, 'A FI payment settlement must match its payment supplier and currency.', 1;
                END
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER dbo.tr_ap_payment_allocation_immutable ON dbo.ap_payment_allocation
                AFTER INSERT, UPDATE, DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM deleted)
                        THROW 51032, 'Posted FI payment allocations are immutable.', 1;
                    IF EXISTS (
                        SELECT 1 FROM inserted i
                        LEFT JOIN dbo.ap_payment_settlement s ON s.company_id = i.company_id AND s.id = i.settlement_id
                        LEFT JOIN dbo.ap_posted_payable p ON p.company_id = i.company_id AND p.id = i.payable_id
                        WHERE s.id IS NULL OR p.id IS NULL OR s.payment_id <> i.payment_id
                           OR p.supplier_id <> s.supplier_id
                           OR p.base_currency COLLATE Latin1_General_BIN <> s.base_currency COLLATE Latin1_General_BIN)
                        THROW 51032, 'A FI payment allocation must match its settlement and payable.', 1;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER dbo.tr_ap_payment_allocation_immutable;");
            migrationBuilder.Sql("DROP TRIGGER dbo.tr_ap_payment_settlement_immutable;");
            migrationBuilder.Sql("DROP TRIGGER dbo.tr_ap_payment_source_origin;");
            migrationBuilder.DropTable(
                name: "ap_payment_allocation");

            migrationBuilder.DropTable(
                name: "ap_payment_settlement");

            migrationBuilder.DropTable(
                name: "ap_payment_source");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_ap_posted_payable_company_id_id",
                table: "ap_posted_payable");
        }
    }
}
