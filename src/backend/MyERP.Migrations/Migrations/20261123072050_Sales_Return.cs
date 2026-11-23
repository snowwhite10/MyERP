using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Sales_Return : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "ak_sal_invoice_line_company_id_id",
                table: "sal_invoice_line",
                columns: new[] { "company_id", "id" });

            migrationBuilder.CreateTable(
                name: "sal_return",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    document_no = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    request_key = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    request_fingerprint = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                    customer_id = table.Column<long>(type: "bigint", nullable: false),
                    order_id = table.Column<long>(type: "bigint", nullable: false),
                    business_date = table.Column<DateOnly>(type: "date", nullable: false),
                    currency_code = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                    base_currency_code = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                    exchange_rate = table.Column<decimal>(type: "decimal(19,8)", precision: 19, scale: 8, nullable: false),
                    status = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    remark = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    approval_instance_id = table.Column<long>(type: "bigint", nullable: true),
                    posted_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    fi_credit_note_id = table.Column<long>(type: "bigint", nullable: true),
                    original_net = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    original_tax = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    original_gross = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    base_net = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    base_tax = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    base_gross = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sal_return", x => x.id);
                    table.UniqueConstraint("ak_sal_return_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("ck_sal_return_amounts", "[original_net] >= 0 AND [original_tax] >= 0 AND [original_gross] = [original_net] + [original_tax] AND [base_net] >= 0 AND [base_tax] >= 0 AND [base_gross] = [base_net] + [base_tax]");
                    table.CheckConstraint("ck_sal_return_deleted", "([is_deleted] = 0 AND [deleted_at] IS NULL) OR ([is_deleted] = 1 AND [deleted_at] IS NOT NULL AND [status] = 'Draft')");
                    table.CheckConstraint("ck_sal_return_fi_credit", "[fi_credit_note_id] IS NULL OR [fi_credit_note_id] > 0");
                    table.CheckConstraint("ck_sal_return_posted", "([status] = 'Posted' AND [posted_at] IS NOT NULL) OR ([status] <> 'Posted' AND [posted_at] IS NULL AND [fi_credit_note_id] IS NULL)");
                    table.CheckConstraint("ck_sal_return_rate", "[exchange_rate] > 0 AND ([currency_code] <> [base_currency_code] OR ([exchange_rate] = 1 AND [base_net] = [original_net] AND [base_tax] = [original_tax] AND [base_gross] = [original_gross]))");
                    table.CheckConstraint("ck_sal_return_refs", "[customer_id] > 0 AND [order_id] > 0 AND ([approval_instance_id] IS NULL OR [approval_instance_id] > 0)");
                    table.CheckConstraint("ck_sal_return_request", "LEN([request_key]) > 0 AND LEN([request_fingerprint]) > 0");
                    table.CheckConstraint("ck_sal_return_status", "[status] IN ('Draft','Pending','Rejected','Posted')");
                    table.ForeignKey(
                        name: "fk_sal_return_sal_order",
                        columns: x => new { x.company_id, x.order_id },
                        principalTable: "sal_order",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sal_return_line",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    return_id = table.Column<long>(type: "bigint", nullable: false),
                    line_no = table.Column<int>(type: "int", nullable: false),
                    delivery_id = table.Column<long>(type: "bigint", nullable: false),
                    delivery_line_id = table.Column<long>(type: "bigint", nullable: false),
                    order_line_id = table.Column<long>(type: "bigint", nullable: false),
                    item_id = table.Column<long>(type: "bigint", nullable: false),
                    original_outbound_movement_id = table.Column<long>(type: "bigint", nullable: false),
                    warehouse_id = table.Column<long>(type: "bigint", nullable: false),
                    location_id = table.Column<long>(type: "bigint", nullable: true),
                    quantity = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false),
                    unbilled_quantity = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false),
                    inv_movement_id = table.Column<long>(type: "bigint", nullable: true),
                    original_net = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    original_tax = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    original_gross = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    base_net = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    base_tax = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    base_gross = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sal_return_line", x => x.id);
                    table.UniqueConstraint("ak_sal_return_line_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("ck_sal_return_line_amounts", "[original_net] >= 0 AND [original_tax] >= 0 AND [original_gross] = [original_net] + [original_tax] AND [base_net] >= 0 AND [base_tax] >= 0 AND [base_gross] = [base_net] + [base_tax]");
                    table.CheckConstraint("ck_sal_return_line_no", "[line_no] > 0");
                    table.CheckConstraint("ck_sal_return_line_quantity", "[quantity] > 0 AND [unbilled_quantity] >= 0 AND [unbilled_quantity] <= [quantity]");
                    table.CheckConstraint("ck_sal_return_line_refs", "[delivery_id] > 0 AND [delivery_line_id] > 0 AND [order_line_id] > 0 AND [item_id] > 0 AND [original_outbound_movement_id] > 0 AND [warehouse_id] > 0 AND ([location_id] IS NULL OR [location_id] > 0) AND ([inv_movement_id] IS NULL OR [inv_movement_id] > 0)");
                    table.ForeignKey(
                        name: "fk_sal_return_line_sal_delivery",
                        columns: x => new { x.company_id, x.delivery_id },
                        principalTable: "sal_delivery",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sal_return_line_sal_delivery_line",
                        columns: x => new { x.company_id, x.delivery_line_id },
                        principalTable: "sal_delivery_line",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sal_return_line_sal_order_line",
                        columns: x => new { x.company_id, x.order_line_id },
                        principalTable: "sal_order_line",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sal_return_line_sal_return",
                        columns: x => new { x.company_id, x.return_id },
                        principalTable: "sal_return",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sal_return_invoice_allocation",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    return_line_id = table.Column<long>(type: "bigint", nullable: false),
                    invoice_id = table.Column<long>(type: "bigint", nullable: false),
                    invoice_line_id = table.Column<long>(type: "bigint", nullable: false),
                    quantity = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false),
                    original_net = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    original_tax = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    original_gross = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    base_net = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    base_tax = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    base_gross = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sal_return_invoice_allocation", x => x.id);
                    table.CheckConstraint("ck_sal_return_invoice_allocation_amounts", "[original_net] >= 0 AND [original_tax] >= 0 AND [original_gross] = [original_net] + [original_tax] AND [base_net] >= 0 AND [base_tax] >= 0 AND [base_gross] = [base_net] + [base_tax]");
                    table.CheckConstraint("ck_sal_return_invoice_allocation_quantity", "[quantity] > 0");
                    table.CheckConstraint("ck_sal_return_invoice_allocation_refs", "[invoice_id] > 0 AND [invoice_line_id] > 0");
                    table.ForeignKey(
                        name: "fk_sal_return_invoice_allocation_sal_invoice",
                        columns: x => new { x.company_id, x.invoice_id },
                        principalTable: "sal_invoice",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sal_return_invoice_allocation_sal_invoice_line",
                        columns: x => new { x.company_id, x.invoice_line_id },
                        principalTable: "sal_invoice_line",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sal_return_invoice_allocation_sal_return_line",
                        columns: x => new { x.company_id, x.return_line_id },
                        principalTable: "sal_return_line",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sal_return_company_id_customer_id_business_date_id",
                table: "sal_return",
                columns: new[] { "company_id", "customer_id", "business_date", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_sal_return_company_id_document_no",
                table: "sal_return",
                columns: new[] { "company_id", "document_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sal_return_company_id_fi_credit_note_id",
                table: "sal_return",
                columns: new[] { "company_id", "fi_credit_note_id" },
                unique: true,
                filter: "[fi_credit_note_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_sal_return_company_id_order_id_status",
                table: "sal_return",
                columns: new[] { "company_id", "order_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_sal_return_company_id_request_key",
                table: "sal_return",
                columns: new[] { "company_id", "request_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sal_return_company_id_status_business_date_id",
                table: "sal_return",
                columns: new[] { "company_id", "status", "business_date", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_sal_return_invoice_allocation_company_id_invoice_id",
                table: "sal_return_invoice_allocation",
                columns: new[] { "company_id", "invoice_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sal_return_invoice_allocation_company_id_invoice_line_id_return_line_id",
                table: "sal_return_invoice_allocation",
                columns: new[] { "company_id", "invoice_line_id", "return_line_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sal_return_invoice_allocation_company_id_return_line_id_invoice_line_id",
                table: "sal_return_invoice_allocation",
                columns: new[] { "company_id", "return_line_id", "invoice_line_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sal_return_line_company_id_delivery_id",
                table: "sal_return_line",
                columns: new[] { "company_id", "delivery_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sal_return_line_company_id_delivery_line_id_return_id",
                table: "sal_return_line",
                columns: new[] { "company_id", "delivery_line_id", "return_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sal_return_line_company_id_inv_movement_id",
                table: "sal_return_line",
                columns: new[] { "company_id", "inv_movement_id" },
                unique: true,
                filter: "[inv_movement_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_sal_return_line_company_id_order_line_id_return_id",
                table: "sal_return_line",
                columns: new[] { "company_id", "order_line_id", "return_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sal_return_line_company_id_return_id_line_no",
                table: "sal_return_line",
                columns: new[] { "company_id", "return_id", "line_no" },
                unique: true);

            // ADR-184: header state machine, permanent identity, Draft-only delete and the Posted evidence check.
            migrationBuilder.Sql("""
                CREATE TRIGGER dbo.tr_sal_return_state ON dbo.sal_return
                AFTER UPDATE, DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM deleted WHERE [status] = 'Posted')
                        THROW 51035, 'Posted sales returns are immutable.', 1;
                    IF EXISTS (SELECT 1 FROM deleted d WHERE NOT EXISTS (SELECT 1 FROM inserted i WHERE i.id = d.id)
                               AND d.[status] <> 'Draft')
                        THROW 51035, 'Only a Draft sales return can be deleted.', 1;
                    IF EXISTS (SELECT 1 FROM deleted WHERE is_deleted = 1)
                        THROW 51035, 'A deleted sales return cannot be changed.', 1;
                    IF EXISTS (
                        SELECT 1 FROM inserted i JOIN deleted d ON d.id = i.id
                        WHERE i.company_id <> d.company_id OR i.document_no <> d.document_no
                           OR i.request_key <> d.request_key OR i.request_fingerprint <> d.request_fingerprint
                           OR i.customer_id <> d.customer_id OR i.order_id <> d.order_id
                           OR i.currency_code <> d.currency_code OR i.base_currency_code <> d.base_currency_code
                           OR i.exchange_rate <> d.exchange_rate
                           OR i.created_at <> d.created_at OR i.created_by <> d.created_by)
                        THROW 51035, 'Saved sales return identity cannot be changed.', 1;
                    IF EXISTS (
                        SELECT 1 FROM inserted i JOIN deleted d ON d.id = i.id
                        WHERE NOT ((d.[status] = 'Draft' AND i.[status] IN ('Draft','Pending'))
                                OR (d.[status] = 'Pending' AND i.[status] IN ('Pending','Rejected','Posted'))
                                OR (d.[status] = 'Rejected' AND i.[status] IN ('Rejected','Draft'))))
                        THROW 51035, 'Sales return status transition is not allowed.', 1;
                    IF EXISTS (
                        SELECT 1 FROM inserted i JOIN deleted d ON d.id = i.id
                        WHERE d.[status] IN ('Pending','Rejected')
                          AND (i.business_date <> d.business_date OR ISNULL(i.remark, N'') <> ISNULL(d.remark, N'')
                            OR i.is_deleted <> d.is_deleted
                            OR i.original_net <> d.original_net OR i.original_tax <> d.original_tax
                            OR i.original_gross <> d.original_gross OR i.base_net <> d.base_net
                            OR i.base_tax <> d.base_tax OR i.base_gross <> d.base_gross))
                        THROW 51035, 'A submitted sales return cannot be edited.', 1;
                    IF EXISTS (
                        SELECT 1 FROM inserted i
                        WHERE i.[status] = 'Posted'
                          AND (NOT EXISTS (SELECT 1 FROM dbo.sal_return_line l
                                           WHERE l.company_id = i.company_id AND l.return_id = i.id)
                            OR EXISTS (SELECT 1 FROM dbo.sal_return_line l
                                       WHERE l.company_id = i.company_id AND l.return_id = i.id
                                         AND (l.inv_movement_id IS NULL
                                           OR l.quantity <> l.unbilled_quantity + ISNULL((
                                                SELECT SUM(a.quantity) FROM dbo.sal_return_invoice_allocation a
                                                WHERE a.company_id = l.company_id AND a.return_line_id = l.id), 0)))
                            OR (i.fi_credit_note_id IS NULL AND EXISTS (
                                    SELECT 1 FROM dbo.sal_return_invoice_allocation a
                                    JOIN dbo.sal_return_line l ON l.id = a.return_line_id AND l.company_id = a.company_id
                                    WHERE l.company_id = i.company_id AND l.return_id = i.id))
                            OR (i.fi_credit_note_id IS NOT NULL AND NOT EXISTS (
                                    SELECT 1 FROM dbo.sal_return_invoice_allocation a
                                    JOIN dbo.sal_return_line l ON l.id = a.return_line_id AND l.company_id = a.company_id
                                    WHERE l.company_id = i.company_id AND l.return_id = i.id))
                            OR EXISTS (SELECT 1 FROM dbo.sal_return_line l
                                       WHERE l.company_id = i.company_id AND l.return_id = i.id
                                       HAVING SUM(l.original_net) <> i.original_net OR SUM(l.original_tax) <> i.original_tax
                                           OR SUM(l.base_net) <> i.base_net OR SUM(l.base_tax) <> i.base_tax)))
                        THROW 51035, 'A Posted sales return needs every inbound movement, complete billed/unbilled portions, matching FI credit and line totals.', 1;
                END
                """);

            // Lines: free while the header is Draft; under Pending only the posting step may stamp the INV inbound
            // movement once (NULL → value); frozen under Rejected and Posted.
            migrationBuilder.Sql("""
                CREATE TRIGGER dbo.tr_sal_return_line_state ON dbo.sal_return_line
                AFTER INSERT, UPDATE, DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.id = i.id
                               WHERE i.company_id <> d.company_id OR i.return_id <> d.return_id)
                        THROW 51036, 'A sales return line cannot move to another return.', 1;
                    IF EXISTS (
                        SELECT 1
                        FROM (SELECT company_id, return_id FROM inserted
                              UNION ALL SELECT company_id, return_id FROM deleted) x
                        JOIN dbo.sal_return h ON h.id = x.return_id AND h.company_id = x.company_id
                        WHERE h.[status] IN ('Rejected','Posted'))
                        THROW 51036, 'Lines of a Rejected or Posted sales return cannot be changed.', 1;
                    IF EXISTS (
                        SELECT 1 FROM inserted i
                        JOIN dbo.sal_return h ON h.id = i.return_id AND h.company_id = i.company_id AND h.[status] = 'Pending'
                        LEFT JOIN deleted d ON d.id = i.id
                        WHERE d.id IS NULL OR d.inv_movement_id IS NOT NULL OR i.inv_movement_id IS NULL
                           OR i.line_no <> d.line_no OR i.delivery_id <> d.delivery_id
                           OR i.delivery_line_id <> d.delivery_line_id OR i.order_line_id <> d.order_line_id
                           OR i.item_id <> d.item_id OR i.original_outbound_movement_id <> d.original_outbound_movement_id
                           OR i.warehouse_id <> d.warehouse_id OR ISNULL(i.location_id, 0) <> ISNULL(d.location_id, 0)
                           OR i.quantity <> d.quantity OR i.unbilled_quantity <> d.unbilled_quantity
                           OR i.original_net <> d.original_net OR i.original_tax <> d.original_tax
                           OR i.original_gross <> d.original_gross OR i.base_net <> d.base_net
                           OR i.base_tax <> d.base_tax OR i.base_gross <> d.base_gross)
                        OR EXISTS (
                        SELECT 1 FROM deleted d
                        JOIN dbo.sal_return h ON h.id = d.return_id AND h.company_id = d.company_id AND h.[status] = 'Pending'
                        WHERE NOT EXISTS (SELECT 1 FROM inserted i WHERE i.id = d.id))
                        THROW 51036, 'A Pending sales return line only accepts its INV inbound movement once.', 1;
                END
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER dbo.tr_sal_return_invoice_allocation_state ON dbo.sal_return_invoice_allocation
                AFTER INSERT, UPDATE, DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (
                        SELECT 1
                        FROM (SELECT company_id, return_line_id FROM inserted
                              UNION ALL SELECT company_id, return_line_id FROM deleted) x
                        JOIN dbo.sal_return_line l ON l.id = x.return_line_id AND l.company_id = x.company_id
                        JOIN dbo.sal_return h ON h.id = l.return_id AND h.company_id = l.company_id
                        WHERE h.[status] <> 'Draft')
                        THROW 51037, 'Billed portions can only change while the sales return is Draft.', 1;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER dbo.tr_sal_return_invoice_allocation_state;");
            migrationBuilder.Sql("DROP TRIGGER dbo.tr_sal_return_line_state;");
            migrationBuilder.Sql("DROP TRIGGER dbo.tr_sal_return_state;");

            migrationBuilder.DropTable(
                name: "sal_return_invoice_allocation");

            migrationBuilder.DropTable(
                name: "sal_return_line");

            migrationBuilder.DropTable(
                name: "sal_return");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_sal_invoice_line_company_id_id",
                table: "sal_invoice_line");
        }
    }
}
