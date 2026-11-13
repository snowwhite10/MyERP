using System;
using Microsoft.EntityFrameworkCore.Migrations;

#pragma warning disable CA1861 // EF Core generates constant column arrays for migration operations.

#nullable disable

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Finance_SalesSourceFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "ak_ar_posted_receivable_company_id_id",
                table: "ar_posted_receivable",
                columns: new[] { "company_id", "id" });

            migrationBuilder.CreateTable(
                name: "ar_sales_source",
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
                    table.PrimaryKey("pk_ar_sales_source", x => x.id);
                    table.UniqueConstraint("ak_ar_sales_source_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("ck_ar_sales_source_amounts", "[net_amount] >= 0 AND [tax_amount] >= 0 AND [gross_amount] > 0 AND [net_amount] + [tax_amount] = [gross_amount] AND [base_net_amount] >= 0 AND [base_tax_amount] >= 0 AND [base_gross_amount] > 0 AND [base_net_amount] + [base_tax_amount] = [base_gross_amount]");
                    table.CheckConstraint("ck_ar_sales_source_currency", "[currency] COLLATE Latin1_General_BIN LIKE '[A-Z][A-Z][A-Z]' AND [base_currency] COLLATE Latin1_General_BIN LIKE '[A-Z][A-Z][A-Z]' AND [exchange_rate] > 0");
                    table.CheckConstraint("ck_ar_sales_source_identity", "[company_id] > 0 AND [customer_id] > 0 AND [source_id] > 0 AND [source_version] > 0 AND [gl_journal_id] > 0 AND LEN(TRIM([fingerprint])) > 0");
                    table.CheckConstraint("ck_ar_sales_source_type", "[source_type] IN ('Invoice','Return')");
                    table.ForeignKey(
                        name: "fk_ar_sales_source_ar_credit_key_company_id_customer_id",
                        columns: x => new { x.company_id, x.customer_id },
                        principalTable: "ar_credit_key",
                        principalColumns: new[] { "company_id", "customer_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ar_sales_source_gl_journal_company_id_gl_journal_id",
                        columns: x => new { x.company_id, x.gl_journal_id },
                        principalTable: "gl_journal",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ar_credit_allocation",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    credit_source_id = table.Column<long>(type: "bigint", nullable: false),
                    receivable_id = table.Column<long>(type: "bigint", nullable: false),
                    invoice_line_id = table.Column<long>(type: "bigint", nullable: false),
                    quantity = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false),
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
                    table.PrimaryKey("pk_ar_credit_allocation", x => x.id);
                    table.CheckConstraint("ck_ar_credit_allocation_amounts", "[net_amount] >= 0 AND [tax_amount] >= 0 AND [gross_amount] > 0 AND [net_amount] + [tax_amount] = [gross_amount] AND [base_net_amount] >= 0 AND [base_tax_amount] >= 0 AND [base_gross_amount] > 0 AND [base_net_amount] + [base_tax_amount] = [base_gross_amount]");
                    table.CheckConstraint("ck_ar_credit_allocation_quantity", "[quantity] > 0");
                    table.CheckConstraint("ck_ar_credit_allocation_refs", "[company_id] > 0 AND [credit_source_id] > 0 AND [receivable_id] > 0 AND [invoice_line_id] > 0");
                    table.ForeignKey(
                        name: "fk_ar_credit_allocation_ar_posted_receivable_company_id_receivable_id",
                        columns: x => new { x.company_id, x.receivable_id },
                        principalTable: "ar_posted_receivable",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ar_credit_allocation_ar_sales_source_company_id_credit_source_id",
                        columns: x => new { x.company_id, x.credit_source_id },
                        principalTable: "ar_sales_source",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ar_credit_allocation_company_id_credit_source_id",
                table: "ar_credit_allocation",
                columns: new[] { "company_id", "credit_source_id" });

            migrationBuilder.CreateIndex(
                name: "ix_ar_credit_allocation_company_id_invoice_line_id",
                table: "ar_credit_allocation",
                columns: new[] { "company_id", "invoice_line_id" })
                .Annotation("SqlServer:Include", new[] { "quantity", "net_amount", "tax_amount", "base_net_amount", "base_tax_amount" });

            migrationBuilder.CreateIndex(
                name: "ix_ar_credit_allocation_company_id_receivable_id",
                table: "ar_credit_allocation",
                columns: new[] { "company_id", "receivable_id" })
                .Annotation("SqlServer:Include", new[] { "base_gross_amount" });

            migrationBuilder.CreateIndex(
                name: "ux_ar_credit_allocation_credit_source_id_invoice_line_id",
                table: "ar_credit_allocation",
                columns: new[] { "credit_source_id", "invoice_line_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ar_sales_source_company_id_customer_id_business_date",
                table: "ar_sales_source",
                columns: new[] { "company_id", "customer_id", "business_date" });

            migrationBuilder.CreateIndex(
                name: "ix_ar_sales_source_company_id_gl_journal_id",
                table: "ar_sales_source",
                columns: new[] { "company_id", "gl_journal_id" });

            migrationBuilder.CreateIndex(
                name: "ux_ar_sales_source_company_id_source_type_source_id",
                table: "ar_sales_source",
                columns: new[] { "company_id", "source_type", "source_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_ar_sales_source_gl_journal_id",
                table: "ar_sales_source",
                column: "gl_journal_id",
                unique: true);

            // BR-FI-014：已過帳銷售來源與信用分配不可修改或刪除；更正須以新的補償來源。
            migrationBuilder.Sql("""
                CREATE TRIGGER dbo.tr_ar_sales_source_immutable ON dbo.ar_sales_source
                AFTER UPDATE, DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM deleted)
                        THROW 51024, 'Posted FI sales sources are immutable.', 1;
                END
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER dbo.tr_ar_credit_allocation_immutable ON dbo.ar_credit_allocation
                AFTER UPDATE, DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM deleted)
                        THROW 51024, 'Posted FI credit allocations are immutable.', 1;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER dbo.tr_ar_credit_allocation_immutable;");
            migrationBuilder.Sql("DROP TRIGGER dbo.tr_ar_sales_source_immutable;");

            migrationBuilder.DropTable(
                name: "ar_credit_allocation");

            migrationBuilder.DropTable(
                name: "ar_sales_source");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_ar_posted_receivable_company_id_id",
                table: "ar_posted_receivable");
        }
    }
}
