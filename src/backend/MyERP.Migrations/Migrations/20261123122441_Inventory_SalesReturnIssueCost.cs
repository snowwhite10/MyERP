using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyERP.Migrations.Migrations
{
    /// <summary>ADR-188（WU-SD-008a3）：銷退入庫按原出庫成本計價所需的出庫成本 SP；不改 schema。</summary>
    public partial class Inventory_SalesReturnIssueCost : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateOrAlterProcedure("inv/usp_inv_movement_issue_cost");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropProcedure("inv/usp_inv_movement_issue_cost");
        }
    }
}
