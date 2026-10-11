using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyERP.Modules.System.Application;
using MyERP.Modules.System.Application.Authorization;
using MyERP.Modules.System.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Paging;
using MyERP.Shared.Security;

namespace MyERP.Modules.System.Api;

/// <summary>公司或分店（REQ_F4118.20、REQ_F4118.21）。</summary>
public sealed record OrganizationResponse(long Id, string Code, string Name, bool IsActive, int Version);

/// <summary>部門（REQ_F4118.22）。</summary>
public sealed record DepartmentResponse(
    long Id, string Code, string Name, long? BranchId, long? ParentDepartmentId, bool IsActive, int Version);

/// <summary>建立公司或分店；代碼保存為大寫，只可用英數字、<c>-</c>、<c>_</c>（最多 30 字）。</summary>
public sealed record OrganizationCreateRequest(string Code, string Name);

/// <summary>修改名稱或啟用狀態，須帶讀取時的 <c>version</c>。</summary>
public sealed record OrganizationUpdateRequest(string Name, bool IsActive, int Version);

public sealed record DepartmentCreateRequest(string Code, string Name, long? BranchId, long? ParentDepartmentId);

public sealed record DepartmentUpdateRequest(string Name, long? BranchId, long? ParentDepartmentId, bool IsActive, int Version);

