using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class System_AuditLog : Migration
    {
        private static readonly string[] CompanyColumns = ["company_id", "occurred_at"];
        private static readonly string[] RecordColumns = ["table_name", "record_id", "occurred_at"];
        private static readonly string[] UserColumns = ["user_id", "occurred_at"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sys_audit_log",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    company_id = table.Column<long>(type: "bigint", nullable: true),
                    user_id = table.Column<long>(type: "bigint", nullable: true),
                    username = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    occurred_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    category = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    action = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    table_name = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: true),
                    record_id = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    changes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    remote_ip = table.Column<string>(type: "varchar(45)", unicode: false, maxLength: 45, nullable: true),
                    trace_id = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sys_audit_log", x => x.id);
                    table.CheckConstraint("ck_sys_audit_log_changes_json", "[changes] IS NULL OR ISJSON([changes]) = 1");
                });

            migrationBuilder.CreateIndex(
                name: "ix_sys_audit_log_company_id_occurred_at",
                table: "sys_audit_log",
                columns: CompanyColumns);

            migrationBuilder.CreateIndex(
                name: "ix_sys_audit_log_table_name_record_id_occurred_at",
                table: "sys_audit_log",
                columns: RecordColumns);

            migrationBuilder.CreateIndex(
                name: "ix_sys_audit_log_user_id_occurred_at",
                table: "sys_audit_log",
                columns: UserColumns);

            // 只增不改（MASTER_PROMPT 6.7、8.2）：任何 UPDATE／DELETE 都被拒絕；封存需由 DBA 明確停用觸發程序
            migrationBuilder.Sql("""
                CREATE TRIGGER [trg_sys_audit_log_append_only] ON [sys_audit_log]
                INSTEAD OF UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51000, 'sys_audit_log is append-only.', 1;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sys_audit_log");
        }
    }
}
