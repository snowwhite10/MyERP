using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Modules.Purchasing.Infrastructure;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.IntegrationTests.Database;

public sealed class RfqSchemaTests(TestDatabase database)
{
    [Fact]
    public async Task RfqSourceLineMustBelongToSameCompany()
    {
        var sourceCompanyId = TestCompanies.Next();
        var targetCompanyId = TestCompanies.Next();
        var sourceCompany = new FixedCompany(sourceCompanyId);
        var stamp = new AuditStamper(new FixedClock(new DateTime(2026, 10, 21)), new FixedUser(7), sourceCompany);
        await using var sourceDb = new MyErpDbContext(new DbContextOptionsBuilder<MyErpDbContext>()
            .UseMyErpSqlServer(database.ConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(stamp)).Options,
            [new RequisitionDbModel(), new RfqDbModel()], sourceCompany);
        var requisition = new Requisition
        {
            CompanyId = sourceCompanyId, DocumentNo = $"REQ-RFQ-{sourceCompanyId}",
            BusinessDate = new DateOnly(2028, 2, 29), RequesterId = 7,
        };
        requisition.Lines.Add(new RequisitionLine
        {
            CompanyId = sourceCompanyId, LineNo = 1, ItemId = 11,
            RequiredDate = new DateOnly(2028, 3, 2), Quantity = 1m,
        });
        sourceDb.Add(requisition);
        await sourceDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        var sourceLineId = requisition.Lines.Single().Id;

        var targetCompany = new FixedCompany(targetCompanyId);
        var targetStamp = new AuditStamper(new FixedClock(new DateTime(2026, 10, 21)), new FixedUser(7), targetCompany);
        await using var targetDb = new MyErpDbContext(new DbContextOptionsBuilder<MyErpDbContext>()
            .UseMyErpSqlServer(database.ConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(targetStamp)).Options,
            [new RequisitionDbModel(), new RfqDbModel()], targetCompany);
        var rfq = new Rfq
        {
            CompanyId = targetCompanyId, DocumentNo = $"RFQ-{targetCompanyId}",
            BusinessDate = new DateOnly(2028, 2, 29), ReplyDeadline = new DateOnly(2028, 3, 1), OwnerId = 7,
        };
        targetDb.Add(rfq);
        await targetDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        await Assert.ThrowsAnyAsync<Exception>(() => targetDb.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO pur_rfq_line
                (company_id, rfq_id, line_no, item_id, requisition_line_id, required_date,
                 quantity, created_at, created_by, version)
            VALUES ({targetCompanyId}, {rfq.Id}, {1}, {11L}, {sourceLineId},
                    {new DateTime(2028, 3, 2)}, {1m}, {new DateTime(2026, 10, 21)}, {7L}, {1})",
            TestContext.Current.CancellationToken));
        Assert.Empty(await targetDb.Set<RfqLine>().Where(x => x.RfqId == rfq.Id)
            .ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RfqSchemaPreservesCompanyBoundaryAndSixDecimalQuantity()
    {
        var companyId = TestCompanies.Next();
        var company = new FixedCompany(companyId);
        var stamp = new AuditStamper(new FixedClock(new DateTime(2026, 10, 21)), new FixedUser(7), company);
        await using var db = new MyErpDbContext(new DbContextOptionsBuilder<MyErpDbContext>()
            .UseMyErpSqlServer(database.ConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(stamp)).Options,
            [new RfqDbModel()], company);
        var rfq = new Rfq
        {
            CompanyId = companyId, DocumentNo = $"RFQ-{companyId}",
            BusinessDate = new DateOnly(2028, 2, 29), ReplyDeadline = new DateOnly(2028, 3, 1), OwnerId = 7,
        };
        rfq.Lines.Add(new RfqLine
        {
            CompanyId = companyId, LineNo = 1, ItemId = 11,
            RequiredDate = new DateOnly(2028, 3, 2), Quantity = 1.123456m,
        });
        rfq.Invitations.Add(new RfqInvitation { CompanyId = companyId, SupplierId = 21 });
        db.Add(rfq);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1.123456m, (await db.Set<Rfq>().Include(x => x.Lines)
            .SingleAsync(x => x.Id == rfq.Id, TestContext.Current.CancellationToken)).Lines.Single().Quantity);

        await using var other = new MyErpDbContext(new DbContextOptionsBuilder<MyErpDbContext>()
            .UseMyErpSqlServer(database.ConnectionString).Options,
            [new RfqDbModel()], new FixedCompany(TestCompanies.Next()));
        Assert.False(await other.Set<Rfq>().AnyAsync(x => x.Id == rfq.Id, TestContext.Current.CancellationToken));

        var token = TestContext.Current.CancellationToken;
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO pur_rfq_line
                (company_id, rfq_id, line_no, item_id, required_date, quantity, created_at, created_by, version)
            VALUES ({companyId + 1}, {rfq.Id}, {2}, {11L}, {new DateTime(2028, 3, 2)}, {1m},
                    {new DateTime(2026, 10, 21)}, {7L}, {1})", token));
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO pur_rfq_line
                (company_id, rfq_id, line_no, item_id, required_date, quantity, created_at, created_by, version)
            VALUES ({companyId}, {rfq.Id}, {2}, {11L}, {new DateTime(2028, 3, 2)}, {-1m},
                    {new DateTime(2026, 10, 21)}, {7L}, {1})", token));
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO pur_rfq_invitation
                (company_id, rfq_id, supplier_id, created_at, created_by, version)
            VALUES ({companyId}, {rfq.Id}, {21L}, {new DateTime(2026, 10, 21)}, {7L}, {1})", token));
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO pur_rfq
                (company_id, document_no, business_date, reply_deadline, owner_id, status,
                 is_deleted, deleted_at, created_at, created_by, version)
            VALUES ({companyId}, {rfq.DocumentNo}, {new DateTime(2028, 2, 29)}, {new DateTime(2028, 3, 1)},
                    {7L}, {"Draft"}, {true}, {new DateTime(2026, 10, 21)},
                    {new DateTime(2026, 10, 21)}, {7L}, {1})", token));
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO pur_rfq
                (company_id, document_no, business_date, reply_deadline, owner_id, status,
                 is_deleted, created_at, created_by, version)
            VALUES ({companyId}, {$"RFQ-BAD-{companyId}"}, {new DateTime(2028, 3, 1)},
                    {new DateTime(2028, 2, 29)}, {7L}, {"Draft"}, {false},
                    {new DateTime(2026, 10, 21)}, {7L}, {1})", token));
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO pur_rfq_invitation
                (company_id, rfq_id, supplier_id, supplier_code_snapshot, created_at, created_by, version)
            VALUES ({companyId}, {rfq.Id}, {22L}, {"SUP22"}, {new DateTime(2026, 10, 21)}, {7L}, {1})", token));
        Assert.Equal(1, await db.Set<RfqLine>().CountAsync(x => x.RfqId == rfq.Id, token));
    }
}
