using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Sales_OrderCommercialHeaderGuard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TRIGGER dbo.tr_sal_order_commercial_header_guard ON dbo.sal_order
                AFTER UPDATE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (
                        SELECT 1
                        FROM inserted i
                        JOIN deleted d ON d.id = i.id
                        WHERE (d.status IN ('Pending', 'Approved', 'Cancelled') OR d.is_deleted = 1)
                          AND EXISTS (
                              SELECT i.company_id, i.document_no, i.customer_id,
                                  i.customer_code, i.customer_name, i.business_date,
                                  i.currency_code, i.base_currency_code, i.exchange_rate,
                                  i.source_quotation_id, i.original_net, i.original_tax,
                                  i.original_gross, i.base_net, i.base_tax, i.base_gross, i.remark
                              EXCEPT
                              SELECT d.company_id, d.document_no, d.customer_id,
                                  d.customer_code, d.customer_name, d.business_date,
                                  d.currency_code, d.base_currency_code, d.exchange_rate,
                                  d.source_quotation_id, d.original_net, d.original_tax,
                                  d.original_gross, d.base_net, d.base_tax, d.base_gross, d.remark
                          )
                    ) THROW 51006, 'Saved sales order commercial header is immutable in this state.', 1;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER dbo.tr_sal_order_commercial_header_guard;");
        }
    }
}
