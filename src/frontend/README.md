# MyERP 前端

Angular 22.2（standalone、zoneless）+ ng-zorro-antd 22.1.1 + AntV G2 5.4.8 + @ngx-translate 18。
規範見 `docs/01-architecture/ui-conventions.md`，實作決策見 ADR-001、ADR-007。

## 指令（repo 根目錄，PowerShell）

需要 Node 24.21.0（`.nvmrc`）。`Invoke-Frontend.ps1` 會自動使用 repo 內的可攜版 Node（第一次執行時下載）。

| 用途 | 指令 |
|---|---|
| 安裝 | `& ./tools/scripts/Invoke-Frontend.ps1 npm ci` |
| 開發伺服器 | `& ./tools/scripts/Invoke-Frontend.ps1 npx ng serve`（http://localhost:4200） |
| 建置 | `& ./tools/scripts/Invoke-Frontend.ps1 npx ng build` |
| 單元測試 | `& ./tools/scripts/Invoke-Frontend.ps1 npx ng test --watch=false` |
| 覆蓋率 | `& ./tools/scripts/Invoke-Frontend.ps1 npx ng test --watch=false --coverage` |

## 結構

```
src/app/
├─ core/       i18n（語言、範圍載入）、theme（亮 / 暗 / 跟隨系統、馬卡龍色）、company、http 攔截器、icons
├─ layout/     主框架（側欄、頂列：公司 / 語言 / 主題）、選單定義
├─ shared/     charts（erp-line-chart、erp-column-chart、erp-pie-chart、erp-kpi-card）
└─ pages/      首頁（示範圖表）、403 / 404
public/i18n/{zh-Hant,zh-Hans,en}/{common,layout,errors}.json
src/styles/theme/ng-zorro-{light,dark}.less   ng-zorro 主題（less 編譯）
src/testing/   測試輔助：語言檔 fixture、假圖表
```
