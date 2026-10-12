using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyERP.Modules.MasterData.Application;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Import;
using MyERP.Shared.Infrastructure.Security;

namespace MyERP.Modules.MasterData.Api;

/// <summary>客戶匯入範本、唯讀預覽與整批提交（ADR-044）。</summary>
[ApiController]
[Route("api/v1/md/customers/import")]
[Tags("MD - Customer Import")]
public sealed class CustomerImportsController(CustomerImportService imports) : ControllerBase
{
    [HttpGet("template")]
    [RequirePermission(MasterDataPermissions.CustomerImport)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Template([FromQuery] string format = "xlsx") =>
        ImportFiles.Template(this, format, "Customers", "customer-import", CustomerImportService.Columns);

    [HttpPost("preview")]
    [RequirePermission(MasterDataPermissions.CustomerImport)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<PartnerImportResult>(StatusCodes.Status200OK)]
    public async Task<PartnerImportResult> Preview(IFormFile file, CancellationToken token) =>
        await imports.PreviewAsync(await ImportFiles.ReadAsync(file, token), token);

    [HttpPost]
    [RequirePermission(MasterDataPermissions.CustomerImport)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<PartnerImportResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<PartnerImportResult> Commit(IFormFile file, CancellationToken token) =>
        await imports.CommitAsync(await ImportFiles.ReadAsync(file, token), token);
}

/// <summary>供應商匯入範本、唯讀預覽與整批提交（ADR-044）；範本沒有地址欄（ADR-043）。</summary>
[ApiController]
[Route("api/v1/md/suppliers/import")]
[Tags("MD - Supplier Import")]
public sealed class SupplierImportsController(SupplierImportService imports) : ControllerBase
{
    [HttpGet("template")]
    [RequirePermission(MasterDataPermissions.SupplierImport)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Template([FromQuery] string format = "xlsx") =>
        ImportFiles.Template(this, format, "Suppliers", "supplier-import", SupplierImportService.Columns);

    [HttpPost("preview")]
    [RequirePermission(MasterDataPermissions.SupplierImport)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<PartnerImportResult>(StatusCodes.Status200OK)]
    public async Task<PartnerImportResult> Preview(IFormFile file, CancellationToken token) =>
        await imports.PreviewAsync(await ImportFiles.ReadAsync(file, token), token);

    [HttpPost]
    [RequirePermission(MasterDataPermissions.SupplierImport)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<PartnerImportResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<PartnerImportResult> Commit(IFormFile file, CancellationToken token) =>
        await imports.CommitAsync(await ImportFiles.ReadAsync(file, token), token);
}

/// <summary>物料主檔匯入範本、唯讀預覽與冪等提交；不匯入期初庫存。</summary>
[ApiController]
[Route("api/v1/md/items/import")]
[Tags("MD - Item Import")]
public sealed class ItemImportsController(ItemImportService imports) : ControllerBase
{
    [HttpGet("template")]
    [RequirePermission(MasterDataPermissions.ItemImport)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Template([FromQuery] string format = "xlsx") =>
        ImportFiles.Template(this, format, "Items", "item-import", ItemImportService.Columns);

    [HttpPost("preview")]
    [RequirePermission(MasterDataPermissions.ItemImport)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<ItemImportResult>(StatusCodes.Status200OK)]
    public async Task<ItemImportResult> Preview(IFormFile file, CancellationToken token) =>
        await imports.PreviewAsync(await ImportFiles.ReadAsync(file, token), token);

    [HttpPost]
    [RequirePermission(MasterDataPermissions.ItemImport)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<ItemImportResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ItemImportResult> Commit(IFormFile file, CancellationToken token) =>
        await imports.CommitAsync(await ImportFiles.ReadAsync(file, token), token);
}

/// <summary>匯入端點共用的範本下載與上傳讀取（2 MB 上限）。</summary>
internal static class ImportFiles
{
    public static IActionResult Template(ControllerBase controller, string format, string sheetName, string fileName,
        IReadOnlyList<string> columns)
    {
        IReadOnlyList<IReadOnlyList<string?>> rows = [columns];
        return format.ToLowerInvariant() switch
        {
            "xlsx" => controller.File(TabularTemplate.CreateXlsx(sheetName, rows), TabularTemplate.XlsxContentType, fileName + ".xlsx"),
            "csv" => controller.File(TabularTemplate.CreateCsv(rows), TabularTemplate.CsvContentType, fileName + ".csv"),
            _ => throw new RequestValidationException("format", "Use xlsx or csv."),
        };
    }

    public static async Task<byte[]> ReadAsync(IFormFile file, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (file.Length > TabularFileReader.MaxBytes)
            throw new BusinessRuleException(ImportErrorCodes.TooLarge, "The file must not exceed 2 MB.");
        await using var stream = new MemoryStream();
        await file.CopyToAsync(stream, token);
        return stream.ToArray();
    }
}
