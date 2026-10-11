namespace MyERP.Modules.System.Application.Auth;

/// <summary>
/// 短期值存放（Redis）：Captcha 答案與 Refresh Token。取用與條件刪除必須原子，
/// 確保同一 Captcha／Refresh Token 在並發下只能成功使用一次。
/// </summary>
public interface IOneTimeStore
{
    Task SetAsync(string key, string value, TimeSpan timeToLive, CancellationToken cancellationToken = default);

    /// <summary>讀取但不刪除；不存在或已過期回傳 null。</summary>
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>原子地取出並刪除；不存在或已過期回傳 null。</summary>
    Task<string?> TakeAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>值等於 <paramref name="expectedValue"/> 時才刪除（原子比較後刪除）。</summary>
    Task<bool> RemoveIfAsync(string key, string expectedValue, CancellationToken cancellationToken = default);
}
