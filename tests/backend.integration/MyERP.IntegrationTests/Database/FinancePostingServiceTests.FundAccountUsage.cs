using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyERP.Modules.Finance.Application;
using MyERP.Modules.Finance.Contracts;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.IntegrationTests.Database;

/// <summary>
/// FI-FUND-USAGE-001～003（WU-FI-005a2e，ADR-124）：已過帳收款（<c>ar_collection_receipt.bank_account_id</c>）引用的
/// MD 資金帳戶由 FI <see cref="IFundAccountUsage"/> 依 ID 回報使用中，MD 拒絕刪除；停用仍允許。真實 SQL。
/// </summary>
public sealed partial class FinancePostingServiceTests
{
    private static async Task<bool> FundInUseAsync(ServiceProvider provider, long fundAccountId, string code, CancellationToken token)
    {
        await using var scope = provider.CreateAsyncScope();
        var usage = Assert.Single(scope.ServiceProvider.GetServices<IFundAccountUsage>());
        return await usage.IsInUseAsync(fundAccountId, code, token);
    }

    private static async Task<FundAccount> FundAsync(ServiceProvider provider, long id, CancellationToken token)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<MyErpDbContext>().Set<FundAccount>().AsNoTracking()
            .SingleAsync(x => x.Id == id, token);
    }

    private static async Task DeleteFundAsync(ServiceProvider provider, long id, CancellationToken token)
    {
        var fund = await FundAsync(provider, id, token);
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<FundAccountService>().DeleteAsync(id, fund.Version, token);
    }

    [Fact]
    public async Task PostedCollectionReceiptKeepsItsFundAccountFromBeingDeletedButNotDeactivated()
    {
        // FI-FUND-USAGE-001（正常／關聯）：過帳前未使用；提交過帳後 BANK1 使用中，MD 刪除 409 IN_USE 且列仍在；
        // 停用允許（歷史以 ID 保存）；未被引用的 CASHBOX 仍可刪除。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var receipts = new FakeCollectionReceipts();
        await using var provider = CollectionProvider(companyId, receipts);
        var setup = await SeedCollectionAsync(provider, companyId, token);
        Assert.False(await FundInUseAsync(provider, setup.BankId, "BANK1", token));

        receipts.Items[2001] = CollectionReceipt(companyId, 2001, setup.BankId);
        Assert.Equal(CollectionPostingOutcomeKind.Posted,
            (await PostCollectionAsync(provider, CollectionCommand(companyId, 2001), commit: true, token)).Kind);

        Assert.True(await FundInUseAsync(provider, setup.BankId, "BANK1", token));
        var error = await Assert.ThrowsAsync<ConflictException>(() => DeleteFundAsync(provider, setup.BankId, token));
        Assert.Equal("MD.FUND_ACCOUNT.IN_USE", error.ErrorCode);
        Assert.False((await FundAsync(provider, setup.BankId, token)).IsDeleted);

        var bank = await FundAsync(provider, setup.BankId, token);
        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<FundAccountService>().UpdateAsync(bank.Id, bank.Name,
                new(FundAccountType.Bank, "Mega", "001", setup.BankLedgerId), isActive: false, bank.Version, token);
        Assert.False((await FundAsync(provider, setup.BankId, token)).IsActive);
        Assert.True(await FundInUseAsync(provider, setup.BankId, "BANK1", token));

        Assert.False(await FundInUseAsync(provider, setup.CashBoxId, "CASHBOX", token));
        await DeleteFundAsync(provider, setup.CashBoxId, token);
        await using (var scope = provider.CreateAsyncScope())
            Assert.Null(await scope.ServiceProvider.GetRequiredService<IFundAccountLookup>().FindByIdAsync(setup.CashBoxId, token));
    }

    [Fact]
    public async Task RolledBackOrRejectedPostingsDoNotMarkTheFundAccountInUse()
    {
        // FI-FUND-USAGE-002（狀態）：呼叫者回滾的過帳、被拒（USD 帳戶幣別不符 422）的過帳都不留收款列，帳戶可刪除。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var receipts = new FakeCollectionReceipts();
        await using var provider = CollectionProvider(companyId, receipts);
        var setup = await SeedCollectionAsync(provider, companyId, token);

        receipts.Items[2101] = CollectionReceipt(companyId, 2101, setup.BankId);
        Assert.Equal(CollectionPostingOutcomeKind.Posted,
            (await PostCollectionAsync(provider, CollectionCommand(companyId, 2101), commit: false, token)).Kind);
        receipts.Items[2102] = CollectionReceipt(companyId, 2102, setup.UsdBankId);
        AssertCollectionRejected(await PostCollectionAsync(provider, CollectionCommand(companyId, 2102), commit: true, token),
            422, CollectionPostingErrors.FundAccountInvalid);

        Assert.False(await FundInUseAsync(provider, setup.BankId, "BANK1", token));
        Assert.False(await FundInUseAsync(provider, setup.UsdBankId, "BANKUSD", token));
        await DeleteFundAsync(provider, setup.BankId, token);
        await DeleteFundAsync(provider, setup.UsdBankId, token);
    }

    [Fact]
    public async Task FundAccountUsageMatchesTheIdWithinTheCurrentCompanyOnly()
    {
        // FI-FUND-USAGE-003（權限：公司／邊界）：依 ID 判斷，代碼不參與；其他公司以同一 ID 詢問為 false；ID 非正為 false。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var receipts = new FakeCollectionReceipts();
        await using var provider = CollectionProvider(companyId, receipts);
        var setup = await SeedCollectionAsync(provider, companyId, token);
        receipts.Items[2201] = CollectionReceipt(companyId, 2201, setup.BankId);
        Assert.Equal(CollectionPostingOutcomeKind.Posted,
            (await PostCollectionAsync(provider, CollectionCommand(companyId, 2201), commit: true, token)).Kind);

        Assert.True(await FundInUseAsync(provider, setup.BankId, "renamed", token));
        Assert.False(await FundInUseAsync(provider, setup.UsdBankId, "BANK1", token));
        Assert.False(await FundInUseAsync(provider, 0, "BANK1", token));
        Assert.False(await FundInUseAsync(provider, -setup.BankId, "BANK1", token));

        await using var other = CollectionProvider(NewCompany(), new FakeCollectionReceipts());
        Assert.False(await FundInUseAsync(other, setup.BankId, "BANK1", token));
    }
}
