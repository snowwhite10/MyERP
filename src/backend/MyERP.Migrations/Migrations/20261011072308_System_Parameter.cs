using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861 // EF migration generator emits literal index column arrays.

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class System_Parameter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sys_parameter",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    param_key = table.Column<string>(type: "varchar(80)", unicode: false, maxLength: 80, nullable: false),
                    param_value = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sys_parameter", x => x.id);
                    table.ForeignKey(
                        name: "fk_sys_parameter_sys_company_company_id",
                        column: x => x.company_id,
                        principalTable: "sys_company",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_sys_parameter_company_id_param_key",
                table: "sys_parameter",
                columns: new[] { "company_id", "param_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sys_parameter");
        }
    }
}
