using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Sales.Application;
using MyERP.Modules.Sales.Domain;
using MyERP.Modules.Sales.Infrastructure;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.IntegrationTests.Database;

public sealed class SalesOrderSchemaTests(TestDatabase database)
{
    private MyErpDbContext Context(long companyId)
    {
        var company = new FixedCompany(companyId);
        return new MyErpDbContext(new DbContextOptionsBuilder<MyErpDbContext>()
                .UseMyErpSqlServer(database.ConnectionString)
                .AddInterceptors(new AuditSaveChangesInterceptor(new AuditStamper(
                    new FixedClock(new DateTime(2026, 10, 27)), new FixedUser(7), company))).Options,
            [new SalesOrderDbModel()], company);
    }

    [Fact]
    public async Task OrderTablesEnforceCompanySourceAndSavedQuantity()
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sys.tables WHERE name IN ('sal_order', 'sal_order_line')";
        Assert.Equal(2, (int)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!);

        command.CommandText = "SELECT COUNT(*) FROM sys.check_constraints WHERE name IN ('ck_sal_order_status', 'ck_sal_order_line_quantity')";
        Assert.Equal(2, (int)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!);

        command.CommandText = "SELECT COUNT(*) FROM sys.indexes WHERE name IN ('ix_sal_order_company_id_source_quotation_id', 'ix_sal_order_line_company_id_source_quotation_line_id') AND is_unique = 1";
        Assert.Equal(2, (int)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!);

        command.CommandText = "SELECT COUNT(*) FROM sys.foreign_keys WHERE name = 'fk_sal_order_line_sal_order_company_id_order_id'";
        Assert.Equal(1, (int)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!);
    }

    [Fact]
    public async Task SourceLinkIsPermanentAndInvalidQuantityRollsBackWholeOrder()
    {
        var companyId = TestCompanies.Next();
        await using (var db = Context(companyId))
        {
            var order = NewOrder(companyId, $"O{companyId}", 901);
            order.Lines.Add(NewLine(companyId, 902, 1m));
            db.Add(order);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            Assert.Equal(SalesOrderStatus.Draft, order.Status);
            Assert.Single(order.Lines);
        }

        await using (var duplicate = Context(companyId))
        {
            var order = NewOrder(companyId, $"O{companyId}B", 901);
            order.Lines.Add(NewLine(companyId, 903, 1m));
            duplicate.Add(order);
            await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync(TestContext.Current.CancellationToken));
        }

        await using (var invalid = Context(companyId))
        {
            var order = NewOrder(companyId, $"O{companyId}C", null);
            order.Lines.Add(NewLine(companyId, null, 0m));
            invalid.Add(order);
            await Assert.ThrowsAsync<DbUpdateException>(() => invalid.SaveChangesAsync(TestContext.Current.CancellationToken));
        }

        await using var verify = Context(companyId);
        Assert.Equal(1, await verify.Set<SalesOrder>().CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await verify.Set<SalesOrderLine>().CountAsync(TestContext.Current.CancellationToken));
    }

    private static SalesOrder NewOrder(long companyId, string number, long? quotationId) => new()
    {
        CompanyId = companyId, DocumentNo = number, CustomerId = 501,
        CustomerCode = "C501", CustomerName = "Customer", BusinessDate = new DateOnly(2026, 10, 27),
        CurrencyCode = "TWD", BaseCurrencyCode = "TWD", ExchangeRate = 1m,
        SourceQuotationId = quotationId
    };

    private static SalesOrderLine NewLine(long companyId, long? sourceId, decimal quantity) => new()
    {
        CompanyId = companyId, LineNo = 1, ItemId = 601, ItemCode = "I601", ItemName = "Item",
        SourceQuotationLineId = sourceId, Quantity = quantity, UnitPrice = 1m,
        TaxRateId = 701, TaxRatePercent = 0m
    };
}
