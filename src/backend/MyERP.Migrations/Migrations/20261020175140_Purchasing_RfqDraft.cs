using System;
using Microsoft.EntityFrameworkCore.Migrations;
#pragma warning disable CA1861 // EF migration generator emits literal index column arrays.

#nullable disable

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Purchasing_RfqDraft : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "ak_pur_requisition_line_company_id_id",
                table: "pur_requisition_line",
                columns: new[] { "company_id", "id" });

            migrationBuilder.CreateTable(
                name: "pur_rfq",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    document_no = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    business_date = table.Column<DateOnly>(type: "date", nullable: false),
                    reply_deadline = table.Column<DateOnly>(type: "date", nullable: false),
                    owner_id = table.Column<long>(type: "bigint", nullable: false),
                    branch_id = table.Column<long>(type: "bigint", nullable: true),
                    department_id = table.Column<long>(type: "bigint", nullable: true),
                    status = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    remark = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    custom_fields = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    sent_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pur_rfq", x => x.id);
                    table.UniqueConstraint("ak_pur_rfq_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("ck_pur_rfq_custom_fields_json", "[custom_fields] IS NULL OR ISJSON([custom_fields]) = 1");
                    table.CheckConstraint("ck_pur_rfq_dates", "[reply_deadline] >= [business_date]");
                    table.CheckConstraint("ck_pur_rfq_deleted", "([is_deleted] = 0 AND [deleted_at] IS NULL) OR ([is_deleted] = 1 AND [deleted_at] IS NOT NULL AND [status] = 'Draft')");
                    table.CheckConstraint("ck_pur_rfq_owner", "[owner_id] > 0");
                    table.CheckConstraint("ck_pur_rfq_sent", "([status] = 'Draft' AND [sent_at] IS NULL) OR ([status] IN ('Sent', 'Closed') AND [sent_at] IS NOT NULL)");
                    table.CheckConstraint("ck_pur_rfq_status", "[status] IN ('Draft', 'Sent', 'Closed')");
                });

            migrationBuilder.CreateTable(
                name: "pur_rfq_invitation",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    rfq_id = table.Column<long>(type: "bigint", nullable: false),
                    supplier_id = table.Column<long>(type: "bigint", nullable: false),
                    supplier_code_snapshot = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: true),
                    supplier_name_snapshot = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    sent_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pur_rfq_invitation", x => x.id);
                    table.CheckConstraint("ck_pur_rfq_invitation_snapshot", "([sent_at] IS NULL AND [supplier_code_snapshot] IS NULL AND [supplier_name_snapshot] IS NULL) OR ([sent_at] IS NOT NULL AND [supplier_code_snapshot] IS NOT NULL AND [supplier_name_snapshot] IS NOT NULL)");
                    table.CheckConstraint("ck_pur_rfq_invitation_supplier", "[supplier_id] > 0");
                    table.ForeignKey(
                        name: "fk_pur_rfq_invitation_pur_rfq_company_id_rfq_id",
                        columns: x => new { x.company_id, x.rfq_id },
                        principalTable: "pur_rfq",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pur_rfq_line",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    rfq_id = table.Column<long>(type: "bigint", nullable: false),
                    line_no = table.Column<int>(type: "int", nullable: false),
                    item_id = table.Column<long>(type: "bigint", nullable: false),
                    requisition_line_id = table.Column<long>(type: "bigint", nullable: true),
                    required_date = table.Column<DateOnly>(type: "date", nullable: false),
                    quantity = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false),
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
                    table.PrimaryKey("pk_pur_rfq_line", x => x.id);
                    table.CheckConstraint("ck_pur_rfq_line_item", "[item_id] > 0");
                    table.CheckConstraint("ck_pur_rfq_line_no", "[line_no] > 0");
                    table.CheckConstraint("ck_pur_rfq_line_quantity", "[quantity] > 0");
                    table.ForeignKey(
                        name: "fk_pur_rfq_line_pur_requisition_line_company_id_requisition_line_id",
                        columns: x => new { x.company_id, x.requisition_line_id },
                        principalTable: "pur_requisition_line",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pur_rfq_line_pur_rfq_company_id_rfq_id",
                        columns: x => new { x.company_id, x.rfq_id },
                        principalTable: "pur_rfq",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_pur_rfq_company_id_branch_id_id",
                table: "pur_rfq",
                columns: new[] { "company_id", "branch_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_pur_rfq_company_id_business_date_id",
                table: "pur_rfq",
                columns: new[] { "company_id", "business_date", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_pur_rfq_company_id_department_id_id",
                table: "pur_rfq",
                columns: new[] { "company_id", "department_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_pur_rfq_company_id_document_no",
                table: "pur_rfq",
                columns: new[] { "company_id", "document_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pur_rfq_company_id_owner_id_id",
                table: "pur_rfq",
                columns: new[] { "company_id", "owner_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_pur_rfq_company_id_status_id",
                table: "pur_rfq",
                columns: new[] { "company_id", "status", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_pur_rfq_invitation_company_id_rfq_id_supplier_id",
                table: "pur_rfq_invitation",
                columns: new[] { "company_id", "rfq_id", "supplier_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pur_rfq_invitation_company_id_supplier_id",
                table: "pur_rfq_invitation",
                columns: new[] { "company_id", "supplier_id" });

            migrationBuilder.CreateIndex(
                name: "ix_pur_rfq_line_company_id_item_id",
                table: "pur_rfq_line",
                columns: new[] { "company_id", "item_id" });

            migrationBuilder.CreateIndex(
                name: "ix_pur_rfq_line_company_id_requisition_line_id",
                table: "pur_rfq_line",
                columns: new[] { "company_id", "requisition_line_id" });

            migrationBuilder.CreateIndex(
                name: "ix_pur_rfq_line_company_id_rfq_id_line_no",
                table: "pur_rfq_line",
                columns: new[] { "company_id", "rfq_id", "line_no" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pur_rfq_invitation");

            migrationBuilder.DropTable(
                name: "pur_rfq_line");

            migrationBuilder.DropTable(
                name: "pur_rfq");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_pur_requisition_line_company_id_id",
                table: "pur_requisition_line");
        }
    }
}
