using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Approvals;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Paging;

namespace MyERP.Modules.MasterData.Api;

public sealed record CustomerResponse(long Id, string Code, string Name, long? CategoryId, bool IsActive, int Version, PartnerReviewStatus ReviewStatus,
    long? DefaultPaymentTermId, long? DefaultSettlementMethodId);
public sealed record CreateCustomerRequest(string Code, string Name, long? CategoryId,
    long? DefaultPaymentTermId = null, long? DefaultSettlementMethodId = null);
public sealed record UpdateCustomerRequest(string Name, bool IsActive, long? CategoryId, int Version,
    long? DefaultPaymentTermId = null, long? DefaultSettlementMethodId = null);
public sealed record CustomerVersionRequest(int Version);

/// <summary>目前公司的客戶主檔；分類須屬同一公司。</summary>
[ApiController]
[Route("api/v1/md/customers")]
[Produces("application/json")]
[Tags("MD - Customers")]
public sealed class CustomersController(CustomerService customers, CustomerApprovalService approval) : ControllerBase
{
    /// <summary>依代碼或名稱搜尋客戶；含停用但排除軟刪除資料。</summary>
    [HttpGet]
    [RequirePermission(MasterDataPermissions.CustomerView)]
    [ProducesResponseType<PagedResult<CustomerResponse>>(StatusCodes.Status200OK)]
    public async Task<PagedResult<CustomerResponse>> List([FromQuery] PagedRequest request, CancellationToken token)
    {
        var page = await customers.ListAsync(request, token);
        return new PagedResult<CustomerResponse>([.. page.Items.Select(Map)], page.Page, page.PageSize, page.TotalCount);
    }

    /// <summary>建立代碼在公司內唯一的客戶。</summary>
    [HttpPost]
    [RequirePermission(MasterDataPermissions.CustomerCreate)]
    [ProducesResponseType<CustomerResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CustomerResponse>> Create(CreateCustomerRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        return StatusCode(StatusCodes.Status201Created,
            Map(await customers.CreateAsync(request.Code, request.Name, request.CategoryId,
                new PartnerDefaults(request.DefaultPaymentTermId, request.DefaultSettlementMethodId), token)));
    }

    /// <summary>按版本修改名稱、分類、預設付款條件與結算方式及啟用狀態；代碼不可修改，缺少的預設值視為清除。</summary>
    [HttpPut("{id:long}")]
    [RequirePermission(MasterDataPermissions.CustomerEdit)]
    [ProducesResponseType<CustomerResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<CustomerResponse> Update(long id, UpdateCustomerRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Map(await customers.UpdateAsync(id, request.Name, request.IsActive, request.CategoryId,
            new PartnerDefaults(request.DefaultPaymentTermId, request.DefaultSettlementMethodId), request.Version, token));
    }

    /// <summary>將待審或遭駁回的客戶送交 SYS 審批流程；無適用步驟時立即核准。</summary>
    [HttpPost("{id:long}/submit")]
    [RequirePermission(MasterDataPermissions.CustomerApprove)]
    [ProducesResponseType<ApprovalState>(StatusCodes.Status200OK)]
    public Task<ApprovalState> Submit(long id, CustomerVersionRequest request, CancellationToken token) =>
        approval.SubmitAsync(id, request.Version, token);

    /// <summary>以版本號軟刪除未被單據引用、且不在審批中的客戶。</summary>
    [HttpDelete("{id:long}")]
    [RequirePermission(MasterDataPermissions.CustomerDelete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(long id, [FromQuery] int version, CancellationToken token)
    {
        await customers.DeleteAsync(id, version, token);
        return NoContent();
    }

    private static CustomerResponse Map(Customer entity) =>
        new(entity.Id, entity.Code, entity.Name, entity.CategoryId, entity.IsActive, entity.Version, entity.ReviewStatus,
            entity.DefaultPaymentTermId, entity.DefaultSettlementMethodId);
}
