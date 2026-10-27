using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Sales_OrderLineSourceConsistency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TRIGGER dbo.tr_sal_order_line_source_consistency ON dbo.sal_order_line
                AFTER INSERT, UPDATE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (
                        SELECT 1 FROM inserted i
                        JOIN dbo.sal_order o ON o.id = i.order_id AND o.company_id = i.company_id
                        WHERE (o.source_quotation_id IS NULL AND i.source_quotation_line_id IS NOT NULL)
                           OR (o.source_quotation_id IS NOT NULL AND i.source_quotation_line_id IS NULL)
                    ) THROW 51003, 'Sales order and line quotation sources must agree.', 1;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER dbo.tr_sal_order_line_source_consistency;");
        }
    }
}
