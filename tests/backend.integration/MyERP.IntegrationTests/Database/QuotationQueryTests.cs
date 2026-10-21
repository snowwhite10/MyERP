using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Purchasing;
using MyERP.Modules.Purchasing.Application;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Modules.Purchasing.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;

namespace MyERP.IntegrationTests.Database;

public sealed class QuotationQueryTests(TestDatabase database)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ListPagesMultipleSuppliersInStableOrderAndFiltersCurrentRevision()
    {
        var companyId = TestCompanies.Next();
        var company = new FixedCompany(companyId);
        var stamp = new AuditStamper(new FixedClock(new DateTime(2026, 10, 21)), new FixedUser(7), company);
        await using var db = new MyErpDbContext(new DbContextOptionsBuilder<MyErpDbContext>()
            .UseMyErpSqlServer(database.ConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(stamp)).Options,
            [new RfqDbModel(), new QuotationDbModel()], company);
        var rfq = new Rfq { CompanyId = companyId, DocumentNo = $"Q-PAGE-{companyId}",
            BusinessDate = new DateOnly(2028, 2, 28), ReplyDeadline = new DateOnly(2028, 3, 1),
            OwnerId = 7 };
        rfq.Lines.Add(new RfqLine { CompanyId = companyId, LineNo = 1, ItemId = 11,
            RequiredDate = new DateOnly(2028, 3, 2), Quantity = 1m });
        foreach (var supplierId in new[] { 103L, 101L, 102L })
            rfq.Invitations.Add(new RfqInvitation { CompanyId = companyId, SupplierId = supplierId });
        db.Add(rfq);
        await db.SaveChangesAsync(Ct);
        foreach (var (supplierId, date) in new[] { (103L, new DateOnly(2028, 2, 28)),
            (101L, new DateOnly(2028, 2, 29)), (102L, new DateOnly(2028, 3, 1)) })
        {
            var quotation = new SupplierQuotation { CompanyId = companyId, RfqId = rfq.Id,
                SupplierId = supplierId, CurrentRevisionNo = 1, Status = SupplierQuotationStatus.Active };
            quotation.Revisions.Add(new QuotationRevision { CompanyId = companyId,
                RevisionNo = 1, QuoteDate = date, ValidUntil = new DateOnly(2028, 3, 5),
                Lines = { new QuotationLine { CompanyId = companyId, RfqLineId = rfq.Lines.Single().Id,
                    UnitPrice = 1m, CurrencyCode = "TWD", TaxRateId = 31 } } });
            db.Add(quotation);
        }
        await db.SaveChangesAsync(Ct);
        var service = Service(db, company, companyId, DataScope.Self);
        var first = await service.ListAsync(rfq.Id, new QuotationQuery { PageSize = 2 }, Ct);
        var second = await service.ListAsync(rfq.Id, new QuotationQuery { Page = 2, PageSize = 2 }, Ct);
        Assert.Equal(3, first.TotalCount);
        Assert.Equal([101L, 102L], first.Items.Select(x => x.SupplierId));
        Assert.Equal([103L], second.Items.Select(x => x.SupplierId));
        var newest = await service.ListAsync(rfq.Id,
            new QuotationQuery { Sort = "-quoteDate", PageSize = 2 }, Ct);
        Assert.Equal([102L, 101L], newest.Items.Select(x => x.SupplierId));
        var filtered = await service.ListAsync(rfq.Id,
            new QuotationQuery { SupplierId = 103, ValidOn = new DateOnly(2028, 3, 1) }, Ct);
        Assert.Equal(103, Assert.Single(filtered.Items).SupplierId);
    }

    [Fact]
    public async Task ListAndDetailApplyScopeBeforePagingAndKeepWithdrawnHistory()
    {
        var companyId = TestCompanies.Next();
        var company = new FixedCompany(companyId);
        var stamp = new AuditStamper(new FixedClock(new DateTime(2026, 10, 21)), new FixedUser(7), company);
        await using var db = new MyErpDbContext(new DbContextOptionsBuilder<MyErpDbContext>()
            .UseMyErpSqlServer(database.ConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(stamp)).Options,
            [new RfqDbModel(), new QuotationDbModel()], company);
        var rfqs = new List<Rfq>();
        foreach (var (owner, branch, department) in new[] { (7L, 10L, 20L), (8L, 10L, 20L),
            (9L, 10L, 21L), (10L, 11L, 22L) })
        {
            var rfq = new Rfq { CompanyId = companyId, DocumentNo = $"Q-{companyId}-{owner}",
                BusinessDate = new DateOnly(2028, 2, 29), ReplyDeadline = new DateOnly(2028, 3, 1),
                OwnerId = owner, BranchId = branch, DepartmentId = department,
                Status = RfqStatus.Sent, SentAt = new DateTime(2026, 10, 21) };
            rfq.Lines.Add(new RfqLine { CompanyId = companyId, LineNo = 1, ItemId = 11,
                RequiredDate = new DateOnly(2028, 3, 2), Quantity = 1m });
            rfq.Invitations.Add(new RfqInvitation { CompanyId = companyId, SupplierId = owner + 100 });
            rfqs.Add(rfq);
        }
        db.AddRange(rfqs);
        await db.SaveChangesAsync(Ct);
        var quotations = rfqs.Select((rfq, index) => new SupplierQuotation
        {
            CompanyId = companyId, RfqId = rfq.Id, SupplierId = rfq.Invitations.Single().SupplierId,
            CurrentRevisionNo = index == 0 ? 2 : 1,
            Status = index == 0 ? SupplierQuotationStatus.Withdrawn : SupplierQuotationStatus.Active
        }).ToList();
        foreach (var (quotation, index) in quotations.Select((value, index) => (value, index)))
        {
            var lineId = rfqs[index].Lines.Single().Id;
            for (var revisionNo = 1; revisionNo <= quotation.CurrentRevisionNo; revisionNo++)
                quotation.Revisions.Add(new QuotationRevision { CompanyId = companyId,
                    RevisionNo = revisionNo, QuoteDate = new DateOnly(2028, 2, 29),
                    ValidUntil = new DateOnly(2028, 3, revisionNo),
                    Lines = { new QuotationLine { CompanyId = companyId, RfqLineId = lineId,
                        UnitPrice = revisionNo == 1 ? 0m : 1.123456m,
                        CurrencyCode = revisionNo == 1 ? "TWD" : "USD", TaxRateId = 31 } } });
        }
        db.AddRange(quotations);
        await db.SaveChangesAsync(Ct);
        foreach (var (scope, count) in new[] { (DataScope.Self, 1L), (DataScope.Department, 2L),
            (DataScope.Branch, 3L), (DataScope.Company, 4L) })
        {
            var service = Service(db, company, companyId, scope);
            var page = await service.ListAsync(rfqs[0].Id, new QuotationQuery { PageSize = 1 }, Ct);
            Assert.Equal(1, page.TotalCount);
            Assert.Equal(quotations[0].Id, Assert.Single(page.Items).Id);
            for (var index = 0; index < rfqs.Count; index++)
            {
                if (index < count)
                    Assert.Equal(1, (await service.ListAsync(rfqs[index].Id,
                        new QuotationQuery { PageSize = 1 }, Ct)).TotalCount);
                else
                    await Assert.ThrowsAsync<NotFoundException>(() => service.ListAsync(rfqs[index].Id,
                        new QuotationQuery { PageSize = 1 }, Ct));
            }
            if (scope == DataScope.Self)
            {
                var detail = await service.GetAsync(rfqs[0].Id, quotations[0].Id, Ct);
                Assert.Equal(SupplierQuotationStatus.Withdrawn, detail.Status);
                Assert.Equal([1, 2], detail.Revisions.Select(x => x.RevisionNo));
                Assert.Equal(1.123456m, detail.Revisions[1].Lines.Single().UnitPrice);
                Assert.Equal("USD", detail.Revisions[1].Lines.Single().CurrencyCode);
                await Assert.ThrowsAsync<NotFoundException>(() => service.ListAsync(rfqs[1].Id,
                    new QuotationQuery(), Ct));
            }
        }
        var all = Service(db, company, companyId, DataScope.Company);
        Assert.Empty((await all.ListAsync(rfqs[0].Id,
            new QuotationQuery { Status = SupplierQuotationStatus.Active }, Ct)).Items);
        Assert.Equal(quotations[0].Id, Assert.Single((await all.ListAsync(rfqs[0].Id,
            new QuotationQuery { SupplierId = quotations[0].SupplierId,
                ValidOn = new DateOnly(2028, 3, 2) }, Ct)).Items).Id);
        Assert.Empty((await all.ListAsync(rfqs[0].Id,
            new QuotationQuery { ValidOn = new DateOnly(2028, 3, 3) }, Ct)).Items);
        await Assert.ThrowsAsync<NotFoundException>(() => all.GetAsync(rfqs[0].Id, quotations[1].Id, Ct));
        foreach (var invalid in new[] { new QuotationQuery { PageSize = 0 },
            new QuotationQuery { Sort = "unitPrice" }, new QuotationQuery { SupplierId = -1 },
            new QuotationQuery { Status = (SupplierQuotationStatus)99 } })
            await Assert.ThrowsAsync<RequestValidationException>(() => all.ListAsync(rfqs[0].Id, invalid, Ct));
        var emptyRfq = new Rfq { CompanyId = companyId,
            DocumentNo = $"Q-EMPTY-{companyId}", BusinessDate = new DateOnly(2028, 2, 29),
            ReplyDeadline = new DateOnly(2028, 3, 1), OwnerId = 7 };
        var deletedDraft = new Rfq { CompanyId = companyId,
            DocumentNo = $"Q-DELETED-{companyId}", BusinessDate = new DateOnly(2028, 2, 29),
            ReplyDeadline = new DateOnly(2028, 3, 1), OwnerId = 7,
            IsDeleted = true, DeletedAt = new DateTime(2026, 10, 21) };
        db.AddRange(emptyRfq, deletedDraft);
        await db.SaveChangesAsync(Ct);
        Assert.Empty((await all.ListAsync(emptyRfq.Id, new QuotationQuery(), Ct)).Items);
        await Assert.ThrowsAsync<NotFoundException>(() => all.ListAsync(deletedDraft.Id,
            new QuotationQuery(), Ct));
        await Assert.ThrowsAsync<ForbiddenException>(() => Service(db, company, companyId, null)
            .ListAsync(rfqs[1].Id, new QuotationQuery(), Ct));
        var otherCompanyId = TestCompanies.Next();
        var otherCompany = new FixedCompany(otherCompanyId);
        await using var otherDb = new MyErpDbContext(new DbContextOptionsBuilder<MyErpDbContext>()
            .UseMyErpSqlServer(database.ConnectionString).Options,
            [new RfqDbModel(), new QuotationDbModel()], otherCompany);
        await Assert.ThrowsAsync<NotFoundException>(() => Service(otherDb, otherCompany,
            otherCompanyId, DataScope.Company).ListAsync(rfqs[1].Id, new QuotationQuery(), Ct));
    }

    private static QuotationQueryService Service(MyErpDbContext db, FixedCompany company,
        long companyId, DataScope? scope) => new(db, new RequisitionCompanyAccess(company),
        new FixedAuthorization(new UserCompanyAuthorization(7, companyId, 10, 20, false,
            scope is null ? new Dictionary<string, DataScope>() :
            new Dictionary<string, DataScope> { [PurchasingPermissions.QuotationView] = scope.Value })));

    private sealed class FixedAuthorization(UserCompanyAuthorization? value) : ICurrentAuthorization
    {
        public Task<UserCompanyAuthorization?> GetAsync(CancellationToken cancellationToken) => Task.FromResult(value);
    }
}