/// <summary>公司維護（REQ_F4118.20）。只列出及可修改目前用戶獲授權的公司。</summary>
[ApiController]
[Route("api/v1/sys/companies")]
[Produces("application/json")]
[Tags("SYS - Organization")]
public sealed class CompaniesController(
    OrganizationService organization, UserAuthorizationStore store, AuthorityGuard guard, ICurrentUser user, ICurrentCompany company) : ControllerBase
{
    /// <summary>
    /// 已授權公司列表：分頁、關鍵字（代碼／名稱）、排序 <c>code</c>／<c>name</c>（單一欄位）。
    /// <c>includeInactive=true</c> 時含已停用公司（可重新啟用）。
    /// </summary>
    [HttpGet]
    [RequirePermission(SystemPermissions.CompanyView)]
    [ProducesResponseType<PagedResult<OrganizationResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<PagedResult<OrganizationResponse>> List(
        [FromQuery] PagedRequest request, [FromQuery] bool includeInactive, CancellationToken cancellationToken)
    {
        var ids = await store.ListManagedCompanyIdsAsync(user.UserId!.Value, cancellationToken);
        var page = await organization.ListCompaniesAsync(request, ids, includeInactive, cancellationToken);
        return page.Map(ToResponse);
    }

    /// <summary>建立公司；建立者自動獲授權新公司，角色沿用其在目前公司的角色（ADR-015）。代碼全系統唯一。</summary>
    [HttpPost]
    [RequirePermission(SystemPermissions.CompanyCreate)]
    [ProducesResponseType<OrganizationResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrganizationResponse>> Create(OrganizationCreateRequest input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        var created = await organization.CreateCompanyForUserAsync(
            input.Code, input.Name, user.UserId!.Value, company.CompanyId!.Value, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, ToResponse(created));
    }

    /// <summary>
    /// 修改公司名稱或啟用／停用。不是用戶獲授權的公司一律回 404；
    /// 操作者在被修改的公司亦須有 <c>SYS.COMPANY.EDIT</c>，否則 403（ADR-032）。
    /// </summary>
    [HttpPut("{id:long}")]
    [RequirePermission(SystemPermissions.CompanyEdit)]
    [ProducesResponseType<OrganizationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<OrganizationResponse> Update(long id, OrganizationUpdateRequest input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!(await store.ListManagedCompanyIdsAsync(user.UserId!.Value, cancellationToken)).Contains(id))
        {
            throw new NotFoundException("SYS.COMPANY.NOT_FOUND", "Company", id);
        }

        await guard.EnsurePermissionAsync(user.UserId!.Value, id, SystemPermissions.CompanyEdit, cancellationToken);

        return ToResponse(await organization.UpdateCompanyAsync(id, input.Name, input.IsActive, input.Version, cancellationToken));
    }

    private static OrganizationResponse ToResponse(SystemCompany c) => new(c.Id, c.Code, c.Name, c.IsActive, c.Version);
}

internal static class PagedResultMapping
{
    /// <summary>把實體分頁結果轉成回應 DTO，不外露審計欄位。</summary>
    public static PagedResult<TOut> Map<TIn, TOut>(this PagedResult<TIn> page, Func<TIn, TOut> map) =>
        new([.. page.Items.Select(map)], page.Page, page.PageSize, page.TotalCount);
}

/// <summary>分店維護（REQ_F4118.21），範圍為 <c>X-Company-Id</c> 指定的公司。</summary>
[ApiController]
[Route("api/v1/sys/branches")]
[Produces("application/json")]
[Tags("SYS - Organization")]
public sealed class BranchesController(OrganizationService organization) : ControllerBase
{
    /// <summary>目前公司的分店（含已停用）：分頁、關鍵字、排序 <c>code</c>／<c>name</c>。</summary>
    [HttpGet]
    [RequirePermission(SystemPermissions.BranchView)]
    [ProducesResponseType<PagedResult<OrganizationResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<PagedResult<OrganizationResponse>> List([FromQuery] PagedRequest request, CancellationToken cancellationToken) =>
        (await organization.ListBranchesAsync(request, cancellationToken)).Map(ToResponse);

    /// <summary>建立分店；代碼在公司內唯一。</summary>
    [HttpPost]
    [RequirePermission(SystemPermissions.BranchCreate)]
    [ProducesResponseType<OrganizationResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrganizationResponse>> Create(OrganizationCreateRequest input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        var created = await organization.CreateBranchAsync(input.Code, input.Name, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, ToResponse(created));
    }

    /// <summary>修改分店名稱或啟用／停用；其他公司的分店回 404。</summary>
    [HttpPut("{id:long}")]
    [RequirePermission(SystemPermissions.BranchEdit)]
    [ProducesResponseType<OrganizationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<OrganizationResponse> Update(long id, OrganizationUpdateRequest input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        return ToResponse(await organization.UpdateBranchAsync(id, input.Name, input.IsActive, input.Version, cancellationToken));
    }

    private static OrganizationResponse ToResponse(SystemBranch b) => new(b.Id, b.Code, b.Name, b.IsActive, b.Version);
}

/// <summary>部門維護（REQ_F4118.22），範圍為 <c>X-Company-Id</c> 指定的公司。</summary>
[ApiController]
[Route("api/v1/sys/departments")]
[Produces("application/json")]
[Tags("SYS - Organization")]
public sealed class DepartmentsController(OrganizationService organization) : ControllerBase
{
    /// <summary>目前公司的部門（含已停用）：分頁、關鍵字、排序 <c>code</c>／<c>name</c>。</summary>
    [HttpGet]
    [RequirePermission(SystemPermissions.DepartmentView)]
    [ProducesResponseType<PagedResult<DepartmentResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<PagedResult<DepartmentResponse>> List([FromQuery] PagedRequest request, CancellationToken cancellationToken) =>
        (await organization.ListDepartmentsAsync(request, cancellationToken)).Map(ToResponse);

    /// <summary>建立部門；所屬分店與上級部門必須是本公司的啟用資料，上級不可形成循環。</summary>
    [HttpPost]
    [RequirePermission(SystemPermissions.DepartmentCreate)]
    [ProducesResponseType<DepartmentResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DepartmentResponse>> Create(DepartmentCreateRequest input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        var created = await organization.CreateDepartmentAsync(
            input.Code, input.Name, input.BranchId, input.ParentDepartmentId, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, ToResponse(created));
    }

    /// <summary>修改部門名稱、分店、上級部門或啟用狀態；其他公司的部門回 404。</summary>
    [HttpPut("{id:long}")]
    [RequirePermission(SystemPermissions.DepartmentEdit)]
    [ProducesResponseType<DepartmentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<DepartmentResponse> Update(long id, DepartmentUpdateRequest input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        return ToResponse(await organization.UpdateDepartmentAsync(
            id, input.Name, input.BranchId, input.ParentDepartmentId, input.IsActive, input.Version, cancellationToken));
    }

    private static DepartmentResponse ToResponse(SystemDepartment d) =>
        new(d.Id, d.Code, d.Name, d.BranchId, d.ParentDepartmentId, d.IsActive, d.Version);
}
