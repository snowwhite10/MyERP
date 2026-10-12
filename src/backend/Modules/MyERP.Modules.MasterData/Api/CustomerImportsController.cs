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
    public IActionResult Template([FromQuery] string format = "xlsx")
    {
        IReadOnlyList<IReadOnlyList<string?>> rows = [CustomerImportService.Columns];
        return format.ToLowerInvariant() switch
        {
            "xlsx" => File(TabularTemplate.CreateXlsx("Customers", rows), TabularTemplate.XlsxContentType, "customer-import.xlsx"),
            "csv" => File(TabularTemplate.CreateCsv(rows), TabularTemplate.CsvContentType, "customer-import.csv"),
            _ => throw new RequestValidationException("format", "Use xlsx or csv."),
        };
    }

    [HttpPost("preview")]
    [RequirePermission(MasterDataPermissions.CustomerImport)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<CustomerImportResult>(StatusCodes.Status200OK)]
    public async Task<CustomerImportResult> Preview(IFormFile file, CancellationToken token) =>
        await imports.PreviewAsync(await ReadAsync(file, token), token);

    [HttpPost]
    [RequirePermission(MasterDataPermissions.CustomerImport)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<CustomerImportResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<CustomerImportResult> Commit(IFormFile file, CancellationToken token) =>
        await imports.CommitAsync(await ReadAsync(file, token), token);

    private static async Task<byte[]> ReadAsync(IFormFile file, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (file.Length > TabularFileReader.MaxBytes)
            throw new BusinessRuleException(ImportErrorCodes.TooLarge, "The file must not exceed 2 MB.");
        await using var stream = new MemoryStream();
        await file.CopyToAsync(stream, token);
        return stream.ToArray();
    }
}
