/** 本地日期轉 `yyyy-MM-dd`（原生日期欄位與 API 的業務日期格式）。 */
export function isoDate(date: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}
