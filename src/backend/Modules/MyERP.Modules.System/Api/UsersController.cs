using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyERP.Modules.System.Application;
using MyERP.Modules.System.Application.Authorization;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Paging;
using MyERP.Shared.Security;

namespace MyERP.Modules.System.Api;

/// <summary>建立用戶；帳號 3–100 個 ASCII 字母／數字／<c>.</c>／<c>_</c>／<c>-</c>，密碼須符合密碼政策。</summary>
public sealed record UserCreateRequest(string Username, string Password);

/// <summary>啟用／停用用戶，須帶讀取時的 <c>version</c>。帳號建立後不可修改。</summary>
public sealed record UserUpdateRequest(bool IsActive, int Version);

/// <summary>管理員重設的新密碼（須符合密碼政策）。</summary>
public sealed record UserPasswordResetRequest(string Password);

/// <summary>
/// 用戶維護（REQ_F4118.23.5–8）。範圍為 <c>X-Company-Id</c> 指定的公司：只列出及可修改在該公司有用戶公司關聯的用戶，
/// 其他用戶一律回 404（ADR-016）。回應不含密碼雜湊、失敗次數或鎖定時間。
/// </summary>
[ApiController]
[Route("api/v1/sys/users")]
[Produces("application/json")]
[Tags("SYS - User")]
public sealed class UsersController(SystemUserService users, AuthorityGuard guard, ICurrentUser user, ICurrentCompany company) : ControllerBase
{
    /// <summary>目前公司的用戶（含已停用）：分頁、關鍵字（帳號包含，不分大小寫）、排序 <c>username</c>／<c>-username</c>。</summary>
    [HttpGet]
    [RequirePermission(SystemPermissions.UserView)]
    [ProducesResponseType<PagedResult<UserSummary>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public Task<PagedResult<UserSummary>> List([FromQuery] PagedRequest request, CancellationToken cancellationToken) =>
        users.ListInCompanyAsync(request, company.CompanyId!.Value, cancellationToken);

    /// <summary>建立用戶並加入目前公司（設為其預設公司，尚無角色）；帳號全系統不分大小寫唯一（含已刪除）。</summary>
    [HttpPost]
    [RequirePermission(SystemPermissions.UserCreate)]
    [ProducesResponseType<UserSummary>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserSummary>> Create(UserCreateRequest input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        var created = await users.CreateInCompanyAsync(input.Username, input.Password, company.CompanyId!.Value, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    /// <summary>
    /// 啟用／停用用戶；停用後不可登入。不可停用自己（422 <c>SYS.USER.SELF_DEACTIVATION</c>）。
    /// 帳號狀態影響用戶所屬的每間公司：操作者在每間都要有權限且用戶角色不超出操作者權限，否則 403（ADR-032）。
    /// </summary>
    [HttpPut("{id:long}")]
    [RequirePermission(SystemPermissions.UserEdit)]
    [ProducesResponseType<UserSummary>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<UserSummary> Update(long id, UserUpdateRequest input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        await guard.EnsureCanManageUserAsync(user.UserId!.Value, id, company.CompanyId!.Value, SystemPermissions.UserEdit, cancellationToken);
        return await users.UpdateInCompanyAsync(id, input.IsActive, input.Version, company.CompanyId!.Value, user.UserId!.Value, cancellationToken);
    }

    /// <summary>重設密碼：依密碼政策驗證，重新起算密碼到期並清除鎖定。跨公司檢查同修改（403，ADR-032）。</summary>
    [HttpPost("{id:long}/reset-password")]
    [RequirePermission(SystemPermissions.UserResetPassword)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResetPassword(long id, UserPasswordResetRequest input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        await guard.EnsureCanManageUserAsync(user.UserId!.Value, id, company.CompanyId!.Value, SystemPermissions.UserResetPassword, cancellationToken);
        await users.ResetPasswordInCompanyAsync(id, input.Password, company.CompanyId!.Value, cancellationToken);
        return NoContent();
    }

    /// <summary>解除鎖定並清除失敗計數（未鎖定亦回 204）。跨公司檢查同修改（403，ADR-032）。</summary>
    [HttpPost("{id:long}/unlock")]
    [RequirePermission(SystemPermissions.UserEdit)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Unlock(long id, CancellationToken cancellationToken)
    {
        await guard.EnsureCanManageUserAsync(user.UserId!.Value, id, company.CompanyId!.Value, SystemPermissions.UserEdit, cancellationToken);
        await users.UnlockInCompanyAsync(id, company.CompanyId!.Value, cancellationToken);
        return NoContent();
    }
}
