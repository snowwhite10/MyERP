# ADR-028：公司 Logo 與印章圖片的儲存與安全檢查

- 日期：2026-10-11
- 狀態：採用（WU-SYS-010d）

## 背景

BlueWhale 的 Logo & Stamp（4.1.1.8.19，介面 4.4.1.8.57）把上傳的圖片存到網站目錄，檔名由副檔名決定，只檢查副檔名，沒有大小或尺寸限制，也沒有權限檢查。MyERP 要按公司保存 Logo 和印章，供列印模組（PUR、SD 等，ADR-027 的 `SYS.PRINT_SHOW_LOGO`／`SHOW_STAMP`）在單據上使用。需要決定：

1. 圖片放在資料庫還是檔案系統；
2. 怎樣檢查上傳的檔案；
3. 前端怎樣顯示（API 回應的 CSP 為 `default-src 'none'`，前端 CSP 的 `img-src` 只允許 `'self' data:`）；
4. 權限與其他模組的讀取方式。

## 決定

1. **存在資料庫**：新增 `sys_company_image`（REQ_DS43.103），每間公司每種圖片（`Logo`／`Stamp`）一列，內容為 `varbinary(max)`，連同類型、原始檔名、大小、寬高與 6.4 節必備欄位。理由：
   - 每間公司只有兩張小圖（上限 1 MB），資料量很小；
   - 與其他資料一起備份、還原和隔離公司，部署時不必設定共用目錄、檔案權限或多台主機同步；
   - 不存在路徑穿越、覆蓋網站檔案或從網站目錄直接執行的問題。
   列表 API 只投影中繼欄位，不讀取內容。
2. **以內容判斷類型**：只接受 PNG 與 JPEG。伺服器讀取檔案開頭的簽章判斷類型，不看副檔名或瀏覽器送來的 Content-Type：
   - PNG：8 位元組簽章，第一個 chunk 必須是 `IHDR`（從中讀寬高），檔案以 `IEND` chunk 結尾；
   - JPEG：以 `FF D8 FF` 開頭、`FF D9` 結尾，逐段解析到 SOF 段（`C0`–`CF`，除了 `C4`、`C8`、`CC`）讀寬高；
   - SVG、GIF、WebP 等一律拒絕（SVG 可內嵌腳本）。
   沒有引入影像函式庫（System.Drawing 只支援 Windows），只解析檔頭；檔案內容不會在伺服器端解碼或執行。
3. **大小與尺寸**：檔案 1 B–1 MB；寬、高各 16–2048 像素。超過時回 422 與專用錯誤代碼（`SYS.COMPANY_IMAGE.UNSUPPORTED_TYPE`／`TOO_LARGE`／`INVALID_DIMENSIONS`）；沒有檔案回 400。上傳端點以 `RequestSizeLimit` 限制整個請求（1 MB + 表單欄位），超過時伺服器不讀完請求本體。
4. **下載**：`GET …/{kind}/content` 回傳已儲存的類型（`image/png`／`image/jpeg`），`Content-Disposition: inline` 並帶 ASCII 安全的檔名，沿用全域 `X-Content-Type-Options: nosniff` 與 API 的 CSP；`ETag` 為版本號，`Cache-Control: private, no-cache`。前端以 HttpClient 取得 Blob，再用 FileReader 轉成 `data:` URL 顯示，前端 CSP 不需加入 `blob:`。
5. **權限沿用 `SYS.PARAMETER.VIEW`／`EDIT`**：Logo 與印章是列印版面設定的一部分，與 ADR-027 一致。上傳與刪除帶 `version`（新上傳為 0），版本不符回 409；同時新增造成唯一索引衝突也回 409。刪除為實體刪除（設定資料，不是主檔或單據），稽核日誌記錄 DELETE；內容欄位在稽核中只記為 `(binary N bytes)`。
6. **其他模組經 Contracts 讀取**：`MyERP.Modules.System.Contracts.ICompanyImageReader.GetAsync(kind)` 回傳目前公司的 `CompanyImage`（類型、寬高、內容），沒有上傳時為 `null`。列印模組在 `ShowLogo`／`ShowStamp` 開啟且有圖片時才印出，沒有圖片時略過，不報錯。
7. **畫面**：`/sys/logo-stamp`（介面 8.57）每種圖片一張卡片：預覽、檔名／大小／尺寸、上傳（或替換）、刪除（確認框）。用戶端先檢查類型與大小以便即時提示，伺服器仍作最終檢查。

## 後果

- 不需要檔案儲存設定；資料庫備份會包含圖片（每間公司最多約 2 MB）。
- 只解析檔頭，檔頭正確但影像資料損壞的檔案仍可上傳（只會在預覽時顯示不出來），可以重新上傳替換。
- 若日後需要更多圖片種類（例如電子簽名），只需在 `CompanyImageKind` 加值與檢查條件，並調整 `ck_sys_company_image_kind`。
