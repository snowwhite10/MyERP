using System;
using Microsoft.EntityFrameworkCore.Migrations;
#pragma warning disable CA1861 // EF Core generates constant column arrays for migration operations.

#nullable disable

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Finance_CollectionSettlementDocument : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ar_collection_settlement_doc",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    document_no = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    customer_id = table.Column<long>(type: "bigint", nullable: false),
                    business_date = table.Column<DateOnly>(type: "date", nullable: false),
                    currency = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                    receipt_id = table.Column<long>(type: "bigint", nullable: false),
                    total_amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    remark = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("pk_ar_collection_settlement_doc", x => x.id);
                    table.UniqueConstraint("ak_ar_collection_settlement_doc_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("ck_ar_collection_settlement_doc_currency", "[currency] COLLATE Latin1_General_BIN LIKE '[A-Z][A-Z][A-Z]'");
                    table.CheckConstraint("ck_ar_collection_settlement_doc_custom_fields_json", "[custom_fields] IS NULL OR ISJSON([custom_fields]) = 1");
                    table.CheckConstraint("ck_ar_collection_settlement_doc_deleted", "([is_deleted] = 0 AND [deleted_at] IS NULL) OR ([is_deleted] = 1 AND [deleted_at] IS NOT NULL AND [status] IN ('Draft','Rejected'))");
                    table.CheckConstraint("ck_ar_collection_settlement_doc_refs", "[customer_id] > 0 AND [receipt_id] > 0 AND LEN(TRIM([document_no])) > 0");
                    table.CheckConstraint("ck_ar_collection_settlement_doc_status", "[status] IN ('Draft','Pending','Rejected','Approved','Posted')");
                    table.CheckConstraint("ck_ar_collection_settlement_doc_total", "[total_amount] > 0");
                });

            migrationBuilder.CreateTable(
                name: "ar_collection_settlement_doc_line",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    settlement_doc_id = table.Column<long>(type: "bigint", nullable: false),
                    line_no = table.Column<int>(type: "int", nullable: false),
                    receivable_id = table.Column<long>(type: "bigint", nullable: false),
                    amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ar_collection_settlement_doc_line", x => x.id);
                    table.CheckConstraint("ck_ar_collection_settlement_doc_line_amount", "[amount] > 0");
                    table.CheckConstraint("ck_ar_collection_settlement_doc_line_line_no", "[line_no] BETWEEN 1 AND 1000");
                    table.ForeignKey(
                        name: "fk_ar_collection_settlement_doc_line_ar_collection_settlement_doc_company_id_settlement_doc_id",
                        columns: x => new { x.company_id, x.settlement_doc_id },
                        principalTable: "ar_collection_settlement_doc",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ar_collection_settlement_doc_line_ar_posted_receivable_company_id_receivable_id",
                        columns: x => new { x.company_id, x.receivable_id },
                        principalTable: "ar_posted_receivable",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ar_collection_settlement_doc_company_id_customer_id_business_date",
                table: "ar_collection_settlement_doc",
                columns: new[] { "company_id", "customer_id", "business_date" });

            migrationBuilder.CreateIndex(
                name: "ix_ar_collection_settlement_doc_company_id_receipt_id",
                table: "ar_collection_settlement_doc",
                columns: new[] { "company_id", "receipt_id" });

            migrationBuilder.CreateIndex(
                name: "ix_ar_collection_settlement_doc_company_id_status_business_date_id",
                table: "ar_collection_settlement_doc",
                columns: new[] { "company_id", "status", "business_date", "id" });

            migrationBuilder.CreateIndex(
                name: "ux_ar_collection_settlement_doc_company_id_document_no",
                table: "ar_collection_settlement_doc",
                columns: new[] { "company_id", "document_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ar_collection_settlement_doc_line_company_id_receivable_id",
                table: "ar_collection_settlement_doc_line",
                columns: new[] { "company_id", "receivable_id" });

            migrationBuilder.CreateIndex(
                name: "ux_ar_collection_settlement_doc_line_company_id_settlement_doc_id_line_no",
                table: "ar_collection_settlement_doc_line",
                columns: new[] { "company_id", "settlement_doc_id", "line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_ar_collection_settlement_doc_line_company_id_settlement_doc_id_receivable_id",
                table: "ar_collection_settlement_doc_line",
                columns: new[] { "company_id", "settlement_doc_id", "receivable_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ar_collection_settlement_doc_line");

            migrationBuilder.DropTable(
                name: "ar_collection_settlement_doc");
        }
    }
}
