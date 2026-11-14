using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using MyERP.Modules.Finance.Application;
using MyERP.Modules.Finance.Contracts;
using MyERP.Modules.Finance.Domain;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.IntegrationTests.Database;

/// <summary>
/// FI-COLL-WO-001～008（WU-FI-005a2c，ADR-122）：<see cref="ICollectionWriteOffPostingService"/> 在呼叫者交易內以真實
/// SQL 把已核准的 FI 收款核銷過帳為 GL 傳票（借未核銷款暫收／貸應收控制）、<c>ar_collection_settlement</c>／
/// <c>ar_collection_allocation</c>，並減少收款未核銷與應收未清。核銷單據來源以測試假實作注入（005b 實作真版）；
/// 收款先經正式的收款過帳服務過帳；發票應收直接寫入 FI 自有的 <c>ar_posted_receivable</c>（發票過帳已另有測試）。
/// </summary>
public sealed partial class FinancePostingServiceTests
{
    private const long WriteOffReceiptId = 1000;

    private sealed class FakeCollectionWriteOffs
    {
        public readonly Dictionary<long, FinanceCollectionWriteOffSnapshot> Items = [];
        public int Reads;
        public Func<long, DbTransaction, CancellationToken, Task>? OnRead;
    }

    private sealed class FakeCollectionWriteOffSource(FakeCollectionWriteOffs writeOffs) : IFinanceCollectionWriteOffSource
    {
        public async Task<FinanceCollectionWriteOffSnapshot?> ReadWriteOffAsync(long companyId, long settlementId,
            DbTransaction transaction, CancellationToken cancellationToken = default)
        {
            Assert.NotNull(transaction.Connection);
            Interlocked.Increment(ref writeOffs.Reads);
            if (writeOffs.OnRead is { } hook)
                await hook(settlementId, transaction, cancellationToken);
            return writeOffs.Items.TryGetValue(settlementId, out var item) && item.CompanyId == companyId ? item : null;
        }
    }

    private ServiceProvider WriteOffProvider(long companyId, FakeCollectionReceipts receipts,
        FakeCollectionWriteOffs writeOffs, bool withSource = true, bool withMap = true) =>
        Provider(companyId, mapping: null, services =>
        {
            if (!withMap)
            {
                foreach (var map in services.Where(x => x.ServiceType == typeof(ISalesCollectionAccountMapSource)).ToList())
                    services.Remove(map);
            }
            services.AddSingleton(receipts);
            services.AddScoped<IFinanceCollectionReceiptSource, FakeCollectionReceiptSource>();
            services.AddSingleton(writeOffs);
            if (withSource)
                services.AddScoped<IFinanceCollectionWriteOffSource, FakeCollectionWriteOffSource>();
            else
                foreach (var real in services.Where(x => x.ServiceType == typeof(IFinanceCollectionWriteOffSource)).ToList())
                    services.Remove(real); // 主機沒有任何核銷來源（含 005b3b 正式版）→ 500 SOURCE_CONTRACT_UNAVAILABLE
        });

    /// <summary>收款種子（<see cref="SeedCollectionAsync"/>）＋已提交過帳的收款單據 1000（金額 1,000、未核銷 1,000）。</summary>
    private static async Task<CollectionSetup> SeedWriteOffAsync(ServiceProvider provider, FakeCollectionReceipts receipts,
        long companyId, CancellationToken token)
    {
        var setup = await SeedCollectionAsync(provider, companyId, token);
        receipts.Items[WriteOffReceiptId] = CollectionReceipt(companyId, WriteOffReceiptId, setup.BankId, 1_000m, "WO-1000");
        Assert.Equal(CollectionPostingOutcomeKind.Posted,
            (await PostCollectionAsync(provider, CollectionCommand(companyId, WriteOffReceiptId), true, token)).Kind);
        return setup;
    }

