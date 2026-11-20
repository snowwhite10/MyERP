/**
 * 依 RFC 4180 計算表頭之後的資料列數；引號內的逗號、雙引號與換行不算分列。
 * 空檔（只有 BOM）或引號未閉合回 null，讓頁面判定檔案不完整而不是顯示成功。
 */
export function countCsvDataRows(text: string): number | null {
  const body = text.startsWith('﻿') ? text.slice(1) : text;
  if (!body) return null;
  let records = 0; let quoted = false; let pending = false;
  for (let i = 0; i < body.length; i++) {
    const ch = body[i];
    if (ch === '"') { quoted = !quoted; pending = true; continue; }
    if (!quoted && (ch === '\n' || ch === '\r')) {
      if (ch === '\r' && body[i + 1] === '\n') i++;
      records++; pending = false; continue;
    }
    pending = true;
  }
  if (quoted) return null;
  if (pending) records++;
  return records > 0 ? records - 1 : null;
}

/** 以瀏覽器下載已驗證的檔案；物件 URL 在點擊後立即釋放。 */
export function saveBlob(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url; link.download = fileName; link.click();
  URL.revokeObjectURL(url);
}
