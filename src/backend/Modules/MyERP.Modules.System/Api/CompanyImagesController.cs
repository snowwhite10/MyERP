using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using MyERP.Modules.System.Application.Authorization;
using MyERP.Modules.System.Application.CompanyImages;
using MyERP.Modules.System.Contracts;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Security;

namespace MyERP.Modules.System.Api;

/// <summary>
/// 公司 Logo 與印章（REQ_F4118.19.1–3，BlueWhale Logo &amp; Stamp）。<c>{kind}</c> 為 <c>logo</c> 或 <c>stamp</c>；
/// 只接受 PNG／JPEG（依內容判斷）、1 MB 以內、寬高 16–2048 像素（ADR-028）。權限沿用系統參數。
/// </summary>
[ApiController]
[Route("api/v1/sys/company-images")]
[Produces("application/json")]
[Tags("SYS - Company Images")]
public sealed class CompanyImagesController(CompanyImageService images) : ControllerBase
{
    /// <summary>請求本體上限：圖片 1 MB 加上 multipart 表單欄位。</summary>
    private const long MaxRequestBytes = CompanyImageService.MaxBytes + 64 * 1024;

    /// <summary>目前公司已上傳的圖片（不含內容，依種類排序）；沒有上傳的種類不列出。</summary>
    [HttpGet]
    [RequirePermission(SystemPermissions.ParameterView)]
    [ProducesResponseType<IReadOnlyList<CompanyImageDetail>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public Task<IReadOnlyList<CompanyImageDetail>> List(CancellationToken cancellationToken) => images.ListAsync(cancellationToken);

    /// <summary>
    /// 圖片內容（<c>image/png</c>／<c>image/jpeg</c>，inline）。<c>ETag</c> 為版本號，帶 <c>If-None-Match</c> 且未變更時回 304。
    /// 沒有上傳時回 404 <c>SYS.COMPANY_IMAGE.NOT_FOUND</c>。
    /// </summary>
    [HttpGet("{kind}/content")]
    [RequirePermission(SystemPermissions.ParameterView)]
    [Produces("image/png", "image/jpeg", "application/json")]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Content(string kind, CancellationToken cancellationToken)
    {
        var imageKind = ParseKind(kind);
        Response.Headers.CacheControl = "private, no-cache";
        // 瀏覽器重新驗證（no-cache）時只查版本，未變更就不讀取圖片內容
        var conditions = Request.GetTypedHeaders().IfNoneMatch;
        if (conditions.Count > 0 && await images.GetVersionAsync(imageKind, cancellationToken) is { } current
            && conditions.Any(tag => tag.Tag == $"\"{current}\""))
        {
            Response.Headers.ETag = $"\"{current}\"";
            return StatusCode(StatusCodes.Status304NotModified);
        }

        var image = await images.GetContentAsync(imageKind, cancellationToken);
        Response.Headers.ContentDisposition = new ContentDispositionHeaderValue("inline") { FileNameStar = image.FileName }.ToString();
        return File(image.Content, image.ContentType, lastModified: null, entityTag: new EntityTagHeaderValue($"\"{image.Version}\""));
    }

    /// <summary>
    /// 上傳或替換圖片（multipart：<c>file</c>、<c>version</c>；新上傳 version 為 0）。沒有檔案或空檔回 400；
    /// 類型、大小、尺寸不符回 422（<c>SYS.COMPANY_IMAGE.UNSUPPORTED_TYPE</c>／<c>TOO_LARGE</c>／<c>INVALID_DIMENSIONS</c>）；版本不符回 409。
    /// </summary>
    [HttpPut("{kind}")]
    [RequirePermission(SystemPermissions.ParameterEdit)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
    [ProducesResponseType<CompanyImageDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<CompanyImageDetail> Upload(string kind, IFormFile? file, [FromForm] int version, CancellationToken cancellationToken)
    {
        var imageKind = ParseKind(kind);
        if (file is null) throw new RequestValidationException("file", "Select an image file.");
        if (file.Length > CompanyImageService.MaxBytes)
            throw new BusinessRuleException(CompanyImageService.TooLargeCode, $"The image must not exceed {CompanyImageService.MaxBytes} bytes.");
        await using var stream = file.OpenReadStream();
        return await images.UploadAsync(imageKind, file.FileName, stream, version, cancellationToken);
    }

    /// <summary>刪除圖片（帶目前 version）。沒有上傳時回 404，版本不符回 409。</summary>
    [HttpDelete("{kind}")]
    [RequirePermission(SystemPermissions.ParameterEdit)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(string kind, [FromQuery] int version, CancellationToken cancellationToken)
    {
        await images.DeleteAsync(ParseKind(kind), version, cancellationToken);
        return NoContent();
    }

    /// <summary>只接受 <c>logo</c>／<c>stamp</c>（不分大小寫）；數字或其他值回 404。</summary>
    private static CompanyImageKind ParseKind(string kind) => kind.ToLowerInvariant() switch
    {
        "logo" => CompanyImageKind.Logo,
        "stamp" => CompanyImageKind.Stamp,
        _ => throw new NotFoundException(CompanyImageService.NotFoundCode, "Company image", kind),
    };
}
