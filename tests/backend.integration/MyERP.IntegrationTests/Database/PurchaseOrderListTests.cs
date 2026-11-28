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
            [new RequisitionDbModel(), new RfqDbModel(), new QuotationDbModel(), new RfqAwardDbModel(),
                new PurchaseInvoiceSourceDbModel()], company);
    }

    private static PurchaseOrderQueryService Query(MyErpDbContext db, long companyId, DataScope scope = DataScope.Company,
        bool export = false, DataScope exportScope = DataScope.Company)
    {
        var permissions = new Dictionary<string, DataScope> { [PurchasingPermissions.OrderView] = scope };
        if (export) permissions[PurchasingPermissions.OrderExport] = exportScope;
        var grants = new UserCompanyAuthorization(7, companyId, 10, 20, false, permissions);
        return new PurchaseOrderQueryService(db, new RequisitionCompanyAccess(new FixedCompany(companyId)),
            new FixedAuthorization(grants));
    }

    [Fact]
    public async Task ExportUsesViewScopeSavedLinesAndWholeFileLimit()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var visible = NewOrder(companyId, "=PO,1", 7, 10, 20, new DateOnly(2028, 2, 29), "Approved", 101);
        var hidden = NewOrder(companyId, "PO-HIDDEN", 8, 11, 21, new DateOnly(2028, 2, 29), "Draft", 102);
        var deleted = NewOrder(companyId, "PO-DELETED", 7, 10, 20, new DateOnly(2028, 2, 29), "Draft", 101);
        deleted.IsDeleted = true;
        deleted.DeletedAt = new DateTime(2028, 3, 1);
        db.AddRange(visible, hidden, deleted);
        await db.SaveChangesAsync(Ct);
        db.AddRange(
            new PurchaseOrderSourceLine { CompanyId = companyId, PurchaseOrderId = visible.Id, LineNo = 1,
                MaterialId = 11, Quantity = 2.000001m, UnitPrice = 3.5m, TaxRatePercent = 5m,
                BaseNet = 7m, BaseTax = 0.35m, BaseGross = 7.35m },
            new PurchaseOrderSourceLine { CompanyId = companyId, PurchaseOrderId = visible.Id, LineNo = 2,
                MaterialId = 12, Quantity = 1m, UnitPrice = 2m, TaxRatePercent = 0m },
            new PurchaseOrderSourceLine { CompanyId = companyId, PurchaseOrderId = hidden.Id, LineNo = 1,
                MaterialId = 11, Quantity = 1m, UnitPrice = 1m, TaxRatePercent = 0m },
            new PurchaseOrderSourceLine { CompanyId = companyId, PurchaseOrderId = deleted.Id, LineNo = 1,
                MaterialId = 11, Quantity = 1m, UnitPrice = 1m, TaxRatePercent = 0m });
        await db.SaveChangesAsync(Ct);

        await using var output = new MemoryStream();
        var query = Query(db, companyId, DataScope.Self, export: true);
        var count = await query.ExportAsync(new PurchaseOrderQuery { Sort = "documentNo" }, "en", output, token: Ct);
        Assert.Equal(2, count);
        var bytes = output.ToArray();
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        var text = System.Text.Encoding.UTF8.GetString(bytes);
        Assert.Equal(3, text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Contains("\"'=PO,1\",2028-02-29,Approved,101,TWD,1,11,2.000001,", text);
        Assert.DoesNotContain("PO-HIDDEN", text);
        Assert.DoesNotContain("PO-DELETED", text);
        await using var narrowed = new MemoryStream();
        Assert.Equal(2, await Query(db, companyId, export: true, exportScope: DataScope.Self)
            .ExportAsync(new PurchaseOrderQuery(), "en", narrowed, token: Ct));
        Assert.DoesNotContain("PO-HIDDEN", System.Text.Encoding.UTF8.GetString(narrowed.ToArray()));

        await using var none = new MemoryStream();
        Assert.Equal(0, await query.ExportAsync(new PurchaseOrderQuery { Keyword = "NO-MATCH" }, "en", none, token: Ct));
        Assert.Single(System.Text.Encoding.UTF8.GetString(none.ToArray()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries));
        await using var limited = new MemoryStream();
        await Assert.ThrowsAsync<BusinessRuleException>(() => query.ExportAsync(new PurchaseOrderQuery(), "en", limited,
            maxRows: 1, token: Ct));
        Assert.Equal(0, limited.Length);
        await Assert.ThrowsAsync<ForbiddenException>(() => Query(db, companyId, DataScope.Self)
            .ExportAsync(new PurchaseOrderQuery(), "en", new MemoryStream(), token: Ct));
        await Assert.ThrowsAsync<RequestValidationException>(() => query.ExportAsync(
            new PurchaseOrderQuery { Sort = "supplierId" }, "en", new MemoryStream(), token: Ct));
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
        Assert.Equal(second.Id, (await Query(db, companyId).GetAsync(second.Id, Ct)).Id);
        await Assert.ThrowsAsync<NotFoundException>(() => Query(db, companyId, DataScope.Self).GetAsync(second.Id, Ct));
        await Assert.ThrowsAsync<NotFoundException>(() => Query(db, companyId).GetAsync(deleted.Id, Ct));
        var otherCompanyId = TestCompanies.Next();
        await using var other = Context(otherCompanyId);
        Assert.Empty((await Query(other, otherCompanyId).ListAsync(new PurchaseOrderQuery(), Ct)).Items);
        await Assert.ThrowsAsync<NotFoundException>(() => Query(other, otherCompanyId).GetAsync(first.Id, Ct));
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

    [Fact]
    public async Task DetailShowsSavedAwardAndCurrentRequisitionQuantities()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var req = new Requisition { CompanyId = companyId, DocumentNo = "REQ-DETAIL", BusinessDate = new DateOnly(2028, 2, 29),
            RequesterId = 7, Status = RequisitionStatus.Approved };
        req.Lines.Add(new RequisitionLine { CompanyId = companyId, LineNo = 2, ItemId = 11,
            RequiredDate = new DateOnly(2028, 3, 1), Quantity = 10.000001m, OrderedQuantity = 4.000001m });
        var rfq = new Rfq { CompanyId = companyId, DocumentNo = "RFQ-DETAIL", BusinessDate = new DateOnly(2028, 2, 29),
            ReplyDeadline = new DateOnly(2028, 3, 2), OwnerId = 7, Status = RfqStatus.Sent,
            SentAt = new DateTime(2028, 2, 29) };
        rfq.Lines.Add(new RfqLine { CompanyId = companyId, LineNo = 1, ItemId = 11,
            RequisitionLineId = null, RequiredDate = new DateOnly(2028, 3, 2), Quantity = 2m });
        rfq.Invitations.Add(new RfqInvitation { CompanyId = companyId, SupplierId = 101 });
        var order = NewOrder(companyId, "PO-DETAIL", 7, 10, 20, new DateOnly(2028, 2, 29), "Approved", 101);
        db.AddRange(req, rfq, order);
        await db.SaveChangesAsync(Ct);
        order.RfqId = rfq.Id;
        var revision = new QuotationRevision { CompanyId = companyId, RevisionNo = 1,
            QuoteDate = new DateOnly(2028, 2, 29), ValidUntil = new DateOnly(2028, 3, 31) };
        revision.Lines.Add(new QuotationLine { CompanyId = companyId, RfqLineId = rfq.Lines[0].Id,
            UnitPrice = 3m, CurrencyCode = "TWD", TaxRateId = 51 });
        var quote = new SupplierQuotation { CompanyId = companyId, RfqId = rfq.Id, SupplierId = 101,
            CurrentRevisionNo = 1, Status = SupplierQuotationStatus.Awarded };
        quote.Revisions.Add(revision);
        db.Add(quote);
        await db.SaveChangesAsync(Ct);
        var award = new RfqAward { CompanyId = companyId, RfqId = rfq.Id, RfqLineId = rfq.Lines[0].Id,
            QuotationId = quote.Id, QuotationLineId = revision.Lines[0].Id, SupplierId = 101,
            Reason = "Award", Quantity = 2m, UnitPrice = 3m, CurrencyCode = "TWD", BaseCurrencyCode = "TWD",
            TaxRateId = 51, TaxRateFrom = new DateOnly(2028, 1, 1), ExchangeRate = 1m,
            ExchangeRateFrom = new DateOnly(2028, 1, 1), ComparisonDate = new DateOnly(2028, 2, 29),
            BaseNet = 6m, BaseGross = 6m };
        db.Add(award);
        await db.SaveChangesAsync(Ct);
        var line = new PurchaseOrderSourceLine { CompanyId = companyId, PurchaseOrderId = order.Id, LineNo = 1,
            MaterialId = 11, RequisitionLineId = req.Lines[0].Id, Quantity = 2m, UnitPrice = 3m, TaxRatePercent = 0m };
        db.Add(line);
        await db.SaveChangesAsync(Ct);
        db.Add(new PurchaseOrderAwardSource { CompanyId = companyId, PurchaseOrderLineId = line.Id, AwardId = award.Id });
        await db.SaveChangesAsync(Ct);

        var detail = await Query(db, companyId).GetAsync(order.Id, Ct);
        Assert.Equal("RFQ-DETAIL", detail.RfqDocumentNo);
        var read = Assert.Single(detail.Lines);
        Assert.Equal(award.Id, read.AwardId);
        Assert.Equal(req.Id, read.RequisitionId);
        Assert.Equal("REQ-DETAIL", read.RequisitionDocumentNo);
        Assert.Equal(2, read.RequisitionLineNo);
        Assert.Equal(10.000001m, read.RequisitionQuantity);
        Assert.Equal(4.000001m, read.RequisitionOrderedQuantity);
        Assert.Equal(6m, read.RequisitionRemainingQuantity);

        req.Status = RequisitionStatus.Draft;
        req.IsDeleted = true;
        req.DeletedAt = new DateTime(2028, 3, 1);
        rfq.Status = RfqStatus.Draft;
        rfq.SentAt = null;
        rfq.IsDeleted = true;
        rfq.DeletedAt = new DateTime(2028, 3, 1);
        await db.SaveChangesAsync(Ct);
        var historic = await Query(db, companyId).GetAsync(order.Id, Ct);
        Assert.Equal("RFQ-DETAIL", historic.RfqDocumentNo);
        Assert.Equal("REQ-DETAIL", Assert.Single(historic.Lines).RequisitionDocumentNo);

        db.Add(new PurchaseOrderSourceLine { CompanyId = companyId, PurchaseOrderId = order.Id, LineNo = 2,
            MaterialId = 11, Quantity = 1m, UnitPrice = 3m, TaxRatePercent = 0m });
        await db.SaveChangesAsync(Ct);
        var manual = Assert.Single((await Query(db, companyId).GetAsync(order.Id, Ct)).Lines, x => x.LineNo == 2);
        Assert.Null(manual.AwardId);
        Assert.Null(manual.RequisitionQuantity);
        Assert.Null(manual.RequisitionDocumentNo);
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
