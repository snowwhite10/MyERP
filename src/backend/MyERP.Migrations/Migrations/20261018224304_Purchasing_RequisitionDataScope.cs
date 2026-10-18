using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Purchasing_RequisitionDataScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "branch_id",
                table: "pur_requisition",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "department_id",
                table: "pur_requisition",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_pur_requisition_company_id_branch_id_id",
                table: "pur_requisition",
                columns: ["company_id", "branch_id", "id"]);

            migrationBuilder.CreateIndex(
                name: "ix_pur_requisition_company_id_department_id_id",
                table: "pur_requisition",
                columns: ["company_id", "department_id", "id"]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_pur_requisition_company_id_branch_id_id",
                table: "pur_requisition");

            migrationBuilder.DropIndex(
                name: "ix_pur_requisition_company_id_department_id_id",
                table: "pur_requisition");

            migrationBuilder.DropColumn(
                name: "branch_id",
                table: "pur_requisition");

            migrationBuilder.DropColumn(
                name: "department_id",
                table: "pur_requisition");
        }
    }
}
