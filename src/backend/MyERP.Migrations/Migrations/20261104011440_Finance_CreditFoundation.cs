using System;
using Microsoft.EntityFrameworkCore.Migrations;

#pragma warning disable CA1861 // EF Core generates constant column arrays for migration operations.

#nullable disable

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Finance_CreditFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ar_credit_key",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    customer_id = table.Column<long>(type: "bigint", nullable: false),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ar_credit_key", x => x.id);
                    table.UniqueConstraint("ux_ar_credit_key_company_id_customer_id", x => new { x.company_id, x.customer_id });
                    table.CheckConstraint("ck_ar_credit_key_refs", "[company_id] > 0 AND [customer_id] > 0");
                });

            migrationBuilder.CreateTable(
                name: "ar_credit_limit",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    customer_id = table.Column<long>(type: "bigint", nullable: false),
                    base_currency = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: false),
                    amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
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
                    table.PrimaryKey("pk_ar_credit_limit", x => x.id);
                    table.CheckConstraint("ck_ar_credit_limit_amount", "[amount] >= 0");
                    table.CheckConstraint("ck_ar_credit_limit_currency", "[base_currency] LIKE '[A-Z][A-Z][A-Z]'");
                    table.CheckConstraint("ck_ar_credit_limit_dates", "[effective_to] >= [effective_from]");
                    table.CheckConstraint("ck_ar_credit_limit_status", "[status] IN ('Active','Deactivated')");
                    table.ForeignKey(
                        name: "fk_ar_credit_limit_ar_credit_key_company_id_customer_id",
                        columns: x => new { x.company_id, x.customer_id },
                        principalTable: "ar_credit_key",
                        principalColumns: new[] { "company_id", "customer_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ar_posted_receivable",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    customer_id = table.Column<long>(type: "bigint", nullable: false),
                    source_type = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    source_id = table.Column<long>(type: "bigint", nullable: false),
                    base_currency = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                    original_base_amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    open_base_amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    posting_date = table.Column<DateOnly>(type: "date", nullable: false),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ar_posted_receivable", x => x.id);
                    table.CheckConstraint("ck_ar_posted_receivable_currency", "[base_currency] LIKE '[A-Z][A-Z][A-Z]'");
                    table.CheckConstraint("ck_ar_posted_receivable_open_amount", "[original_base_amount] >= 0 AND [open_base_amount] >= 0 AND [open_base_amount] <= [original_base_amount]");
                    table.CheckConstraint("ck_ar_posted_receivable_source", "[source_id] > 0 AND LEN([source_type]) > 0");
                    table.ForeignKey(
                        name: "fk_ar_posted_receivable_ar_credit_key_company_id_customer_id",
                        columns: x => new { x.company_id, x.customer_id },
                        principalTable: "ar_credit_key",
                        principalColumns: new[] { "company_id", "customer_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ar_credit_limit_company_id_customer_id_effective_from_effective_to",
                table: "ar_credit_limit",
                columns: new[] { "company_id", "customer_id", "effective_from", "effective_to" });

            migrationBuilder.CreateIndex(
                name: "ix_ar_posted_receivable_company_id_customer_id",
                table: "ar_posted_receivable",
                columns: new[] { "company_id", "customer_id" })
                .Annotation("SqlServer:Include", new[] { "base_currency", "open_base_amount" });

            migrationBuilder.CreateIndex(
                name: "ux_ar_posted_receivable_company_id_source_type_source_id",
                table: "ar_posted_receivable",
                columns: new[] { "company_id", "source_type", "source_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ar_credit_limit");

            migrationBuilder.DropTable(
                name: "ar_posted_receivable");

            migrationBuilder.DropTable(
                name: "ar_credit_key");
        }
    }
}
