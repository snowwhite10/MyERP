using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Paging;

namespace MyERP.Modules.MasterData.Api;

public sealed record CurrencyResponse(long Id, string Code, string Name, string? Symbol, int DecimalPlaces, bool IsBase,
    bool IsActive, int Version);
public sealed record CreateCurrencyRequest(string Code, string Name, string? Symbol, int DecimalPlaces, bool IsBase);
public sealed record UpdateCurrencyRequest(string Name, string? Symbol, int DecimalPlaces, bool IsBase, bool IsActive, int Version);

/// <summary>目前公司的幣別與本位幣（ADR-037）。</summary>
[ApiController]
[Route("api/v1/md/currencies")]
[Produces("application/json")]
[Tags("MD - Currencies")]
public sealed class CurrenciesController(CurrencyService currencies) : ControllerBase
{
    /// <summary>依代碼或名稱搜尋幣別；含停用幣別。</summary>
    [HttpGet]
    [RequirePermission(MasterDataPermissions.CurrencyView)]
    [ProducesResponseType<PagedResult<CurrencyResponse>>(StatusCodes.Status200OK)]
    public async Task<PagedResult<CurrencyResponse>> List([FromQuery] PagedRequest request, CancellationToken token)
    {
        var page = await currencies.ListAsync(request, token);
        return new PagedResult<CurrencyResponse>([.. page.Items.Select(Map)], page.Page, page.PageSize, page.TotalCount);
    }

