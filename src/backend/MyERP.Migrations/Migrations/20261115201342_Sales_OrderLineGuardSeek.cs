using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyERP.Migrations.Migrations
{
    /// <summary>
    /// The sales order line guard triggers read the parent order. On a small header
    /// table SQL Server chose a clustered scan, which under the Serializable command
    /// transactions took range locks on every other company's order and deadlocked
    /// unrelated edits. Both triggers now seek the parent row by primary key.
    /// </summary>
    public partial class Sales_OrderLineGuardSeek : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(StateGuard("WITH (FORCESEEK)"));
            migrationBuilder.Sql(SourceConsistency("WITH (FORCESEEK)"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(StateGuard(string.Empty));
            migrationBuilder.Sql(SourceConsistency(string.Empty));
        }

        private static string StateGuard(string hint) => $"""
            CREATE OR ALTER TRIGGER dbo.tr_sal_order_line_state_guard ON dbo.sal_order_line
            AFTER INSERT, UPDATE, DELETE AS
            BEGIN
                SET NOCOUNT ON;
                IF EXISTS (
                    SELECT 1 FROM (
                        SELECT company_id, order_id FROM inserted
                        UNION
                        SELECT company_id, order_id FROM deleted
                    ) affected
                    JOIN dbo.sal_order o {hint} ON o.id = affected.order_id
                        AND o.company_id = affected.company_id
                    WHERE o.status <> 'Draft' OR o.is_deleted = 1
                ) THROW 51004, 'Only active Draft sales order lines can change.', 1;
            END
            """;

        private static string SourceConsistency(string hint) => $"""
            CREATE OR ALTER TRIGGER dbo.tr_sal_order_line_source_consistency ON dbo.sal_order_line
            AFTER INSERT, UPDATE AS
            BEGIN
                SET NOCOUNT ON;
                IF EXISTS (
                    SELECT 1 FROM inserted i
                    JOIN dbo.sal_order o {hint} ON o.id = i.order_id AND o.company_id = i.company_id
                    WHERE (o.source_quotation_id IS NULL AND i.source_quotation_line_id IS NOT NULL)
                       OR (o.source_quotation_id IS NOT NULL AND i.source_quotation_line_id IS NULL)
                ) THROW 51003, 'Sales order and line quotation sources must agree.', 1;
            END
            """;
    }
}
