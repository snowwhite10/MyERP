using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class System_Approval : Migration
    {
        private static readonly string[] CompanyIdColumns = ["company_id", "id"];
        private static readonly string[] DelegateColumns = ["company_id", "delegate_user_id", "start_date"];
        private static readonly string[] DelegatorColumns = ["company_id", "delegator_user_id", "start_date"];
        private static readonly string[] FlowTypeColumns = ["company_id", "document_type"];
        private static readonly string[] FlowStepColumns = ["company_id", "flow_id", "step_no"];
        private static readonly string[] InstanceHistoryColumns = ["company_id", "document_type", "document_id", "submitted_at"];
        private static readonly string[] InstanceFlowColumns = ["company_id", "flow_id"];
        private static readonly string[] InstanceDocumentColumns = ["company_id", "document_type", "document_id"];
        private static readonly string[] StepRoleColumns = ["company_id", "status", "role_id"];
        private static readonly string[] StepUserColumns = ["company_id", "status", "user_id"];
        private static readonly string[] InstanceStepColumns = ["company_id", "instance_id", "step_no"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sys_approval_delegation",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    delegator_user_id = table.Column<long>(type: "bigint", nullable: false),
                    delegate_user_id = table.Column<long>(type: "bigint", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    remark = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sys_approval_delegation", x => x.id);
                    table.CheckConstraint("ck_sys_approval_delegation_dates", "[end_date] >= [start_date]");
                    table.CheckConstraint("ck_sys_approval_delegation_users", "[delegator_user_id] <> [delegate_user_id]");
                    table.ForeignKey(
                        name: "fk_sys_approval_delegation_sys_company_company_id",
                        column: x => x.company_id,
                        principalTable: "sys_company",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sys_approval_delegation_sys_user_delegate_user_id",
                        column: x => x.delegate_user_id,
                        principalTable: "sys_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sys_approval_delegation_sys_user_delegator_user_id",
                        column: x => x.delegator_user_id,
                        principalTable: "sys_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sys_approval_flow",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    document_type = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    allow_self_approval = table.Column<bool>(type: "bit", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sys_approval_flow", x => x.id);
                    table.UniqueConstraint("ak_sys_approval_flow_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "fk_sys_approval_flow_sys_company_company_id",
                        column: x => x.company_id,
                        principalTable: "sys_company",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sys_approval_flow_step",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    flow_id = table.Column<long>(type: "bigint", nullable: false),
                    step_no = table.Column<int>(type: "int", nullable: false),
                    name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    approver_type = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    role_id = table.Column<long>(type: "bigint", nullable: true),
                    user_id = table.Column<long>(type: "bigint", nullable: true),
                    min_amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sys_approval_flow_step", x => x.id);
                    table.CheckConstraint("ck_sys_approval_flow_step_approver", "([approver_type] = 'Role' AND [role_id] IS NOT NULL AND [user_id] IS NULL) OR ([approver_type] = 'User' AND [user_id] IS NOT NULL AND [role_id] IS NULL)");
                    table.CheckConstraint("ck_sys_approval_flow_step_min_amount", "[min_amount] IS NULL OR [min_amount] >= 0");
                    table.CheckConstraint("ck_sys_approval_flow_step_step_no", "[step_no] BETWEEN 1 AND 10");
                    table.ForeignKey(
                        name: "fk_sys_approval_flow_step_sys_approval_flow_company_id_flow_id",
                        columns: x => new { x.company_id, x.flow_id },
                        principalTable: "sys_approval_flow",
                        principalColumns: CompanyIdColumns,
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_sys_approval_flow_step_sys_role_role_id",
                        column: x => x.role_id,
                        principalTable: "sys_role",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sys_approval_flow_step_sys_user_user_id",
                        column: x => x.user_id,
                        principalTable: "sys_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sys_approval_instance",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    flow_id = table.Column<long>(type: "bigint", nullable: true),
                    document_type = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    document_id = table.Column<long>(type: "bigint", nullable: false),
                    document_no = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    summary = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    status = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    current_step_no = table.Column<int>(type: "int", nullable: true),
                    allow_self_approval = table.Column<bool>(type: "bit", nullable: false),
                    submitted_by = table.Column<long>(type: "bigint", nullable: false),
                    submitted_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    completed_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sys_approval_instance", x => x.id);
                    table.UniqueConstraint("ak_sys_approval_instance_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("ck_sys_approval_instance_amount", "[amount] >= 0");
                    table.CheckConstraint("ck_sys_approval_instance_current_step", "([status] = 'Pending' AND [current_step_no] IS NOT NULL) OR ([status] <> 'Pending' AND [current_step_no] IS NULL)");
                    table.ForeignKey(
                        name: "fk_sys_approval_instance_sys_approval_flow_company_id_flow_id",
                        columns: x => new { x.company_id, x.flow_id },
                        principalTable: "sys_approval_flow",
                        principalColumns: CompanyIdColumns,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sys_approval_instance_sys_company_company_id",
                        column: x => x.company_id,
                        principalTable: "sys_company",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sys_approval_instance_step",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    instance_id = table.Column<long>(type: "bigint", nullable: false),
                    step_no = table.Column<int>(type: "int", nullable: false),
                    name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    approver_type = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    role_id = table.Column<long>(type: "bigint", nullable: true),
                    user_id = table.Column<long>(type: "bigint", nullable: true),
                    min_amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    status = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    acted_by = table.Column<long>(type: "bigint", nullable: true),
                    on_behalf_of_user_id = table.Column<long>(type: "bigint", nullable: true),
                    acted_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    comment = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sys_approval_instance_step", x => x.id);
                    table.CheckConstraint("ck_sys_approval_instance_step_approver", "([approver_type] = 'Role' AND [role_id] IS NOT NULL AND [user_id] IS NULL) OR ([approver_type] = 'User' AND [user_id] IS NOT NULL AND [role_id] IS NULL)");
                    table.ForeignKey(
                        name: "fk_sys_approval_instance_step_sys_approval_instance_company_id_instance_id",
                        columns: x => new { x.company_id, x.instance_id },
                        principalTable: "sys_approval_instance",
                        principalColumns: CompanyIdColumns,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sys_approval_instance_step_sys_role_role_id",
                        column: x => x.role_id,
                        principalTable: "sys_role",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sys_approval_instance_step_sys_user_acted_by",
                        column: x => x.acted_by,
                        principalTable: "sys_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sys_approval_instance_step_sys_user_on_behalf_of_user_id",
                        column: x => x.on_behalf_of_user_id,
                        principalTable: "sys_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sys_approval_instance_step_sys_user_user_id",
                        column: x => x.user_id,
                        principalTable: "sys_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sys_approval_delegation_company_id_delegate_user_id_start_date",
                table: "sys_approval_delegation",
                columns: DelegateColumns);

            migrationBuilder.CreateIndex(
                name: "ix_sys_approval_delegation_company_id_delegator_user_id_start_date",
                table: "sys_approval_delegation",
                columns: DelegatorColumns);

            migrationBuilder.CreateIndex(
                name: "ix_sys_approval_delegation_delegate_user_id",
                table: "sys_approval_delegation",
                column: "delegate_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_sys_approval_delegation_delegator_user_id",
                table: "sys_approval_delegation",
                column: "delegator_user_id");

            migrationBuilder.CreateIndex(
                name: "ux_sys_approval_flow_company_id_document_type",
                table: "sys_approval_flow",
                columns: FlowTypeColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sys_approval_flow_step_role_id",
                table: "sys_approval_flow_step",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_sys_approval_flow_step_user_id",
                table: "sys_approval_flow_step",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ux_sys_approval_flow_step_company_id_flow_id_step_no",
                table: "sys_approval_flow_step",
                columns: FlowStepColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sys_approval_instance_company_id_document_type_document_id_submitted_at",
                table: "sys_approval_instance",
                columns: InstanceHistoryColumns);

            migrationBuilder.CreateIndex(
                name: "ix_sys_approval_instance_company_id_flow_id",
                table: "sys_approval_instance",
                columns: InstanceFlowColumns);

            migrationBuilder.CreateIndex(
                name: "ux_sys_approval_instance_pending_document",
                table: "sys_approval_instance",
                columns: InstanceDocumentColumns,
                unique: true,
                filter: "[status] = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "ix_sys_approval_instance_step_acted_by",
                table: "sys_approval_instance_step",
                column: "acted_by");

            migrationBuilder.CreateIndex(
                name: "ix_sys_approval_instance_step_company_id_status_role_id",
                table: "sys_approval_instance_step",
                columns: StepRoleColumns);

            migrationBuilder.CreateIndex(
                name: "ix_sys_approval_instance_step_company_id_status_user_id",
                table: "sys_approval_instance_step",
                columns: StepUserColumns);

            migrationBuilder.CreateIndex(
                name: "ix_sys_approval_instance_step_on_behalf_of_user_id",
                table: "sys_approval_instance_step",
                column: "on_behalf_of_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_sys_approval_instance_step_role_id",
                table: "sys_approval_instance_step",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_sys_approval_instance_step_user_id",
                table: "sys_approval_instance_step",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ux_sys_approval_instance_step_company_id_instance_id_step_no",
                table: "sys_approval_instance_step",
                columns: InstanceStepColumns,
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sys_approval_delegation");

            migrationBuilder.DropTable(
                name: "sys_approval_flow_step");

            migrationBuilder.DropTable(
                name: "sys_approval_instance_step");

            migrationBuilder.DropTable(
                name: "sys_approval_instance");

            migrationBuilder.DropTable(
                name: "sys_approval_flow");
        }
    }
}
