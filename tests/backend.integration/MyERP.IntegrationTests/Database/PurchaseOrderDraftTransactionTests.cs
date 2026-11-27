using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.Purchasing;
using MyERP.Modules.Purchasing.Application;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Modules.Purchasing.Infrastructure;
using MyERP.Modules.System.Application.Numbering;
using MyERP.Modules.System.Domain;
using MyERP.Modules.System.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;

namespace MyERP.IntegrationTests.Database;

/// <summary>
/// WU-PUR-006a4（REQ_F4114.1.2，PUR-PO-001／PUR-PO-021）：人工 CREATE 以真實 SYS 發號與真實 SQL 交易驗收
/// 發號後保存失敗整單回滾（含流水號），以及雙連線同時建立單號唯一。
/// </summary>
public sealed class PurchaseOrderDraftTransactionTests(TestDatabase database)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateTime Today = new(2028, 2, 29, 9, 30, 0);
    private static readonly DateOnly Day = DateOnly.FromDateTime(Today);

    private static PurchaseOrderInput Input(string remark) => new(101, Day, remark,
        [new(1, 11, null, 2m, 10m, "TWD", 51), new(2, 12, null, 3m, 20m, "TWD", 51)]);

    [Fact]
    public async Task FailureAfterSysNumberRollsBackHeaderLinesAndSequence()
    {
        var companyId = await NewCompanyAsync();
        foreach (var table in new[] { "pur_purchase_order_line", "pur_purchase_order" })
        {
            var fault = new FailInsert(table);
            await using (var failing = NewDb(companyId, fault))
            {
                var error = await Assert.ThrowsAsync<DbUpdateException>(() =>
                    Service(failing, companyId).CreateAsync(Input("fail"), Ct));
                Assert.IsType<InvalidOperationException>(error.InnerException);
            }
            Assert.True(fault.Fired);
            await using var verify = NewDb(companyId);
            Assert.Equal(0, await verify.Set<PurchaseOrderSource>().CountAsync(x => x.CompanyId == companyId, Ct));
            Assert.Equal(0, await verify.Set<PurchaseOrderSourceLine>().CountAsync(x => x.CompanyId == companyId, Ct));
            Assert.Empty(await verify.Set<NumberSequence>().AsNoTracking()
                .Where(x => x.CompanyId == companyId).ToListAsync(Ct));
        }

        await using var retry = NewDb(companyId);
        var created = await Service(retry, companyId).CreateAsync(Input("retry"), Ct);
        Assert.Equal("PO20280229001", created.DocumentNo);
        Assert.Equal(2, created.Lines.Count);
    }

    [Fact]
    public async Task SimultaneousCreatesOnTwoConnectionsGetDistinctSequentialNumbers()
    {
        var companyId = await NewCompanyAsync();
        await using var first = NewDb(companyId);
        await using var second = NewDb(companyId);
        var outcomes = await Task.WhenAll(
            Capture(() => Service(first, companyId).CreateAsync(Input("a"), Ct)),
            Capture(() => Service(second, companyId).CreateAsync(Input("b"), Ct)));

        // A serialization loser may only surface as 409; never a duplicate number or a half-saved order.
        Assert.All(outcomes.Where(x => x.Error is not null), x => Assert.IsType<ConflictException>(x.Error));
        var numbers = outcomes.Where(x => x.Detail is not null).Select(x => x.Detail!.DocumentNo).ToArray();
        Assert.NotEmpty(numbers);
        Assert.Equal(numbers.Length, numbers.Distinct(StringComparer.Ordinal).Count());
        await using var verify = NewDb(companyId);
        var saved = await verify.Set<PurchaseOrderSource>().AsNoTracking()
            .Where(x => x.CompanyId == companyId).OrderBy(x => x.DocumentNo).Select(x => x.DocumentNo!).ToListAsync(Ct);
        Assert.Equal(numbers.Order(StringComparer.Ordinal), saved);
        Assert.Equal(numbers.Length * 2,
            await verify.Set<PurchaseOrderSourceLine>().CountAsync(x => x.CompanyId == companyId, Ct));
        Assert.Equal(numbers.Length, (await verify.Set<NumberSequence>().AsNoTracking()
            .SingleAsync(x => x.CompanyId == companyId, Ct)).CurrentValue);
        Assert.Equal(Enumerable.Range(1, numbers.Length).Select(i => $"PO20280229{i:000}"), saved);
    }

    private sealed record Outcome(PurchaseOrderDetail? Detail, Exception? Error);

    private static async Task<Outcome> Capture(Func<Task<PurchaseOrderDetail>> action)
    {
        try { return new Outcome(await action(), null); }
        catch (Exception ex) { return new Outcome(null, ex); }
    }

    private async Task<long> NewCompanyAsync()
    {
        await using var setup = NewDb(null);
        var company = new SystemCompany
        {
            Code = "PO" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
            Name = "PO create transaction test"
        };
        setup.Add(company);
        await setup.SaveChangesAsync(Ct);
        await using var db = NewDb(company.Id);
        db.Add(new NumberRule
        {
            CompanyId = company.Id, DocumentType = RfqConversionService.DocumentType, Name = "Purchase order",
            Prefix = "PO", DateFormat = NumberDateFormat.YearMonthDay, ResetPeriod = NumberResetPeriod.Never,
            SequenceLength = 3
        });
        await db.SaveChangesAsync(Ct);
        return company.Id;
    }

    private static PurchaseOrderDraftService Service(MyErpDbContext db, long companyId)
    {
        var supplier = new Mock<ISupplierLookup>();
        supplier.Setup(x => x.RequireApprovedAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((long id, CancellationToken _) => new SupplierIdentity(id, "SUP", "Supplier"));
        var items = new Mock<IRequisitionItemGuard>();
        items.Setup(x => x.RunAsync(It.IsAny<IReadOnlyCollection<long>>(), It.IsAny<Func<Task<PurchaseOrderDetail>>>(),
                It.IsAny<CancellationToken>()))
            .Returns<IReadOnlyCollection<long>, Func<Task<PurchaseOrderDetail>>, CancellationToken>((_, action, _) => action());
        var currency = new Mock<ICurrencyLookup>();
        var twd = new CurrencyInfo("TWD", "Taiwan Dollar", "$", 0, true, true);
        currency.Setup(x => x.GetBaseCurrencyAsync(It.IsAny<CancellationToken>())).ReturnsAsync(twd);
        currency.Setup(x => x.FindCurrencyAsync("TWD", It.IsAny<CancellationToken>())).ReturnsAsync(twd);
        currency.Setup(x => x.GetRateSnapshotAsync("TWD", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExchangeRateSnapshot(1m, new DateOnly(2028, 1, 1), null, null));
        var tax = new Mock<ITaxRateLookup>();
        tax.Setup(x => x.GetRateSnapshotAsync(51, TaxDirection.Input, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TaxRateSnapshot(51, 5m, new DateOnly(2028, 1, 1), null));
        var authorization = new Mock<ICurrentAuthorization>();
        authorization.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
            new UserCompanyAuthorization(7, companyId, null, null, false,
                new Dictionary<string, DataScope> { [PurchasingPermissions.OrderCreate] = DataScope.Self }));
        var clock = new FixedClock(Today);
        return new PurchaseOrderDraftService(db, new RequisitionCompanyAccess(new FixedCompany(companyId)),
            authorization.Object, new NumberGenerator(db, clock, new FixedUser(7)), items.Object,
            supplier.Object, currency.Object, tax.Object);
    }

    private MyErpDbContext NewDb(long? companyId, DbCommandInterceptor? fault = null)
    {
        var company = new FixedCompany(companyId);
        var audit = new AuditSaveChangesInterceptor(new AuditStamper(new FixedClock(Today), new FixedUser(7), company));
        return new(new DbContextOptionsBuilder<MyErpDbContext>()
                .UseMyErpSqlServer(database.ConnectionString)
                .AddInterceptors(fault is null ? [audit] : [audit, fault]).Options,
            [new RequisitionDbModel(), new RfqDbModel(), new QuotationDbModel(), new RfqAwardDbModel(),
                new PurchaseInvoiceSourceDbModel(), new SystemOrganizationDbModel()], company);
    }

    private sealed class FailInsert(string table) : DbCommandInterceptor
    {
        public bool Fired { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("INSERT", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains($"[{table}]", StringComparison.OrdinalIgnoreCase))
            {
                Fired = true;
                throw new InvalidOperationException($"Injected {table} persistence failure.");
            }
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
