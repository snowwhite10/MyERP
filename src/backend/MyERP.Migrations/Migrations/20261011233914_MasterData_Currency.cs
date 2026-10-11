using System;
using Microsoft.EntityFrameworkCore.Migrations;

#pragma warning disable CA1861 // EF Core generated index column arrays

#nullable disable

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class MasterData_Currency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "md_currency",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    symbol = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    decimal_places = table.Column<int>(type: "int", nullable: false),
                    is_base = table.Column<bool>(type: "bit", nullable: false),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    code = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    custom_fields = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_md_currency", x => x.id);
                    table.UniqueConstraint("ak_md_currency_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("ck_md_currency_custom_fields_json", "[custom_fields] IS NULL OR ISJSON([custom_fields]) = 1");
                    table.CheckConstraint("ck_md_currency_decimal_places", "[decimal_places] BETWEEN 0 AND 4");
                });

            migrationBuilder.CreateTable(
                name: "md_exchange_rate",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    currency_id = table.Column<long>(type: "bigint", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    rate = table.Column<decimal>(type: "decimal(19,8)", precision: 19, scale: 8, nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_md_exchange_rate", x => x.id);
                    table.CheckConstraint("ck_md_exchange_rate_period", "[effective_to] IS NULL OR [effective_to] >= [effective_from]");
                    table.CheckConstraint("ck_md_exchange_rate_rate", "[rate] > 0");
                    table.ForeignKey(
                        name: "fk_md_exchange_rate_md_currency_company_id_currency_id",
                        columns: x => new { x.company_id, x.currency_id },
                        principalTable: "md_currency",
                        principalColumns: new[] { "company_id", "id" });
                });

            migrationBuilder.CreateIndex(
                name: "ix_md_currency_company_id_name",
                table: "md_currency",
                columns: new[] { "company_id", "name" });

            migrationBuilder.CreateIndex(
                name: "ux_md_currency_company_id_base",
                table: "md_currency",
                column: "company_id",
                unique: true,
                filter: "[is_base] = 1 AND [is_deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "ux_md_currency_company_id_code",
                table: "md_currency",
                columns: new[] { "company_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_md_exchange_rate_company_id_currency_id",
                table: "md_exchange_rate",
                columns: new[] { "company_id", "currency_id" });

            migrationBuilder.CreateIndex(
                name: "ux_md_exchange_rate_company_id_currency_id_effective_from",
                table: "md_exchange_rate",
                columns: new[] { "company_id", "currency_id", "effective_from" },
                unique: true,
                filter: "[is_deleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "md_exchange_rate");

            migrationBuilder.DropTable(
                name: "md_currency");
        }
    }
}
