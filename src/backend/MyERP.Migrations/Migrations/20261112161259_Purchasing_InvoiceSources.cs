using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Migrations.Migrations;

[DbContext(typeof(MyErpDbContext))]
[Migration("20261112161259_Purchasing_InvoiceSources")]
public sealed class Purchasing_InvoiceSources : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE dbo.pur_purchase_order (
                id bigint IDENTITY(1,1) NOT NULL CONSTRAINT pk_pur_purchase_order PRIMARY KEY,
                company_id bigint NOT NULL, supplier_id bigint NOT NULL,
                currency char(3) NOT NULL, status varchar(30) NOT NULL,
                is_deleted bit NOT NULL, deleted_at datetime2(6) NULL,
                created_at datetime2(6) NOT NULL, created_by bigint NOT NULL,
                updated_at datetime2(6) NULL, updated_by bigint NULL, version int NOT NULL,
                CONSTRAINT ak_pur_purchase_order_company_id_id UNIQUE (company_id,id),
                CONSTRAINT ck_pur_order_supplier CHECK (supplier_id > 0),
                CONSTRAINT ck_pur_order_status CHECK (status IN ('Draft','PendingApproval','Approved','Rejected','Cancelled'))
            );
            CREATE INDEX ix_pur_purchase_order_company_id_supplier_id_status_id
                ON dbo.pur_purchase_order(company_id,supplier_id,status,id);

            CREATE TABLE dbo.pur_purchase_order_line (
                id bigint IDENTITY(1,1) NOT NULL CONSTRAINT pk_pur_purchase_order_line PRIMARY KEY,
                company_id bigint NOT NULL, purchase_order_id bigint NOT NULL,
                line_no int NOT NULL, material_id bigint NOT NULL,
                quantity decimal(19,6) NOT NULL, unit_price decimal(19,6) NOT NULL,
                tax_rate_percent decimal(9,4) NOT NULL,
                created_at datetime2(6) NOT NULL, created_by bigint NOT NULL,
                updated_at datetime2(6) NULL, updated_by bigint NULL, version int NOT NULL,
                CONSTRAINT ak_pur_purchase_order_line_company_id_id UNIQUE (company_id,id),
                CONSTRAINT fk_pur_order_line_order FOREIGN KEY(company_id,purchase_order_id)
                    REFERENCES dbo.pur_purchase_order(company_id,id),
                CONSTRAINT ck_pur_order_line_refs CHECK (purchase_order_id > 0 AND line_no > 0 AND material_id > 0),
                CONSTRAINT ck_pur_order_line_values CHECK (quantity > 0 AND unit_price >= 0 AND tax_rate_percent BETWEEN 0 AND 100)
            );
            CREATE UNIQUE INDEX ux_pur_purchase_order_line_company_id_purchase_order_id_line_no
                ON dbo.pur_purchase_order_line(company_id,purchase_order_id,line_no);
            CREATE INDEX ix_pur_purchase_order_line_company_id_material_id
                ON dbo.pur_purchase_order_line(company_id,material_id);

            CREATE TABLE dbo.pur_purchase_receipt (
                id bigint IDENTITY(1,1) NOT NULL CONSTRAINT pk_pur_purchase_receipt PRIMARY KEY,
                company_id bigint NOT NULL, purchase_order_id bigint NOT NULL,
                supplier_id bigint NOT NULL, currency char(3) NOT NULL,
                status varchar(30) NOT NULL, is_deleted bit NOT NULL,
                deleted_at datetime2(6) NULL,
                created_at datetime2(6) NOT NULL, created_by bigint NOT NULL,
                updated_at datetime2(6) NULL, updated_by bigint NULL, version int NOT NULL,
                CONSTRAINT ak_pur_purchase_receipt_company_id_id UNIQUE (company_id,id),
                CONSTRAINT fk_pur_receipt_order FOREIGN KEY(company_id,purchase_order_id)
                    REFERENCES dbo.pur_purchase_order(company_id,id),
                CONSTRAINT ck_pur_receipt_refs CHECK (purchase_order_id > 0 AND supplier_id > 0),
                CONSTRAINT ck_pur_receipt_status CHECK (status IN ('Draft','PendingApproval','Rejected','Posted'))
            );
            CREATE INDEX ix_pur_purchase_receipt_company_id_purchase_order_id_status_id
                ON dbo.pur_purchase_receipt(company_id,purchase_order_id,status,id);

            CREATE TABLE dbo.pur_purchase_receipt_line (
                id bigint IDENTITY(1,1) NOT NULL CONSTRAINT pk_pur_purchase_receipt_line PRIMARY KEY,
                company_id bigint NOT NULL, purchase_receipt_id bigint NOT NULL,
                purchase_order_line_id bigint NOT NULL, line_no int NOT NULL,
                material_id bigint NOT NULL, posted_received decimal(19,6) NOT NULL,
                posted_returned decimal(19,6) NOT NULL,
                pending_return_reserved decimal(19,6) NOT NULL,
                posted_invoiced decimal(19,6) NOT NULL,
                matched_reserved decimal(19,6) NOT NULL,
                created_at datetime2(6) NOT NULL, created_by bigint NOT NULL,
                updated_at datetime2(6) NULL, updated_by bigint NULL, version int NOT NULL,
                CONSTRAINT fk_pur_receipt_line_receipt FOREIGN KEY(company_id,purchase_receipt_id)
                    REFERENCES dbo.pur_purchase_receipt(company_id,id),
                CONSTRAINT fk_pur_receipt_line_order_line FOREIGN KEY(company_id,purchase_order_line_id)
                    REFERENCES dbo.pur_purchase_order_line(company_id,id),
                CONSTRAINT ck_pur_receipt_line_refs CHECK (purchase_receipt_id > 0 AND purchase_order_line_id > 0 AND line_no > 0 AND material_id > 0),
                CONSTRAINT ck_pur_receipt_line_quantities CHECK (posted_received > 0 AND posted_returned >= 0
                    AND pending_return_reserved >= 0 AND posted_invoiced >= 0 AND matched_reserved >= 0
                    AND posted_returned+pending_return_reserved+posted_invoiced+matched_reserved <= posted_received)
            );
            CREATE UNIQUE INDEX ux_pur_purchase_receipt_line_company_id_purchase_receipt_id_line_no
                ON dbo.pur_purchase_receipt_line(company_id,purchase_receipt_id,line_no);
            CREATE INDEX ix_pur_purchase_receipt_line_company_id_purchase_order_line_id
                ON dbo.pur_purchase_receipt_line(company_id,purchase_order_line_id);
            CREATE INDEX ix_pur_purchase_receipt_line_company_id_material_id
                ON dbo.pur_purchase_receipt_line(company_id,material_id);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TABLE dbo.pur_purchase_receipt_line;
            DROP TABLE dbo.pur_purchase_receipt;
            DROP TABLE dbo.pur_purchase_order_line;
            DROP TABLE dbo.pur_purchase_order;
            """);
    }
}
