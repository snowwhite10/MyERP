using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Infrastructure.Security;

namespace MyERP.Modules.MasterData.Api;

public sealed record CustomerContactResponse(long Id, long CustomerId, string Name, string? Position, string? Mobile, string? Phone,
    string? Email, string? MessagingId, string? Note, bool IsDefault, bool IsActive, int Version);
public sealed record CreateCustomerContactRequest(string Name, string? Position, string? Mobile, string? Phone, string? Email,
    string? MessagingId, string? Note, bool IsDefault);
public sealed record UpdateCustomerContactRequest(string Name, string? Position, string? Mobile, string? Phone, string? Email,
    string? MessagingId, string? Note, bool IsDefault, bool IsActive, int Version);

/// <summary>客戶聯絡人；查看需要客戶查看權限，維護需要客戶修改權限（ADR-043）。</summary>
[ApiController]
[Route("api/v1/md/customers/{customerId:long}/contacts")]
[Produces("application/json")]
[Tags("MD - Customers")]
public sealed class CustomerContactsController(CustomerContactService contacts) : ControllerBase
{
    /// <summary>列出客戶的聯絡人（含停用）；預設聯絡人排第一。</summary>
    [HttpGet]
    [RequirePermission(MasterDataPermissions.CustomerView)]
    [ProducesResponseType<IReadOnlyList<CustomerContactResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IReadOnlyList<CustomerContactResponse>> List(long customerId, CancellationToken token) =>
        [.. (await contacts.ListAsync(customerId, token)).Select(Map)];

    /// <summary>新增聯絡人；設為預設時自動取消原預設聯絡人。</summary>
    [HttpPost]
    [RequirePermission(MasterDataPermissions.CustomerEdit)]
    [ProducesResponseType<CustomerContactResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CustomerContactResponse>> Create(long customerId, CreateCustomerContactRequest request,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        var input = new ContactInput(request.Name, request.Position, request.Mobile, request.Phone, request.Email,
            request.MessagingId, request.Note, request.IsDefault);
        return StatusCode(StatusCodes.Status201Created, Map(await contacts.CreateAsync(customerId, input, token)));
    }

    /// <summary>按版本修改聯絡人；停用的聯絡人不可為預設（400 <c>isDefault</c>）。</summary>
    [HttpPut("{id:long}")]
    [RequirePermission(MasterDataPermissions.CustomerEdit)]
    [ProducesResponseType<CustomerContactResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<CustomerContactResponse> Update(long customerId, long id, UpdateCustomerContactRequest request,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        var input = new ContactInput(request.Name, request.Position, request.Mobile, request.Phone, request.Email,
            request.MessagingId, request.Note, request.IsDefault);
        return Map(await contacts.UpdateAsync(customerId, id, input, request.IsActive, request.Version, token));
    }

    /// <summary>按版本軟刪除聯絡人；刪除預設聯絡人後客戶暫無預設。</summary>
    [HttpDelete("{id:long}")]
    [RequirePermission(MasterDataPermissions.CustomerEdit)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(long customerId, long id, [FromQuery] int version, CancellationToken token)
    {
        await contacts.DeleteAsync(customerId, id, version, token);
        return NoContent();
    }

    private static CustomerContactResponse Map(CustomerContact x) => new(x.Id, x.PartnerId, x.Name, x.Position, x.Mobile, x.Phone,
        x.Email, x.MessagingId, x.Note, x.IsDefault, x.IsActive, x.Version);
}

public sealed record CustomerAddressResponse(long Id, long CustomerId, string Address, string? PostalCode, string? Recipient,
    string? Phone, bool IsDefault, bool IsActive, int Version);
public sealed record CreateCustomerAddressRequest(string Address, string? PostalCode, string? Recipient, string? Phone, bool IsDefault);
public sealed record UpdateCustomerAddressRequest(string Address, string? PostalCode, string? Recipient, string? Phone,
    bool IsDefault, bool IsActive, int Version);

/// <summary>客戶送貨地址；查看需要客戶查看權限，維護需要客戶修改權限（ADR-043）。</summary>
[ApiController]
[Route("api/v1/md/customers/{customerId:long}/addresses")]
[Produces("application/json")]
[Tags("MD - Customers")]
public sealed class CustomerAddressesController(CustomerAddressService addresses) : ControllerBase
{
    /// <summary>列出客戶的地址（含停用）；預設地址排第一。</summary>
    [HttpGet]
    [RequirePermission(MasterDataPermissions.CustomerView)]
    [ProducesResponseType<IReadOnlyList<CustomerAddressResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IReadOnlyList<CustomerAddressResponse>> List(long customerId, CancellationToken token) =>
        [.. (await addresses.ListAsync(customerId, token)).Select(Map)];

    /// <summary>新增地址；設為預設時自動取消原預設地址。</summary>
    [HttpPost]
    [RequirePermission(MasterDataPermissions.CustomerEdit)]
    [ProducesResponseType<CustomerAddressResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CustomerAddressResponse>> Create(long customerId, CreateCustomerAddressRequest request,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        var input = new AddressInput(request.Address, request.PostalCode, request.Recipient, request.Phone, request.IsDefault);
        return StatusCode(StatusCodes.Status201Created, Map(await addresses.CreateAsync(customerId, input, token)));
    }

    /// <summary>按版本修改地址；停用的地址不可為預設（400 <c>isDefault</c>）。</summary>
    [HttpPut("{id:long}")]
    [RequirePermission(MasterDataPermissions.CustomerEdit)]
    [ProducesResponseType<CustomerAddressResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<CustomerAddressResponse> Update(long customerId, long id, UpdateCustomerAddressRequest request,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        var input = new AddressInput(request.Address, request.PostalCode, request.Recipient, request.Phone, request.IsDefault);
        return Map(await addresses.UpdateAsync(customerId, id, input, request.IsActive, request.Version, token));
    }

    /// <summary>按版本軟刪除地址。</summary>
    [HttpDelete("{id:long}")]
    [RequirePermission(MasterDataPermissions.CustomerEdit)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(long customerId, long id, [FromQuery] int version, CancellationToken token)
    {
        await addresses.DeleteAsync(customerId, id, version, token);
        return NoContent();
    }

    private static CustomerAddressResponse Map(CustomerAddress x) =>
        new(x.Id, x.PartnerId, x.Address, x.PostalCode, x.Recipient, x.Phone, x.IsDefault, x.IsActive, x.Version);
}
