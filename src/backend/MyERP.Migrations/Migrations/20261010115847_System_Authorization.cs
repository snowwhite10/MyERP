using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class System_Authorization : Migration
    {
        private static readonly string[] CompanyIdColumns = ["company_id", "id"];
        private static readonly string[] UserCompanyColumns = ["user_id", "company_id"];
        private static readonly string[] RolePermissionColumns = ["role_id", "permission_code"];
        private static readonly string[] CompanyBranchColumns = ["company_id", "branch_id"];
        private static readonly string[] CompanyDepartmentColumns = ["company_id", "department_id"];
        private static readonly string[] UserCompanyRoleColumns = ["user_id", "company_id", "role_id"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sys_role",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    code = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    data_scope = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    is_full_access = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("pk_sys_role", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sys_user_company",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    branch_id = table.Column<long>(type: "bigint", nullable: true),
                    department_id = table.Column<long>(type: "bigint", nullable: true),
                    is_default = table.Column<bool>(type: "bit", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sys_user_company", x => x.id);
                    table.UniqueConstraint("ak_sys_user_company_user_id_company_id", x => new { x.user_id, x.company_id });
                    table.ForeignKey(
                        name: "fk_sys_user_company_sys_branch_company_id_branch_id",
                        columns: x => new { x.company_id, x.branch_id },
                        principalTable: "sys_branch",
                        principalColumns: CompanyIdColumns,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sys_user_company_sys_company_company_id",
                        column: x => x.company_id,
                        principalTable: "sys_company",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sys_user_company_sys_department_company_id_department_id",
                        columns: x => new { x.company_id, x.department_id },
                        principalTable: "sys_department",
                        principalColumns: CompanyIdColumns,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sys_user_company_sys_user_user_id",
                        column: x => x.user_id,
                        principalTable: "sys_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sys_role_permission",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    role_id = table.Column<long>(type: "bigint", nullable: false),
                    permission_code = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sys_role_permission", x => x.id);
                    table.ForeignKey(
                        name: "fk_sys_role_permission_sys_role_role_id",
                        column: x => x.role_id,
                        principalTable: "sys_role",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sys_user_role",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    role_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sys_user_role", x => x.id);
                    table.ForeignKey(
                        name: "fk_sys_user_role_sys_role_role_id",
                        column: x => x.role_id,
                        principalTable: "sys_role",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sys_user_role_sys_user_company_user_id_company_id",
                        columns: x => new { x.user_id, x.company_id },
                        principalTable: "sys_user_company",
                        principalColumns: UserCompanyColumns,
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_sys_role_code",
                table: "sys_role",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_sys_role_permission_role_id_permission_code",
                table: "sys_role_permission",
                columns: RolePermissionColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sys_user_company_company_id_branch_id",
                table: "sys_user_company",
                columns: CompanyBranchColumns);

            migrationBuilder.CreateIndex(
                name: "ix_sys_user_company_company_id_department_id",
                table: "sys_user_company",
                columns: CompanyDepartmentColumns);

            migrationBuilder.CreateIndex(
                name: "ux_sys_user_company_default",
                table: "sys_user_company",
                column: "user_id",
                unique: true,
                filter: "[is_default] = 1");

            migrationBuilder.CreateIndex(
                name: "ix_sys_user_role_role_id",
                table: "sys_user_role",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ux_sys_user_role_user_id_company_id_role_id",
                table: "sys_user_role",
                columns: UserCompanyRoleColumns,
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sys_role_permission");

            migrationBuilder.DropTable(
                name: "sys_user_role");

            migrationBuilder.DropTable(
                name: "sys_role");

            migrationBuilder.DropTable(
                name: "sys_user_company");
        }
    }
}
