using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class MasterData_CustomerReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "review_status",
                table: "md_customer",
                type: "varchar(16)",
                unicode: false,
                maxLength: 16,
                nullable: false,
                defaultValue: "Pending");

            migrationBuilder.AddCheckConstraint(
                name: "ck_md_customer_review_status",
                table: "md_customer",
                sql: "[review_status] IN ('Pending', 'Approved', 'Rejected')");
            // 舊客戶已可作交易，升級後須維持可用；新增客戶仍由預設值進入待審。
            migrationBuilder.Sql("UPDATE [md_customer] SET [review_status] = 'Approved'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_md_customer_review_status",
                table: "md_customer");

            migrationBuilder.DropColumn(
                name: "review_status",
                table: "md_customer");
        }
    }
}
