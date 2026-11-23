using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Sales_Invoice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "ak_sal_order_line_company_id_id",
                table: "sal_order_line",
                columns: new[] { "company_id", "id" });

            migrationBuilder.CreateTable(
                name: "sal_invoice",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    document_no = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    request_key = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    request_fingerprint = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                    customer_id = table.Column<long>(type: "bigint", nullable: false),
                    order_id = table.Column<long>(type: "bigint", nullable: false),
                    business_date = table.Column<DateOnly>(type: "date", nullable: false),
                    currency_code = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                    base_currency_code = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                    exchange_rate = table.Column<decimal>(type: "decimal(19,8)", precision: 19, scale: 8, nullable: false),
                    status = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    posted_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    fi_sales_source_id = table.Column<long>(type: "bigint", nullable: true),
                    fi_receivable_id = table.Column<long>(type: "bigint", nullable: true),
                    fi_journal_id = table.Column<long>(type: "bigint", nullable: true),
                    original_net = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    original_tax = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    original_gross = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    base_net = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    base_tax = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    base_gross = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sal_invoice", x => x.id);
                    table.UniqueConstraint("ak_sal_invoice_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("ck_sal_invoice_amounts", "[original_net] >= 0 AND [original_tax] >= 0 AND [original_gross] = [original_net] + [original_tax] AND [base_net] >= 0 AND [base_tax] >= 0 AND [base_gross] = [base_net] + [base_tax]");
                    table.CheckConstraint("ck_sal_invoice_posted", "([status] = 'Posted' AND [posted_at] IS NOT NULL AND [fi_sales_source_id] > 0 AND [fi_receivable_id] > 0 AND [fi_journal_id] > 0) OR ([status] = 'Issuing' AND [posted_at] IS NULL AND [fi_sales_source_id] IS NULL AND [fi_receivable_id] IS NULL AND [fi_journal_id] IS NULL)");
                    table.CheckConstraint("ck_sal_invoice_rate", "[exchange_rate] > 0 AND ([currency_code] <> [base_currency_code] OR ([exchange_rate] = 1 AND [base_net] = [original_net] AND [base_tax] = [original_tax] AND [base_gross] = [original_gross]))");
                    table.CheckConstraint("ck_sal_invoice_refs", "[customer_id] > 0 AND [order_id] > 0");
                    table.CheckConstraint("ck_sal_invoice_request", "LEN([request_key]) > 0 AND LEN([request_fingerprint]) > 0");
                    table.CheckConstraint("ck_sal_invoice_status", "[status] IN ('Issuing','Posted')");
                    table.ForeignKey(
                        name: "fk_sal_invoice_sal_order",
                        columns: x => new { x.company_id, x.order_id },
                        principalTable: "sal_order",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sal_invoice_line",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    invoice_id = table.Column<long>(type: "bigint", nullable: false),
                    line_no = table.Column<int>(type: "int", nullable: false),
                    delivery_id = table.Column<long>(type: "bigint", nullable: false),
                    delivery_line_id = table.Column<long>(type: "bigint", nullable: false),
                    order_line_id = table.Column<long>(type: "bigint", nullable: false),
                    item_id = table.Column<long>(type: "bigint", nullable: false),
                    tax_rate_id = table.Column<long>(type: "bigint", nullable: false),
                    tax_rate_percent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    quantity = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false),
                    original_net = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    original_tax = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    original_gross = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    base_net = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    base_tax = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    base_gross = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sal_invoice_line", x => x.id);
                    table.CheckConstraint("ck_sal_invoice_line_amounts", "[original_net] >= 0 AND [original_tax] >= 0 AND [original_gross] = [original_net] + [original_tax] AND [base_net] >= 0 AND [base_tax] >= 0 AND [base_gross] = [base_net] + [base_tax]");
                    table.CheckConstraint("ck_sal_invoice_line_no", "[line_no] > 0");
                    table.CheckConstraint("ck_sal_invoice_line_quantity", "[quantity] > 0");
                    table.CheckConstraint("ck_sal_invoice_line_refs", "[delivery_id] > 0 AND [delivery_line_id] > 0 AND [order_line_id] > 0 AND [item_id] > 0 AND [tax_rate_id] > 0");
                    table.CheckConstraint("ck_sal_invoice_line_tax_rate", "[tax_rate_percent] BETWEEN 0 AND 100");
                    table.ForeignKey(
                        name: "fk_sal_invoice_line_sal_delivery",
                        columns: x => new { x.company_id, x.delivery_id },
                        principalTable: "sal_delivery",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sal_invoice_line_sal_delivery_line",
                        columns: x => new { x.company_id, x.delivery_line_id },
                        principalTable: "sal_delivery_line",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sal_invoice_line_sal_invoice",
                        columns: x => new { x.company_id, x.invoice_id },
                        principalTable: "sal_invoice",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sal_invoice_line_sal_order_line",
                        columns: x => new { x.company_id, x.order_line_id },
                        principalTable: "sal_order_line",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sal_invoice_company_id_customer_id_business_date_id",
                table: "sal_invoice",
                columns: new[] { "company_id", "customer_id", "business_date", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_sal_invoice_company_id_document_no",
                table: "sal_invoice",
                columns: new[] { "company_id", "document_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sal_invoice_company_id_fi_sales_source_id",
                table: "sal_invoice",
                columns: new[] { "company_id", "fi_sales_source_id" },
                unique: true,
                filter: "[fi_sales_source_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_sal_invoice_company_id_order_id_id",
                table: "sal_invoice",
                columns: new[] { "company_id", "order_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_sal_invoice_company_id_request_key",
                table: "sal_invoice",
                columns: new[] { "company_id", "request_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sal_invoice_company_id_status_business_date_id",
                table: "sal_invoice",
                columns: new[] { "company_id", "status", "business_date", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_sal_invoice_line_company_id_delivery_id",
                table: "sal_invoice_line",
                columns: new[] { "company_id", "delivery_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sal_invoice_line_company_id_delivery_line_id_invoice_id",
                table: "sal_invoice_line",
                columns: new[] { "company_id", "delivery_line_id", "invoice_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sal_invoice_line_company_id_invoice_id_line_no",
                table: "sal_invoice_line",
                columns: new[] { "company_id", "invoice_id", "line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sal_invoice_line_company_id_order_line_id_invoice_id",
                table: "sal_invoice_line",
                columns: new[] { "company_id", "order_line_id", "invoice_id" });

            // WU-SD-007a1 (ADR-176, BR-SD-040/042): a Posted invoice is immutable; while Issuing (inside the ISSUE
            // transaction) only the status/posting fields may change and the saved content stays as written.
            migrationBuilder.Sql("""
                CREATE TRIGGER dbo.tr_sal_invoice_immutable ON dbo.sal_invoice
                AFTER UPDATE, DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM deleted WHERE [status] = 'Posted')
                        THROW 51032, 'Posted sales invoices are immutable.', 1;
                    IF EXISTS (
                        SELECT 1 FROM inserted i JOIN deleted d ON d.id = i.id
                        WHERE i.company_id <> d.company_id OR i.document_no <> d.document_no
                           OR i.request_key <> d.request_key OR i.request_fingerprint <> d.request_fingerprint
                           OR i.customer_id <> d.customer_id OR i.order_id <> d.order_id
                           OR i.business_date <> d.business_date OR i.currency_code <> d.currency_code
                           OR i.base_currency_code <> d.base_currency_code OR i.exchange_rate <> d.exchange_rate
                           OR i.original_net <> d.original_net OR i.original_tax <> d.original_tax
                           OR i.original_gross <> d.original_gross OR i.base_net <> d.base_net
                           OR i.base_tax <> d.base_tax OR i.base_gross <> d.base_gross
                           OR i.created_at <> d.created_at OR i.created_by <> d.created_by)
                        THROW 51032, 'Saved sales invoice content cannot be changed.', 1;
                END
                """);

            // Lines are write-once: never updated, and neither added to nor removed from a Posted invoice. Deleting
            // the lines of an Issuing invoice remains possible for an in-transaction cleanup.
            migrationBuilder.Sql("""
                CREATE TRIGGER dbo.tr_sal_invoice_line_immutable ON dbo.sal_invoice_line
                AFTER INSERT, UPDATE, DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
                        THROW 51033, 'Sales invoice lines cannot be changed.', 1;
                    IF EXISTS (
                        SELECT 1
                        FROM (SELECT company_id, invoice_id FROM inserted
                              UNION ALL SELECT company_id, invoice_id FROM deleted) x
                        JOIN dbo.sal_invoice h ON h.id = x.invoice_id AND h.company_id = x.company_id
                        WHERE h.[status] = 'Posted')
                        THROW 51033, 'Lines of a Posted sales invoice cannot be added or removed.', 1;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER dbo.tr_sal_invoice_line_immutable;");
            migrationBuilder.Sql("DROP TRIGGER dbo.tr_sal_invoice_immutable;");

            migrationBuilder.DropTable(
                name: "sal_invoice_line");

            migrationBuilder.DropTable(
                name: "sal_invoice");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_sal_order_line_company_id_id",
                table: "sal_order_line");
        }
    }
}
