using System.Data;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Sales.Application;
using MyERP.Modules.Sales.Domain;
using MyERP.Modules.Sales.Infrastructure;
using MyERP.Shared.Approvals;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.IntegrationTests.Database;

public sealed class SalesQuotationApprovalResultTests(TestDatabase database)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly FixedClock Clock = new(new DateTime(2026, 10, 29));

    [Theory]
    [InlineData(ApprovalStatus.Approved, SalesQuotationStatus.Approved)]
    [InlineData(ApprovalStatus.Rejected, SalesQuotationStatus.Rejected)]
    public async Task FinalDecisionChangesQuotationOnlyInsideItsTransaction(
        ApprovalStatus decision, SalesQuotationStatus expected)
    {
        var number = "QA" + Guid.NewGuid().ToString("N")[..12];
        long id;
        await using (var db = Context())
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, Ct);
            var quote = Draft(number);
            await new SalesQuotationDraftStore(db, Clock).CreateAsync(quote, Ct);
            quote.Status = SalesQuotationStatus.Pending;
            quote.ApprovalInstanceId = 712;
            await db.SaveChangesAsync(Ct);
            await tx.CommitAsync(Ct);
            id = quote.Id;
        }

        await using (var db = Context())
        {
            await using var tx = await db.Database.BeginTransactionAsync(Ct);
            await new SalesQuotationApprovalResultHandler(db, Clock).HandleAsync(
                new ApprovalCompleted(712, "SAL.QUOTATION", id, decision, 8, "decision"), Ct);
            await db.SaveChangesAsync(Ct);
            await tx.RollbackAsync(Ct);
        }
        await using (var db = Context())
            Assert.Equal(SalesQuotationStatus.Pending,
                (await db.Set<SalesQuotation>().SingleAsync(x => x.Id == id, Ct)).Status);

        await using (var db = Context())
        {
            await using var tx = await db.Database.BeginTransactionAsync(Ct);
            await Assert.ThrowsAsync<ConflictException>(() =>
                new SalesQuotationApprovalResultHandler(db, Clock).HandleAsync(
                    new ApprovalCompleted(713, "SAL.QUOTATION", id, decision, 8, null), Ct));
            await tx.RollbackAsync(Ct);
        }
        await using (var db = Context())
        {
            await using var tx = await db.Database.BeginTransactionAsync(Ct);
            var handler = new SalesQuotationApprovalResultHandler(db, Clock);
            await handler.HandleAsync(new ApprovalCompleted(712, "SAL.QUOTATION", id, decision, 8, null), Ct);
            await db.SaveChangesAsync(Ct);
            await tx.CommitAsync(Ct);
        }
        await using (var db = Context())
        {
            var saved = await db.Set<SalesQuotation>().SingleAsync(x => x.Id == id, Ct);
            Assert.Equal(expected, saved.Status);
            Assert.Equal(decision == ApprovalStatus.Approved, saved.ApprovedAt.HasValue);
        }
        await using (var db = Context())
        {
            await using var tx = await db.Database.BeginTransactionAsync(Ct);
            await Assert.ThrowsAsync<ConflictException>(() =>
                new SalesQuotationApprovalResultHandler(db, Clock).HandleAsync(
                    new ApprovalCompleted(712, "SAL.QUOTATION", id, decision, 8, null), Ct));
            await tx.RollbackAsync(Ct);
        }
    }

    private MyErpDbContext Context() => new(
        new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(new AuditStamper(
                Clock, new FixedUser(7), new FixedCompany(1)))).Options,
        [new SalesQuotationDbModel()], new FixedCompany(1));

    private static SalesQuotation Draft(string number)
    {
        var quote = new SalesQuotation
        {
            CompanyId = 1, OwnerUserId = 7, DocumentNo = number, CustomerId = 21,
            CustomerCode = "C21", CustomerName = "Customer", BusinessDate = new DateOnly(2026, 10, 29),
            ExpiryDate = new DateOnly(2026, 11, 29), CurrencyCode = "TWD", BaseCurrencyCode = "TWD",
            ExchangeRate = 1, OriginalNet = 10, OriginalGross = 10, BaseNet = 10, BaseGross = 10
        };
        quote.Lines.Add(new SalesQuotationLine
        {
            CompanyId = 1, LineNo = 1, ItemId = 31, ItemCode = "I31", ItemName = "Item",
            TaxRateId = 41, Quantity = 1, UnitPrice = 10,
            OriginalNet = 10, OriginalGross = 10, BaseNet = 10, BaseGross = 10
        });
        return quote;
    }
}
