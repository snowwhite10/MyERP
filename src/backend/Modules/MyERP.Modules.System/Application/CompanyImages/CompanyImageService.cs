using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.System.Contracts;
using MyERP.Modules.System.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.System.Application.CompanyImages;

/// <summary>已上傳圖片的中繼資料（不含內容）；<c>UpdatedAt</c> 為最後上傳時間。</summary>
public sealed record CompanyImageDetail(
    CompanyImageKind Kind, string ContentType, string FileName, int SizeBytes, int Width, int Height, int Version, DateTime UpdatedAt);

/// <summary>下載用的圖片內容。</summary>
public sealed record CompanyImageContent(string ContentType, string FileName, byte[] Content, int Version);

/// <summary>
/// 公司 Logo／印章維護（REQ_F4118.19.1–3、ADR-028）。只處理目前公司；呼叫端以權限代碼保護。
/// 上傳時以內容判斷類型並檢查大小與尺寸，新上傳帶 version 0，替換或刪除帶目前版本。
/// </summary>
public sealed class CompanyImageService(MyErpDbContext db)
{
    public const int MaxBytes = 1024 * 1024;
    public const int MinDimension = 16;
    public const int MaxDimension = 2048;

    public const string NotFoundCode = "SYS.COMPANY_IMAGE.NOT_FOUND";
    public const string UnsupportedTypeCode = "SYS.COMPANY_IMAGE.UNSUPPORTED_TYPE";
    public const string TooLargeCode = "SYS.COMPANY_IMAGE.TOO_LARGE";
    public const string InvalidDimensionsCode = "SYS.COMPANY_IMAGE.INVALID_DIMENSIONS";

    public async Task<IReadOnlyList<CompanyImageDetail>> ListAsync(CancellationToken cancellationToken = default)
    {
        await ActiveCompanyIdAsync(cancellationToken);
        var rows = await db.Set<CompanyImageRecord>().AsNoTracking()
            .Select(x => new CompanyImageDetail(ToKind(x.ImageKind), x.ContentType, x.FileName, x.SizeBytes, x.Width, x.Height, x.Version, x.UpdatedAt ?? x.CreatedAt))
            .ToListAsync(cancellationToken);
        return rows.OrderBy(x => x.Kind).ToList();
    }

