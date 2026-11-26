using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861 // EF migration column arrays are emitted once.

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Purchasing_InvoiceResolution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pur_purchase_invoice_resolution",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    invoice_id = table.Column<long>(type: "bigint", nullable: false),
                    invoice_version = table.Column<int>(type: "int", nullable: false),
                    line_no = table.Column<int>(type: "int", nullable: false),
                    action = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    net_difference = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    tax_difference = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    gross_difference = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pur_purchase_invoice_resolution", x => x.id);
                    table.CheckConstraint("ck_pur_invoice_resolution_action", "[action] IN ('Accept', 'Reject')");
                    table.CheckConstraint("ck_pur_invoice_resolution_reason", "LEN([reason]) > 0");
                    table.CheckConstraint("ck_pur_invoice_resolution_refs", "[invoice_id] > 0 AND [invoice_version] > 0 AND [line_no] > 0");
                    table.ForeignKey(
                        name: "fk_pur_purchase_invoice_resolution_invoice",
                        columns: x => new { x.company_id, x.invoice_id },
                        principalTable: "pur_purchase_invoice",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_pur_purchase_invoice_resolution_company_id_invoice_id_invoice_version_line_no",
                table: "pur_purchase_invoice_resolution",
                columns: new[] { "company_id", "invoice_id", "invoice_version", "line_no" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pur_purchase_invoice_resolution");
        }
    }
}
