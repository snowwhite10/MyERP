using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Purchasing;
using MyERP.Modules.Purchasing.Application;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Modules.Purchasing.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;

namespace MyERP.IntegrationTests.Database;

public sealed class PurchaseOrderListTests(TestDatabase database)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private MyErpDbContext Context(long companyId)
    {
        var company = new FixedCompany(companyId);
        var stamper = new AuditStamper(new FixedClock(new DateTime(2028, 2, 29)), new FixedUser(7), company);
        return new MyErpDbContext(new DbContextOptionsBuilder<MyErpDbContext>()
            .UseMyErpSqlServer(database.ConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(stamper)).Options,
            [new RequisitionDbModel(), new PurchaseInvoiceSourceDbModel()], company);
    }

    private static PurchaseOrderQueryService Query(MyErpDbContext db, long companyId, DataScope scope = DataScope.Company)
    {
        var grants = new UserCompanyAuthorization(7, companyId, 10, 20, false,
            new Dictionary<string, DataScope> { [PurchasingPermissions.OrderView] = scope });
        return new PurchaseOrderQueryService(db, new RequisitionCompanyAccess(new FixedCompany(companyId)),
            new FixedAuthorization(grants));
    }

    [Fact]
    public async Task ListFiltersAndPagesOnlySavedVisibleOrders()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var requisition = new Requisition { CompanyId = companyId, DocumentNo = "REQ-LIST", BusinessDate = new DateOnly(2028, 2, 28),
            RequesterId = 7, Status = RequisitionStatus.Approved };
        requisition.Lines.Add(new RequisitionLine { CompanyId = companyId, LineNo = 1, ItemId = 11,
            RequiredDate = new DateOnly(2028, 3, 1), Quantity = 10m });
        db.Add(requisition);
        await db.SaveChangesAsync(Ct);
        var sourceId = requisition.Lines[0].Id;
        var first = NewOrder(companyId, "PO-A", 7, 10, 20, new DateOnly(2028, 2, 29), "Draft", 101);
        var second = NewOrder(companyId, "PO-B", 8, 10, 20, new DateOnly(2028, 2, 29), "Approved", 102);
        var third = NewOrder(companyId, "PO-C", 9, 11, 21, new DateOnly(2028, 3, 1), "Cancelled", 101);
        var deleted = NewOrder(companyId, "PO-D", 7, 10, 20, new DateOnly(2028, 3, 1), "Draft", 101);
        deleted.IsDeleted = true;
        deleted.DeletedAt = new DateTime(2028, 3, 1);
        db.AddRange(first, second, third, deleted);
        await db.SaveChangesAsync(Ct);
        db.Add(new PurchaseOrderSourceLine { CompanyId = companyId, PurchaseOrderId = first.Id, LineNo = 1,
            MaterialId = 11, RequisitionLineId = sourceId, Quantity = 2m, UnitPrice = 3m, TaxRatePercent = 0m });
        await db.SaveChangesAsync(Ct);

        var query = Query(db, companyId);
        var page = await query.ListAsync(new PurchaseOrderQuery { PageSize = 1, Sort = "-businessDate" }, Ct);
        Assert.Equal(3, page.TotalCount);
        Assert.Equal(third.Id, Assert.Single(page.Items).Id);
        Assert.Equal(first.Id, Assert.Single((await query.ListAsync(new PurchaseOrderQuery
            { Keyword = "PO-A", From = new DateOnly(2028, 2, 29), To = new DateOnly(2028, 2, 29),
                SupplierId = 101, Status = "Draft", RequisitionLineId = sourceId }, Ct)).Items).Id);
        Assert.Equal(1, Assert.Single((await query.ListAsync(new PurchaseOrderQuery { RequisitionLineId = sourceId }, Ct)).Items).LineCount);
        Assert.Equal(second.Id, Assert.Single((await query.ListAsync(new PurchaseOrderQuery { Page = 2, PageSize = 1, Sort = "businessDate" }, Ct)).Items).Id);
        Assert.Equal(1, (await Query(db, companyId, DataScope.Self).ListAsync(new PurchaseOrderQuery(), Ct)).TotalCount);
        Assert.Equal(2, (await Query(db, companyId, DataScope.Department).ListAsync(new PurchaseOrderQuery(), Ct)).TotalCount);
        Assert.Equal(2, (await Query(db, companyId, DataScope.Branch).ListAsync(new PurchaseOrderQuery(), Ct)).TotalCount);
        var otherCompanyId = TestCompanies.Next();
        await using var other = Context(otherCompanyId);
        Assert.Empty((await Query(other, otherCompanyId).ListAsync(new PurchaseOrderQuery(), Ct)).Items);
    }

    [Fact]
    public async Task ListRejectsInvalidFiltersAndMissingViewGrant()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var query = Query(db, companyId);
        foreach (var bad in new[] { new PurchaseOrderQuery { PageSize = 0 }, new PurchaseOrderQuery { Sort = "supplierId" },
            new PurchaseOrderQuery { From = new DateOnly(2028, 3, 2), To = new DateOnly(2028, 3, 1) },
            new PurchaseOrderQuery { SupplierId = 0 }, new PurchaseOrderQuery { RequisitionLineId = 0 },
            new PurchaseOrderQuery { Status = "Posted" } })
            await Assert.ThrowsAsync<RequestValidationException>(() => query.ListAsync(bad, Ct));
        var noGrant = new PurchaseOrderQueryService(db, new RequisitionCompanyAccess(new FixedCompany(companyId)),
            new FixedAuthorization(new UserCompanyAuthorization(7, companyId, 10, 20, false, new Dictionary<string, DataScope>())));
        await Assert.ThrowsAsync<ForbiddenException>(() => noGrant.ListAsync(new PurchaseOrderQuery(), Ct));
    }

    private static PurchaseOrderSource NewOrder(long companyId, string no, long owner, long branch, long department,
        DateOnly date, string status, long supplier) => new()
    {
        CompanyId = companyId, DocumentNo = no, OwnerId = owner, BranchId = branch, DepartmentId = department,
        BusinessDate = date, Status = status, SupplierId = supplier, Currency = "TWD", BaseNet = 10m,
        BaseTax = 0m, BaseGross = 10m
    };

    private sealed class FixedAuthorization(UserCompanyAuthorization value) : ICurrentAuthorization
    {
        public Task<UserCompanyAuthorization?> GetAsync(CancellationToken cancellationToken) => Task.FromResult<UserCompanyAuthorization?>(value);
    }
}
