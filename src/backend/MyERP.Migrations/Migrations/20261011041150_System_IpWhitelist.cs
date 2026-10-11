using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861 // EF migration generator emits literal index column arrays.

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class System_IpWhitelist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sys_ip_whitelist",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    scope = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    role_id = table.Column<long>(type: "bigint", nullable: true),
                    user_id = table.Column<long>(type: "bigint", nullable: true),
                    ip_range = table.Column<string>(type: "varchar(43)", unicode: false, maxLength: 43, nullable: false),
                    description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: true),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sys_ip_whitelist", x => x.id);
                    table.CheckConstraint("ck_sys_ip_whitelist_dates", "[valid_from] IS NULL OR [valid_to] IS NULL OR [valid_to] >= [valid_from]");
                    table.CheckConstraint("ck_sys_ip_whitelist_target", "([scope] = 'System' AND [role_id] IS NULL AND [user_id] IS NULL) OR ([scope] = 'Role' AND [role_id] IS NOT NULL AND [user_id] IS NULL) OR ([scope] = 'User' AND [user_id] IS NOT NULL AND [role_id] IS NULL)");
                    table.ForeignKey(
                        name: "fk_sys_ip_whitelist_sys_role_role_id",
                        column: x => x.role_id,
                        principalTable: "sys_role",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sys_ip_whitelist_sys_user_user_id",
                        column: x => x.user_id,
                        principalTable: "sys_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sys_ip_whitelist_role_id",
                table: "sys_ip_whitelist",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_sys_ip_whitelist_user_id",
                table: "sys_ip_whitelist",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ux_sys_ip_whitelist_role_range",
                table: "sys_ip_whitelist",
                columns: new[] { "role_id", "ip_range" },
                unique: true,
                filter: "[scope] = 'Role'");

            migrationBuilder.CreateIndex(
                name: "ux_sys_ip_whitelist_system_range",
                table: "sys_ip_whitelist",
                column: "ip_range",
                unique: true,
                filter: "[scope] = 'System'");

            migrationBuilder.CreateIndex(
                name: "ux_sys_ip_whitelist_user_range",
                table: "sys_ip_whitelist",
                columns: new[] { "user_id", "ip_range" },
                unique: true,
                filter: "[scope] = 'User'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sys_ip_whitelist");
        }
    }
}
