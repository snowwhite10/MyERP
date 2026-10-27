using System;
using Microsoft.EntityFrameworkCore.Migrations;

#pragma warning disable CA1861 // EF Core generates constant column arrays for migration operations.

#nullable disable

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Sales_OrderDraft : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sal_order",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    document_no = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    customer_id = table.Column<long>(type: "bigint", nullable: false),
                    customer_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    customer_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    business_date = table.Column<DateOnly>(type: "date", nullable: false),
                    currency_code = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                    base_currency_code = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                    exchange_rate = table.Column<decimal>(type: "decimal(19,8)", precision: 19, scale: 8, nullable: false),
                    status = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    source_quotation_id = table.Column<long>(type: "bigint", nullable: true),
                    original_net = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    original_tax = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    original_gross = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    base_net = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    base_tax = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    base_gross = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    remark = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    approval_instance_id = table.Column<long>(type: "bigint", nullable: true),
                    approved_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
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
                    table.PrimaryKey("pk_sal_order", x => x.id);
                    table.UniqueConstraint("ak_sal_order_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("ck_sal_order_deleted", "([is_deleted] = 0 AND [deleted_at] IS NULL) OR ([is_deleted] = 1 AND [deleted_at] IS NOT NULL AND [status] = 'Draft')");
                    table.CheckConstraint("ck_sal_order_rate", "[exchange_rate] > 0");
                    table.CheckConstraint("ck_sal_order_refs", "[customer_id] > 0 AND ([source_quotation_id] IS NULL OR [source_quotation_id] > 0)");
                    table.CheckConstraint("ck_sal_order_status", "[status] IN ('Draft','Pending','Rejected','Approved','Cancelled')");
                });

            migrationBuilder.CreateTable(
                name: "sal_order_line",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    order_id = table.Column<long>(type: "bigint", nullable: false),
                    line_no = table.Column<int>(type: "int", nullable: false),
                    item_id = table.Column<long>(type: "bigint", nullable: false),
                    item_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    item_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    source_quotation_line_id = table.Column<long>(type: "bigint", nullable: true),
                    quantity = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false),
                    unit_price = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false),
                    discount_rate = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    tax_rate_id = table.Column<long>(type: "bigint", nullable: false),
                    tax_rate_percent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
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
                    table.PrimaryKey("pk_sal_order_line", x => x.id);
                    table.CheckConstraint("ck_sal_order_line_no", "[line_no] > 0");
                    table.CheckConstraint("ck_sal_order_line_prices", "[unit_price] >= 0 AND [discount_rate] BETWEEN 0 AND 100 AND [tax_rate_percent] BETWEEN 0 AND 100");
                    table.CheckConstraint("ck_sal_order_line_quantity", "[quantity] > 0");
                    table.CheckConstraint("ck_sal_order_line_refs", "[item_id] > 0 AND [tax_rate_id] > 0 AND ([source_quotation_line_id] IS NULL OR [source_quotation_line_id] > 0)");
                    table.ForeignKey(
                        name: "fk_sal_order_line_sal_order_company_id_order_id",
                        columns: x => new { x.company_id, x.order_id },
                        principalTable: "sal_order",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sal_order_company_id_customer_id_business_date",
                table: "sal_order",
                columns: new[] { "company_id", "customer_id", "business_date" });

            migrationBuilder.CreateIndex(
                name: "ix_sal_order_company_id_document_no",
                table: "sal_order",
                columns: new[] { "company_id", "document_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sal_order_company_id_source_quotation_id",
                table: "sal_order",
                columns: new[] { "company_id", "source_quotation_id" },
                unique: true,
                filter: "[source_quotation_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_sal_order_company_id_status_business_date_id",
                table: "sal_order",
                columns: new[] { "company_id", "status", "business_date", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_sal_order_line_company_id_item_id",
                table: "sal_order_line",
                columns: new[] { "company_id", "item_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sal_order_line_company_id_order_id_line_no",
                table: "sal_order_line",
                columns: new[] { "company_id", "order_id", "line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sal_order_line_company_id_source_quotation_line_id",
                table: "sal_order_line",
                columns: new[] { "company_id", "source_quotation_line_id" },
                unique: true,
                filter: "[source_quotation_line_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_sal_order_line_company_id_tax_rate_id",
                table: "sal_order_line",
                columns: new[] { "company_id", "tax_rate_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sal_order_line");

            migrationBuilder.DropTable(
                name: "sal_order");
        }
    }
}
