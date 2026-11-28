using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Purchasing_ReturnFoundation : Migration
    {
        private static readonly string[] CompanyIdColumns = ["company_id", "id"];
        private static readonly string[] ReturnDocumentColumns = ["company_id", "document_no"];
        private static readonly string[] ReturnOwnerColumns = ["company_id", "owner_id", "id"];
        private static readonly string[] ReturnOrderColumns = ["company_id", "purchase_order_id", "status", "id"];
        private static readonly string[] ReturnLineColumns = ["company_id", "purchase_return_id", "line_no"];
        private static readonly string[] ReceiptLineColumns = ["company_id", "receipt_line_id"];
        private static readonly string[] TrackingColumns = ["company_id", "line_id", "seq_no"];
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pur_purchase_return",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    document_no = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    purchase_order_id = table.Column<long>(type: "bigint", nullable: false),
                    business_date = table.Column<DateOnly>(type: "date", nullable: false),
                    owner_id = table.Column<long>(type: "bigint", nullable: true),
                    branch_id = table.Column<long>(type: "bigint", nullable: true),
                    department_id = table.Column<long>(type: "bigint", nullable: true),
                    status = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
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
                    table.PrimaryKey("pk_pur_purchase_return", x => x.id);
                    table.UniqueConstraint("ak_pur_purchase_return_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("ck_pur_return_order", "[purchase_order_id] > 0");
                    table.CheckConstraint("ck_pur_return_status", "[status] IN ('Draft','PendingApproval','Rejected','Posted')");
                    table.ForeignKey(
                        name: "fk_pur_purchase_return_order",
                        columns: x => new { x.company_id, x.purchase_order_id },
                        principalTable: "pur_purchase_order",
                        principalColumns: CompanyIdColumns,
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pur_purchase_return_line",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    purchase_return_id = table.Column<long>(type: "bigint", nullable: false),
                    receipt_line_id = table.Column<long>(type: "bigint", nullable: false),
                    original_movement_id = table.Column<long>(type: "bigint", nullable: false),
                    line_no = table.Column<int>(type: "int", nullable: false),
                    material_id = table.Column<long>(type: "bigint", nullable: false),
                    warehouse_id = table.Column<long>(type: "bigint", nullable: false),
                    location_id = table.Column<long>(type: "bigint", nullable: true),
                    quantity = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false),
                    original_unit_cost = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: true),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pur_purchase_return_line", x => x.id);
                    table.UniqueConstraint("ak_pur_purchase_return_line_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("ck_pur_return_line_quantity", "[purchase_return_id] > 0 AND [receipt_line_id] > 0 AND [original_movement_id] > 0 AND [line_no] > 0 AND [material_id] > 0 AND [warehouse_id] > 0 AND [quantity] > 0 AND ([original_unit_cost] IS NULL OR [original_unit_cost] >= 0)");
                    table.ForeignKey(
                        name: "fk_pur_purchase_return_line_receipt_line",
                        columns: x => new { x.company_id, x.receipt_line_id },
                        principalTable: "pur_purchase_receipt_line",
                        principalColumns: CompanyIdColumns,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pur_purchase_return_line_return",
                        columns: x => new { x.company_id, x.purchase_return_id },
                        principalTable: "pur_purchase_return",
                        principalColumns: CompanyIdColumns,
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pur_purchase_return_line_tracking",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    line_id = table.Column<long>(type: "bigint", nullable: false),
                    seq_no = table.Column<int>(type: "int", nullable: false),
                    lot_no = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    serial_no = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    quantity = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pur_purchase_return_line_tracking", x => x.id);
                    table.CheckConstraint("ck_pur_return_tracking_seq", "[seq_no] > 0");
                    table.CheckConstraint("ck_pur_return_tracking_target", "([lot_no] IS NOT NULL AND [serial_no] IS NULL AND LEN([lot_no]) > 0 AND [quantity] > 0) OR ([lot_no] IS NULL AND [serial_no] IS NOT NULL AND LEN([serial_no]) > 0 AND [quantity] = 1)");
                    table.ForeignKey(
                        name: "fk_pur_purchase_return_tracking_line",
                        columns: x => new { x.company_id, x.line_id },
                        principalTable: "pur_purchase_return_line",
                        principalColumns: CompanyIdColumns,
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_pur_purchase_return_company_id_document_no",
                table: "pur_purchase_return",
                columns: ReturnDocumentColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pur_purchase_return_company_id_owner_id_id",
                table: "pur_purchase_return",
                columns: ReturnOwnerColumns);

            migrationBuilder.CreateIndex(
                name: "ix_pur_purchase_return_company_id_purchase_order_id_status_id",
                table: "pur_purchase_return",
                columns: ReturnOrderColumns);

            migrationBuilder.CreateIndex(
                name: "ix_pur_purchase_return_line_company_id_purchase_return_id_line_no",
                table: "pur_purchase_return_line",
                columns: ReturnLineColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pur_purchase_return_line_company_id_receipt_line_id",
                table: "pur_purchase_return_line",
                columns: ReceiptLineColumns);

            migrationBuilder.CreateIndex(
                name: "ix_pur_purchase_return_line_tracking_company_id_line_id_seq_no",
                table: "pur_purchase_return_line_tracking",
                columns: TrackingColumns,
                unique: true);

            migrationBuilder.Sql("""
                CREATE OR ALTER TRIGGER dbo.trg_pur_purchase_return_posted_immutable
                ON dbo.pur_purchase_return AFTER UPDATE, DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM deleted WHERE status = 'Posted')
                        THROW 51020, 'Posted purchase return is immutable.', 1;
                END
                """);
            migrationBuilder.Sql("""
                CREATE OR ALTER TRIGGER dbo.trg_pur_purchase_return_line_posted_immutable
                ON dbo.pur_purchase_return_line AFTER INSERT, UPDATE, DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM inserted i JOIN dbo.pur_purchase_return h
                        ON h.company_id = i.company_id AND h.id = i.purchase_return_id WHERE h.status = 'Posted')
                       OR EXISTS (SELECT 1 FROM deleted d JOIN dbo.pur_purchase_return h
                        ON h.company_id = d.company_id AND h.id = d.purchase_return_id WHERE h.status = 'Posted')
                        THROW 51021, 'Posted purchase return lines are immutable.', 1;
                END
                """);
            migrationBuilder.Sql("""
                CREATE OR ALTER TRIGGER dbo.trg_pur_purchase_return_tracking_posted_immutable
                ON dbo.pur_purchase_return_line_tracking AFTER INSERT, UPDATE, DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM inserted i JOIN dbo.pur_purchase_return_line l
                        ON l.company_id = i.company_id AND l.id = i.line_id
                        JOIN dbo.pur_purchase_return h ON h.company_id = l.company_id AND h.id = l.purchase_return_id
                        WHERE h.status = 'Posted')
                       OR EXISTS (SELECT 1 FROM deleted d JOIN dbo.pur_purchase_return_line l
                        ON l.company_id = d.company_id AND l.id = d.line_id
                        JOIN dbo.pur_purchase_return h ON h.company_id = l.company_id AND h.id = l.purchase_return_id
                        WHERE h.status = 'Posted')
                        THROW 51022, 'Posted purchase return tracking is immutable.', 1;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pur_purchase_return_line_tracking");

            migrationBuilder.DropTable(
                name: "pur_purchase_return_line");

            migrationBuilder.DropTable(
                name: "pur_purchase_return");
        }
    }
}
