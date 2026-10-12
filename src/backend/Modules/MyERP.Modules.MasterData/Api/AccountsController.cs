using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Shared.Infrastructure.Security;

namespace MyERP.Modules.MasterData.Api;

public sealed record AccountResponse(long Id, string Code, string Name, long? ParentId, AccountType AccountType,
    BalanceDirection BalanceDirection, bool IsActive, bool IsPostable, int Version);
public sealed record CreateAccountRequest(string Code, string Name, long? ParentId, AccountType? AccountType,
    BalanceDirection? BalanceDirection);
public sealed record UpdateAccountRequest(string Name, BalanceDirection BalanceDirection, bool IsActive, int Version);

/// <summary>會計科目樹（ADR-039）；所有查詢依目前授權公司的 X-Company-Id 隔離。</summary>
[ApiController]
[Route("api/v1/md/accounts")]
[Produces("application/json")]
[Tags("MD - Chart of Accounts")]
public sealed class AccountsController(AccountService accounts) : ControllerBase
{
    /// <summary>列出目前公司全部未刪除科目（含停用，按代碼排序），以 parentId 組樹；isPostable = 啟用且沒有子科目。不分頁。</summary>
    [HttpGet]
    [RequirePermission(MasterDataPermissions.AccountView)]
    [ProducesResponseType<IReadOnlyList<AccountResponse>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<AccountResponse>> List(CancellationToken token) =>
        [.. (await accounts.TreeAsync(token)).Select(Map)];

    /// <summary>
    /// 建立科目：代碼 1–30 位數字且公司內唯一；子科目代碼以上層代碼開頭、類別同上層（可省略）；
    /// 根科目必填類別；方向省略時按類別預設。
    /// </summary>
    [HttpPost]
    [RequirePermission(MasterDataPermissions.AccountCreate)]
    [ProducesResponseType<AccountResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<AccountResponse>> Create(CreateAccountRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        var account = await accounts.CreateAsync(request.Code, request.Name, request.ParentId, request.AccountType,
            request.BalanceDirection, token);
        return StatusCode(StatusCodes.Status201Created, Map(new AccountNode(account, account.IsActive)));
    }

    /// <summary>按版本修改名稱、借貸方向與啟用狀態；代碼、上層與類別不可修改。已被使用的科目不可改方向。</summary>
    [HttpPut("{id:long}")]
    [RequirePermission(MasterDataPermissions.AccountEdit)]
    [ProducesResponseType<AccountResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<AccountResponse> Update(long id, UpdateAccountRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        var account = await accounts.UpdateAsync(id, request.Name, request.BalanceDirection, request.IsActive, request.Version, token);
        return Map(new AccountNode(account, await accounts.IsPostableAsync(account, token)));
    }

    /// <summary>按版本軟刪除末級且未被使用的科目，代碼仍保留。</summary>
    [HttpDelete("{id:long}")]
    [RequirePermission(MasterDataPermissions.AccountDelete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(long id, [FromQuery] int version, CancellationToken token)
    {
        await accounts.DeleteAsync(id, version, token);
        return NoContent();
    }

    private static AccountResponse Map(AccountNode node) =>
        new(node.Account.Id, node.Account.Code, node.Account.Name, node.Account.ParentId,
            Enum.Parse<AccountType>(node.Account.AccountType), Enum.Parse<BalanceDirection>(node.Account.BalanceDirection),
            node.Account.IsActive, node.IsPostable, node.Account.Version);
}
