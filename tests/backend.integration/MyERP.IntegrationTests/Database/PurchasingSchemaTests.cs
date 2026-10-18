using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Modules.Purchasing.Infrastructure;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.IntegrationTests.Database;

public sealed class PurchasingSchemaTests(TestDatabase database)
{
    [Fact]
    public async Task RequisitionSchemaEnforcesCompanyLineAndQuantityRules()
    {
        var companyId = TestCompanies.Next();
        var company = new FixedCompany(companyId);
        var stamper = new AuditStamper(new FixedClock(new DateTime(2026, 10, 19)), new FixedUser(7), company);
        await using var db = new MyErpDbContext(new DbContextOptionsBuilder<MyErpDbContext>()
                .UseMyErpSqlServer(database.ConnectionString)
                .AddInterceptors(new AuditSaveChangesInterceptor(stamper)).Options,
            [new RequisitionDbModel()], company);
        var document = new Requisition
        {
            CompanyId = companyId, DocumentNo = $"PUR-{companyId}", BusinessDate = new DateOnly(2028, 2, 29),
            RequesterId = 7,
        };
        document.Lines.Add(new RequisitionLine
        {
            CompanyId = companyId, LineNo = 1, ItemId = 11, RequiredDate = new DateOnly(2028, 3, 1),
            Quantity = 1.123456m,
        });
        db.Add(document);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        Assert.True(document.Id > 0);
        Assert.Equal(1.123456m, (await db.Set<Requisition>().Include(x => x.Lines)
            .SingleAsync(x => x.Id == document.Id, TestContext.Current.CancellationToken)).Lines.Single().Quantity);

        await using var otherCompany = new MyErpDbContext(new DbContextOptionsBuilder<MyErpDbContext>()
            .UseMyErpSqlServer(database.ConnectionString).Options, [new RequisitionDbModel()],
            new FixedCompany(TestCompanies.Next()));
        Assert.False(await otherCompany.Set<Requisition>().AnyAsync(x => x.Id == document.Id, TestContext.Current.CancellationToken));

        var cancellationToken = TestContext.Current.CancellationToken;
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO pur_requisition_line
                (company_id, requisition_id, line_no, item_id, required_date, quantity, ordered_quantity,
                 created_at, created_by, version)
            VALUES ({companyId}, {document.Id}, {2}, {11L}, {new DateTime(2028, 3, 1)}, {-1m}, {0m},
                    {new DateTime(2026, 10, 19)}, {7L}, {1})", cancellationToken));
        Assert.Equal(1, await db.Set<RequisitionLine>().CountAsync(x => x.RequisitionId == document.Id, cancellationToken));
    }
}
