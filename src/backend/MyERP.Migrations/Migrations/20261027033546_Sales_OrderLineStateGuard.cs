using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Sales_OrderLineStateGuard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TRIGGER dbo.tr_sal_order_line_state_guard ON dbo.sal_order_line
                AFTER INSERT, UPDATE, DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (
                        SELECT 1 FROM (
                            SELECT company_id, order_id FROM inserted
                            UNION
                            SELECT company_id, order_id FROM deleted
                        ) affected
                        JOIN dbo.sal_order o ON o.id = affected.order_id
                            AND o.company_id = affected.company_id
                        WHERE o.status <> 'Draft' OR o.is_deleted = 1
                    ) THROW 51004, 'Only active Draft sales order lines can change.', 1;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER dbo.tr_sal_order_line_state_guard;");
        }
    }
}
