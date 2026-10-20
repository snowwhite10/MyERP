using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Modules.Purchasing.Infrastructure;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.IntegrationTests.Database;

public sealed class QuotationSchemaTests(TestDatabase database)
{
    [Fact]
    public async Task QuotationsKeepImmutableRevisionsAndCompanyBoundaries()
    {
        var companyId = TestCompanies.Next();
        var company = new FixedCompany(companyId);
        var stamp = new AuditStamper(new FixedClock(new DateTime(2026, 10, 21)), new FixedUser(7), company);
        await using var db = new MyErpDbContext(new DbContextOptionsBuilder<MyErpDbContext>()
            .UseMyErpSqlServer(database.ConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(stamp)).Options,
            [new RfqDbModel(), new QuotationDbModel()], company);
        var rfq = new Rfq
        {
            CompanyId = companyId, DocumentNo = $"RFQ-Q-{companyId}",
            BusinessDate = new DateOnly(2028, 2, 29), ReplyDeadline = new DateOnly(2028, 3, 1), OwnerId = 7,
        };
        rfq.Lines.Add(new RfqLine { CompanyId = companyId, LineNo = 1, ItemId = 11,
            RequiredDate = new DateOnly(2028, 3, 2), Quantity = 1.123456m });
        rfq.Invitations.Add(new RfqInvitation { CompanyId = companyId, SupplierId = 21 });
        db.Add(rfq);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var quotation = new SupplierQuotation { CompanyId = companyId, RfqId = rfq.Id,
            SupplierId = 21, CurrentRevisionNo = 2 };
        quotation.Revisions.Add(new QuotationRevision { CompanyId = companyId, RevisionNo = 1,
            QuoteDate = new DateOnly(2028, 2, 29), ValidUntil = new DateOnly(2028, 3, 31),
            Lines = { new QuotationLine { CompanyId = companyId, RfqLineId = rfq.Lines.Single().Id,
                UnitPrice = 0m, CurrencyCode = "TWD", TaxRateId = 31 } } });
        quotation.Revisions.Add(new QuotationRevision { CompanyId = companyId, RevisionNo = 2,
            QuoteDate = new DateOnly(2028, 3, 1), ValidUntil = new DateOnly(2028, 4, 30),
            Lines = { new QuotationLine { CompanyId = companyId, RfqLineId = rfq.Lines.Single().Id,
                UnitPrice = 1.123456m, CurrencyCode = "USD", TaxRateId = 31 } } });
        db.Add(quotation);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var saved = await db.Set<SupplierQuotation>().Include(x => x.Revisions).ThenInclude(x => x.Lines)
            .SingleAsync(x => x.Id == quotation.Id, TestContext.Current.CancellationToken);
        Assert.Equal([1, 2], saved.Revisions.Select(x => x.RevisionNo).Order());
        Assert.Equal(1.123456m, saved.Revisions.Single(x => x.RevisionNo == 2).Lines.Single().UnitPrice);

        await using var other = new MyErpDbContext(new DbContextOptionsBuilder<MyErpDbContext>()
            .UseMyErpSqlServer(database.ConnectionString).Options,
            [new RfqDbModel(), new QuotationDbModel()], new FixedCompany(TestCompanies.Next()));
        Assert.False(await other.Set<SupplierQuotation>().AnyAsync(x => x.Id == quotation.Id,
            TestContext.Current.CancellationToken));

        var token = TestContext.Current.CancellationToken;
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO pur_supplier_quotation
                (company_id, rfq_id, supplier_id, current_revision_no, status, created_at, created_by, version)
            VALUES ({companyId}, {rfq.Id}, {21L}, {1}, {"Active"}, {new DateTime(2026, 10, 21)}, {7L}, {1})", token));
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO pur_supplier_quotation
                (company_id, rfq_id, supplier_id, current_revision_no, status, created_at, created_by, version)
            VALUES ({companyId + 1}, {rfq.Id}, {21L}, {1}, {"Active"}, {new DateTime(2026, 10, 21)}, {7L}, {1})", token));
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO pur_quotation_revision
                (company_id, quotation_id, revision_no, quote_date, valid_until, created_at, created_by, version)
            VALUES ({companyId}, {quotation.Id}, {2}, {new DateTime(2028, 3, 1)},
                {new DateTime(2028, 4, 30)}, {new DateTime(2026, 10, 21)}, {7L}, {1})", token));
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO pur_quotation_line
                (company_id, revision_id, rfq_line_id, unit_price, currency_code, tax_rate_id,
                 created_at, created_by, version)
            VALUES ({companyId}, {saved.Revisions.Single(x => x.RevisionNo == 2).Id},
                {rfq.Lines.Single().Id}, {-1m}, {"USD"}, {31L}, {new DateTime(2026, 10, 21)}, {7L}, {1})", token));
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE pur_quotation_line SET unit_price = {9m}
            WHERE id = {saved.Revisions.Single(x => x.RevisionNo == 2).Lines.Single().Id}", token));
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($@"
            DELETE FROM pur_quotation_revision WHERE id = {saved.Revisions.Single(x => x.RevisionNo == 1).Id}", token));
    }
}
