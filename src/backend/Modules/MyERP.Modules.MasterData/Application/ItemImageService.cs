using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Imaging;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.MasterData.Application;

/// <summary>物料圖片的中繼資料（不含內容）。</summary>
public sealed record ItemImageView(long Id, long ItemId, string ContentType, string FileName, int SizeBytes,
    int Width, int Height, int SortOrder, bool IsPrimary, int Version);

/// <summary>下載用的圖片內容。</summary>
public sealed record ItemImageContent(string ContentType, string FileName, byte[] Content, int Version);

/// <summary>
/// 物料圖片維護（REQ_F4118.44.1–4、ADR-045）。上傳時以內容判斷類型並檢查大小、尺寸與張數；
/// 有圖片的物料恰好一張主圖。上傳、修改、刪除以公司級 applock 序列化，確保張數上限與主圖接任在併發下正確。
/// </summary>
public sealed class ItemImageService(MyErpDbContext db)
{
    public const int MaxBytes = 2 * 1024 * 1024;
    public const int MinDimension = 16;
    public const int MaxDimension = 4096;
    public const int MaxImagesPerItem = 10;

    public const string NotFoundCode = "MD.ITEM_IMAGE.NOT_FOUND";
    public const string UnsupportedTypeCode = "MD.ITEM_IMAGE.UNSUPPORTED_TYPE";
    public const string TooLargeCode = "MD.ITEM_IMAGE.TOO_LARGE";
    public const string InvalidDimensionsCode = "MD.ITEM_IMAGE.INVALID_DIMENSIONS";
    public const string LimitReachedCode = "MD.ITEM_IMAGE.LIMIT_REACHED";
    internal const string LockName = "md_item_image";

    /// <summary>主圖排第一，其餘按排序號、id。</summary>
    public async Task<IReadOnlyList<ItemImageView>> ListAsync(long itemId, CancellationToken token = default)
    {
        RequireCompany();
        await RequireItemAsync(itemId, token);
        return await db.Set<ItemImage>().AsNoTracking().Where(x => x.ItemId == itemId)
            .OrderByDescending(x => x.IsPrimary).ThenBy(x => x.SortOrder).ThenBy(x => x.Id)
            .Select(x => new ItemImageView(x.Id, x.ItemId, x.ContentType, x.FileName, x.SizeBytes,
                x.Width, x.Height, x.SortOrder, x.IsPrimary, x.Version))
            .ToListAsync(token);
    }

    /// <summary>目前版本（圖片不存在時為 null）；條件請求只比對版本，不讀取內容。</summary>
    public async Task<int?> GetVersionAsync(long itemId, long id, CancellationToken token = default)
    {
        RequireCompany();
        return await db.Set<ItemImage>().AsNoTracking().Where(x => x.Id == id && x.ItemId == itemId)
            .Select(x => (int?)x.Version).SingleOrDefaultAsync(token);
    }

    public async Task<ItemImageContent> GetContentAsync(long itemId, long id, CancellationToken token = default)
    {
        RequireCompany();
        await RequireItemAsync(itemId, token);
        return await db.Set<ItemImage>().AsNoTracking().Where(x => x.Id == id && x.ItemId == itemId)
                   .Select(x => new ItemImageContent(x.ContentType, x.FileName, x.Content, x.Version))
                   .SingleOrDefaultAsync(token)
               ?? throw NotFound(id);
    }

    /// <summary>
    /// 上傳圖片。<paramref name="sortOrder"/> 省略時為目前最大值 + 1；第一張或 <paramref name="isPrimary"/> 時成為主圖（取消原主圖）。
    /// <paramref name="content"/> 最多讀取 <see cref="MaxBytes"/> + 1 位元組。
    /// </summary>
    public async Task<ItemImageView> UploadAsync(long itemId, string? fileName, Stream content, int? sortOrder, bool isPrimary,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var companyId = RequireCompany();
        if (sortOrder is not null) ValidateSortOrder(sortOrder.Value);
        await RequireItemAsync(itemId, token);
        var bytes = await ImageInspector.ReadLimitedAsync(content, MaxBytes, token);
        if (bytes.Length == 0) throw new RequestValidationException("file", "The file is empty.");
        if (bytes.Length > MaxBytes) throw new BusinessRuleException(TooLargeCode, $"The image must not exceed {MaxBytes} bytes.");
        var info = ImageInspector.Inspect(bytes) ?? throw new BusinessRuleException(UnsupportedTypeCode, "Only PNG and JPEG images are accepted.");
        if (info.Width is < MinDimension or > MaxDimension || info.Height is < MinDimension or > MaxDimension)
            throw new BusinessRuleException(InvalidDimensionsCode, $"Width and height must be {MinDimension}–{MaxDimension} pixels.");
        var name = ImageInspector.SafeFileName(fileName, "image", info.ContentType, ItemImage.MaxFileNameLength);

        var entity = await CompanyLock.RunAsync(db, LockName, companyId, async () =>
        {
            var existing = await SlotsAsync(itemId, token);
            if (existing.Count >= MaxImagesPerItem)
                throw new BusinessRuleException(LimitReachedCode, $"An item can have at most {MaxImagesPerItem} images.");
            var primary = existing.Count == 0 || isPrimary;
            var created = new ItemImage
            {
                CompanyId = companyId, ItemId = itemId, ContentType = info.ContentType, FileName = name, Content = bytes,
                SizeBytes = bytes.Length, Width = info.Width, Height = info.Height,
                SortOrder = sortOrder ?? Math.Min(ItemImage.MaxSortOrder, existing.Select(x => x.SortOrder + 1).DefaultIfEmpty(1).Max()),
                IsPrimary = primary,
            };
            if (primary) await SetPrimaryAsync(existing.SingleOrDefault(x => x.IsPrimary), false, token);
            db.Add(created);
            await SaveAsync(token);
            return created;
        }, token);
        return Map(entity);
    }

