# 前端 UI 規範

> 來源：MASTER_PROMPT 第 9 節；版本見 [ADR-001](adr/ADR-001-技術版本固定.md)（Angular、ng-zorro-antd、AntV G2、@ngx-translate）。各模組前端必須遵守；WU-P0-010 建立骨架、WU-SYS-014 建立共用範本元件。
> 設計原則取自 Ant Design（確定、自然、意義感、生長性），套用於 ng-zorro-antd。

## 1. 專案結構

```
src/frontend/src/app/
├─ core/            # 單例服務：auth、http 攔截器（token、X-Company-Id、Accept-Language、錯誤）、i18n、theme、permission
├─ shared/          # 共用元件 / 指令 / pipe：列表頁、單據頁範本、選擇視窗、確認框、圖表、captcha
├─ layout/          # 主框架：側邊選單（menu.ts）、頂列、麵包屑、分頁標籤
├─ pages/           # 不屬於業務模組的頁面：首頁、403 / 404
└─ modules/<prefix>/   # 每個業務模組一個資料夾，lazy-loaded route（例如 modules/sal）
     ├─ <prefix>.routes.ts
     ├─ <resource>/<resource>-list.page.ts、<resource>-form.page.ts
     └─ services/<resource>.api.ts
```

- 一律 **standalone components**、`OnPush`、signals 優先；模組路由 `loadChildren` lazy-load。
- 前端路由：`/{prefix}/{resource}`、`/{prefix}/{resource}/new`、`/{prefix}/{resource}/{id}`、`/{prefix}/{resource}/{id}/edit`（prefix 與 API 相同，見 api-conventions）。
- 檔名 kebab-case；元件 selector 前綴 `erp-`；權限指令 `*erpHasPermission`。
- API 呼叫集中於 `*.api.ts`，型別與後端 DTO 對應；頁面不直接用 `HttpClient`。

## 2. 版面（Layout）

- 左側選單（可收合，對應 BlueWhale *Navigation Menu Collapsed / Expanded*）＋頂列（公司切換、語言、主題、通知、用戶選單）＋內容區（麵包屑 + 頁面標題 + 內容）。
- 選單依權限動態產生：無 `VIEW` 權限的模組 / 頁面不顯示。
- 間距使用 8 px 格線（8 / 16 / 24 / 32）；內容區左右留白 24 px；卡片圓角 8 px。
- 響應式斷點沿用 ng-zorro：`≥ 1200`（桌面）完整版面；`768–1199`（平板）選單預設收合、表單兩欄改一欄；`< 768` 只保證可瀏覽與審批。

## 3. 多語言（i18n）

- 使用 `@ngx-translate`，執行期切換：`zh-Hant`（預設）、`zh-Hans`、`en`。
- 語言檔：`src/frontend/public/i18n/{lang}/{scope}.json`（網站路徑 `/i18n/...`），`scope` = `common`、`layout`、`errors`（啟動時載入）、各模組 prefix（模組路由加 `canActivate: [i18nScopeGuard('<prefix>')]` 延遲載入）。檔案內容不含範圍名稱，由 `ScopedTranslateLoader` 放在 `{scope}` 節點下（ADR-007）。新增範圍時要在 `src/testing/i18n-fixtures.ts` 登記三種語言，`i18n-keys.spec.ts` 會檢查 key 是否一致。
- Key 格式：`{scope}.{page/resource}.{item}`，小駝峰，例如 `sal.order.docNo`、`common.action.save`、`errors.SAL.ORDER.CREDIT_LIMIT_EXCEEDED`。
- **所有**可見文字（標籤、按鈕、提示、表頭、空狀態、驗證訊息、圖表標題與圖例）使用 key，不得寫死；前端單元測試檢查三語 key 完全一致（REQ_NF423.3）。
- ng-zorro 元件語系（`NZ_I18N`）與日期格式同步切換；用戶選擇儲存於個人設定（後端）與 localStorage。
- 匯出檔欄名依目前語言。

## 4. 主題：馬卡龍色系與 Dark mode

### 4.1 色彩

