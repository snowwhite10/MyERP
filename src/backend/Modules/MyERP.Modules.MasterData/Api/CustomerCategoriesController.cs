using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Paging;

namespace MyERP.Modules.MasterData.Api;

public sealed record CustomerCategoryResponse(long Id, string Code, string Name, bool IsActive, int Version);
public sealed record CreateCustomerCategoryRequest(string Code, string Name);
public sealed record UpdateCustomerCategoryRequest(string Name, bool IsActive, int Version);

/// <summary>客戶分類；所有查詢依目前授權公司的 X-Company-Id 隔離。</summary>
[ApiController]
[Route("api/v1/md/customer-categories")]
[Produces("application/json")]
[Tags("MD - Customer Categories")]
public sealed class CustomerCategoriesController(CustomerCategoryService categories) : ControllerBase
{
    /// <summary>分頁列出目前公司未刪除的分類，包含停用分類。</summary>
    [HttpGet]
    [RequirePermission(MasterDataPermissions.CustomerCategoryView)]
    [ProducesResponseType<PagedResult<CustomerCategoryResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<PagedResult<CustomerCategoryResponse>> List([FromQuery] PagedRequest request, CancellationToken token)
    {
        var page = await categories.ListAsync(request, token);
        return new PagedResult<CustomerCategoryResponse>([.. page.Items.Select(Map)], page.Page, page.PageSize, page.TotalCount);
    }

    /// <summary>建立公司內代碼唯一的分類。</summary>
    [HttpPost]
    [RequirePermission(MasterDataPermissions.CustomerCategoryCreate)]
    [ProducesResponseType<CustomerCategoryResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CustomerCategoryResponse>> Create(CreateCustomerCategoryRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        var category = await categories.CreateAsync(request.Code, request.Name, token);
        return StatusCode(StatusCodes.Status201Created, Map(category));
    }

    /// <summary>按版本修改名稱及啟用狀態；代碼不可修改。</summary>
    [HttpPut("{id:long}")]
    [RequirePermission(MasterDataPermissions.CustomerCategoryEdit)]
    [ProducesResponseType<CustomerCategoryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<CustomerCategoryResponse> Update(long id, UpdateCustomerCategoryRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Map(await categories.UpdateAsync(id, request.Name, request.IsActive, request.Version, token));
    }

    /// <summary>按版本軟刪除；代碼仍保留。</summary>
    [HttpDelete("{id:long}")]
    [RequirePermission(MasterDataPermissions.CustomerCategoryDelete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(long id, [FromQuery] int version, CancellationToken token)
    {
        await categories.DeleteAsync(id, version, token);
        return NoContent();
    }

    private static CustomerCategoryResponse Map(CustomerCategory entity) =>
        new(entity.Id, entity.Code, entity.Name, entity.IsActive, entity.Version);
}
