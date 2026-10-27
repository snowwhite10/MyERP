using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Sales_OrderStateGuard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TRIGGER dbo.tr_sal_order_state_guard ON dbo.sal_order
                AFTER UPDATE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (
                        SELECT 1
                        FROM inserted i
                        JOIN deleted d ON d.id = i.id
                        WHERE (d.is_deleted = 1 AND i.is_deleted = 0)
                           OR (d.is_deleted = 1 AND i.status <> d.status)
                           OR (i.status <> d.status AND NOT (
                               (d.status = 'Draft' AND i.status IN ('Pending', 'Approved'))
                               OR (d.status = 'Pending' AND i.status IN ('Approved', 'Rejected'))
                               OR (d.status = 'Rejected' AND i.status = 'Draft')
                               OR (d.status = 'Approved' AND i.status = 'Cancelled')
                           ))
                    ) THROW 51005, 'Invalid sales order state transition or deleted order revival.', 1;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER dbo.tr_sal_order_state_guard;");
        }
    }
}
