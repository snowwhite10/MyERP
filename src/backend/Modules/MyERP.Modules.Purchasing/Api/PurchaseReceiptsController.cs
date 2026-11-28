using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyERP.Modules.Purchasing.Application;
using MyERP.Shared.Infrastructure.Security;

namespace MyERP.Modules.Purchasing.Api;

/// <summary>Saved purchase receipt drafts; stock is posted only after SYS final approval.</summary>
[ApiController]
[Route("api/v1/pur/purchase-receipts")]
public sealed class PurchaseReceiptsController(PurchaseReceiptDraftService drafts,
    PurchaseReceiptSubmissionService submissions) : ControllerBase
{
    /// <summary>Creates a receipt draft from one approved order, with saved warehouse and base cost snapshots.</summary>
    [HttpPost]
    [RequirePermission(PurchasingPermissions.ReceiptCreate)]
    [ProducesResponseType<PurchaseReceiptReadDetail>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PurchaseReceiptReadDetail>> Create(PurchaseReceiptSaveInput input,
        CancellationToken token) => StatusCode(StatusCodes.Status201Created,
            await drafts.CreateAsync(input, token));

    /// <summary>Reads a saved inbound document in the VIEW data scope.</summary>
    [HttpGet("{id:long}")]
    [RequirePermission(PurchasingPermissions.ReceiptView)]
    [ProducesResponseType<PurchaseReceiptReadDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<PurchaseReceiptReadDetail> Get(long id, CancellationToken token) => drafts.GetAsync(id, token);

    /// <summary>Reads the same saved document in the EDIT data scope.</summary>
    [HttpGet("{id:long}/edit")]
    [RequirePermission(PurchasingPermissions.ReceiptEdit)]
    [ProducesResponseType<PurchaseReceiptReadDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<PurchaseReceiptReadDetail> GetForEdit(long id, CancellationToken token) =>
        drafts.GetForEditAsync(id, token);

    /// <summary>Replaces Draft or Rejected lines after version and locked source validation.</summary>
    [HttpPut("{id:long}")]
    [RequirePermission(PurchasingPermissions.ReceiptEdit)]
    [ProducesResponseType<PurchaseReceiptReadDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public Task<PurchaseReceiptReadDetail> Update(long id, [FromQuery] int version,
        PurchaseReceiptSaveInput input, CancellationToken token) => drafts.UpdateAsync(id, version, input, token);

    /// <summary>
    /// Submits a saved Draft with body <c>version</c>: one transaction reserves purchase order line capacity from saved
    /// lines and creates the SYS approval instance. Stock is not posted until final approval (WU-PUR-007b).
    /// </summary>
    [HttpPost("{id:long}/submit")]
    [RequirePermission(PurchasingPermissions.ReceiptSubmit)]
    [ProducesResponseType<PurchaseReceiptSubmissionResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public Task<PurchaseReceiptSubmissionResult> Submit(long id, [FromBody] PurchaseReceiptSubmitInput input,
        CancellationToken token) => submissions.SubmitAsync(id, input.Version, token);
}

/// <summary>SUBMIT request: the currently saved receipt version.</summary>
public sealed record PurchaseReceiptSubmitInput(int Version);
