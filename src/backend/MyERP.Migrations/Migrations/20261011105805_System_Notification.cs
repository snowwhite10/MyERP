using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861 // EF Core generated index column arrays

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class System_Notification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sys_notification",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    recipient_user_id = table.Column<long>(type: "bigint", nullable: false),
                    category = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    title = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    body = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    target_path = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    source_key = table.Column<string>(type: "varchar(160)", unicode: false, maxLength: 160, nullable: false),
                    read_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    expires_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sys_notification", x => x.id);
                    table.CheckConstraint("ck_sys_notification_expiry", "[expires_at] > [created_at]");
                    table.ForeignKey(
                        name: "fk_sys_notification_sys_company_company_id",
                        column: x => x.company_id,
                        principalTable: "sys_company",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sys_notification_sys_user_recipient_user_id",
                        column: x => x.recipient_user_id,
                        principalTable: "sys_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sys_notification_inbox",
                table: "sys_notification",
                columns: new[] { "company_id", "recipient_user_id", "read_at", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_sys_notification_recipient_user_id",
                table: "sys_notification",
                column: "recipient_user_id");

            migrationBuilder.CreateIndex(
                name: "ux_sys_notification_company_recipient_source",
                table: "sys_notification",
                columns: new[] { "company_id", "recipient_user_id", "source_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sys_notification");
        }
    }
}