    /// <summary>按版本修改排序號與主圖；不可直接取消主圖（改把另一張設為主圖）。</summary>
    public async Task<ItemImageView> UpdateAsync(long itemId, long id, int sortOrder, bool isPrimary, int version,
        CancellationToken token = default)
    {
        var companyId = RequireCompany();
        ValidateSortOrder(sortOrder);
        await RequireItemAsync(itemId, token);
        return await CompanyLock.RunAsync(db, LockName, companyId, async () =>
        {
            var slots = await SlotsAsync(itemId, token);
            var slot = slots.SingleOrDefault(x => x.Id == id) ?? throw NotFound(id);
            if (version < 1 || slot.Version != version) throw new ConflictException();
            if (slot.IsPrimary && !isPrimary)
                throw new RequestValidationException("isPrimary", "Set another image as primary instead of clearing the primary image.");
            if (isPrimary && !slot.IsPrimary) await SetPrimaryAsync(slots.SingleOrDefault(x => x.IsPrimary), false, token);
            var image = Track(slot);
            image.IsPrimary = isPrimary;
            image.SortOrder = sortOrder;
            await SaveAsync(token);
            return Map(image);
        }, token);
    }

    /// <summary>按版本刪除圖片；刪除主圖時由排序號最小（相同時 id 最小）的圖片接任。</summary>
    public async Task DeleteAsync(long itemId, long id, int version, CancellationToken token = default)
    {
        var companyId = RequireCompany();
        await RequireItemAsync(itemId, token);
        await CompanyLock.RunAsync(db, LockName, companyId, async () =>
        {
            var slots = await SlotsAsync(itemId, token);
            var slot = slots.SingleOrDefault(x => x.Id == id) ?? throw NotFound(id);
            if (version < 1 || slot.Version != version) throw new ConflictException();
            db.Remove(Track(slot));
            await SaveAsync(token);
            if (slot.IsPrimary)
                await SetPrimaryAsync(slots.Where(x => x.Id != id).OrderBy(x => x.SortOrder).ThenBy(x => x.Id).FirstOrDefault(), true, token);
            return true;
        }, token);
    }

    private sealed record Slot(long Id, long CompanyId, long ItemId, string ContentType, string FileName, int SizeBytes,
        int Width, int Height, int SortOrder, bool IsPrimary, int Version);

    /// <summary>物料現有圖片的中繼資料（不讀取內容）。</summary>
    private Task<List<Slot>> SlotsAsync(long itemId, CancellationToken token) =>
        db.Set<ItemImage>().AsNoTracking().Where(x => x.ItemId == itemId)
            .Select(x => new Slot(x.Id, x.CompanyId, x.ItemId, x.ContentType, x.FileName, x.SizeBytes, x.Width, x.Height,
                x.SortOrder, x.IsPrimary, x.Version))
            .ToListAsync(token);

    /// <summary>
    /// 取得可修改或刪除的圖片實體而不讀取最大 2 MB 的內容（MD-B08）：已追蹤者沿用，否則以中繼資料附加。
    /// 仍經 ChangeTracker 保存，所以稽核、版本遞增與樂觀鎖照常（不用 ExecuteUpdate）；修改只寫變更欄位。
    /// 內容未載入，刪除的稽核記錄以 size_bytes 表示大小，content 舊值為 null。
    /// </summary>
    private ItemImage Track(Slot slot)
    {
        if (db.Set<ItemImage>().Local.FirstOrDefault(x => x.Id == slot.Id) is { } tracked) return tracked;
        var image = new ItemImage
        {
            Id = slot.Id, CompanyId = slot.CompanyId, ItemId = slot.ItemId, ContentType = slot.ContentType, FileName = slot.FileName,
            SizeBytes = slot.SizeBytes, Width = slot.Width, Height = slot.Height, Content = null!, SortOrder = slot.SortOrder,
            IsPrimary = slot.IsPrimary, Version = slot.Version,
        };
        db.Attach(image);
        return image;
    }

    /// <summary>
    /// 設定或取消一張圖片的主圖旗標並立即保存（經稽核與版本遞增）；取消原主圖必須先於設定新主圖保存，避免違反篩選唯一索引。
    /// </summary>
    private async Task SetPrimaryAsync(Slot? slot, bool isPrimary, CancellationToken token)
    {
        if (slot is null) return;
        Track(slot).IsPrimary = isPrimary;
        await SaveAsync(token);
    }

    private async Task RequireItemAsync(long itemId, CancellationToken token)
    {
        if (!await db.Set<Item>().AnyAsync(x => x.Id == itemId, token))
            throw new NotFoundException("MD.ITEM.NOT_FOUND", "Item", itemId);
    }

    private static void ValidateSortOrder(int sortOrder)
    {
        if (sortOrder is < 0 or > ItemImage.MaxSortOrder)
            throw new RequestValidationException("sortOrder", $"Sort order must be 0–{ItemImage.MaxSortOrder}.");
    }

    private async Task SaveAsync(CancellationToken token)
    {
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateConcurrencyException) { throw new ConflictException(); }
        catch (DbUpdateException ex) when (MasterDataQueries.IsUniqueViolation(ex)) { throw new ConflictException(); }
    }

    private long RequireCompany() => MasterDataQueries.RequireCompany(db);

    private static NotFoundException NotFound(long id) => new(NotFoundCode, "Item image", id);

    private static ItemImageView Map(ItemImage x) =>
        new(x.Id, x.ItemId, x.ContentType, x.FileName, x.SizeBytes, x.Width, x.Height, x.SortOrder, x.IsPrimary, x.Version);
}