    /// <summary>建立幣別；代碼為 ISO 4217 三碼，公司內永久唯一。設為本位幣時取代原本位幣（已有匯率時 409 <c>MD.CURRENCY.BASE_LOCKED</c>）。</summary>
    [HttpPost]
    [RequirePermission(MasterDataPermissions.CurrencyCreate)]
    [ProducesResponseType<CurrencyResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CurrencyResponse>> Create(CreateCurrencyRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        return StatusCode(StatusCodes.Status201Created, Map(await currencies.CreateAsync(
            request.Code, request.Name, request.Symbol, request.DecimalPlaces, request.IsBase, token)));
    }

    /// <summary>按版本修改幣別；代碼不可修改。本位幣不可取消（422 <c>BASE_REQUIRED</c>）或停用（422 <c>BASE_INACTIVE</c>）。</summary>
    [HttpPut("{id:long}")]
    [RequirePermission(MasterDataPermissions.CurrencyEdit)]
    [ProducesResponseType<CurrencyResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<CurrencyResponse> Update(long id, UpdateCurrencyRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Map(await currencies.UpdateAsync(id, request.Name, request.Symbol, request.DecimalPlaces, request.IsBase,
            request.IsActive, request.Version, token));
    }

    /// <summary>按版本軟刪除；本位幣 409 <c>MD.CURRENCY.IS_BASE</c>，有匯率 409 <c>MD.CURRENCY.IN_USE</c>。</summary>
    [HttpDelete("{id:long}")]
    [RequirePermission(MasterDataPermissions.CurrencyDelete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(long id, [FromQuery] int version, CancellationToken token)
    {
        await currencies.DeleteAsync(id, version, token);
        return NoContent();
    }

    private static CurrencyResponse Map(Currency entity) => new(entity.Id, entity.Code, entity.Name, entity.Symbol,
        entity.DecimalPlaces, entity.IsBase, entity.IsActive, entity.Version);
}

public sealed record ExchangeRateResponse(long Id, long CurrencyId, DateOnly EffectiveFrom, DateOnly? EffectiveTo, decimal Rate, int Version);
public sealed record CreateExchangeRateRequest(DateOnly EffectiveFrom, DateOnly? EffectiveTo, decimal Rate);
public sealed record UpdateExchangeRateRequest(DateOnly EffectiveFrom, DateOnly? EffectiveTo, decimal Rate, int Version);
public sealed record ExchangeRateLookupResponse(string CurrencyCode, string BaseCurrencyCode, DateOnly Date, decimal Rate);

/// <summary>外幣匯率：1 外幣 = rate 本位幣；查看需要幣別查看權限，維護需要幣別修改權限（ADR-037）。</summary>
[ApiController]
[Route("api/v1/md/currencies/{currencyId:long}/rates")]
[Produces("application/json")]
[Tags("MD - Currencies")]
public sealed class ExchangeRatesController(ExchangeRateService rates) : ControllerBase
{
    /// <summary>某幣別的匯率，預設按生效日新到舊（<c>sort=effectiveFrom</c> 舊到新）；<c>on</c> 只列出該日適用的匯率。</summary>
    [HttpGet]
    [RequirePermission(MasterDataPermissions.CurrencyView)]
    [ProducesResponseType<PagedResult<ExchangeRateResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<PagedResult<ExchangeRateResponse>> List(long currencyId, [FromQuery] PagedRequest request,
        [FromQuery] DateOnly? on, CancellationToken token)
    {
        var page = await rates.ListAsync(currencyId, request, on, token);
        return new PagedResult<ExchangeRateResponse>([.. page.Items.Select(Map)], page.Page, page.PageSize, page.TotalCount);
    }

    /// <summary>新增匯率；期間不可與同幣別其他匯率重疊（409 <c>MD.EXCHANGE_RATE.OVERLAP</c>），本位幣與停用幣別不可設匯率（422）。</summary>
    [HttpPost]
    [RequirePermission(MasterDataPermissions.CurrencyEdit)]
    [ProducesResponseType<ExchangeRateResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ExchangeRateResponse>> Create(long currencyId, CreateExchangeRateRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        return StatusCode(StatusCodes.Status201Created,
            Map(await rates.CreateAsync(currencyId, request.EffectiveFrom, request.EffectiveTo, request.Rate, token)));
    }

    /// <summary>按版本修改期間與匯率；幣別不可修改。</summary>
    [HttpPut("{id:long}")]
    [RequirePermission(MasterDataPermissions.CurrencyEdit)]
    [ProducesResponseType<ExchangeRateResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ExchangeRateResponse> Update(long currencyId, long id, UpdateExchangeRateRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Map(await rates.UpdateAsync(currencyId, id, request.EffectiveFrom, request.EffectiveTo, request.Rate, request.Version, token));
    }

    /// <summary>按版本軟刪除匯率；已存單據自己保存成交匯率，不受影響。</summary>
    [HttpDelete("{id:long}")]
    [RequirePermission(MasterDataPermissions.CurrencyEdit)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(long currencyId, long id, [FromQuery] int version, CancellationToken token)
    {
        await rates.DeleteAsync(currencyId, id, version, token);
        return NoContent();
    }

    private static ExchangeRateResponse Map(ExchangeRate entity) =>
        new(entity.Id, entity.CurrencyId, entity.EffectiveFrom, entity.EffectiveTo, entity.Rate, entity.Version);
}

/// <summary>按業務日期查詢匯率（與 <see cref="ICurrencyLookup"/> 相同規則）。</summary>
[ApiController]
[Route("api/v1/md/exchange-rates")]
[Produces("application/json")]
[Tags("MD - Currencies")]
public sealed class ExchangeRateLookupController(ICurrencyLookup lookup) : ControllerBase
{
    /// <summary>
    /// 回傳 <c>date</c> 適用的匯率（1 外幣 = rate 本位幣，本位幣為 1）。幣別不存在 404；沒有本位幣或沒有涵蓋該日的匯率 422。
    /// </summary>
    [HttpGet("lookup")]
    [RequirePermission(MasterDataPermissions.CurrencyView)]
    [ProducesResponseType<ExchangeRateLookupResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ExchangeRateLookupResponse> Lookup([FromQuery] string currencyCode, [FromQuery] DateOnly date, CancellationToken token)
    {
        var rate = await lookup.GetRateAsync(currencyCode, date, token);
        var baseCurrency = await lookup.GetBaseCurrencyAsync(token);
        return new ExchangeRateLookupResponse(currencyCode.Trim().ToUpperInvariant(), baseCurrency!.Code, date, rate);
    }
}
