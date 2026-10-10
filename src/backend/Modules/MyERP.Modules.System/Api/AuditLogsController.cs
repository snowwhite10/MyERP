using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyERP.Modules.System.Application.Auditing;
using MyERP.Modules.System.Application.Authorization;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Paging;
using MyERP.Shared.Security;

namespace MyERP.Modules.System.Api;

/// <summary>稽核日誌查詢（REQ_F4118.27.4）。只讀：sys_audit_log 只增不改，沒有修改或刪除端點。</summary>
[ApiController]
[Route("api/v1/sys/audit-logs")]
[Produces("application/json")]
[Tags("SYS - Audit")]
public sealed class AuditLogsController(AuditLogService auditLogs, ICurrentCompany company) : ControllerBase
{
    /// <summary>
    /// 目前公司的資料變更與安全事件：分頁；篩選日期範圍 <c>from</c>／<c>to</c>（含頭尾）、<c>category</c>、<c>action</c>、
    /// <c>tableName</c>＋<c>recordId</c>（單據修改歷史）、<c>userId</c>；排序 <c>occurredAt</c>／<c>-occurredAt</c>（預設）。
    /// </summary>
    [HttpGet]
    [RequirePermission(SystemPermissions.AuditView)]
    [ProducesResponseType<PagedResult<AuditLogItem>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public Task<PagedResult<AuditLogItem>> List([FromQuery] AuditLogQuery query, CancellationToken cancellationToken) =>
        auditLogs.ListAsync(query, company.CompanyId!.Value, cancellationToken);
}
