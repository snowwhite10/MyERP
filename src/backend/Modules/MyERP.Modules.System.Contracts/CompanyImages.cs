using System.Text.Json.Serialization;

namespace MyERP.Modules.System.Contracts;

/// <summary>公司圖片種類（BlueWhale Logo &amp; Stamp，REQ_F4118.19）。</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CompanyImageKind>))]
public enum CompanyImageKind
{
    Logo,
    Stamp,
}

/// <summary>公司圖片內容；<see cref="ContentType"/> 只會是 <c>image/png</c> 或 <c>image/jpeg</c>（依檔案內容判斷，ADR-028）。</summary>
public sealed record CompanyImage(CompanyImageKind Kind, string ContentType, int Width, int Height, byte[] Content);

/// <summary>列印單據的模組經此介面讀取目前公司的 Logo／印章；沒有上傳時回傳 null，沒有公司上下文時拋出 403。</summary>
public interface ICompanyImageReader
{
    Task<CompanyImage?> GetAsync(CompanyImageKind kind, CancellationToken cancellationToken = default);
}
