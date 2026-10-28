using System.Data;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Sales.Domain;
using MyERP.Modules.Sales.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Time;
using MyERP.Shared.Security;

namespace MyERP.IntegrationTests.Database;

public sealed class SalesQuotationDraftStoreTests(TestDatabase database)
{
    private static readonly DateOnly Day = new(2028, 2, 29);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CreateReplaceAndDeleteAreAtomicAndVersioned()
    {
        await using var db = Context(1);
        var store = new SalesQuotationDraftStore(db, new FixedClock(new DateTime(2026, 10, 29)));
        var quote = Draft("QT" + Guid.NewGuid().ToString("N")[..12]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.CreateAsync(quote, Ct));
        await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, Ct))
        {
            await store.CreateAsync(quote, Ct);
            await tx.CommitAsync(Ct);
        }
        Assert.Equal(1, quote.Version);
        var number = quote.DocumentNo;
        var replacement = Draft("unused");
        replacement.Remark = "revised";
        replacement.Lines.Clear();
        replacement.Lines.Add(Line(2));
        await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, Ct))
        {
            await store.ReplaceAsync(quote.Id, 1, replacement, Ct);
            await tx.CommitAsync(Ct);
        }
        db.ChangeTracker.Clear();
        var saved = await db.Set<SalesQuotation>().Include(x => x.Lines).SingleAsync(x => x.Id == quote.Id, Ct);
        Assert.Equal(number, saved.DocumentNo);
        Assert.Equal(2, saved.Version);
        Assert.Equal("revised", saved.Remark);
        Assert.Equal(2, Assert.Single(saved.Lines).LineNo);

        await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, Ct))
        {
            await Assert.ThrowsAsync<ConflictException>(() => store.SoftDeleteAsync(quote.Id, 1, Ct));
            await tx.RollbackAsync(Ct);
        }
        db.ChangeTracker.Clear();
        await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, Ct))
        {
            await store.SoftDeleteAsync(quote.Id, 2, Ct);
            await tx.CommitAsync(Ct);
        }
        db.ChangeTracker.Clear();
        Assert.Null(await db.Set<SalesQuotation>().SingleOrDefaultAsync(x => x.Id == quote.Id, Ct));
        var deleted = await db.Set<SalesQuotation>().IgnoreQueryFilters().SingleAsync(x => x.Id == quote.Id, Ct);
        Assert.True(deleted.IsDeleted);
        Assert.Equal(number, deleted.DocumentNo);
    }

    [Fact]
    public async Task RolledBackCreateLeavesNoQuotationOrLines()
    {
        var number = "QR" + Guid.NewGuid().ToString("N")[..12];
        long quotationId;
        await using (var db = Context(1))
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, Ct);
            var quote = Draft(number);
            await new SalesQuotationDraftStore(db, new FixedClock(new DateTime(2026, 10, 29)))
                .CreateAsync(quote, Ct);
            quotationId = quote.Id;
            await tx.RollbackAsync(Ct);
        }
        await using var verify = Context(1);
        Assert.False(await verify.Set<SalesQuotation>().AnyAsync(x => x.DocumentNo == number, Ct));
        Assert.False(await verify.Set<SalesQuotationLine>().AnyAsync(x => x.QuotationId == quotationId, Ct));
    }

    [Fact]
    public async Task OtherCompanyDraftCannotBeInserted()
    {
        await using var db = Context(2);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, Ct);
        var store = new SalesQuotationDraftStore(db, new FixedClock(new DateTime(2026, 10, 29)));
        await Assert.ThrowsAsync<ForbiddenException>(() => store.CreateAsync(Draft(
            "QF" + Guid.NewGuid().ToString("N")[..12]), Ct));
        await tx.RollbackAsync(Ct);
    }

    private MyErpDbContext Context(long companyId) => new(
        new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(new AuditStamper(
                new FixedClock(new DateTime(2026, 10, 29)), new FixedUser(7), new FixedCompany(companyId)))).Options,
        [new SalesQuotationDbModel()], new FixedCompany(companyId));

    private static SalesQuotation Draft(string number)
    {
        var quote = new SalesQuotation
        {
            CompanyId = 1, OwnerUserId = 7, DocumentNo = number, CustomerId = 21,
            CustomerCode = "C21", CustomerName = "Customer", BusinessDate = Day,
            ExpiryDate = Day.AddDays(30), CurrencyCode = "TWD", BaseCurrencyCode = "TWD",
            ExchangeRate = 1, OriginalNet = 10, OriginalGross = 10, BaseNet = 10, BaseGross = 10
        };
        quote.Lines.Add(Line(1));
        return quote;
    }

    private static SalesQuotationLine Line(int lineNo) => new()
    {
        CompanyId = 1, LineNo = lineNo, ItemId = 31, ItemCode = "I31", ItemName = "Item",
        TaxRateId = 41, Quantity = 1, UnitPrice = 10,
        OriginalNet = 10, OriginalGross = 10, BaseNet = 10, BaseGross = 10
    };
}
