using System.Security.Cryptography;
using System.Text;
using System.Buffers.Binary;

namespace MyERP.DocGen.Word;

/// <summary>Reuses rendered diagrams across isolated document runs.</summary>
public sealed class MermaidImageCache(string directory)
{
    private static ReadOnlySpan<byte> PngSignature => [137, 80, 78, 71, 13, 10, 26, 10];

    public byte[] GetOrRender(string source, string rendererFingerprint, Func<byte[]> render)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(rendererFingerprint);
        ArgumentNullException.ThrowIfNull(render);

        var key = SHA256.HashData(Encoding.UTF8.GetBytes("myerp-mermaid-v1\n" + rendererFingerprint + "\n" + source));
        var path = Path.Combine(directory, Convert.ToHexString(key) + ".png");
        if (File.Exists(path))
        {
            var cached = File.ReadAllBytes(path);
            if (IsPng(cached)) return cached;
        }

        var image = render();
        if (!IsPng(image)) throw new InvalidDataException("Mermaid renderer did not return a PNG image.");
        Directory.CreateDirectory(directory);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, image);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        return image;
    }

    private static bool IsPng(byte[]? bytes)
    {
        if (bytes is null || bytes.Length < 45 || !bytes.AsSpan(0, 8).SequenceEqual(PngSignature)) return false;
        var offset = 8;
        var hasIdat = false;
        var first = true;
        while (offset <= bytes.Length - 12)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4));
            if (length > bytes.Length - offset - 12) return false;
            var type = bytes.AsSpan(offset + 4, 4);
            if (first && (!type.SequenceEqual("IHDR"u8) || length != 13 ||
                BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset + 8, 4)) == 0 ||
                BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset + 12, 4)) == 0)) return false;
            var crc = 0xffffffffu;
            foreach (var value in bytes.AsSpan(offset + 4, checked((int)length) + 4))
            {
                crc ^= value;
                for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ (0xedb88320u & (uint)-(int)(crc & 1));
            }
            if (~crc != BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset + 8 + (int)length, 4))) return false;
            if (type.SequenceEqual("IDAT"u8)) hasIdat = true;
            offset += checked((int)length) + 12;
            if (type.SequenceEqual("IEND"u8)) return length == 0 && hasIdat && offset == bytes.Length;
            first = false;
        }
        return false;
    }
}
