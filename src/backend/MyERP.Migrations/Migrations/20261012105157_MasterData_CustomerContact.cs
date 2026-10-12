using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861 // EF migration generator emits literal index column arrays.

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class MasterData_CustomerContact : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "ak_md_customer_company_id_id",
                table: "md_customer",
                columns: new[] { "company_id", "id" });

            migrationBuilder.CreateTable(
                name: "md_customer_address",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    address = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    postal_code = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    recipient = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    phone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    customer_id = table.Column<long>(type: "bigint", nullable: false),
                    is_default = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_md_customer_address", x => x.id);
                    table.CheckConstraint("ck_md_customer_address_default_active", "[is_default] = 0 OR ([is_active] = 1 AND [is_deleted] = 0)");
                    table.ForeignKey(
                        name: "fk_md_customer_address_md_customer_company_id_customer_id",
                        columns: x => new { x.company_id, x.customer_id },
                        principalTable: "md_customer",
                        principalColumns: new[] { "company_id", "id" });
                });

            migrationBuilder.CreateTable(
                name: "md_customer_contact",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    customer_id = table.Column<long>(type: "bigint", nullable: false),
                    is_default = table.Column<bool>(type: "bit", nullable: false),
                    name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    position = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    mobile = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    phone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    email = table.Column<string>(type: "varchar(200)", unicode: false, maxLength: 200, nullable: true),
                    messaging_id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_md_customer_contact", x => x.id);
                    table.CheckConstraint("ck_md_customer_contact_default_active", "[is_default] = 0 OR ([is_active] = 1 AND [is_deleted] = 0)");
                    table.ForeignKey(
                        name: "fk_md_customer_contact_md_customer_company_id_customer_id",
                        columns: x => new { x.company_id, x.customer_id },
                        principalTable: "md_customer",
                        principalColumns: new[] { "company_id", "id" });
                });

            migrationBuilder.CreateIndex(
                name: "ix_md_customer_address_company_id_customer_id",
                table: "md_customer_address",
                columns: new[] { "company_id", "customer_id" });

            migrationBuilder.CreateIndex(
                name: "ux_md_customer_address_company_id_customer_id_default",
                table: "md_customer_address",
                columns: new[] { "company_id", "customer_id" },
                unique: true,
                filter: "[is_default] = 1");

            migrationBuilder.CreateIndex(
                name: "ix_md_customer_contact_company_id_customer_id",
                table: "md_customer_contact",
                columns: new[] { "company_id", "customer_id" });

            migrationBuilder.CreateIndex(
                name: "ux_md_customer_contact_company_id_customer_id_default",
                table: "md_customer_contact",
                columns: new[] { "company_id", "customer_id" },
                unique: true,
                filter: "[is_default] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "md_customer_address");

            migrationBuilder.DropTable(
                name: "md_customer_contact");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_md_customer_company_id_id",
                table: "md_customer");
        }
    }
}
