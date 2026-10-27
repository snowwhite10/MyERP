using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Sales_OrderSourceImmutable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TRIGGER dbo.tr_sal_order_source_immutable ON dbo.sal_order
                AFTER UPDATE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (
                        SELECT 1 FROM inserted i JOIN deleted d ON d.id = i.id
                        WHERE ISNULL(i.source_quotation_id, -1) <> ISNULL(d.source_quotation_id, -1)
                    ) THROW 51001, 'Saved sales order quotation source is immutable.', 1;
                END
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER dbo.tr_sal_order_line_source_immutable ON dbo.sal_order_line
                AFTER UPDATE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (
                        SELECT 1 FROM inserted i JOIN deleted d ON d.id = i.id
                        WHERE ISNULL(i.source_quotation_line_id, -1) <> ISNULL(d.source_quotation_line_id, -1)
                    ) THROW 51002, 'Saved sales order line quotation source is immutable.', 1;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER dbo.tr_sal_order_line_source_immutable;");
            migrationBuilder.Sql("DROP TRIGGER dbo.tr_sal_order_source_immutable;");
        }
    }
}