- 主色與圖表色盤採**馬卡龍色系**（柔和、低飽和），以 CSS 變數 / ng-zorro token 定義於 `styles/theme/`，元件不得寫死色碼。
- 主色（品牌）`--erp-color-primary` 取柔和藍，需與白底 / 深底皆達 WCAG AA（文字 4.5:1）；不足時文字用深一階色。
- 狀態色（標籤）：

| 狀態 | 語意 token | 用途 |
|---|---|---|
| Draft | `--erp-status-draft`（灰） | 草稿 |
| Submitted | `--erp-status-pending`（馬卡龍黃） | 待審 |
| Approved / Posted | `--erp-status-success`（馬卡龍綠） | 已核准 / 已過帳 |
| Rejected | `--erp-status-error`（馬卡龍紅） | 退回 |
| Voided / Closed | `--erp-status-muted`（淺灰 + 刪除線） | 作廢 / 結案 |

- 圖表色盤（G2）：8 色馬卡龍序列，亮 / 暗主題各一組，WU-P0-010 已用對比檢查定稿（`src/app/core/theme/macaron-palette.ts`）：
  - 亮色（中等飽和度，每色與白底 ≥ 1.8:1）：`#5fb3d4 #f29e85 #86c97f #d9a93f #a68fd6 #f28fb0 #6fa8dc #f5a96b`
  - 暗色（淺馬卡龍色，每色與 `#1f1f1f` ≥ 3:1）：`#7ec8e3 #f7b7a3 #b5e3b0 #f9e79f #c3b1e1 #ffc2d4 #a8d8ea #ffdab9`
- 主色：亮色 `#3f6fa3`（白字）、暗色 `#6fa8dc`（主要按鈕用深色字 `#141414`）。色值同時存在於 `macaron-palette.ts`、`src/styles.scss`（`--erp-*`）、`src/styles/theme/ng-zorro-{light,dark}.less`，修改時三處必須同步；文字、狀態標籤、漲跌色的 AA 對比由 `macaron-palette.spec.ts` 檢查。

### 4.2 Dark mode

- 三種模式：亮 / 暗 / 跟隨系統（`prefers-color-scheme`）；儲存於個人設定。
- 切換時：ng-zorro 主題 token、CSS 變數、**G2 圖表主題**同步切換（圖表元件訂閱 `ThemeService`，重新渲染）。
- 實作（ADR-007）：`ThemeService.mode()`（用戶選擇）/ `effective()`（實際亮或暗）/ `tokens()`；暗色時在 `<html>` 加上 `erp-dark` class，並插入 `theme-dark.css`（less 編譯的暗色 ng-zorro bundle）。
- 重要頁面需另有 Dark mode 截圖（SDS 4.2）。

## 5. 標準列表頁（`erp-list-page`）

區塊由上到下：

1. **頁面標題列**：標題、主要動作（新增）、次要動作（匯入、匯出、更多）。
2. **篩選區**：常用 3–4 個欄位 + 「展開更多」；日期範圍附**快選**（今天、本週、本月、上月、本季、本年、自訂）；「查詢」「重設」按鈕。篩選條件寫入 URL query，可分享 / 返回保留。
3. **批次操作列**：勾選後出現（批次審核、批次刪除、批次匯出），顯示已選筆數。
4. **表格**（`nz-table`）：伺服器端分頁（10 / 20 / 50 / 100 / 200）、排序、**欄位顯示設定**（勾選 / 拖曳排序，存個人設定）、固定首欄（單號）與操作欄；單號為連結開啟單據頁。
5. 空狀態、載入中骨架、錯誤重試。

實作（WU-SYS-014，ADR-024）：

```html
<erp-list-page titleKey="sal.order.title" testId="sal-order" [selectedCount]="selected().size" [errorKey]="loadError()"
  (querySubmit)="search()" (filterReset)="reset()" (retryLoad)="reload()" (selectionClear)="clearSelection()">
  <button erpListActions nz-button nzType="primary" *erpHasPermission="'SAL.ORDER.CREATE'">…</button>
  <erp-date-range erpListFilters testId="sal-order-date" [(from)]="from" [(to)]="to" (quickSelected)="search()" />
  <button erpListBatch nz-button *erpHasPermission="'SAL.ORDER.APPROVE'">…</button>
  <nz-table …>…</nz-table>
</erp-list-page>
```