    /// <summary>FI 自有的已過帳應收（發票或退貨），需要時先建立信用鍵。</summary>
    private static async Task<long> AddReceivableAsync(ServiceProvider provider, long sourceId, decimal amount,
        decimal? open = null, long customerId = CollectionCustomerId, string type = "Invoice", DateOnly? date = null,
        CancellationToken token = default)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MyErpDbContext>();
        if (!await db.Set<CreditKey>().AnyAsync(x => x.CustomerId == customerId, token))
            db.Add(new CreditKey { CustomerId = customerId });
        var row = new PostedReceivable
        {
            CustomerId = customerId,
            SourceType = type,
            SourceId = sourceId,
            BaseCurrency = "TWD",
            OriginalBaseAmount = amount,
            OpenBaseAmount = open ?? amount,
            PostingDate = date ?? BusinessDate
        };
        db.Add(row);
        await db.SaveChangesAsync(token);
        return row.Id;
    }

    private static FinanceCollectionWriteOffSnapshot WriteOff(long companyId, long settlementId,
        (long ReceivableId, decimal Amount)[] lines, long receiptId = WriteOffReceiptId, decimal? total = null,
        FinanceCollectionWriteOffState state = FinanceCollectionWriteOffState.Approved) =>
        new(companyId, settlementId, 1, "sha256:wo", CollectionCustomerId, state, BusinessDate, "TWD", "TWD", receiptId,
            total ?? lines.Sum(x => x.Amount), lines.Select(x => new FinanceCollectionWriteOffLine(x.ReceivableId, x.Amount)).ToArray());

    private static CollectionWriteOffPostingCommand WriteOffCommand(long companyId, long settlementId,
        string fingerprint = "sha256:wo", long customerId = CollectionCustomerId, int version = 1) =>
        new(companyId, settlementId, customerId, version, fingerprint);

    private static async Task<CollectionWriteOffPostingResult> PostWriteOffAsync(ServiceProvider provider,
        CollectionWriteOffPostingCommand command, bool commit, CancellationToken token)
    {
        await using var scope = provider.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<MyErpDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        var result = await sp.GetRequiredService<ICollectionWriteOffPostingService>()
            .PostInCurrentTransactionAsync(command, token);
        if (db.Database.CurrentTransaction is null)
            return result;
        if (commit)
            await transaction.CommitAsync(token);
        else
            await transaction.RollbackAsync(token);
        return result;
    }

    /// <summary>核銷數、分配數、傳票數、收款 1000 的未核銷金額。</summary>
    private async Task<(int Settlements, int Allocations, int Journals, decimal Unapplied)> WriteOffStateAsync(
        long companyId, CancellationToken token)
    {
        await using var db = database.CreateRawContext();
        var settlements = await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM dbo.ar_collection_settlement WHERE company_id={companyId}").SingleAsync(token);
        var allocations = await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM dbo.ar_collection_allocation WHERE company_id={companyId}").SingleAsync(token);
        var unapplied = await db.Database.SqlQuery<decimal>($"SELECT unapplied_amount AS [Value] FROM dbo.ar_collection_receipt WHERE company_id={companyId} AND source_id={WriteOffReceiptId}").SingleAsync(token);
        var (_, journals, _) = await CountsAsync(companyId, token);
        return (settlements, allocations, journals, unapplied);
    }

    private async Task<decimal[]> OpenAmountsAsync(long companyId, CancellationToken token, params long[] ids)
    {
        await using var db = database.CreateRawContext();
        var result = new decimal[ids.Length];
        for (var i = 0; i < ids.Length; i++)
            result[i] = await db.Database.SqlQuery<decimal>($"SELECT open_base_amount AS [Value] FROM dbo.ar_posted_receivable WHERE company_id={companyId} AND id={ids[i]}").SingleAsync(token);
        return result;
    }

    private static void AssertWriteOffRejected(CollectionWriteOffPostingResult result, int status, string code)
    {
        Assert.Equal((CollectionPostingOutcomeKind.Rejected, false, status, code),
            (result.Kind, result.Changed, result.HttpStatus, result.ErrorCode));
        Assert.Null(result.Receipt);
    }

    [Fact]
    public async Task WriteOffPostsClearingDebitArCreditAndReducesBalancesOnlyWhenTheCallerCommits()
    {
        // FI-COLL-WO-001（FI-AR-COLL-021、BR-FI-034／035／037／038：部分核銷、最後剛好用完餘額、超過 0.0001 即拒絕）
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var (receipts, writeOffs) = (new FakeCollectionReceipts(), new FakeCollectionWriteOffs());
        await using var provider = WriteOffProvider(companyId, receipts, writeOffs);
        await SeedWriteOffAsync(provider, receipts, companyId, token);
        var a = await AddReceivableAsync(provider, 5001, 600m, token: token);
        var b = await AddReceivableAsync(provider, 5002, 400m, token: token);
        // 文件行不按 ID 排序；FI 依應收 ID 升冪鎖定與回傳。
        writeOffs.Items[2001] = WriteOff(companyId, 2001, [(b, 150.1234m), (a, 300m)]);

        var rolledBack = await PostWriteOffAsync(provider, WriteOffCommand(companyId, 2001), commit: false, token);
        Assert.Equal((CollectionPostingOutcomeKind.Posted, true, 200), (rolledBack.Kind, rolledBack.Changed, rolledBack.HttpStatus));
        Assert.Equal((0, 0, 1, 1_000m), await WriteOffStateAsync(companyId, token));
        Assert.Equal([600m, 400m], await OpenAmountsAsync(companyId, token, a, b));

        var posted = await PostWriteOffAsync(provider, WriteOffCommand(companyId, 2001), commit: true, token);
        Assert.Equal((CollectionPostingOutcomeKind.Posted, true, 200, (string?)null),
            (posted.Kind, posted.Changed, posted.HttpStatus, posted.ErrorCode));
        var receipt = Assert.IsType<CollectionWriteOffPostingReceipt>(posted.Receipt);
        Assert.Equal((companyId, 2001L, CollectionCustomerId, 1, "sha256:wo", "TWD", 450.1234m, WriteOffReceiptId, 549.8766m),
            (receipt.CompanyId, receipt.SettlementId, receipt.CustomerId, receipt.SourceVersion, receipt.Fingerprint,
                receipt.BaseCurrency, receipt.TotalAmount, receipt.ReceiptId, receipt.ReceiptUnappliedAmount));
        Assert.Equal(new CollectionWriteOffAllocationReceipt[] { new(a, 300m, 300m), new(b, 150.1234m, 249.8766m) }, receipt.Allocations);
        Assert.Equal((1, 2, 2, 549.8766m), await WriteOffStateAsync(companyId, token));
        Assert.Equal([300m, 249.8766m], await OpenAmountsAsync(companyId, token, a, b));

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MyErpDbContext>();
            var map = await db.Set<SalesCollectionAccountMapping>().AsNoTracking().SingleAsync(token);
            var settlement = await db.Set<CollectionSettlement>().AsNoTracking().SingleAsync(token);
            Assert.Equal((receipt.CollectionSettlementId, "CollectionWriteOff", 2001L, receipt.CollectionReceiptId,
                    receipt.JournalId, BusinessDate, "TWD", 450.1234m),
                (settlement.Id, settlement.SourceType, settlement.SourceId, settlement.ReceiptId, settlement.GlJournalId,
                    settlement.BusinessDate, settlement.BaseCurrency, settlement.TotalAmount));
            var lines = await db.Set<GlJournalLine>().AsNoTracking().Where(x => x.JournalId == receipt.JournalId)
                .OrderBy(x => x.Sequence).ToListAsync(token);
            Assert.Equal([("2191", 450.1234m, 0m), ("1131", 0m, 450.1234m)],
                lines.Select(x => (x.AccountCode, x.Debit, x.Credit)).ToArray());
            Assert.All(lines, x => Assert.Equal(map.Version, x.MappingVersion));
            var key = await db.Set<GlSourceKey>().AsNoTracking().SingleAsync(x => x.SourceType == "CollectionWriteOff", token);
            Assert.Equal(("FI", 2001L, 1, "sha256:wo"), (key.SourceModule, key.SourceId, key.SourceVersion, key.Fingerprint));
        }

        // 第二次核銷剛好用完收款與兩張發票的餘額（BR-FI-035 允許剛好的最後餘額）。
        writeOffs.Items[2002] = WriteOff(companyId, 2002, [(a, 300m), (b, 249.8766m)]);
        Assert.Equal(CollectionPostingOutcomeKind.Posted,
            (await PostWriteOffAsync(provider, WriteOffCommand(companyId, 2002), true, token)).Kind);
        Assert.Equal((2, 4, 3, 0m), await WriteOffStateAsync(companyId, token));
        Assert.Equal([0m, 0m], await OpenAmountsAsync(companyId, token, a, b));

        // 已全額核銷：再 0.0001 即超過收款未核銷。
        var c = await AddReceivableAsync(provider, 5003, 10m, token: token);
        writeOffs.Items[2003] = WriteOff(companyId, 2003, [(c, 0.0001m)]);
        AssertWriteOffRejected(await PostWriteOffAsync(provider, WriteOffCommand(companyId, 2003), true, token),
            422, CollectionWriteOffPostingErrors.ReceiptInsufficient);
        Assert.Equal((2, 4, 3, 0m), await WriteOffStateAsync(companyId, token));
    }

    [Fact]
    public async Task SameWriteOffClaimReplaysUnchangedWhileAnotherFingerprintVersionOrCustomerConflicts()
    {
        // FI-COLL-WO-002（FI-AR-COLL-009／010、BR-FI-038：同指紋重放 changed=false、零新增列、不再讀單據；其他主張 409）
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var (receipts, writeOffs) = (new FakeCollectionReceipts(), new FakeCollectionWriteOffs());
        await using var provider = WriteOffProvider(companyId, receipts, writeOffs);
        await SeedWriteOffAsync(provider, receipts, companyId, token);
        var a = await AddReceivableAsync(provider, 5011, 500m, token: token);
        writeOffs.Items[2011] = WriteOff(companyId, 2011, [(a, 200m)]);
        var first = await PostWriteOffAsync(provider, WriteOffCommand(companyId, 2011), true, token);
        Assert.Equal(CollectionPostingOutcomeKind.Posted, first.Kind);

        writeOffs.Items[2011] = writeOffs.Items[2011] with { State = FinanceCollectionWriteOffState.Posted };
        var reads = writeOffs.Reads;
        var replay = await PostWriteOffAsync(provider, WriteOffCommand(companyId, 2011), true, token);
        Assert.Equal((CollectionPostingOutcomeKind.Unchanged, false, 200, (string?)null),
            (replay.Kind, replay.Changed, replay.HttpStatus, replay.ErrorCode));
        Assert.Equal(first.Receipt, replay.Receipt! with { Allocations = first.Receipt!.Allocations });
        Assert.Equal(first.Receipt!.Allocations, replay.Receipt!.Allocations);

        AssertWriteOffRejected(await PostWriteOffAsync(provider, WriteOffCommand(companyId, 2011, "sha256:other"), true, token),
            409, CollectionWriteOffPostingErrors.SourceConflict);
        AssertWriteOffRejected(await PostWriteOffAsync(provider, WriteOffCommand(companyId, 2011, version: 2), true, token),
            409, CollectionWriteOffPostingErrors.SourceConflict);
        AssertWriteOffRejected(await PostWriteOffAsync(provider,
                WriteOffCommand(companyId, 2011, customerId: CollectionCustomerId + 1), true, token),
            409, CollectionWriteOffPostingErrors.SourceConflict);
        Assert.Equal(reads, writeOffs.Reads);
        Assert.Equal((1, 1, 2, 800m), await WriteOffStateAsync(companyId, token));
        Assert.Equal([300m], await OpenAmountsAsync(companyId, token, a));
    }

    [Fact]
    public async Task UnpostableMismatchedInvalidForeignAndMalformedWriteOffsAreRejectedWithoutRows()
    {
        // FI-COLL-WO-003（FI-AR-COLL-018：非 Approved 422、主張不符 409、內容 422、跨公司 404、格式 400）
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var (receipts, writeOffs) = (new FakeCollectionReceipts(), new FakeCollectionWriteOffs());
        await using var provider = WriteOffProvider(companyId, receipts, writeOffs);
        await SeedWriteOffAsync(provider, receipts, companyId, token);
        var a = await AddReceivableAsync(provider, 5021, 500m, token: token);
        var b = await AddReceivableAsync(provider, 5022, 500m, token: token);

        var id = 2020L;
        foreach (var state in new[] { FinanceCollectionWriteOffState.Draft, FinanceCollectionWriteOffState.Pending,
                     FinanceCollectionWriteOffState.Rejected, FinanceCollectionWriteOffState.Deleted,
                     FinanceCollectionWriteOffState.Posted })
        {
            writeOffs.Items[++id] = WriteOff(companyId, id, [(a, 10m)], state: state);
            AssertWriteOffRejected(await PostWriteOffAsync(provider, WriteOffCommand(companyId, id), true, token),
                422, CollectionWriteOffPostingErrors.SourceNotPostable);
        }

        writeOffs.Items[++id] = WriteOff(companyId, id, [(a, 10m)]);
        AssertWriteOffRejected(await PostWriteOffAsync(provider, WriteOffCommand(companyId, id, "sha256:stale"), true, token),
            409, CollectionWriteOffPostingErrors.SourceMismatch);
        AssertWriteOffRejected(await PostWriteOffAsync(provider, WriteOffCommand(companyId, id, version: 2), true, token),
            409, CollectionWriteOffPostingErrors.SourceMismatch);
        AssertWriteOffRejected(await PostWriteOffAsync(provider, WriteOffCommand(companyId, id, customerId: 999), true, token),
            409, CollectionWriteOffPostingErrors.SourceMismatch);

        foreach (var invalid in new[]
                 {
                     WriteOff(companyId, ++id, []),
                     WriteOff(companyId, ++id, [(a, 10m), (a, 5m)]),
                     WriteOff(companyId, ++id, [(a, 10m), (b, 5m)], total: 16m),
                     WriteOff(companyId, ++id, [(a, 0m)]),
                     WriteOff(companyId, ++id, [(a, 1.00001m)]),
                     WriteOff(companyId, ++id, [(a, 10m)]) with { Currency = "USD" },
                     WriteOff(companyId, ++id, [(a, 10m)], receiptId: 0)
                 })
        {
            writeOffs.Items[invalid.SettlementId] = invalid;
            AssertWriteOffRejected(await PostWriteOffAsync(provider, WriteOffCommand(companyId, invalid.SettlementId), true, token),
                422, CollectionWriteOffPostingErrors.InvalidSource);
        }

        writeOffs.Items[++id] = WriteOff(companyId + 1, id, [(a, 10m)]);
        AssertWriteOffRejected(await PostWriteOffAsync(provider, WriteOffCommand(companyId, id), true, token),
            404, CollectionWriteOffPostingErrors.SourceNotFound);
        AssertWriteOffRejected(await PostWriteOffAsync(provider, WriteOffCommand(companyId + 1, id), true, token),
            404, CollectionWriteOffPostingErrors.SourceNotFound);
        AssertWriteOffRejected(await PostWriteOffAsync(provider, WriteOffCommand(companyId, 9_999), true, token),
            404, CollectionWriteOffPostingErrors.SourceNotFound);
        AssertWriteOffRejected(await PostWriteOffAsync(provider, WriteOffCommand(companyId, id, " sha256:wo"), true, token),
            400, CollectionWriteOffPostingErrors.InvalidInput);
        AssertWriteOffRejected(await PostWriteOffAsync(provider, null!, true, token), 400, CollectionWriteOffPostingErrors.InvalidInput);

        Assert.Equal((0, 0, 1, 1_000m), await WriteOffStateAsync(companyId, token));
    }

    [Fact]
    public async Task UnpostedForeignLaterOrInsufficientReceiptsCannotFundAWriteOff()
    {
        // FI-COLL-WO-004（FI-AR-COLL-018／020、BR-FI-034／035：未過帳收款 422、他客戶或晚於核銷日的收款 422、超過未核銷 422）
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var (receipts, writeOffs) = (new FakeCollectionReceipts(), new FakeCollectionWriteOffs());
        await using var provider = WriteOffProvider(companyId, receipts, writeOffs);
        var setup = await SeedWriteOffAsync(provider, receipts, companyId, token);
        var a = await AddReceivableAsync(provider, 5031, 5_000m, token: token);

        // 收款單據 1001 存在但未過帳（草稿或回滾）：沒有 FI 收款列。
        receipts.Items[1001] = CollectionReceipt(companyId, 1001, setup.BankId, reference: "WO-1001",
            state: FinanceCollectionReceiptState.Draft);
        writeOffs.Items[2031] = WriteOff(companyId, 2031, [(a, 10m)], receiptId: 1001);
        AssertWriteOffRejected(await PostWriteOffAsync(provider, WriteOffCommand(companyId, 2031), true, token),
            422, CollectionWriteOffPostingErrors.ReceiptNotPosted);

        // 另一客戶的已過帳收款。
        receipts.Items[1002] = CollectionReceipt(companyId, 1002, setup.BankId, reference: "WO-1002") with
        {
            CustomerId = CollectionCustomerId + 1
        };
        Assert.Equal(CollectionPostingOutcomeKind.Posted, (await PostCollectionAsync(provider,
            CollectionCommand(companyId, 1002, customerId: CollectionCustomerId + 1), true, token)).Kind);
        writeOffs.Items[2032] = WriteOff(companyId, 2032, [(a, 10m)], receiptId: 1002);
        AssertWriteOffRejected(await PostWriteOffAsync(provider, WriteOffCommand(companyId, 2032), true, token),
            422, CollectionWriteOffPostingErrors.ReceiptMismatch);

        // 收款日晚於核銷日。
        receipts.Items[1003] = CollectionReceipt(companyId, 1003, setup.BankId, reference: "WO-1003") with
        {
            BusinessDate = BusinessDate.AddDays(1)
        };
        Assert.Equal(CollectionPostingOutcomeKind.Posted,
            (await PostCollectionAsync(provider, CollectionCommand(companyId, 1003), true, token)).Kind);
        writeOffs.Items[2033] = WriteOff(companyId, 2033, [(a, 10m)], receiptId: 1003);
        AssertWriteOffRejected(await PostWriteOffAsync(provider, WriteOffCommand(companyId, 2033), true, token),
            422, CollectionWriteOffPostingErrors.ReceiptMismatch);

        // 合計超過收款未核銷 1,000（應收未清 5,000 足夠）。
        writeOffs.Items[2034] = WriteOff(companyId, 2034, [(a, 1_000.0001m)]);
        AssertWriteOffRejected(await PostWriteOffAsync(provider, WriteOffCommand(companyId, 2034), true, token),
            422, CollectionWriteOffPostingErrors.ReceiptInsufficient);

        Assert.Equal((0, 0, 3, 1_000m), await WriteOffStateAsync(companyId, token));
        Assert.Equal([5_000m], await OpenAmountsAsync(companyId, token, a));
    }

    [Fact]
    public async Task EveryInvalidLineIsNamedAndNothingIsPostedWhenAnyLineFails()
    {
        // FI-COLL-WO-005（FI-AR-COLL-020、BR-FI-035／040：逐行原因、無部分成功）
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var (receipts, writeOffs) = (new FakeCollectionReceipts(), new FakeCollectionWriteOffs());
        await using var provider = WriteOffProvider(companyId, receipts, writeOffs);
        await SeedWriteOffAsync(provider, receipts, companyId, token);
        var good = await AddReceivableAsync(provider, 5041, 100m, token: token);
        var otherCustomer = await AddReceivableAsync(provider, 5042, 100m, customerId: CollectionCustomerId + 1, token: token);
        var credit = await AddReceivableAsync(provider, 5043, 100m, type: "Return", token: token);
        var later = await AddReceivableAsync(provider, 5044, 100m, date: BusinessDate.AddDays(1), token: token);
        var credited = await AddReceivableAsync(provider, 5045, 100m, open: 20m, token: token);
        var settled = await AddReceivableAsync(provider, 5046, 100m, open: 0m, token: token);
        var foreignCompany = NewCompany();
        long foreign;
        await using (var other = WriteOffProvider(foreignCompany, new FakeCollectionReceipts(), new FakeCollectionWriteOffs()))
        {
            foreign = await AddReceivableAsync(other, 5047, 100m, token: token);
        }
        writeOffs.Items[2041] = WriteOff(companyId, 2041,
        [
            (good, 10m), (9_999_999, 10m), (otherCustomer, 10m), (credit, 10m), (later, 10m), (credited, 20.0001m),
            (settled, 0.0001m), (foreign, 1m)
        ]);

        var result = await PostWriteOffAsync(provider, WriteOffCommand(companyId, 2041), true, token);
        AssertWriteOffRejected(result, 422, CollectionWriteOffPostingErrors.AllocationInvalid);
        Assert.Equal(new CollectionWriteOffLineError[]
        {
            new CollectionWriteOffLineError(2, 9_999_999, CollectionWriteOffLineReasons.ReceivableNotFound),
            new(3, otherCustomer, CollectionWriteOffLineReasons.ReceivableMismatch),
            new(4, credit, CollectionWriteOffLineReasons.ReceivableMismatch),
            new(5, later, CollectionWriteOffLineReasons.ReceivableAfterSettlement),
            new(6, credited, CollectionWriteOffLineReasons.ExceedsOpen),
            new(7, settled, CollectionWriteOffLineReasons.ExceedsOpen),
            new(8, foreign, CollectionWriteOffLineReasons.ReceivableNotFound)
        }, result.LineErrors);
        Assert.Equal((0, 0, 1, 1_000m), await WriteOffStateAsync(companyId, token));
        Assert.Equal([100m, 20m], await OpenAmountsAsync(companyId, token, good, credited));

        // 只留有效行即可過帳；剛好等於未清餘額（20）也可以。
        writeOffs.Items[2042] = WriteOff(companyId, 2042, [(good, 10m), (credited, 20m)]);
        Assert.Equal(CollectionPostingOutcomeKind.Posted,
            (await PostWriteOffAsync(provider, WriteOffCommand(companyId, 2042), true, token)).Kind);
        Assert.Equal([90m, 0m], await OpenAmountsAsync(companyId, token, good, credited));
    }

    [Fact]
    public async Task MissingContractMappingAccountsOrPeriodAreNamedRejectionsThatKeepTheCallerTransaction()
    {
        // FI-COLL-WO-006（FI-AR-COLL-019、BR-FI-037／151：單據契約不可用 500、應收控制科目停用 422 第 2 行、
        // 暫收科目不存在 422 第 1 行、GL 期間拒絕原樣轉出且交易仍開啟）
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var (receipts, writeOffs) = (new FakeCollectionReceipts(), new FakeCollectionWriteOffs());
        await using var provider = WriteOffProvider(companyId, receipts, writeOffs);
        await SeedWriteOffAsync(provider, receipts, companyId, token);
        var a = await AddReceivableAsync(provider, 5051, 500m, token: token);
        writeOffs.Items[2051] = WriteOff(companyId, 2051, [(a, 100m)]);

        await using (var noSource = WriteOffProvider(companyId, receipts, writeOffs, withSource: false))
        {
            AssertWriteOffRejected(await PostWriteOffAsync(noSource, WriteOffCommand(companyId, 2051), true, token),
                500, CollectionWriteOffPostingErrors.SourceContractUnavailable);
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<AccountService>();
            var found = (await scope.ServiceProvider.GetRequiredService<IChartOfAccounts>().FindAsync("1131", token))!;
            var row = await scope.ServiceProvider.GetRequiredService<MyErpDbContext>().Set<Account>().AsNoTracking()
                .SingleAsync(x => x.Id == found.Id, token);
            await accounts.UpdateAsync(row.Id, row.Name, BalanceDirection.Debit, false, row.Version, token);
        }
        var inactive = await PostWriteOffAsync(provider, WriteOffCommand(companyId, 2051), true, token);
        AssertWriteOffRejected(inactive, 422, CollectionWriteOffPostingErrors.AccountInvalid);
        Assert.Equal(2, inactive.InvalidLine);

        await SaveCollectionMapAsync(provider, "2199", "1131", token);
        var missing = await PostWriteOffAsync(provider, WriteOffCommand(companyId, 2051), true, token);
        AssertWriteOffRejected(missing, 422, CollectionWriteOffPostingErrors.AccountInvalid);
        Assert.Equal(1, missing.InvalidLine);

        await SaveCollectionMapAsync(provider, "2191", "1131", token);
        await using (var scope = provider.CreateAsyncScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<AccountService>();
            var found = (await scope.ServiceProvider.GetRequiredService<IChartOfAccounts>().FindAsync("1131", token))!;
            var row = await scope.ServiceProvider.GetRequiredService<MyErpDbContext>().Set<Account>().AsNoTracking()
                .SingleAsync(x => x.Id == found.Id, token);
            await accounts.UpdateAsync(row.Id, row.Name, BalanceDirection.Debit, true, row.Version, token);
        }

        // 核銷日 2027-01-05 沒有 GL 期間（收款與發票都較早，日期檢查通過）。
        writeOffs.Items[2052] = WriteOff(companyId, 2052, [(a, 100m)]) with { BusinessDate = new DateOnly(2027, 1, 5) };
        await using (var scope = provider.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<MyErpDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
            var period = await sp.GetRequiredService<ICollectionWriteOffPostingService>()
                .PostInCurrentTransactionAsync(WriteOffCommand(companyId, 2052), token);
            AssertWriteOffRejected(period, 422, GlPostingErrors.PeriodMissing);
            Assert.Same(transaction, db.Database.CurrentTransaction);
            await transaction.CommitAsync(token);
        }

        Assert.Equal((0, 0, 1, 1_000m), await WriteOffStateAsync(companyId, token));
        Assert.Equal(CollectionPostingOutcomeKind.Posted,
            (await PostWriteOffAsync(provider, WriteOffCommand(companyId, 2051), true, token)).Kind);
        Assert.Equal((1, 1, 2, 900m), await WriteOffStateAsync(companyId, token));
    }

    [Fact]
    public async Task MissingWriteOffMappingIsRejectedBeforeGl()
    {
        // FI-COLL-WO-006b（BR-FI-151：沒有可用的收款映射來源 → 422 MAPPING_MISSING，不寫任何列）。映射列不可刪除（51030），
        // 因此收款以正式來源過帳後，改用移除映射來源的 provider 過帳核銷。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var (receipts, writeOffs) = (new FakeCollectionReceipts(), new FakeCollectionWriteOffs());
        await using var provider = WriteOffProvider(companyId, receipts, writeOffs);
        await SeedWriteOffAsync(provider, receipts, companyId, token);
        var a = await AddReceivableAsync(provider, 5061, 500m, token: token);
        writeOffs.Items[2061] = WriteOff(companyId, 2061, [(a, 100m)]);

        await using (var noMap = WriteOffProvider(companyId, receipts, writeOffs, withMap: false))
        {
            AssertWriteOffRejected(await PostWriteOffAsync(noMap, WriteOffCommand(companyId, 2061), true, token),
                422, CollectionWriteOffPostingErrors.MappingMissing);
        }
        Assert.Equal((0, 0, 1, 1_000m), await WriteOffStateAsync(companyId, token));
    }

    [Fact]
    public async Task WriteOffRequiresACallerTransactionAndAGlFaultRollsBackEverything()
    {
        // FI-COLL-WO-007（FI-AR-COLL-023、BR-FI-037：無交易是程式錯誤；GL 失敗 → 整個呼叫者交易回滾，之後可重過帳）
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var (receipts, writeOffs) = (new FakeCollectionReceipts(), new FakeCollectionWriteOffs());
        await using var provider = WriteOffProvider(companyId, receipts, writeOffs);
        await SeedWriteOffAsync(provider, receipts, companyId, token);
        var a = await AddReceivableAsync(provider, 5071, 500m, token: token);
        writeOffs.Items[2071] = WriteOff(companyId, 2071, [(a, 100m)]);

        await using (var scope = provider.CreateAsyncScope())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider
                .GetRequiredService<ICollectionWriteOffPostingService>()
                .PostInCurrentTransactionAsync(WriteOffCommand(companyId, 2071), token));
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<MyErpDbContext>();
            var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
            db.Set<CreditKey>().Add(new CreditKey { CustomerId = 1 });
            var fault = await sp.GetRequiredService<ICollectionWriteOffPostingService>()
                .PostInCurrentTransactionAsync(WriteOffCommand(companyId, 2071), token);
            Assert.Equal((CollectionPostingOutcomeKind.Fault, false, 500, GlPostingErrors.WriteFailed),
                (fault.Kind, fault.Changed, fault.HttpStatus, fault.ErrorCode));
            Assert.Null(db.Database.CurrentTransaction);
            db.ChangeTracker.Clear();
            await transaction.DisposeAsync();
        }
        Assert.Equal((0, 0, 1, 1_000m), await WriteOffStateAsync(companyId, token));
        Assert.Equal([500m], await OpenAmountsAsync(companyId, token, a));

        Assert.Equal(CollectionPostingOutcomeKind.Posted,
            (await PostWriteOffAsync(provider, WriteOffCommand(companyId, 2071), true, token)).Kind);
        Assert.Equal((1, 1, 2, 900m), await WriteOffStateAsync(companyId, token));
    }

    [Fact]
    public async Task WriteOffTakesCreditKeyBeforeTheReadAndReceiptReceivableAndSettlementKeysAfterIt()
    {
        // FI-COLL-WO-008（BR-FI-036 鎖序：讀單據時只持有信用鍵；過帳後收款、各應收與核銷鍵皆由呼叫者交易持有）
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var (receipts, writeOffs) = (new FakeCollectionReceipts(), new FakeCollectionWriteOffs());
        await using var provider = WriteOffProvider(companyId, receipts, writeOffs);
        await SeedWriteOffAsync(provider, receipts, companyId, token);
        var a = await AddReceivableAsync(provider, 5081, 500m, token: token);
        var b = await AddReceivableAsync(provider, 5082, 500m, token: token);
        writeOffs.Items[2081] = WriteOff(companyId, 2081, [(b, 1m), (a, 1m)]);
        string[] resources =
        [
            $"ar_credit:{companyId}:{CollectionCustomerId}", $"ar_collection:{companyId}:{WriteOffReceiptId}",
            $"ar_receivable:{companyId}:{a}", $"ar_receivable:{companyId}:{b}", $"ar_collection_writeoff:{companyId}:2081"
        ];

        static async Task<List<string?>> ModesAsync(DbTransaction transaction, string[] resources, CancellationToken ct)
        {
            var modes = new List<string?>();
            foreach (var resource in resources)
            {
                await using var command = new SqlCommand("SELECT APPLOCK_MODE('public', @resource, 'Transaction')",
                    (SqlConnection)transaction.Connection!, (SqlTransaction)transaction);
                command.Parameters.Add("@resource", SqlDbType.NVarChar, 255).Value = resource;
                modes.Add((string?)await command.ExecuteScalarAsync(ct));
            }
            return modes;
        }

        List<string?>? atRead = null;
        writeOffs.OnRead = async (_, transaction, ct) => atRead = await ModesAsync(transaction, resources, ct);
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MyErpDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        Assert.Equal(CollectionPostingOutcomeKind.Posted, (await scope.ServiceProvider
            .GetRequiredService<ICollectionWriteOffPostingService>()
            .PostInCurrentTransactionAsync(WriteOffCommand(companyId, 2081), token)).Kind);
        var afterPost = await ModesAsync(tx.GetDbTransaction(), resources, token);
        await tx.CommitAsync(token);

        Assert.Equal(["Exclusive", "NoLock", "NoLock", "NoLock", "NoLock"], atRead);
        Assert.Equal(["Exclusive", "Exclusive", "Exclusive", "Exclusive", "Exclusive"], afterPost);
    }
}
