using System;
using Microsoft.EntityFrameworkCore.Migrations;

#pragma warning disable CA1861 // EF Core generates constant column arrays for migration operations.

#nullable disable

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Sales_QuotationDraft : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sal_quotation",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    branch_id = table.Column<long>(type: "bigint", nullable: true),
                    department_id = table.Column<long>(type: "bigint", nullable: true),
                    owner_user_id = table.Column<long>(type: "bigint", nullable: false),
                    document_no = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    customer_id = table.Column<long>(type: "bigint", nullable: false),
                    customer_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    customer_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    business_date = table.Column<DateOnly>(type: "date", nullable: false),
                    expiry_date = table.Column<DateOnly>(type: "date", nullable: false),
                    currency_code = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                    base_currency_code = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                    exchange_rate = table.Column<decimal>(type: "decimal(19,8)", precision: 19, scale: 8, nullable: false),
                    status = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    original_net = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    original_tax = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    original_gross = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    base_net = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    base_tax = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    base_gross = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    remark = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    approval_instance_id = table.Column<long>(type: "bigint", nullable: true),
                    submitted_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    approved_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    voided_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
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
                    table.PrimaryKey("pk_sal_quotation", x => x.id);
                    table.UniqueConstraint("ak_sal_quotation_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("ck_sal_quotation_dates", "[expiry_date] >= [business_date]");
                    table.CheckConstraint("ck_sal_quotation_deleted", "([is_deleted] = 0 AND [deleted_at] IS NULL) OR ([is_deleted] = 1 AND [deleted_at] IS NOT NULL AND [status] = 'Draft')");
                    table.CheckConstraint("ck_sal_quotation_rate", "[exchange_rate] > 0");
                    table.CheckConstraint("ck_sal_quotation_refs", "[customer_id] > 0 AND [owner_user_id] > 0");
                    table.CheckConstraint("ck_sal_quotation_status", "[status] IN ('Draft','Pending','Rejected','Approved','Voided')");
                });

            migrationBuilder.CreateTable(
                name: "sal_quotation_line",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    quotation_id = table.Column<long>(type: "bigint", nullable: false),
                    line_no = table.Column<int>(type: "int", nullable: false),
                    item_id = table.Column<long>(type: "bigint", nullable: false),
                    item_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    item_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
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
                    table.PrimaryKey("pk_sal_quotation_line", x => x.id);
                    table.CheckConstraint("ck_sal_quotation_line_no", "[line_no] > 0");
                    table.CheckConstraint("ck_sal_quotation_line_prices", "[unit_price] >= 0 AND [discount_rate] BETWEEN 0 AND 100 AND [tax_rate_percent] BETWEEN 0 AND 100");
                    table.CheckConstraint("ck_sal_quotation_line_quantity", "[quantity] > 0");
                    table.CheckConstraint("ck_sal_quotation_line_refs", "[item_id] > 0 AND [tax_rate_id] > 0");
                    table.ForeignKey(
                        name: "fk_sal_quotation_line_sal_quotation_company_id_quotation_id",
                        columns: x => new { x.company_id, x.quotation_id },
                        principalTable: "sal_quotation",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sal_quotation_company_id_branch_id_department_id_owner_user_id",
                table: "sal_quotation",
                columns: new[] { "company_id", "branch_id", "department_id", "owner_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sal_quotation_company_id_customer_id_business_date",
                table: "sal_quotation",
                columns: new[] { "company_id", "customer_id", "business_date" });

            migrationBuilder.CreateIndex(
                name: "ix_sal_quotation_company_id_document_no",
                table: "sal_quotation",
                columns: new[] { "company_id", "document_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sal_quotation_company_id_expiry_date_status",
                table: "sal_quotation",
                columns: new[] { "company_id", "expiry_date", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_sal_quotation_company_id_status_business_date_id",
                table: "sal_quotation",
                columns: new[] { "company_id", "status", "business_date", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_sal_quotation_line_company_id_item_id",
                table: "sal_quotation_line",
                columns: new[] { "company_id", "item_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sal_quotation_line_company_id_quotation_id_line_no",
                table: "sal_quotation_line",
                columns: new[] { "company_id", "quotation_id", "line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sal_quotation_line_company_id_tax_rate_id",
                table: "sal_quotation_line",
                columns: new[] { "company_id", "tax_rate_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sal_quotation_line");

            migrationBuilder.DropTable(
                name: "sal_quotation");
        }
    }
}
