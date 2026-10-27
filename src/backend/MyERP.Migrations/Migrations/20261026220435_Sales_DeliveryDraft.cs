using System;
using Microsoft.EntityFrameworkCore.Migrations;

#pragma warning disable CA1861 // EF Core generates constant column arrays for migration operations.

#nullable disable

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Sales_DeliveryDraft : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sal_delivery",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    document_no = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    order_id = table.Column<long>(type: "bigint", nullable: false),
                    business_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    remark = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    approval_instance_id = table.Column<long>(type: "bigint", nullable: true),
                    posted_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
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
                    table.PrimaryKey("pk_sal_delivery", x => x.id);
                    table.UniqueConstraint("ak_sal_delivery_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("ck_sal_delivery_deleted", "([is_deleted] = 0 AND [deleted_at] IS NULL) OR ([is_deleted] = 1 AND [deleted_at] IS NOT NULL AND [status] = 'Draft')");
                    table.CheckConstraint("ck_sal_delivery_order", "[order_id] > 0");
                    table.CheckConstraint("ck_sal_delivery_posted", "([status] = 'Posted' AND [posted_at] IS NOT NULL) OR ([status] <> 'Posted' AND [posted_at] IS NULL)");
                    table.CheckConstraint("ck_sal_delivery_status", "[status] IN ('Draft', 'Pending', 'Rejected', 'Posted')");
                });

            migrationBuilder.CreateTable(
                name: "sal_delivery_line",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    delivery_id = table.Column<long>(type: "bigint", nullable: false),
                    line_no = table.Column<int>(type: "int", nullable: false),
                    order_line_id = table.Column<long>(type: "bigint", nullable: false),
                    item_id = table.Column<long>(type: "bigint", nullable: false),
                    warehouse_id = table.Column<long>(type: "bigint", nullable: false),
                    location_id = table.Column<long>(type: "bigint", nullable: true),
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
                    table.PrimaryKey("pk_sal_delivery_line", x => x.id);
                    table.CheckConstraint("ck_sal_delivery_line_no", "[line_no] > 0");
                    table.CheckConstraint("ck_sal_delivery_line_quantity", "[quantity] > 0");
                    table.CheckConstraint("ck_sal_delivery_line_refs", "[order_line_id] > 0 AND [item_id] > 0 AND [warehouse_id] > 0 AND ([location_id] IS NULL OR [location_id] > 0)");
                    table.ForeignKey(
                        name: "fk_sal_delivery_line_sal_delivery_company_id_delivery_id",
                        columns: x => new { x.company_id, x.delivery_id },
                        principalTable: "sal_delivery",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sal_delivery_company_id_document_no",
                table: "sal_delivery",
                columns: new[] { "company_id", "document_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sal_delivery_company_id_order_id_status",
                table: "sal_delivery",
                columns: new[] { "company_id", "order_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_sal_delivery_company_id_status_business_date_id",
                table: "sal_delivery",
                columns: new[] { "company_id", "status", "business_date", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_sal_delivery_line_company_id_delivery_id_line_no",
                table: "sal_delivery_line",
                columns: new[] { "company_id", "delivery_id", "line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sal_delivery_line_company_id_item_id",
                table: "sal_delivery_line",
                columns: new[] { "company_id", "item_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sal_delivery_line_company_id_order_line_id_delivery_id",
                table: "sal_delivery_line",
                columns: new[] { "company_id", "order_line_id", "delivery_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sal_delivery_line_company_id_warehouse_id_location_id",
                table: "sal_delivery_line",
                columns: new[] { "company_id", "warehouse_id", "location_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sal_delivery_line");

            migrationBuilder.DropTable(
                name: "sal_delivery");
        }
    }
}
