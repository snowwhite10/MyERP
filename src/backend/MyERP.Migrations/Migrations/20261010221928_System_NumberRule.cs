using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class System_NumberRule : Migration
    {
        private static readonly string[] RulePrincipalColumns = ["company_id", "id"];
        private static readonly string[] RuleTypeColumns = ["company_id", "document_type"];
        private static readonly string[] SequenceColumns = ["company_id", "rule_id", "period_key"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sys_number_rule",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    document_type = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    prefix = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    date_format = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    sequence_length = table.Column<int>(type: "int", nullable: false),
                    reset_period = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sys_number_rule", x => x.id);
                    table.UniqueConstraint("ak_sys_number_rule_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("ck_sys_number_rule_sequence_length", "[sequence_length] BETWEEN 3 AND 10");
                    table.ForeignKey(
                        name: "fk_sys_number_rule_sys_company_company_id",
                        column: x => x.company_id,
                        principalTable: "sys_company",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sys_number_sequence",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    rule_id = table.Column<long>(type: "bigint", nullable: false),
                    period_key = table.Column<string>(type: "varchar(8)", unicode: false, maxLength: 8, nullable: false),
                    current_value = table.Column<long>(type: "bigint", nullable: false),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sys_number_sequence", x => x.id);
                    table.CheckConstraint("ck_sys_number_sequence_current_value", "[current_value] >= 1");
                    table.ForeignKey(
                        name: "fk_sys_number_sequence_sys_number_rule_company_id_rule_id",
                        columns: x => new { x.company_id, x.rule_id },
                        principalTable: "sys_number_rule",
                        principalColumns: RulePrincipalColumns,
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_sys_number_rule_company_id_document_type",
                table: "sys_number_rule",
                columns: RuleTypeColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_sys_number_sequence_company_id_rule_id_period_key",
                table: "sys_number_sequence",
                columns: SequenceColumns,
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sys_number_sequence");

            migrationBuilder.DropTable(
                name: "sys_number_rule");
        }
    }
}
