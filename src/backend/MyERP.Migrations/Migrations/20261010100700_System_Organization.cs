using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class System_Organization : Migration
    {
        private static readonly string[] CompanyIdColumns = ["company_id", "id"];
        private static readonly string[] CompanyCodeColumns = ["company_id", "code"];
        private static readonly string[] CompanyBranchColumns = ["company_id", "branch_id"];
        private static readonly string[] CompanyParentColumns = ["company_id", "parent_department_id"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sys_company",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    code = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    custom_fields = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sys_company", x => x.id);
                    table.CheckConstraint("ck_sys_company_custom_fields_json", "[custom_fields] IS NULL OR ISJSON([custom_fields]) = 1");
                });

            migrationBuilder.CreateTable(
                name: "sys_branch",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    code = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    custom_fields = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sys_branch", x => x.id);
                    table.UniqueConstraint("ak_sys_branch_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("ck_sys_branch_custom_fields_json", "[custom_fields] IS NULL OR ISJSON([custom_fields]) = 1");
                    table.ForeignKey(
                        name: "fk_sys_branch_sys_company_company_id",
                        column: x => x.company_id,
                        principalTable: "sys_company",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sys_department",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    code = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    branch_id = table.Column<long>(type: "bigint", nullable: true),
                    parent_department_id = table.Column<long>(type: "bigint", nullable: true),
                    custom_fields = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sys_department", x => x.id);
                    table.UniqueConstraint("ak_sys_department_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("ck_sys_department_custom_fields_json", "[custom_fields] IS NULL OR ISJSON([custom_fields]) = 1");
                    table.ForeignKey(
                        name: "fk_sys_department_sys_branch_company_id_branch_id",
                        columns: x => new { x.company_id, x.branch_id },
                        principalTable: "sys_branch",
                        principalColumns: CompanyIdColumns,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sys_department_sys_company_company_id",
                        column: x => x.company_id,
                        principalTable: "sys_company",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sys_department_sys_department_company_id_parent_department_id",
                        columns: x => new { x.company_id, x.parent_department_id },
                        principalTable: "sys_department",
                        principalColumns: CompanyIdColumns,
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sys_branch_company_id",
                table: "sys_branch",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ux_sys_branch_company_id_code",
                table: "sys_branch",
                columns: CompanyCodeColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_sys_company_code",
                table: "sys_company",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sys_department_branch_id",
                table: "sys_department",
                column: "branch_id");

            migrationBuilder.CreateIndex(
                name: "ix_sys_department_company_id",
                table: "sys_department",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_sys_department_company_id_branch_id",
                table: "sys_department",
                columns: CompanyBranchColumns);

            migrationBuilder.CreateIndex(
                name: "ix_sys_department_company_id_parent_department_id",
                table: "sys_department",
                columns: CompanyParentColumns);

            migrationBuilder.CreateIndex(
                name: "ix_sys_department_parent_department_id",
                table: "sys_department",
                column: "parent_department_id");

            migrationBuilder.CreateIndex(
                name: "ux_sys_department_company_id_code",
                table: "sys_department",
                columns: CompanyCodeColumns,
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sys_department");

            migrationBuilder.DropTable(
                name: "sys_branch");

            migrationBuilder.DropTable(
                name: "sys_company");
        }
    }
}
