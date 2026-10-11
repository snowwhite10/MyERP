using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyERP.Modules.System.Application.Authorization;
using MyERP.Modules.System.Application.Parameters;
using MyERP.Shared.Infrastructure.Security;

namespace MyERP.Modules.System.Api;

/// <summary>
/// 系統參數（REQ_F4118.17.1–2，BlueWhale Parameter Settings）。參數由各模組宣告，值按公司保存；
/// 沒有新增或刪除端點，未修改過的參數使用預設值。各模組在進程內經 <c>ISystemParameters</c> 讀取。
/// </summary>
[ApiController]
[Route("api/v1/sys/parameters")]
[Produces("application/json")]
[Tags("SYS - Parameters")]
public sealed class ParametersController(SystemParameterService parameters) : ControllerBase
{
    /// <summary>目前公司的全部參數（依模組、分組、鍵排序），可用 <c>module</c>（例如 <c>SYS</c>）篩選。</summary>
    [HttpGet]
    [RequirePermission(SystemPermissions.ParameterView)]
    [ProducesResponseType<IReadOnlyList<ParameterDetail>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public Task<IReadOnlyList<ParameterDetail>> List([FromQuery] string? module, CancellationToken cancellationToken) =>
        parameters.ListAsync(module, cancellationToken);

    /// <summary>
    /// 一次保存 1–200 個參數值（同一交易），每項帶 <c>version</c>（未修改過為 0）。未知鍵、重複鍵或值不合規則回 400
    /// （<c>items[i].key</c>／<c>items[i].value</c>），版本不符回 409。回傳保存後的完整清單。
    /// </summary>
    [HttpPut]
    [RequirePermission(SystemPermissions.ParameterEdit)]
    [ProducesResponseType<IReadOnlyList<ParameterDetail>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<IReadOnlyList<ParameterDetail>> Update(ParameterBatchUpdate input, CancellationToken cancellationToken) =>
        parameters.UpdateAsync(input, cancellationToken);
}
