using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Sales_CreditOverride : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sal_credit_override",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    order_id = table.Column<long>(type: "bigint", nullable: false),
                    order_version = table.Column<int>(type: "int", nullable: false),
                    approval_instance_id = table.Column<long>(type: "bigint", nullable: false),
                    stage = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    evaluation_date = table.Column<DateOnly>(type: "date", nullable: false),
                    credit_limit_version = table.Column<int>(type: "int", nullable: false),
                    base_currency_code = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                    credit_limit = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    posted_open_ar = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    other_commitments = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    candidate = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    excess = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    reason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sal_credit_override", x => x.id);
                    table.CheckConstraint("ck_sal_credit_override_amounts", "[credit_limit] >= 0 AND [posted_open_ar] >= 0 AND [other_commitments] >= 0 AND [candidate] >= 0 AND [excess] > 0 AND [excess] = [posted_open_ar] + [other_commitments] + [candidate] - [credit_limit]");
                    table.CheckConstraint("ck_sal_credit_override_reason", "LEN([reason]) > 0");
                    table.CheckConstraint("ck_sal_credit_override_refs", "[order_version] > 0 AND [approval_instance_id] > 0 AND [credit_limit_version] > 0");
                    table.CheckConstraint("ck_sal_credit_override_stage", "[stage] IN ('Submit','FinalApproval')");
                    table.ForeignKey(
                        name: "fk_sal_credit_override_sal_order",
                        columns: x => new { x.company_id, x.order_id },
                        principalTable: "sal_order",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sal_credit_override_company_id_order_id_approval_instance_id_stage",
                table: "sal_credit_override",
                columns: new[] { "company_id", "order_id", "approval_instance_id", "stage" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sal_credit_override_company_id_order_id_created_at_id",
                table: "sal_credit_override",
                columns: new[] { "company_id", "order_id", "created_at", "id" });

            // WU-SD-006b1 (BR-SD-036): override evidence is append-only.
            migrationBuilder.Sql("""
                CREATE TRIGGER dbo.tr_sal_credit_override_immutable ON dbo.sal_credit_override
                AFTER UPDATE, DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM deleted)
                        THROW 51031, 'Sales credit override records are append-only.', 1;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER dbo.tr_sal_credit_override_immutable;");

            migrationBuilder.DropTable(
                name: "sal_credit_override");
        }
    }
}
