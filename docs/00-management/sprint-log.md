# Sprint Log

> 一個 Sprint = 一組相關工作單元（建議 8–15 個）。記錄目標、完成內容與回顧。
> 本項目以工作單元計量，「期間」記錄實際開始 / 結束日期。

| Sprint | 期間 | 目標 | 完成工作單元 | 未完成 / 延後 | 回顧（做得好 / 要改善） |
|---|---|---|---|---|---|
| Sprint 1 — P0 基礎 | 2026-10-10 ～ 進行中 | 完成項目規劃文件與技術骨架，令後續模組可直接開發（里程碑 M0） | WU-P0-001、002、003 | - | 進行中 |

## Sprint 計劃

### Sprint 1 — P0 基礎（WU-P0-001～013，里程碑 M0）

**Sprint 目標**：規劃文件齊備；後端 / 前端骨架可建置、可測試；共用核心與 DB 基線完成；CI 綠燈；docgen 骨架可產生 SRS / SDS 空殼。

| 次序 | 工作單元 | 內容 | 依賴 | 驗收重點 |
|---|---|---|---|---|
| 1 | WU-P0-001 | Runner 檢查 | - | ✔ 已完成 |
| 2 | WU-P0-002 | BlueWhale 參考筆記與對照表 | - | ✔ 已完成 |
| 3 | WU-P0-003 | 章程、風險、Sprint 計劃 | 002 | ✔ 已完成 |
| 4 | WU-P0-004 | ADR-001 技術版本固定 | - | .NET / Angular / ng-zorro / 主要套件版本已查證相容 |
| 5 | WU-P0-005 | architecture.md、api / db / ui conventions、NFR | 004 | Mermaid 依賴圖；NFR 每項有 REQ_NF ID 與量化指標 |
| 6 | WU-P0-006 | 後端解決方案骨架 | 005 | `dotnet build` / `dotnet test` 通過 |
| 7 | WU-P0-007 | 共用核心（IClock、Money、ICurrentUser…、interceptor、Query Filter） | 006 | 單元測試覆蓋率 ≥ 80% |
| 8 | WU-P0-008 | 資料庫基線（定序、snake_case、myerp_test 重建、SP 部署） | 006 | migration 可建庫與還原 |
| 9 | WU-P0-010 | 前端骨架（ng-zorro、i18n、dark mode、主題、Layout、G2） | 004 | `ng build` / `ng test` 通過 |
| 10 | WU-P0-009 | Serilog 與 enricher | 006 | 慢 API Warning 有測試 |
| 11 | WU-P0-011 | CI（GitHub Actions windows-latest） | 006、010 | CI 綠燈 |
| 12 | WU-P0-012 | Playwright 基礎設定 + 冒煙測試 | 010 | 冒煙測試通過 |
| 13 | WU-P0-013 | docgen 骨架 | 002 | 產生 SRS / SDS / xlsx 空殼並套用 BlueWhale 樣式 |

> 次序依依賴與優先級（P1 先於 P2）排列；010 只依賴 004，可提前於 009。

### Sprint 2 — P0 SYS（WU-SYS-001～014，里程碑 M1）— 預定

在 Sprint 1 完成後開始；計劃於 Sprint 1 回顧時細化。
