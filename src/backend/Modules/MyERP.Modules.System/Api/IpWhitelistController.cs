using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyERP.Modules.System.Application.Authorization;
using MyERP.Modules.System.Application.Security;
using MyERP.Modules.System.Domain;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Paging;
using MyERP.Shared.Security;

namespace MyERP.Modules.System.Api;

/// <summary>
/// 全系統 IP 白名單（REQ_F4118.32.1–5）。關閉總開關只能修改伺服器設定。
/// 新增或修改規則時，操作者在規則影響的每間公司都必須全權（ADR-033）：系統層級影響全部公司，
/// 角色層級影響角色已指派的公司，用戶層級影響用戶所屬的公司；否則 403 <c>SYS.AUTHORITY.FULL_ACCESS_REQUIRED</c>。
/// </summary>
[ApiController]
[Route("api/v1/sys/ip-whitelist")]
[Produces("application/json")]
[Tags("SYS - IP Whitelist")]
public sealed class IpWhitelistController(IpWhitelistService service, AuthorityGuard guard, ICurrentUser user) : ControllerBase
{
    /// <summary>分頁查詢。排序欄位為 id（預設）或 ipRange，keyword 搜尋網段與備註。</summary>
    [HttpGet]
    [RequirePermission(SystemPermissions.IpWhitelistView)]
    [ProducesResponseType<PagedResult<IpWhitelistDetail>>(StatusCodes.Status200OK)]
    public Task<PagedResult<IpWhitelistDetail>> List([FromQuery] PagedRequest request, CancellationToken token) =>
        service.ListAsync(request, token);

    /// <summary>依 ID 查詢單一規則。</summary>
    [HttpGet("{id:long}")]
    [RequirePermission(SystemPermissions.IpWhitelistView)]
    [ProducesResponseType<IpWhitelistDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<IpWhitelistDetail> Get(long id, CancellationToken token) => service.GetAsync(id, token);

    /// <summary>新增單一 IPv4／IPv6 位址或 CIDR 網段；角色／用戶層級須指定 targetId。</summary>
    [HttpPost]
    [RequirePermission(SystemPermissions.IpWhitelistCreate)]
    [ProducesResponseType<IpWhitelistDetail>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<IpWhitelistDetail>> Create(IpWhitelistInput input, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(input);
        await EnsureFullAccessAsync(input.Scope, input.TargetId, token);
        return StatusCode(StatusCodes.Status201Created, await service.CreateAsync(input, token));
    }

    /// <summary>修改有效期限、備註或啟用狀態，須帶 version；網段與層級不可變。</summary>
    [HttpPut("{id:long}")]
    [RequirePermission(SystemPermissions.IpWhitelistEdit)]
    [ProducesResponseType<IpWhitelistDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IpWhitelistDetail> Update(long id, IpWhitelistUpdate input, CancellationToken token)
    {
        var entry = await service.GetAsync(id, token);
        await EnsureFullAccessAsync(entry.Scope, entry.TargetId, token);
        return await service.UpdateAsync(id, input, token);
    }

    private async Task EnsureFullAccessAsync(IpAccessScope scope, long? targetId, CancellationToken token)
    {
        IReadOnlyList<long>? companies = (scope, targetId) switch
        {
            (IpAccessScope.Role, { } roleId) => await guard.CompaniesUsingRoleAsync(roleId, token),
            (IpAccessScope.User, { } userId) => await guard.CompaniesOfUserAsync(userId, token),
            (IpAccessScope.System, _) => null,
            _ => [], // 目標缺漏或層級無效：之後由服務回 400

        };
        await guard.EnsureFullAccessAsync(user.UserId!.Value, companies, token);
    }
}
