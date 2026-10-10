using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyERP.Modules.System.Application.Authorization;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Paging;
using MyERP.Shared.Security;

namespace MyERP.Modules.System.Api;

public sealed record PermissionResponse(string Code, string Module);

public sealed record PermissionGrant(string Code, DataScope DataScope);

/// <summary>目前用戶的授權：可切換的公司，以及目前（或預設）公司的權限與資料範圍。</summary>
public sealed record CurrentAuthorizationResponse(
    IReadOnlyList<AuthorizedCompany> Companies, long? CompanyId, bool IsFullAccess, IReadOnlyList<PermissionGrant> Permissions);

/// <summary>角色與權限維護（REQ_F4118.25.1–5）。</summary>
[ApiController]
[Route("api/v1/sys/roles")]
[Produces("application/json")]
[Tags("SYS - Role")]
public sealed class RolesController(RoleService roles) : ControllerBase
{
    /// <summary>角色列表：分頁、關鍵字（代碼／名稱）、排序 <c>code</c>／<c>name</c>。</summary>
    [HttpGet]
    [RequirePermission(SystemPermissions.RoleView)]
    [ProducesResponseType<PagedResult<RoleDetail>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public Task<PagedResult<RoleDetail>> List([FromQuery] PagedRequest request, CancellationToken cancellationToken) =>
        roles.ListAsync(request, cancellationToken);

    /// <summary>單一角色與其權限代碼。</summary>
    [HttpGet("{id:long}")]
    [RequirePermission(SystemPermissions.RoleView)]
    [ProducesResponseType<RoleDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<RoleDetail> Get(long id, CancellationToken cancellationToken) => roles.GetAsync(id, cancellationToken);

    /// <summary>建立角色；代碼全系統唯一（保存為大寫），權限必須在權限目錄內。</summary>
    [HttpPost]
    [RequirePermission(SystemPermissions.RoleCreate)]
    [ProducesResponseType<RoleDetail>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RoleDetail>> Create(RoleInput input, CancellationToken cancellationToken)
    {
        var role = await roles.CreateAsync(input, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = role.Id }, role);
    }

    /// <summary>修改角色（含啟用／停用與整組權限），須帶 <c>version</c>。</summary>
    [HttpPut("{id:long}")]
    [RequirePermission(SystemPermissions.RoleEdit)]
    [ProducesResponseType<RoleDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<RoleDetail> Update(long id, RoleUpdate input, CancellationToken cancellationToken) =>
        roles.UpdateAsync(id, input, cancellationToken);

    /// <summary>刪除（軟刪除）角色；仍有用戶使用時回 422 <c>SYS.ROLE.IN_USE</c>。</summary>
    [HttpDelete("{id:long}")]
    [RequirePermission(SystemPermissions.RoleDelete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Delete(long id, [FromQuery] int version, CancellationToken cancellationToken)
    {
        await roles.DeleteAsync(id, version, cancellationToken);
        return NoContent();
    }
}

/// <summary>權限目錄（各模組宣告的全部權限代碼）。</summary>
[ApiController]
[Route("api/v1/sys/permissions")]
[Produces("application/json")]
[Tags("SYS - Role")]
public sealed class PermissionsController(PermissionCatalog catalog) : ControllerBase
{
    /// <summary>全部權限代碼，供角色設定畫面選擇。</summary>
    [HttpGet]
    [RequirePermission(SystemPermissions.RoleView)]
    [ProducesResponseType<IReadOnlyList<PermissionResponse>>(StatusCodes.Status200OK)]
    public IReadOnlyList<PermissionResponse> List() => [.. catalog.All.Select(p => new PermissionResponse(p.Code, p.Module))];
}

/// <summary>用戶的公司授權與角色指派（REQ_F4118.25.6）。</summary>
[ApiController]
[Route("api/v1/sys/users/{userId:long}/companies")]
[Produces("application/json")]
[Tags("SYS - Role")]
public sealed class UserCompaniesController(RoleService roles) : ControllerBase
{
    /// <summary>設定用戶在某公司的組織歸屬、預設公司與角色（整組取代）。</summary>
    [HttpPut("{companyId:long}")]
    [RequirePermission(SystemPermissions.UserEdit)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Assign(long userId, long companyId, UserCompanyAssignment input, CancellationToken cancellationToken)
    {
        await roles.AssignAsync(userId, companyId, input, cancellationToken);
        return NoContent();
    }
}

/// <summary>目前用戶的授權（前端權限指令與公司切換使用）。</summary>
[ApiController]
[Route("api/v1/sys/auth/authorization")]
[Produces("application/json")]
[Tags("SYS - Auth")]
public sealed class CurrentAuthorizationController(UserAuthorizationStore store, ICurrentUser user, ICurrentCompany company) : ControllerBase
{
    /// <summary>
    /// 回傳可存取的公司與權限。帶 <c>X-Company-Id</c> 時取該公司（不可存取已由中介層回 403），否則取預設公司（沒有預設時取第一間）。
    /// 沒有任何公司時 <c>companyId</c> 為 null、權限為空。
    /// </summary>
    [HttpGet]
    [Authorize]
    [ProducesResponseType<CurrentAuthorizationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<CurrentAuthorizationResponse> Get(CancellationToken cancellationToken)
    {
        var userId = user.UserId!.Value;
        var companies = await store.ListCompaniesAsync(userId, cancellationToken);
        var companyId = company.CompanyId ?? (companies.Count > 0 ? companies[0].Id : null);
        var authorization = companyId is { } id ? await store.GetAsync(userId, id, cancellationToken) : null;
        if (authorization is null)
        {
            return new CurrentAuthorizationResponse(companies, null, false, []);
        }

        var permissions = authorization.Permissions
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => new PermissionGrant(p.Key, p.Value))
            .ToList();
        return new CurrentAuthorizationResponse(companies, authorization.CompanyId, authorization.IsFullAccess, permissions);
    }
}