    /// <summary>目前版本（沒有上傳時為 null）；條件請求只比對版本，不讀取圖片內容。</summary>
    public async Task<int?> GetVersionAsync(CompanyImageKind kind, CancellationToken cancellationToken = default)
    {
        await ActiveCompanyIdAsync(cancellationToken);
        var type = ToType(kind);
        return await db.Set<CompanyImageRecord>().AsNoTracking().Where(x => x.ImageKind == type).Select(x => (int?)x.Version).SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<CompanyImageContent> GetContentAsync(CompanyImageKind kind, CancellationToken cancellationToken = default)
    {
        await ActiveCompanyIdAsync(cancellationToken);
        var type = ToType(kind);
        return await db.Set<CompanyImageRecord>().AsNoTracking().Where(x => x.ImageKind == type)
                   .Select(x => new CompanyImageContent(x.ContentType, x.FileName, x.Content, x.Version))
                   .SingleOrDefaultAsync(cancellationToken)
               ?? throw new NotFoundException(NotFoundCode, "Company image", kind);
    }

    /// <summary>上傳或替換圖片。<paramref name="content"/> 最多讀取 <see cref="MaxBytes"/> + 1 位元組，超過即拒絕。</summary>
    public async Task<CompanyImageDetail> UploadAsync(CompanyImageKind kind, string? fileName, Stream content, int version, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var companyId = await ActiveCompanyIdAsync(cancellationToken);
        var bytes = await ReadLimitedAsync(content, cancellationToken);
        if (bytes.Length == 0) throw new RequestValidationException("file", "The file is empty.");
        if (bytes.Length > MaxBytes) throw new BusinessRuleException(TooLargeCode, $"The image must not exceed {MaxBytes} bytes.");
        var info = ImageInspector.Inspect(bytes) ?? throw new BusinessRuleException(UnsupportedTypeCode, "Only PNG and JPEG images are accepted.");
        if (info.Width is < MinDimension or > MaxDimension || info.Height is < MinDimension or > MaxDimension)
            throw new BusinessRuleException(InvalidDimensionsCode, $"Width and height must be {MinDimension}–{MaxDimension} pixels.");

        var name = SafeFileName(fileName, kind, info.ContentType);
        var type = ToType(kind);
        var row = await db.Set<CompanyImageRecord>().SingleOrDefaultAsync(x => x.ImageKind == type, cancellationToken);
        if (row is null)
        {
            if (version != 0) throw new ConflictException();
            row = new CompanyImageRecord { CompanyId = companyId, ImageKind = type, ContentType = info.ContentType, FileName = name, Content = bytes };
            db.Add(row);
        }
        else
        {
            if (row.Version != version) throw new ConflictException();
            row.ContentType = info.ContentType;
            row.FileName = name;
            row.Content = bytes;
        }

        row.SizeBytes = bytes.Length;
        row.Width = info.Width;
        row.Height = info.Height;
        await SaveAsync(cancellationToken);
        return new CompanyImageDetail(kind, row.ContentType, row.FileName, row.SizeBytes, row.Width, row.Height, row.Version, row.UpdatedAt ?? row.CreatedAt);
    }

    public async Task DeleteAsync(CompanyImageKind kind, int version, CancellationToken cancellationToken = default)
    {
        await ActiveCompanyIdAsync(cancellationToken);
        var type = ToType(kind);
        var row = await db.Set<CompanyImageRecord>().SingleOrDefaultAsync(x => x.ImageKind == type, cancellationToken)
                  ?? throw new NotFoundException(NotFoundCode, "Company image", kind);
        if (row.Version != version) throw new ConflictException();
        db.Remove(row);
        await SaveAsync(cancellationToken);
    }

    internal static CompanyImageType ToType(CompanyImageKind kind) => kind switch
    {
        CompanyImageKind.Logo => CompanyImageType.Logo,
        CompanyImageKind.Stamp => CompanyImageType.Stamp,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    internal static CompanyImageKind ToKind(CompanyImageType type) => type switch
    {
        CompanyImageType.Logo => CompanyImageKind.Logo,
        CompanyImageType.Stamp => CompanyImageKind.Stamp,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    /// <summary>只保留檔名部分、移除控制字元並截斷；空白時以種類與類型命名（例如 <c>logo.png</c>）。</summary>
    internal static string SafeFileName(string? fileName, CompanyImageKind kind, string contentType)
    {
        var name = (fileName ?? "").Replace('\\', '/');
        name = new string(name[(name.LastIndexOf('/') + 1)..].Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (name.Length == 0) name = kind.ToString().ToLowerInvariant() + (contentType == ImageInspector.Png ? ".png" : ".jpg");
        return name.Length <= CompanyImageRecord.MaxFileNameLength ? name : name[..CompanyImageRecord.MaxFileNameLength];
    }

    private static async Task<byte[]> ReadLimitedAsync(Stream content, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(chunk, cancellationToken)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > MaxBytes) break;
        }

        return buffer.ToArray();
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException();
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // 另一個請求同時上傳了同一種圖片
            throw new ConflictException();
        }
    }

    private async Task<long> ActiveCompanyIdAsync(CancellationToken token)
    {
        var id = db.CurrentCompanyId;
        if (id is null || !await db.Set<SystemCompany>().AnyAsync(x => x.Id == id && x.IsActive, token))
            throw new ForbiddenException("SYS.COMPANY.UNAVAILABLE", "An active company context is required.");
        return id.Value;
    }
}

/// <summary><see cref="ICompanyImageReader"/> 實作：讀取目前公司的圖片，沒有上傳時回傳 null。</summary>
public sealed class CompanyImageReader(MyErpDbContext db) : ICompanyImageReader
{
    public async Task<CompanyImage?> GetAsync(CompanyImageKind kind, CancellationToken cancellationToken = default)
    {
        _ = db.CurrentCompanyId ?? throw new ForbiddenException("SYS.COMPANY.UNAVAILABLE", "An active company context is required.");
        var type = CompanyImageService.ToType(kind);
        return await db.Set<CompanyImageRecord>().AsNoTracking().Where(x => x.ImageKind == type)
            .Select(x => new CompanyImage(kind, x.ContentType, x.Width, x.Height, x.Content))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
