using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyERP.Modules.Finance.Application;
using MyERP.Modules.Finance.Domain;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.IntegrationTests.Database;

/// <summary>
/// FI-OP-USAGE-001～004（WU-FI-008b1，ADR-212）：其他付款以 ID 保存 MD 費用類別與資金帳戶。FI 的
/// <see cref="IIncomeExpenseCategoryUsage"/> 依 ID 回報「未刪除 Draft／Rejected／Posted 單據、永久過帳來源或費用類別對照列」
/// 引用的類別，<see cref="IFundAccountUsage"/> 另回報其他付款單據與來源引用的資金帳戶；MD 刪除回 409 IN_USE，停用仍允許。真實 SQL。
/// </summary>
public sealed partial class FinancePostingServiceTests
{
    private static async Task<bool> CategoryInUseAsync(ServiceProvider provider, long categoryId, string code,
        CancellationToken token)
    {
        await using var scope = provider.CreateAsyncScope();
        var usage = Assert.Single(scope.ServiceProvider.GetServices<IIncomeExpenseCategoryUsage>());
        return await usage.IsInUseAsync(categoryId, code, token);
    }

    private static async Task<IncomeExpenseCategory> CategoryAsync(ServiceProvider provider, long id, CancellationToken token)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<MyErpDbContext>().Set<IncomeExpenseCategory>().AsNoTracking()
            .SingleAsync(x => x.Id == id, token);
    }

    private static async Task DeleteCategoryAsync(ServiceProvider provider, long id, CancellationToken token)
    {
        var category = await CategoryAsync(provider, id, token);
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IncomeExpenseCategoryService>().DeleteAsync(id, category.Version, token);
    }

    [Fact]
    public async Task OtherPaymentDraftKeepsItsCategoryAndFundAccountFromBeingDeletedUntilTheDraftIsDeleted()
    {
        // FI-OP-USAGE-001（正常／關聯）：建立 Draft 前未使用；Draft 引用 RENT 與 CASHBOX 後兩者 MD 刪除 409 IN_USE 且列仍在；
        // 停用仍允許；Draft 軟刪除後釋放，兩者可刪除；未被引用的 SALEINC 一直可刪。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        await using var provider = OtherPaymentProvider(companyId, new OtherPaymentActor());
        var setup = await SeedOtherPaymentAsync(provider, companyId, token);
        var cashBoxId = await FundIdAsync(provider, "CASHBOX", token);
        Assert.False(await CategoryInUseAsync(provider, setup.RentId, "RENT", token));
        Assert.False(await FundInUseAsync(provider, cashBoxId, "CASHBOX", token));

        var draft = await OtherPaymentAsync(provider, s => s.CreateAsync(
            OtherPaymentInput(setup, categoryId: setup.RentId, bankId: cashBoxId), token));

        Assert.True(await CategoryInUseAsync(provider, setup.RentId, "RENT", token));
        Assert.True(await FundInUseAsync(provider, cashBoxId, "CASHBOX", token));
        var categoryError = await Assert.ThrowsAsync<ConflictException>(() => DeleteCategoryAsync(provider, setup.RentId, token));
        Assert.Equal("MD.INCOME_EXPENSE_CATEGORY.IN_USE", categoryError.ErrorCode);
        var fundError = await Assert.ThrowsAsync<ConflictException>(() => DeleteFundAsync(provider, cashBoxId, token));
        Assert.Equal("MD.FUND_ACCOUNT.IN_USE", fundError.ErrorCode);
        Assert.False((await CategoryAsync(provider, setup.RentId, token)).IsDeleted);
        Assert.False((await FundAsync(provider, cashBoxId, token)).IsDeleted);

        var rent = await CategoryAsync(provider, setup.RentId, token);
        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IncomeExpenseCategoryService>()
                .UpdateAsync(rent.Id, rent.Name, rent.LedgerAccountId, isActive: false, rent.Version, token);
        Assert.False((await CategoryAsync(provider, setup.RentId, token)).IsActive);

        await DeleteCategoryAsync(provider, setup.IncomeId, token);

        await OtherPaymentAsync(provider, s => s.DeleteAsync(draft.Id, draft.Version, token));
        Assert.False(await CategoryInUseAsync(provider, setup.RentId, "RENT", token));
        Assert.False(await FundInUseAsync(provider, cashBoxId, "CASHBOX", token));
        await DeleteCategoryAsync(provider, setup.RentId, token);
        await DeleteFundAsync(provider, cashBoxId, token);
    }

    [Fact]
    public async Task AccountMapAloneAndPostedOtherPaymentKeepTheCategoryInUse()
    {
        // FI-OP-USAGE-002（狀態／歷史）：未有單據時，對照列本身即使用中（對照列不可刪，SQL 觸發器）；呼叫者回滾的過帳
        // 不留來源列；提交過帳後 Posted 單據與永久來源以 ID 保留類別與資金帳戶，MD 刪除 409。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        await using var provider = OtherPaymentPostingProvider(companyId);
        var setup = await SeedOtherPaymentPostingAsync(provider, companyId, token, withMap: false);
        Assert.False(await CategoryInUseAsync(provider, setup.CourierId, "COURIER", token));

        await SaveOtherPaymentMapAsync(provider, setup.CourierId, setup.ExpenseLedgerId, token);
        Assert.True(await CategoryInUseAsync(provider, setup.CourierId, "COURIER", token));

        var draft = await OtherPaymentAsync(provider, s => s.CreateAsync(OtherPaymentInput(setup), token));
        var claim = await OtherPaymentClaimAsync(provider, draft.Id, token);
        Assert.Equal(OtherPaymentPostingOutcomeKind.Posted,
            (await PostOtherPaymentAsync(provider, claim, commit: false, token)).Kind);
        Assert.Equal(0, (await OtherPaymentPostingCountsAsync(companyId, token)).Sources);
        Assert.Equal(OtherPaymentPostingOutcomeKind.Posted,
            (await PostOtherPaymentAsync(provider, claim, commit: true, token)).Kind);

        Assert.Equal(1, (await OtherPaymentPostingCountsAsync(companyId, token)).Sources);
        Assert.True(await CategoryInUseAsync(provider, setup.CourierId, "COURIER", token));
        Assert.True(await FundInUseAsync(provider, setup.BankId, "BANK1", token));
        var error = await Assert.ThrowsAsync<ConflictException>(() => DeleteCategoryAsync(provider, setup.CourierId, token));
        Assert.Equal("MD.INCOME_EXPENSE_CATEGORY.IN_USE", error.ErrorCode);
    }

    [Fact]
    public async Task CategoryUsageMatchesTheIdWithinTheCurrentCompanyOnly()
    {
        // FI-OP-USAGE-003（權限：公司／邊界）：依 ID 判斷，代碼不參與；其他類別、非正 ID、其他公司以同一 ID 詢問皆為 false。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        await using var provider = OtherPaymentProvider(companyId, new OtherPaymentActor());
        var setup = await SeedOtherPaymentAsync(provider, companyId, token);
        await OtherPaymentAsync(provider, s => s.CreateAsync(OtherPaymentInput(setup), token));

        Assert.True(await CategoryInUseAsync(provider, setup.CourierId, "renamed", token));
        Assert.False(await CategoryInUseAsync(provider, setup.RentId, "COURIER", token));
        Assert.False(await CategoryInUseAsync(provider, 0, "COURIER", token));
        Assert.False(await CategoryInUseAsync(provider, -setup.CourierId, "COURIER", token));

        await using var other = OtherPaymentProvider(NewCompany(), new OtherPaymentActor());
        Assert.False(await CategoryInUseAsync(other, setup.CourierId, "COURIER", token));
        Assert.False(await FundInUseAsync(other, setup.BankId, "BANK1", token));
    }

    [Fact]
    public async Task RejectedOtherPaymentStillHoldsItsCategory()
    {
        // FI-OP-USAGE-004（狀態）：Rejected 仍可修改重送，屬未刪除單據，類別與資金帳戶仍使用中。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        await using var provider = OtherPaymentProvider(companyId, new OtherPaymentActor());
        var setup = await SeedOtherPaymentAsync(provider, companyId, token);
        var draft = await OtherPaymentAsync(provider, s => s.CreateAsync(OtherPaymentInput(setup), token));
        await SetOtherPaymentStatusAsync(draft.Id, "Rejected");

        Assert.True(await CategoryInUseAsync(provider, setup.CourierId, "COURIER", token));
        Assert.True(await FundInUseAsync(provider, setup.BankId, "BANK1", token));
    }
}
