using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Paging;

namespace MyERP.Modules.MasterData.Api;

public sealed record SettlementMethodResponse(long Id, string Code, string Name, bool IsActive, int Version);
public sealed record CreateSettlementMethodRequest(string Code, string Name);
public sealed record UpdateSettlementMethodRequest(string Name, bool IsActive, int Version);

/// <summary>結算方式（BlueWhale 4.1.1.8.14）；收付款明細另選資金帳戶，兩者不綁定（ADR-040）。</summary>
[ApiController]
[Route("api/v1/md/settlement-methods")]
[Produces("application/json")]
[Tags("MD - Settlement Methods")]
public sealed class SettlementMethodsController(SettlementMethodService methods) : ControllerBase
{
    /// <summary>分頁列出目前公司未刪除的結算方式（含停用），可依代碼或名稱搜尋、按代碼或名稱排序。</summary>
    [HttpGet]
    [RequirePermission(MasterDataPermissions.SettlementMethodView)]
    [ProducesResponseType<PagedResult<SettlementMethodResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<PagedResult<SettlementMethodResponse>> List([FromQuery] PagedRequest request, CancellationToken token)
    {
        var page = await methods.ListAsync(request, token);
        return new PagedResult<SettlementMethodResponse>([.. page.Items.Select(Map)], page.Page, page.PageSize, page.TotalCount);
    }

    /// <summary>建立結算方式；代碼公司內永久唯一（409 <c>MD.SETTLEMENT_METHOD.DUPLICATE_CODE</c>）。</summary>
    [HttpPost]
    [RequirePermission(MasterDataPermissions.SettlementMethodCreate)]
    [ProducesResponseType<SettlementMethodResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SettlementMethodResponse>> Create(CreateSettlementMethodRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        return StatusCode(StatusCodes.Status201Created, Map(await methods.CreateAsync(request.Code, request.Name, token)));
    }

    /// <summary>按版本修改名稱與啟用狀態；代碼不可修改。</summary>
    [HttpPut("{id:long}")]
    [RequirePermission(MasterDataPermissions.SettlementMethodEdit)]
    [ProducesResponseType<SettlementMethodResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<SettlementMethodResponse> Update(long id, UpdateSettlementMethodRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Map(await methods.UpdateAsync(id, request.Name, request.IsActive, request.Version, token));
    }

    /// <summary>按版本軟刪除，代碼仍保留；已被單據引用 409 <c>MD.SETTLEMENT_METHOD.IN_USE</c>，只可停用。</summary>
    [HttpDelete("{id:long}")]
    [RequirePermission(MasterDataPermissions.SettlementMethodDelete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(long id, [FromQuery] int version, CancellationToken token)
    {
        await methods.DeleteAsync(id, version, token);
        return NoContent();
    }

    private static SettlementMethodResponse Map(SettlementMethod entity) =>
        new(entity.Id, entity.Code, entity.Name, entity.IsActive, entity.Version);
}

public sealed record PaymentTermResponse(long Id, string Code, string Name, PaymentTermDueType DueType, int? Days, int? ClosingDay,
    int? Months, int? PaymentDay, bool IsActive, int Version);
public sealed record CreatePaymentTermRequest(string Code, string Name, PaymentTermDueType DueType, int? Days, int? ClosingDay,
    int? Months, int? PaymentDay);
public sealed record UpdatePaymentTermRequest(string Name, PaymentTermDueType DueType, int? Days, int? ClosingDay, int? Months,
    int? PaymentDay, bool IsActive, int Version);
public sealed record PaymentTermDueDateResponse(string Code, DateOnly DocumentDate, DateOnly DueDate);

/// <summary>付款條件：由單據日期推算到期日的規則（ADR-040）。</summary>
[ApiController]
[Route("api/v1/md/payment-terms")]
[Produces("application/json")]
[Tags("MD - Payment Terms")]
public sealed class PaymentTermsController(PaymentTermService terms, IPaymentTermLookup lookup) : ControllerBase
{
    /// <summary>分頁列出目前公司未刪除的付款條件（含停用），可依代碼或名稱搜尋、按代碼或名稱排序。</summary>
    [HttpGet]
    [RequirePermission(MasterDataPermissions.PaymentTermView)]
    [ProducesResponseType<PagedResult<PaymentTermResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<PagedResult<PaymentTermResponse>> List([FromQuery] PagedRequest request, CancellationToken token)
    {
        var page = await terms.ListAsync(request, token);
        return new PagedResult<PaymentTermResponse>([.. page.Items.Select(Map)], page.Page, page.PageSize, page.TotalCount);
    }

    /// <summary>
    /// 建立付款條件。<c>NetDays</c> 需 <c>days</c>（0–365）；<c>ClosingDays</c> 需 <c>closingDay</c>（1–31，31 = 月底）與 <c>days</c>；
    /// <c>ClosingMonthDay</c> 需 <c>closingDay</c>、<c>months</c>（0–12）與 <c>paymentDay</c>（1–31）。不適用的欄位忽略並存 null。
    /// </summary>
    [HttpPost]
    [RequirePermission(MasterDataPermissions.PaymentTermCreate)]
    [ProducesResponseType<PaymentTermResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PaymentTermResponse>> Create(CreatePaymentTermRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        var rule = new PaymentTermRule(request.DueType, request.Days, request.ClosingDay, request.Months, request.PaymentDay);
        return StatusCode(StatusCodes.Status201Created, Map(await terms.CreateAsync(request.Code, request.Name, rule, token)));
    }

    /// <summary>按版本修改名稱、到期規則與啟用狀態；代碼不可修改。已存單據的到期日不受影響。</summary>
    [HttpPut("{id:long}")]
    [RequirePermission(MasterDataPermissions.PaymentTermEdit)]
    [ProducesResponseType<PaymentTermResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<PaymentTermResponse> Update(long id, UpdatePaymentTermRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        var rule = new PaymentTermRule(request.DueType, request.Days, request.ClosingDay, request.Months, request.PaymentDay);
        return Map(await terms.UpdateAsync(id, request.Name, rule, request.IsActive, request.Version, token));
    }

    /// <summary>按版本軟刪除，代碼仍保留；已被單據引用 409 <c>MD.PAYMENT_TERM.IN_USE</c>，只可停用。</summary>
    [HttpDelete("{id:long}")]
    [RequirePermission(MasterDataPermissions.PaymentTermDelete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(long id, [FromQuery] int version, CancellationToken token)
    {
        await terms.DeleteAsync(id, version, token);
        return NoContent();
    }

    /// <summary>按啟用的付款條件計算單據日期的到期日（與 <see cref="IPaymentTermLookup"/> 相同規則）；不存在 404，停用 422。</summary>
    [HttpGet("due-date")]
    [RequirePermission(MasterDataPermissions.PaymentTermView)]
    [ProducesResponseType<PaymentTermDueDateResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<PaymentTermDueDateResponse> DueDate([FromQuery] string code, [FromQuery] DateOnly documentDate,
        CancellationToken token)
    {
        var dueDate = await lookup.CalculateDueDateAsync(code, documentDate, token);
        return new PaymentTermDueDateResponse(code.Trim().ToUpperInvariant(), documentDate, dueDate);
    }

    private static PaymentTermResponse Map(PaymentTerm x) => new(x.Id, x.Code, x.Name, Enum.Parse<PaymentTermDueType>(x.DueType),
        x.Days, x.ClosingDay, x.Months, x.PaymentDay, x.IsActive, x.Version);
}
