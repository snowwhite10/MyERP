using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class MasterData_SupplierReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "review_status",
                table: "md_supplier",
                type: "varchar(16)",
                unicode: false,
                maxLength: 16,
                nullable: false,
                defaultValue: "Pending");

            migrationBuilder.AddCheckConstraint(
                name: "ck_md_supplier_review_status",
                table: "md_supplier",
                sql: "[review_status] IN ('Pending', 'Approved', 'Rejected')");
            // 舊供應商已可作交易，升級後須維持可用；新增供應商仍由預設值進入待審。
            migrationBuilder.Sql("UPDATE [md_supplier] SET [review_status] = 'Approved'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_md_supplier_review_status",
                table: "md_supplier");

            migrationBuilder.DropColumn(
                name: "review_status",
                table: "md_supplier");
        }
    }
}
