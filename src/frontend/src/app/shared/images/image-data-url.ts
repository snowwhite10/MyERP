/** 上傳圖片只接受 PNG／JPEG（ADR-028、ADR-045）；用戶端只作即時提示，伺服器以檔案內容作最終檢查。 */
export const IMAGE_TYPES: readonly string[] = ['image/png', 'image/jpeg'];

/** 下載的圖片轉成 data: URL 顯示（前端 CSP 的 img-src 只允許 'self' data:）；不是 PNG／JPEG 時回傳 null。 */
export function toDataUrl(blob: Blob): Promise<string | null> {
  if (!IMAGE_TYPES.includes(blob.type)) return Promise.resolve(null);
  return new Promise((resolve) => {
    const reader = new FileReader();
    reader.onload = () => resolve(typeof reader.result === 'string' ? reader.result : null);
    reader.onerror = () => resolve(null);
    reader.readAsDataURL(blob);
  });
}
