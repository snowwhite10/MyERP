using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Paging;
using MyERP.Shared.Time;

namespace MyERP.Modules.MasterData.Api;

public sealed record TaxCodeResponse(long Id, string Code, string Name, string TaxType, decimal? CurrentRatePercent,
    bool IsActive, int Version);
public sealed record CreateTaxCodeRequest(string Code, string Name, string TaxType);
public sealed record UpdateTaxCodeRequest(string Name, string TaxType, bool IsActive, int Version);

/// <summary>公司稅別主檔；代碼建立後不可修改（ADR-038）。</summary>
[ApiController]
[Route("api/v1/md/tax-codes")]
[Produces("application/json")]
[Tags("MD - Tax Codes")]
public sealed class TaxCodesController(TaxCodeService codes, IClock clock) : ControllerBase
{
    [HttpGet]
    [RequirePermission(MasterDataPermissions.TaxCodeView)]
    [ProducesResponseType<PagedResult<TaxCodeResponse>>(StatusCodes.Status200OK)]
    public async Task<PagedResult<TaxCodeResponse>> List([FromQuery] PagedRequest request, [FromQuery] DateOnly? on,
        CancellationToken token)
    {
        var page = await codes.ListAsync(request, token);
        var rates = await codes.RatesOnAsync(page.Items.Select(x => x.Id), on ?? clock.Today, token);
        return new PagedResult<TaxCodeResponse>([.. page.Items.Select(x =>
            Map(x, rates.TryGetValue(x.Id, out var rate) ? rate : null))],
            page.Page, page.PageSize, page.TotalCount);
    }

    [HttpPost]
    [RequirePermission(MasterDataPermissions.TaxCodeCreate)]
    [ProducesResponseType<TaxCodeResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TaxCodeResponse>> Create(CreateTaxCodeRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        return StatusCode(StatusCodes.Status201Created,
            Map(await codes.CreateAsync(request.Code, request.Name, request.TaxType, token), null));
    }

    [HttpPut("{id:long}")]
    [RequirePermission(MasterDataPermissions.TaxCodeEdit)]
    [ProducesResponseType<TaxCodeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<TaxCodeResponse> Update(long id, UpdateTaxCodeRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Map(await codes.UpdateAsync(id, request.Name, request.TaxType, request.IsActive, request.Version, token), null);
    }

    [HttpDelete("{id:long}")]
    [RequirePermission(MasterDataPermissions.TaxCodeDelete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(long id, [FromQuery] int version, CancellationToken token)
    {
        await codes.DeleteAsync(id, version, token);
        return NoContent();
    }

    private static TaxCodeResponse Map(TaxCode x, decimal? rate) =>
        new(x.Id, x.Code, x.Name, x.TaxType, rate, x.IsActive, x.Version);
}

public sealed record TaxRateResponse(long Id, long TaxCodeId, DateOnly EffectiveFrom, DateOnly? EffectiveTo,
    decimal RatePercent, int Version);
public sealed record CreateTaxRateRequest(DateOnly EffectiveFrom, DateOnly? EffectiveTo, decimal RatePercent);
public sealed record UpdateTaxRateRequest(DateOnly EffectiveFrom, DateOnly? EffectiveTo, decimal RatePercent, int Version);

/// <summary>稅率期間，百分比 0–100；同稅別有效期間不可重疊。</summary>
[ApiController]
[Route("api/v1/md/tax-codes/{taxCodeId:long}/rates")]
[Produces("application/json")]
[Tags("MD - Tax Codes")]
public sealed class TaxRatesController(TaxRateService rates) : ControllerBase
{
    [HttpGet]
    [RequirePermission(MasterDataPermissions.TaxCodeView)]
    [ProducesResponseType<PagedResult<TaxRateResponse>>(StatusCodes.Status200OK)]
    public async Task<PagedResult<TaxRateResponse>> List(long taxCodeId, [FromQuery] PagedRequest request,
        [FromQuery] DateOnly? on, CancellationToken token)
    {
        var page = await rates.ListAsync(taxCodeId, request, on, token);
        return new PagedResult<TaxRateResponse>([.. page.Items.Select(Map)], page.Page, page.PageSize, page.TotalCount);
    }

    [HttpPost]
    [RequirePermission(MasterDataPermissions.TaxCodeEdit)]
    [ProducesResponseType<TaxRateResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TaxRateResponse>> Create(long taxCodeId, CreateTaxRateRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        return StatusCode(StatusCodes.Status201Created,
            Map(await rates.CreateAsync(taxCodeId, request.EffectiveFrom, request.EffectiveTo, request.RatePercent, token)));
    }

    [HttpPut("{id:long}")]
    [RequirePermission(MasterDataPermissions.TaxCodeEdit)]
    [ProducesResponseType<TaxRateResponse>(StatusCodes.Status200OK)]
    public async Task<TaxRateResponse> Update(long taxCodeId, long id, UpdateTaxRateRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Map(await rates.UpdateAsync(taxCodeId, id, request.EffectiveFrom, request.EffectiveTo,
            request.RatePercent, request.Version, token));
    }

    [HttpDelete("{id:long}")]
    [RequirePermission(MasterDataPermissions.TaxCodeEdit)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(long taxCodeId, long id, [FromQuery] int version, CancellationToken token)
    {
        await rates.DeleteAsync(taxCodeId, id, version, token);
        return NoContent();
    }

    private static TaxRateResponse Map(TaxRate x) =>
        new(x.Id, x.TaxCodeId, x.EffectiveFrom, x.EffectiveTo, x.RatePercent, x.Version);
}

public sealed record TaxRateLookupResponse(string TaxCode, TaxDirection Direction, DateOnly Date, decimal RatePercent);
public sealed record CalculateTaxRequest(string TaxCode, TaxDirection Direction, DateOnly Date,
    decimal Amount, bool PriceIncludesTax, string CurrencyCode);

/// <summary>
/// 按業務日期、方向查詢稅率並依幣別小數位試算單行稅額。這是稅別維護頁的試算工具，
/// 可核對已停用稅別的歷史稅率（ADR-049 第 2 點）；其他模組經契約查詢時預設拒絕停用稅別。
/// </summary>
[ApiController]
[Route("api/v1/md/tax-rates")]
[Produces("application/json")]
[Tags("MD - Tax Codes")]
public sealed class TaxRateLookupController(ITaxRateLookup lookup) : ControllerBase
{
    [HttpGet("lookup")]
    [RequirePermission(MasterDataPermissions.TaxCodeView)]
    [ProducesResponseType<TaxRateLookupResponse>(StatusCodes.Status200OK)]
    public async Task<TaxRateLookupResponse> Lookup([FromQuery] string taxCode, [FromQuery] TaxDirection direction,
        [FromQuery] DateOnly date, CancellationToken token) =>
        new(taxCode.Trim().ToUpperInvariant(), direction, date,
            await lookup.GetRatePercentAsync(taxCode, direction, date, includeInactive: true, token));

    [HttpPost("calculate")]
    [RequirePermission(MasterDataPermissions.TaxCodeView)]
    [ProducesResponseType<TaxCalculation>(StatusCodes.Status200OK)]
    public Task<TaxCalculation> Calculate(CalculateTaxRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        return lookup.CalculateAsync(request.TaxCode, request.Direction, request.Date,
            request.Amount, request.PriceIncludesTax, request.CurrencyCode, includeInactive: true, token);
    }
}
