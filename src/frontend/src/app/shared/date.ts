/** 本地日期轉 `yyyy-MM-dd`（原生日期欄位與 API 的業務日期格式）。 */
export function isoDate(date: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

/**
 * API 的伺服器本地時間（`yyyy-MM-ddTHH:mm:ss…`，UTC+8）顯示為 `yyyy-MM-dd HH:mm`；
 * `seconds` 為 true 時保留秒（稽核紀錄用）。不換算瀏覽器時區；空值回傳空字串。
 */
export function displayTime(value: string | null | undefined, seconds = false): string {
  return value ? value.slice(0, seconds ? 19 : 16).replace('T', ' ') : '';
}
