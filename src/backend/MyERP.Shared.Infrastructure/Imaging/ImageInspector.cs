using System.Buffers.Binary;

namespace MyERP.Shared.Infrastructure.Imaging;

/// <summary>由檔案內容判斷出的圖片類型與尺寸。</summary>
public sealed record ImageInfo(string ContentType, int Width, int Height);

/// <summary>
/// 以檔案內容判斷圖片類型並讀取寬高（ADR-028；物料圖片沿用，ADR-045）：只接受 PNG 與 JPEG，不看副檔名或宣告的 Content-Type。
/// 只解析檔頭與結尾，不解碼影像資料。
/// </summary>
public static class ImageInspector
{
    public const string Png = "image/png";
    public const string Jpeg = "image/jpeg";

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static ReadOnlySpan<byte> PngEnd => [0x00, 0x00, 0x00, 0x00, (byte)'I', (byte)'E', (byte)'N', (byte)'D', 0xAE, 0x42, 0x60, 0x82];

    /// <summary>不是完整的 PNG／JPEG 檔頭或結尾時回傳 null。</summary>
    public static ImageInfo? Inspect(ReadOnlySpan<byte> data) => InspectPng(data) ?? InspectJpeg(data);

    /// <summary>
    /// 讀取上傳內容，最多 <paramref name="maxBytes"/> + 1 位元組：回傳長度大於 <paramref name="maxBytes"/> 即表示超過上限，
    /// 呼叫端不必讀完整個串流。
    /// </summary>
    public static async Task<byte[]> ReadLimitedAsync(Stream content, int maxBytes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(chunk, cancellationToken)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > maxBytes) break;
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// 上傳檔名：只保留最後一段、移除控制字元並截斷到 <paramref name="maxLength"/>；
    /// 空白時以 <paramref name="baseName"/> 加上依類型的副檔名（<c>.png</c>／<c>.jpg</c>）。
    /// </summary>
    public static string SafeFileName(string? fileName, string baseName, string contentType, int maxLength)
    {
        var name = (fileName ?? "").Replace('\\', '/');
        name = new string(name[(name.LastIndexOf('/') + 1)..].Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (name.Length == 0) name = baseName + (contentType == Png ? ".png" : ".jpg");
        return name.Length <= maxLength ? name : name[..maxLength];
    }

    private static ImageInfo? InspectPng(ReadOnlySpan<byte> data)
    {
        // 簽章 8 + IHDR chunk 25（長度 4、類型 4、資料 13、CRC 4）+ IEND chunk 12
        if (data.Length < 8 + 25 + 12 || !data.StartsWith(PngSignature) || !data.EndsWith(PngEnd)) return null;
        if (BinaryPrimitives.ReadUInt32BigEndian(data[8..]) != 13 || !data.Slice(12, 4).SequenceEqual("IHDR"u8)) return null;
        return new ImageInfo(Png, Dimension(BinaryPrimitives.ReadUInt32BigEndian(data[16..])), Dimension(BinaryPrimitives.ReadUInt32BigEndian(data[20..])));
    }

    private static ImageInfo? InspectJpeg(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8 || data[2] != 0xFF || data[^2] != 0xFF || data[^1] != 0xD9) return null;
        var position = 2;
        while (position < data.Length)
        {
            if (data[position] != 0xFF) return null;
            while (position < data.Length && data[position] == 0xFF) position++; // 段落前可有填充的 FF
            if (position >= data.Length) return null;
            var marker = data[position++];
            if (marker is 0x01 or (>= 0xD0 and <= 0xD7)) continue; // 沒有長度的獨立標記
            if (marker is 0xD9 or 0xDA) return null; // 在 SOF 之前遇到影像資料或結尾
            if (position + 2 > data.Length) return null;
            var length = BinaryPrimitives.ReadUInt16BigEndian(data[position..]);
            if (length < 2 || position + length > data.Length) return null;
            if (marker is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC))
            {
                // SOF：長度 2、精度 1、高 2、寬 2
                if (length < 7) return null;
                return new ImageInfo(Jpeg, BinaryPrimitives.ReadUInt16BigEndian(data[(position + 5)..]), BinaryPrimitives.ReadUInt16BigEndian(data[(position + 3)..]));
            }

            position += length;
        }

        return null;
    }

    private static int Dimension(uint value) => value > int.MaxValue ? int.MaxValue : (int)value;
}