- `onQueryParams` 用 `nextTableState(params, 目前狀態, 排序白名單, 預設排序)`（相同時回傳 null，不重查），`nzSortOrder` 用 `sortOrderOf()`；分頁結果型別 `PagedResult<T>`、每頁筆數 `PAGE_SIZE_OPTIONS`（`shared/list-page/table-query.ts`）。
- 日期範圍一律用 `erp-date-range`（快選今天／本週（星期一起）／本月／上月／本季／本年；計算見 `quickRange()`）。
- 範本輸出不得用原生 DOM 事件名稱（`search`、`reset`…），否則會收到投影內容冒泡的原生事件。

欄位格式：

| 類型 | 對齊 | 格式 |
|---|---|---|
| 金額 | 右 | 千分位，小數位依幣別（TWD 0、HKD / USD / CNY 2、JPY 0）；負數紅色 |
| 數量 / 單價 | 右 | 千分位，依物料 / 系統參數小數位 |
| 日期 | 左 | `yyyy-MM-dd`；時間戳 `yyyy-MM-dd HH:mm` |
| 狀態 | 中 | `nz-tag` 使用第 4.1 狀態色 |
| 代碼 / 單號 | 左 | 等寬字型可選 |

## 6. 標準單據頁（`erp-document-page`）

1. **表頭列**：單號、狀態標籤、**上一筆 / 下一筆**（依進入時的列表篩選與排序）、動作按鈕（依狀態與權限顯示：儲存、送審、核准、退回、作廢、過帳、列印、複製）。
2. **表頭表單**：桌面 3 欄 / 平板 1–2 欄；必填欄位標示 `*`；選擇主檔一律用**選擇視窗**（`erp-select-dialog`，對應 BlueWhale *Select Client Interface* 等）或可搜尋下拉。
3. **明細表格**：可編輯表格（新增行、刪除行、複製行、鍵盤 Tab / Enter 移動）；行金額即時預覽，儲存後以後端計算為準；表尾合計列。
4. **頁籤區**：審批記錄、附件、稽核歷史（欄位級新舊值，來自 `sys_audit_log`）、關聯單據。
5. 未儲存離開時提示確認；409（版本衝突）提示重新載入。

實作（WU-SYS-014）：`erp-document-page`（`titleKey`、`testId`、`docNo`、`status`、`navigable`／`hasPrevious`／`hasNext`、`dirty`、`errorKey`、`conflict`；輸出 `back`、`previous`、`next`、`reload`、`errorClose`；插槽 `[erpDocumentActions]`、預設內容、`[erpDocumentTabs]`）。前後筆以 `adjacentIds(列表 ID 順序, 目前 ID)` 計算。頁面實作 `HasUnsavedChanges`，路由加 `canDeactivate: [unsavedChangesGuard]`。狀態標籤用 `erp-status-tag`（`DocumentStatus`：Draft、Submitted、Approved、Posted、Rejected、Voided、Closed；模組自有狀態可傳 `labelKey`）。

## 7. 表單與互動

