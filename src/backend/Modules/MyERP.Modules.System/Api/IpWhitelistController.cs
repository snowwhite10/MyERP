using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyERP.Modules.System.Application.Authorization;
using MyERP.Modules.System.Application.Security;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Paging;

namespace MyERP.Modules.System.Api;

/// <summary>全系統 IP 白名單（REQ_F4118.32.1–5）。關閉總開關只能修改伺服器設定。</summary>
[ApiController]
[Route("api/v1/sys/ip-whitelist")]
[Produces("application/json")]
[Tags("SYS - IP Whitelist")]
public sealed class IpWhitelistController(IpWhitelistService service) : ControllerBase
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
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<IpWhitelistDetail>> Create(IpWhitelistInput input, CancellationToken token) =>
        StatusCode(StatusCodes.Status201Created, await service.CreateAsync(input, token));

    /// <summary>修改有效期限、備註或啟用狀態，須帶 version；網段與層級不可變。</summary>
    [HttpPut("{id:long}")]
    [RequirePermission(SystemPermissions.IpWhitelistEdit)]
    [ProducesResponseType<IpWhitelistDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<IpWhitelistDetail> Update(long id, IpWhitelistUpdate input, CancellationToken token) =>
        service.UpdateAsync(id, input, token);
}
