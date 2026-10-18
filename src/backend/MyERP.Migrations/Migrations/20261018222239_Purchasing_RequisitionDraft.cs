using System;
using Microsoft.EntityFrameworkCore.Migrations;
#pragma warning disable CA1861 // EF migration generator emits literal index column arrays.

#nullable disable

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Purchasing_RequisitionDraft : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pur_requisition",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    document_no = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    business_date = table.Column<DateOnly>(type: "date", nullable: false),
                    requester_id = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    remark = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    custom_fields = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pur_requisition", x => x.id);
                    table.UniqueConstraint("ak_pur_requisition_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("ck_pur_requisition_custom_fields_json", "[custom_fields] IS NULL OR ISJSON([custom_fields]) = 1");
                    table.CheckConstraint("ck_pur_requisition_deleted", "([is_deleted] = 0 AND [deleted_at] IS NULL) OR ([is_deleted] = 1 AND [deleted_at] IS NOT NULL AND [status] = 'Draft')");
                    table.CheckConstraint("ck_pur_requisition_requester", "[requester_id] > 0");
                    table.CheckConstraint("ck_pur_requisition_status", "[status] IN ('Draft', 'PendingApproval', 'Rejected', 'Approved', 'Closed')");
                });

            migrationBuilder.CreateTable(
                name: "pur_requisition_line",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    requisition_id = table.Column<long>(type: "bigint", nullable: false),
                    line_no = table.Column<int>(type: "int", nullable: false),
                    item_id = table.Column<long>(type: "bigint", nullable: false),
                    required_date = table.Column<DateOnly>(type: "date", nullable: false),
                    quantity = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false),
                    ordered_quantity = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false),
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
                    table.PrimaryKey("pk_pur_requisition_line", x => x.id);
                    table.CheckConstraint("ck_pur_requisition_line_item", "[item_id] > 0");
                    table.CheckConstraint("ck_pur_requisition_line_no", "[line_no] > 0");
                    table.CheckConstraint("ck_pur_requisition_line_quantity", "[quantity] > 0 AND [ordered_quantity] >= 0 AND [ordered_quantity] <= [quantity]");
                    table.ForeignKey(
                        name: "fk_pur_requisition_line_pur_requisition_company_id_requisition_id",
                        columns: x => new { x.company_id, x.requisition_id },
                        principalTable: "pur_requisition",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_pur_requisition_company_id_business_date_id",
                table: "pur_requisition",
                columns: new[] { "company_id", "business_date", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_pur_requisition_company_id_document_no",
                table: "pur_requisition",
                columns: new[] { "company_id", "document_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pur_requisition_company_id_requester_id_id",
                table: "pur_requisition",
                columns: new[] { "company_id", "requester_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_pur_requisition_company_id_status_id",
                table: "pur_requisition",
                columns: new[] { "company_id", "status", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_pur_requisition_line_company_id_item_id",
                table: "pur_requisition_line",
                columns: new[] { "company_id", "item_id" });

            migrationBuilder.CreateIndex(
                name: "ix_pur_requisition_line_company_id_requisition_id_line_no",
                table: "pur_requisition_line",
                columns: new[] { "company_id", "requisition_id", "line_no" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pur_requisition_line");

            migrationBuilder.DropTable(
                name: "pur_requisition");
        }
    }
}
