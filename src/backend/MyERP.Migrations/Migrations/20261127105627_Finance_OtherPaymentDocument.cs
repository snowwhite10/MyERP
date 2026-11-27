using System;
using Microsoft.EntityFrameworkCore.Migrations;
#pragma warning disable CA1861 // EF Core generates constant column arrays for migration operations.

#nullable disable

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Finance_OtherPaymentDocument : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ap_other_payment_doc",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    document_no = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    payee_description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    expense_category_id = table.Column<long>(type: "bigint", nullable: false),
                    bank_account_id = table.Column<long>(type: "bigint", nullable: false),
                    bank_reference = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    business_date = table.Column<DateOnly>(type: "date", nullable: false),
                    currency = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                    amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
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
                    table.PrimaryKey("pk_ap_other_payment_doc", x => x.id);
                    table.UniqueConstraint("ak_ap_other_payment_doc_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("ck_ap_other_payment_doc_amount", "[amount] > 0");
                    table.CheckConstraint("ck_ap_other_payment_doc_currency", "[currency] COLLATE Latin1_General_BIN LIKE '[A-Z][A-Z][A-Z]'");
                    table.CheckConstraint("ck_ap_other_payment_doc_custom_fields_json", "[custom_fields] IS NULL OR ISJSON([custom_fields]) = 1");
                    table.CheckConstraint("ck_ap_other_payment_doc_deleted", "([is_deleted] = 0 AND [deleted_at] IS NULL) OR ([is_deleted] = 1 AND [deleted_at] IS NOT NULL AND [status] IN ('Draft','Rejected'))");
                    table.CheckConstraint("ck_ap_other_payment_doc_refs", "[expense_category_id] > 0 AND [bank_account_id] > 0 AND LEN(TRIM([document_no])) > 0 AND LEN(TRIM([payee_description])) > 0 AND LEN(TRIM([bank_reference])) > 0");
                    table.CheckConstraint("ck_ap_other_payment_doc_status", "[status] IN ('Draft','Pending','Rejected','Approved','Posted')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_ap_other_payment_doc_company_id_expense_category_id_business_date",
                table: "ap_other_payment_doc",
                columns: new[] { "company_id", "expense_category_id", "business_date" });

            migrationBuilder.CreateIndex(
                name: "ix_ap_other_payment_doc_company_id_status_business_date_id",
                table: "ap_other_payment_doc",
                columns: new[] { "company_id", "status", "business_date", "id" });

            migrationBuilder.CreateIndex(
                name: "ux_ap_other_payment_doc_company_id_bank_account_id_bank_reference",
                table: "ap_other_payment_doc",
                columns: new[] { "company_id", "bank_account_id", "bank_reference" },
                unique: true,
                filter: "[is_deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "ux_ap_other_payment_doc_company_id_document_no",
                table: "ap_other_payment_doc",
                columns: new[] { "company_id", "document_no" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ap_other_payment_doc");
        }
    }
}