- 驗證：前端即時驗證（必填、長度、格式）＋後端 400 `errors` 對應回欄位顯示；422 業務錯誤以頁面頂部 `nz-alert` 顯示 `errorCode` 翻譯。
- 按鈕順序：主要動作在右（儲存 / 確定），取消在左；危險動作（刪除、作廢）用 `nzDanger` 並**必須**二次確認（`erp-confirm`，對應 BlueWhale 確認框）。
- 提示：成功操作用 `nz-message`（3 秒）；需要用戶注意的結果（匯入結果）用 `nz-modal` 提示框。
- 防重複提交：送出後按鈕 loading 並停用。
- 鍵盤：`Ctrl+S` 儲存、`Esc` 關閉對話框。
- 對話框一律經 `ErpDialogService`（`shared/dialogs/dialog.service.ts`，不直接用 NzModalService／popconfirm）：
  - 確認框 `confirm({ titleKey, contentKey?, params?, okKey?, danger? })` → `Observable<boolean>`；刪除用 `okKey: 'common.action.delete'`、`danger: true`、內容 `common.confirm.irreversible`；
  - 提示框 `alert('info' | 'success' | 'warning' | 'error', { titleKey, contentKey?, params? })`；成功提示 `success(key, params)`（3 秒）；
  - 選擇視窗 `select({ titleKey, columns, load, rowKey, multiple?, selected? })` → `Observable<T[] | null>`；`load` 傳 `*.api.ts` 的查詢方法。
  - E2E 以 `erp-message-ok`／`erp-message-cancel`、`erp-select-row-{key}`／`erp-select-confirm` 操作；元件單元測試提供 `{ provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' }` 與 `provideNzIcons(APP_ICONS)`（ADR-024）。

## 8. 權限

- `*erpHasPermission="'SAL.ORDER.APPROVE'"`：無權限的按鈕、選單、頁籤**不顯示**（不是停用）。
- 路由守衛檢查 `VIEW` 權限；無權限導向 403 頁。
- 前端權限只為體驗，**後端為準**。

## 9. 圖表（AntV G2 共用元件）

| 元件 | 用途 |
|---|---|
| `erp-line-chart` | 趨勢（月份銷售、現金流） |
| `erp-column-chart` | 比較（本期 vs 上期 / 預算） |
| `erp-pie-chart` | 佔比（品類、客戶分佈；類別 > 7 時合併「其他」） |
| `erp-kpi-card` | 數字指標 + 環比 / 同比 + 迷你趨勢 |

- 輸入：資料陣列 + 欄位對應 + i18n key；元件負責主題（亮 / 暗、馬卡龍色盤）、響應式尺寸、空狀態、tooltip 千分位。
- 用法：`import { LineChart, ColumnChart, PieChart, KpiCard } from 'shared/charts'`；`titleKey` 為標題 i18n key；系列 / 類別值是 i18n key 時，設定 `[translateSeries]="true"` / `[translateCategories]="true"`；`(elementClick)` 輸出被點擊的那筆資料，供頁面鑽取。KPI 卡片：`value`、`previous`（null / 0 時顯示「無上期資料」）、`currency`（TWD / JPY 0 位小數，其他 2 位）、`trend`（至少 2 點才顯示迷你趨勢）。
- 測試：G2 需要 canvas，元件測試以 `provideFakeCharts()`（`src/testing/fake-chart.ts`）替代，只驗證產生的 spec。
- 圖表點擊可鑽取（輸出事件，由頁面決定導向明細）。

## 10. 匯出、列印與 Captcha

- 匯出 Excel / PDF 由**後端**產生（api-conventions `export`），前端只傳篩選條件與語言。
- 列印：後端產生 PDF（套用列印設定、Logo 與印章），前端新視窗預覽。
- Captcha 元件 `erp-captcha`：顯示圖片 + 刷新按鈕 + 數字輸入框（長度 4–6）。

## 11. 可測試性與無障礙

- 互動元素加 `data-testid="{prefix}-{resource}-{element}"`（例如 `sal-order-save`），Playwright 只用 `data-testid` 或角色定位。
- 圖示按鈕必須有 `nz-tooltip` 與 `aria-label`（i18n）。
- 對比度 ≥ WCAG 2.1 AA；可鍵盤操作主要流程。

## 12. 檢查清單（code review 用）

- [ ] standalone + lazy route，頁面不直接用 HttpClient
- [ ] 無寫死文字，三語 key 齊全
- [ ] 亮 / 暗主題皆正常，圖表同步
- [ ] 列表：篩選（日期快選）、分頁、排序、欄位設定、匯出、批次操作
- [ ] 單據：狀態標籤、審批記錄、附件、稽核歷史、上一筆 / 下一筆
- [ ] 權限指令覆蓋所有動作按鈕
- [ ] 平板寬度（768）可正常使用
- [ ] `data-testid` 齊全
