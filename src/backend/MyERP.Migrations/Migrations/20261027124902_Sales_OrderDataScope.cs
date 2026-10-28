using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Sales_OrderDataScope : Migration
    {
        private static readonly string[] ScopeColumns =
            ["company_id", "branch_id", "department_id", "owner_user_id"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "branch_id",
                table: "sal_order",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "department_id",
                table: "sal_order",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "owner_user_id",
                table: "sal_order",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            // Existing orders remain visible to their original creator under Self scope.
            migrationBuilder.Sql("UPDATE dbo.sal_order SET owner_user_id = created_by WHERE owner_user_id = 0;");

            migrationBuilder.CreateIndex(
                name: "ix_sal_order_company_id_branch_id_department_id_owner_user_id",
                table: "sal_order",
                columns: ScopeColumns);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_sal_order_company_id_branch_id_department_id_owner_user_id",
                table: "sal_order");

            migrationBuilder.DropColumn(
                name: "branch_id",
                table: "sal_order");

            migrationBuilder.DropColumn(
                name: "department_id",
                table: "sal_order");

            migrationBuilder.DropColumn(
                name: "owner_user_id",
                table: "sal_order");
        }
    }
}
