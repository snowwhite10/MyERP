using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Paging;

namespace MyERP.Modules.MasterData.Api;

public sealed record FundAccountResponse(long Id, string Code, string Name, FundAccountType FundType, long CurrencyId,
    string? BankName, string? BankAccountNo, long? LedgerAccountId, bool IsActive, int Version);
public sealed record CreateFundAccountRequest(string Code, string Name, long CurrencyId, FundAccountType FundType,
    string? BankName, string? BankAccountNo, long? LedgerAccountId);
public sealed record UpdateFundAccountRequest(string Name, FundAccountType FundType, string? BankName,
    string? BankAccountNo, long? LedgerAccountId, bool IsActive, int Version);

/// <summary>公司資金帳戶主檔；交易與餘額由 FI 管理（ADR-041）。</summary>
[ApiController]
[Route("api/v1/md/fund-accounts")]
[Produces("application/json")]
[Tags("MD - Fund Accounts")]
public sealed class FundAccountsController(FundAccountService service) : ControllerBase
{
    /// <summary>分頁查詢目前公司未刪除的資金帳戶，可篩現金或銀行。</summary>
    [HttpGet]
    [RequirePermission(MasterDataPermissions.FundAccountView)]
    [ProducesResponseType<PagedResult<FundAccountResponse>>(StatusCodes.Status200OK)]
    public async Task<PagedResult<FundAccountResponse>> List([FromQuery] PagedRequest request, [FromQuery] FundAccountType? fundType, CancellationToken token)
    {
        var page = await service.ListAsync(request, fundType, token);
        return new([.. page.Items.Select(Map)], page.Page, page.PageSize, page.TotalCount);
    }

    /// <summary>建立帳戶，驗證幣別及可選資產科目；幣別日後不可變更。</summary>
    [HttpPost]
    [RequirePermission(MasterDataPermissions.FundAccountCreate)]
    [ProducesResponseType<FundAccountResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<FundAccountResponse>> Create(CreateFundAccountRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        var item = await service.CreateAsync(request.Code, request.Name, request.CurrencyId,
            new(request.FundType, request.BankName, request.BankAccountNo, request.LedgerAccountId), token);
        return StatusCode(StatusCodes.Status201Created, Map(item));
    }

    /// <summary>按版本修改名稱、類型、銀行資料、對應科目及啟用狀態。</summary>
    [HttpPut("{id:long}")]
    [RequirePermission(MasterDataPermissions.FundAccountEdit)]
    [ProducesResponseType<FundAccountResponse>(StatusCodes.Status200OK)]
    public async Task<FundAccountResponse> Update(long id, UpdateFundAccountRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Map(await service.UpdateAsync(id, request.Name,
            new(request.FundType, request.BankName, request.BankAccountNo, request.LedgerAccountId),
            request.IsActive, request.Version, token));
    }

    /// <summary>按版本軟刪除；被交易引用時只能停用。</summary>
    [HttpDelete("{id:long}")]
    [RequirePermission(MasterDataPermissions.FundAccountDelete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(long id, [FromQuery] int version, CancellationToken token)
    {
        await service.DeleteAsync(id, version, token);
        return NoContent();
    }

    private static FundAccountResponse Map(FundAccount x) => new(x.Id, x.Code, x.Name, Enum.Parse<FundAccountType>(x.FundType),
        x.CurrencyId, x.BankName, x.BankAccountNo, x.LedgerAccountId, x.IsActive, x.Version);
}

public sealed record IncomeExpenseCategoryResponse(long Id, string Code, string Name, IncomeExpenseDirection Direction,
    long? LedgerAccountId, bool IsActive, int Version);
public sealed record CreateIncomeExpenseCategoryRequest(string Code, string Name, IncomeExpenseDirection Direction, long? LedgerAccountId);
public sealed record UpdateIncomeExpenseCategoryRequest(string Name, long? LedgerAccountId, bool IsActive, int Version);

/// <summary>其他收入與其他支出類別；方向建立後固定（ADR-041）。</summary>
[ApiController]
[Route("api/v1/md/income-expense-categories")]
[Produces("application/json")]
[Tags("MD - Income Expense Categories")]
public sealed class IncomeExpenseCategoriesController(IncomeExpenseCategoryService service) : ControllerBase
{
    /// <summary>分頁查詢目前公司未刪除類別，可按收入或支出篩選。</summary>
    [HttpGet]
    [RequirePermission(MasterDataPermissions.IncomeExpenseCategoryView)]
    [ProducesResponseType<PagedResult<IncomeExpenseCategoryResponse>>(StatusCodes.Status200OK)]
    public async Task<PagedResult<IncomeExpenseCategoryResponse>> List([FromQuery] PagedRequest request, [FromQuery] IncomeExpenseDirection? direction, CancellationToken token)
    {
        var page = await service.ListAsync(request, direction, token);
        return new([.. page.Items.Select(Map)], page.Page, page.PageSize, page.TotalCount);
    }

    /// <summary>建立收入或支出類別及可選對應科目。</summary>
    [HttpPost]
    [RequirePermission(MasterDataPermissions.IncomeExpenseCategoryCreate)]
    [ProducesResponseType<IncomeExpenseCategoryResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<IncomeExpenseCategoryResponse>> Create(CreateIncomeExpenseCategoryRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        return StatusCode(StatusCodes.Status201Created,
            Map(await service.CreateAsync(request.Code, request.Name, request.Direction, request.LedgerAccountId, token)));
    }

    /// <summary>按版本修改名稱、對應科目與啟用狀態；方向不可修改。</summary>
    [HttpPut("{id:long}")]
    [RequirePermission(MasterDataPermissions.IncomeExpenseCategoryEdit)]
    [ProducesResponseType<IncomeExpenseCategoryResponse>(StatusCodes.Status200OK)]
    public async Task<IncomeExpenseCategoryResponse> Update(long id, UpdateIncomeExpenseCategoryRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Map(await service.UpdateAsync(id, request.Name, request.LedgerAccountId, request.IsActive, request.Version, token));
    }

    /// <summary>按版本軟刪除；被交易引用時只能停用。</summary>
    [HttpDelete("{id:long}")]
    [RequirePermission(MasterDataPermissions.IncomeExpenseCategoryDelete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(long id, [FromQuery] int version, CancellationToken token)
    {
        await service.DeleteAsync(id, version, token);
        return NoContent();
    }

    private static IncomeExpenseCategoryResponse Map(IncomeExpenseCategory x) => new(x.Id, x.Code, x.Name,
        Enum.Parse<IncomeExpenseDirection>(x.Direction), x.LedgerAccountId, x.IsActive, x.Version);
}
