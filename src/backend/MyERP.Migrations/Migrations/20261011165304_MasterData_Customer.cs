using System;
using Microsoft.EntityFrameworkCore.Migrations;

#pragma warning disable CA1861 // EF Core generated index column arrays

#nullable disable

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class MasterData_Customer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "ak_md_customer_category_company_id_id",
                table: "md_customer_category",
                columns: new[] { "company_id", "id" });

            migrationBuilder.CreateTable(
                name: "md_customer",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    code = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    category_id = table.Column<long>(type: "bigint", nullable: true),
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
                    table.PrimaryKey("pk_md_customer", x => x.id);
                    table.CheckConstraint("ck_md_customer_custom_fields_json", "[custom_fields] IS NULL OR ISJSON([custom_fields]) = 1");
                    table.ForeignKey(
                        name: "fk_md_customer_md_customer_category_company_id_category_id",
                        columns: x => new { x.company_id, x.category_id },
                        principalTable: "md_customer_category",
                        principalColumns: new[] { "company_id", "id" });
                });

            migrationBuilder.CreateIndex(
                name: "ix_md_customer_company_id_category_id",
                table: "md_customer",
                columns: new[] { "company_id", "category_id" });

            migrationBuilder.CreateIndex(
                name: "ix_md_customer_company_id_name",
                table: "md_customer",
                columns: new[] { "company_id", "name" });

            migrationBuilder.CreateIndex(
                name: "ux_md_customer_company_id_code",
                table: "md_customer",
                columns: new[] { "company_id", "code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "md_customer");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_md_customer_category_company_id_id",
                table: "md_customer_category");
        }
    }
}
