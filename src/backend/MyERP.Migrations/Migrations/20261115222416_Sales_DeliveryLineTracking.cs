using System;
using Microsoft.EntityFrameworkCore.Migrations;

#pragma warning disable CA1861 // EF Core generates constant column arrays for migration operations.

#nullable disable

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Sales_DeliveryLineTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "ak_sal_delivery_line_company_id_id",
                table: "sal_delivery_line",
                columns: new[] { "company_id", "id" });

            migrationBuilder.CreateTable(
                name: "sal_delivery_line_tracking",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    line_id = table.Column<long>(type: "bigint", nullable: false),
                    seq_no = table.Column<int>(type: "int", nullable: false),
                    lot_no = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    serial_no = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    quantity = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sal_delivery_line_tracking", x => x.id);
                    table.CheckConstraint("ck_sal_delivery_line_tracking_quantity", "[quantity] > 0");
                    table.CheckConstraint("ck_sal_delivery_line_tracking_seq_no", "[seq_no] > 0");
                    table.CheckConstraint("ck_sal_delivery_line_tracking_target", "([lot_no] IS NOT NULL AND [serial_no] IS NULL) OR ([lot_no] IS NULL AND [serial_no] IS NOT NULL AND [quantity] = 1)");
                    table.ForeignKey(
                        name: "fk_sal_delivery_line_tracking_sal_delivery_line_company_id_line_id",
                        columns: x => new { x.company_id, x.line_id },
                        principalTable: "sal_delivery_line",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sal_delivery_line_tracking_company_id_line_id_seq_no",
                table: "sal_delivery_line_tracking",
                columns: new[] { "company_id", "line_id", "seq_no" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sal_delivery_line_tracking");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_sal_delivery_line_company_id_id",
                table: "sal_delivery_line");
        }
    }
}
