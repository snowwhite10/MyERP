using System;
using Microsoft.EntityFrameworkCore.Migrations;
#pragma warning disable CA1861 // EF migration generator emits literal index column arrays.

#nullable disable

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Purchasing_SupplierQuotation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "ak_pur_rfq_line_company_id_id",
                table: "pur_rfq_line",
                columns: new[] { "company_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_pur_rfq_invitation_company_id_rfq_id_supplier_id",
                table: "pur_rfq_invitation",
                columns: new[] { "company_id", "rfq_id", "supplier_id" });

            migrationBuilder.CreateTable(
                name: "pur_supplier_quotation",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    rfq_id = table.Column<long>(type: "bigint", nullable: false),
                    supplier_id = table.Column<long>(type: "bigint", nullable: false),
                    current_revision_no = table.Column<int>(type: "int", nullable: false),
                    status = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pur_supplier_quotation", x => x.id);
                    table.UniqueConstraint("ak_pur_supplier_quotation_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("ck_pur_supplier_quotation_revision", "[current_revision_no] > 0");
                    table.CheckConstraint("ck_pur_supplier_quotation_status", "[status] IN ('Active', 'Withdrawn', 'Awarded')");
                    table.CheckConstraint("ck_pur_supplier_quotation_supplier", "[supplier_id] > 0");
                    table.ForeignKey(
                        name: "fk_pur_supplier_quotation_pur_rfq_invitation_company_id_rfq_id_supplier_id",
                        columns: x => new { x.company_id, x.rfq_id, x.supplier_id },
                        principalTable: "pur_rfq_invitation",
                        principalColumns: new[] { "company_id", "rfq_id", "supplier_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pur_quotation_revision",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    quotation_id = table.Column<long>(type: "bigint", nullable: false),
                    revision_no = table.Column<int>(type: "int", nullable: false),
                    quote_date = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_until = table.Column<DateOnly>(type: "date", nullable: false),
                    remark = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pur_quotation_revision", x => x.id);
                    table.UniqueConstraint("ak_pur_quotation_revision_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("ck_pur_quotation_revision_dates", "[valid_until] >= [quote_date]");
                    table.CheckConstraint("ck_pur_quotation_revision_no", "[revision_no] > 0");
                    table.ForeignKey(
                        name: "fk_pur_quotation_revision_pur_supplier_quotation_company_id_quotation_id",
                        columns: x => new { x.company_id, x.quotation_id },
                        principalTable: "pur_supplier_quotation",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pur_quotation_line",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    revision_id = table.Column<long>(type: "bigint", nullable: false),
                    rfq_line_id = table.Column<long>(type: "bigint", nullable: false),
                    unit_price = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false),
                    currency_code = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                    tax_rate_id = table.Column<long>(type: "bigint", nullable: false),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pur_quotation_line", x => x.id);
                    table.CheckConstraint("ck_pur_quotation_line_currency", "LEN([currency_code]) = 3");
                    table.CheckConstraint("ck_pur_quotation_line_price", "[unit_price] >= 0");
                    table.CheckConstraint("ck_pur_quotation_line_tax", "[tax_rate_id] > 0");
                    table.ForeignKey(
                        name: "fk_pur_quotation_line_pur_quotation_revision_company_id_revision_id",
                        columns: x => new { x.company_id, x.revision_id },
                        principalTable: "pur_quotation_revision",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pur_quotation_line_pur_rfq_line_company_id_rfq_line_id",
                        columns: x => new { x.company_id, x.rfq_line_id },
                        principalTable: "pur_rfq_line",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_pur_quotation_line_company_id_revision_id_rfq_line_id",
                table: "pur_quotation_line",
                columns: new[] { "company_id", "revision_id", "rfq_line_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pur_quotation_line_company_id_rfq_line_id",
                table: "pur_quotation_line",
                columns: new[] { "company_id", "rfq_line_id" });

            migrationBuilder.CreateIndex(
                name: "ix_pur_quotation_line_company_id_tax_rate_id",
                table: "pur_quotation_line",
                columns: new[] { "company_id", "tax_rate_id" });

            migrationBuilder.CreateIndex(
                name: "ix_pur_quotation_revision_company_id_quotation_id_revision_no",
                table: "pur_quotation_revision",
                columns: new[] { "company_id", "quotation_id", "revision_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pur_supplier_quotation_company_id_rfq_id_supplier_id",
                table: "pur_supplier_quotation",
                columns: new[] { "company_id", "rfq_id", "supplier_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pur_supplier_quotation_company_id_status_id",
                table: "pur_supplier_quotation",
                columns: new[] { "company_id", "status", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_pur_supplier_quotation_company_id_supplier_id_id",
                table: "pur_supplier_quotation",
                columns: new[] { "company_id", "supplier_id", "id" });

            migrationBuilder.Sql("""
                CREATE TRIGGER dbo.tr_pur_quotation_revision_immutable
                ON dbo.pur_quotation_revision AFTER UPDATE, DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51070, 'Quotation revisions are immutable.', 1;
                END
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER dbo.tr_pur_quotation_line_immutable
                ON dbo.pur_quotation_line AFTER UPDATE, DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51071, 'Quotation lines are immutable.', 1;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pur_quotation_line");

            migrationBuilder.DropTable(
                name: "pur_quotation_revision");

            migrationBuilder.DropTable(
                name: "pur_supplier_quotation");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_pur_rfq_line_company_id_id",
                table: "pur_rfq_line");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_pur_rfq_invitation_company_id_rfq_id_supplier_id",
                table: "pur_rfq_invitation");
        }
    }
}
