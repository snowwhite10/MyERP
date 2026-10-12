using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Infrastructure.Security;

namespace MyERP.Modules.MasterData.Api;

public sealed record SupplierContactResponse(long Id, long SupplierId, string Name, string? Position, string? Mobile, string? Phone,
    string? Email, string? MessagingId, string? Note, bool IsDefault, bool IsActive, int Version);
public sealed record CreateSupplierContactRequest(string Name, string? Position, string? Mobile, string? Phone, string? Email,
    string? MessagingId, string? Note, bool IsDefault);
public sealed record UpdateSupplierContactRequest(string Name, string? Position, string? Mobile, string? Phone, string? Email,
    string? MessagingId, string? Note, bool IsDefault, bool IsActive, int Version);

/// <summary>供應商聯絡人；查看需要供應商查看權限，維護需要供應商修改權限（ADR-043）。</summary>
[ApiController]
[Route("api/v1/md/suppliers/{supplierId:long}/contacts")]
[Produces("application/json")]
[Tags("MD - Suppliers")]
public sealed class SupplierContactsController(SupplierContactService contacts) : ControllerBase
{
    /// <summary>列出供應商的聯絡人（含停用）；預設聯絡人排第一。</summary>
    [HttpGet]
    [RequirePermission(MasterDataPermissions.SupplierView)]
    [ProducesResponseType<IReadOnlyList<SupplierContactResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IReadOnlyList<SupplierContactResponse>> List(long supplierId, CancellationToken token) =>
        [.. (await contacts.ListAsync(supplierId, token)).Select(Map)];

    /// <summary>新增聯絡人；設為預設時自動取消原預設聯絡人。</summary>
    [HttpPost]
    [RequirePermission(MasterDataPermissions.SupplierEdit)]
    [ProducesResponseType<SupplierContactResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SupplierContactResponse>> Create(long supplierId, CreateSupplierContactRequest request,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        var input = new ContactInput(request.Name, request.Position, request.Mobile, request.Phone, request.Email,
            request.MessagingId, request.Note, request.IsDefault);
        return StatusCode(StatusCodes.Status201Created, Map(await contacts.CreateAsync(supplierId, input, token)));
    }

    /// <summary>按版本修改聯絡人；停用的聯絡人不可為預設（400 <c>isDefault</c>）。</summary>
    [HttpPut("{id:long}")]
    [RequirePermission(MasterDataPermissions.SupplierEdit)]
    [ProducesResponseType<SupplierContactResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<SupplierContactResponse> Update(long supplierId, long id, UpdateSupplierContactRequest request,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        var input = new ContactInput(request.Name, request.Position, request.Mobile, request.Phone, request.Email,
            request.MessagingId, request.Note, request.IsDefault);
        return Map(await contacts.UpdateAsync(supplierId, id, input, request.IsActive, request.Version, token));
    }

    /// <summary>按版本軟刪除聯絡人；刪除預設聯絡人後供應商暫無預設。</summary>
    [HttpDelete("{id:long}")]
    [RequirePermission(MasterDataPermissions.SupplierEdit)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(long supplierId, long id, [FromQuery] int version, CancellationToken token)
    {
        await contacts.DeleteAsync(supplierId, id, version, token);
        return NoContent();
    }

    private static SupplierContactResponse Map(SupplierContact x) => new(x.Id, x.PartnerId, x.Name, x.Position, x.Mobile, x.Phone,
        x.Email, x.MessagingId, x.Note, x.IsDefault, x.IsActive, x.Version);
}
