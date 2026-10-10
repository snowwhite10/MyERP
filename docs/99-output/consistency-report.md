# 一致性檢查報告

> 由 `tools/docgen` 自動產生（MASTER_PROMPT 15.8），不得手動修改。產生日期：2026-10-11；資料庫：no --connection supplied。

## 摘要

| 項目 | 數量 |
|---|---|
| 功能需求 REQ_F | 44 |
| 非功能需求 REQ_NF | 33 |
| 資料結構 REQ_DS43 | 9 |
| 介面 | 9 |
| 介面欄位 REQ_IO_A | 36 |
| 測試案例 | 101 |
| BlueWhale 對照項目 | 460 |
| Error | 0 |
| Warning | 25 |
| Info | 0 |

**結果：通過（沒有 Error）。**

## Error（0）

無。

## Warning（25）

| 代碼 | 說明 | 位置 |
|---|---|---|
| MISSING_SOURCE | MyERP_SRS.docx: 47 narrative source file(s) not written yet: docs/05-doc-sources/srs/nda.md, docs/05-doc-sources/srs/ch1-1-purpose.md, docs/05-doc-sources/srs/ch1-2-scope.md, docs/05-doc-sources/srs/ch1-3-definitions.md, docs/05-doc-sources/srs/ch1-4-intended-audience.md, docs/05-doc-sources/srs/ch1-5-references.md, docs/05-doc-sources/srs/ch1-6-roles.md, docs/05-doc-sources/srs/ch1-7-overview.md, docs/05-doc-sources/srs/ch2-1-product-perspective.md, docs/05-doc-sources/srs/ch2-2-02-sales.md, docs/05-doc-sources/srs/ch2-2-03-production.md, docs/05-doc-sources/srs/ch2-2-04-purchase.md, docs/05-doc-sources/srs/ch2-2-05-inventory.md, docs/05-doc-sources/srs/ch2-2-06-accounting.md, docs/05-doc-sources/srs/ch2-2-07-report.md, docs/05-doc-sources/srs/ch2-2-08-settings.md, docs/05-doc-sources/srs/ch2-2-10-controlling.md, docs/05-doc-sources/srs/ch2-2-11-quality.md, docs/05-doc-sources/srs/ch2-2-12-maintenance.md, docs/05-doc-sources/srs/ch2-2-13-crm.md, docs/05-doc-sources/srs/ch2-2-14-human-resources.md, docs/05-doc-sources/srs/ch2-2-15-project.md, docs/05-doc-sources/srs/ch2-2-16-supply-chain.md, docs/05-doc-sources/srs/ch2-3-user-classes.md, docs/05-doc-sources/srs/ch2-4-1-operating-system.md, docs/05-doc-sources/srs/ch2-4-2-hardware.md, docs/05-doc-sources/srs/ch2-4-3-tools.md, docs/05-doc-sources/srs/ch2-5-constraints.md, docs/05-doc-sources/srs/ch2-6-1-assumptions.md, docs/05-doc-sources/srs/ch2-6-2-dependencies.md, docs/05-doc-sources/srs/ch3-1-interview.md, docs/05-doc-sources/srs/ch3-2-1-economic.md, docs/05-doc-sources/srs/ch3-2-2-operational.md, docs/05-doc-sources/srs/ch3-2-3-technical.md, docs/05-doc-sources/srs/ch4-1-intro.md, docs/05-doc-sources/srs/ch4-2-1-performance.md, docs/05-doc-sources/srs/ch4-2-2-security.md, docs/05-doc-sources/srs/ch4-2-3-quality.md, docs/05-doc-sources/srs/ch4-2-4-availability.md, docs/05-doc-sources/srs/ch4-2-5-testability.md, docs/05-doc-sources/srs/ch4-3-intro.md, docs/05-doc-sources/srs/ch4-4-intro.md, docs/05-doc-sources/srs/ch4-6-1-scrum.md, docs/05-doc-sources/srs/ch4-6-2-timeline.md, docs/05-doc-sources/srs/ch4-6-3-human-resources.md, docs/05-doc-sources/srs/ch4-6-4-budget.md, docs/05-doc-sources/srs/ch4-6-5-product-design.md. | - |
| MISSING_SOURCE | MyERP_SDS.docx: 12 narrative source file(s) not written yet: docs/05-doc-sources/sds/ch1-1-purpose.md, docs/05-doc-sources/sds/ch1-2-scope.md, docs/05-doc-sources/sds/ch1-3-definitions.md, docs/05-doc-sources/sds/ch1-4-intended-audience.md, docs/05-doc-sources/sds/ch1-5-references.md, docs/05-doc-sources/sds/ch1-6-overview.md, docs/05-doc-sources/sds/ch2-1-goals.md, docs/05-doc-sources/sds/ch2-2-development-methods.md, docs/05-doc-sources/sds/ch2-3-architecture-strategies.md, docs/05-doc-sources/sds/ch3-1-system-architecture.md, docs/05-doc-sources/sds/ch3-2-security-architecture.md, docs/05-doc-sources/sds/ch3-3-software-architecture.md. | - |
| PARITY_OPEN | BlueWhale parity (Function): 222 of 222 entries are not done yet. | - |
| PARITY_OPEN | BlueWhale parity (Interface): 150 of 150 entries are not done yet. | - |
| PARITY_OPEN | BlueWhale parity (Table): 85 of 88 entries are not done yet. | - |
| SCHEMA_NOT_CHECKED | Database schema not read (no --connection supplied); SRS 4.3 vs SDS 4.1 vs database was not checked. | - |
| TRACE_GAP | 'REQ_F4118.26.5' has no data structure, interface. | docs/02-modules/SYS/requirements.md:100 |
| TRACE_GAP | 'REQ_F4118.26.6' has no data structure, interface. | docs/02-modules/SYS/requirements.md:101 |
| TRACE_GAP | 'REQ_F4118.26.7' has no data structure, interface. | docs/02-modules/SYS/requirements.md:102 |
| TRACE_GAP | 'REQ_F4118.27.1' has no interface. | docs/02-modules/SYS/requirements.md:114 |
| TRACE_GAP | 'REQ_F4118.27.3' has no interface. | docs/02-modules/SYS/requirements.md:116 |
| TRACE_GAP | 'REQ_F4118.27.5' has no interface. | docs/02-modules/SYS/requirements.md:118 |
| TRACE_GAP | 'REQ_F4118.23.3' has no interface. | docs/02-modules/SYS/requirements.md:42 |
| TRACE_GAP | 'REQ_F4118.23.4' has no interface. | docs/02-modules/SYS/requirements.md:43 |
| TRACE_GAP | 'REQ_F4118.24.1' has no data structure. | docs/02-modules/SYS/requirements.md:59 |
| TRACE_GAP | 'REQ_F4118.24.2' has no data structure, interface. | docs/02-modules/SYS/requirements.md:60 |
| TRACE_GAP | 'REQ_F4118.24.3' has no data structure, interface. | docs/02-modules/SYS/requirements.md:61 |
| TRACE_GAP | 'REQ_F4118.24.4' has no data structure, interface. | docs/02-modules/SYS/requirements.md:62 |
| TRACE_GAP | 'REQ_F4118.24.5' has no data structure. | docs/02-modules/SYS/requirements.md:63 |
| TRACE_GAP | 'REQ_F4118.25.5' has no interface. | docs/02-modules/SYS/requirements.md:79 |
| TRACE_GAP | 'REQ_F4118.25.7' has no interface. | docs/02-modules/SYS/requirements.md:81 |
| TRACE_GAP | 'REQ_F4118.26.1' has no data structure, interface. | docs/02-modules/SYS/requirements.md:96 |
| TRACE_GAP | 'REQ_F4118.26.2' has no data structure, interface. | docs/02-modules/SYS/requirements.md:97 |
| TRACE_GAP | 'REQ_F4118.26.3' has no data structure, interface. | docs/02-modules/SYS/requirements.md:98 |
| TRACE_GAP | 'REQ_F4118.26.4' has no data structure, interface. | docs/02-modules/SYS/requirements.md:99 |

## Info（0）

無。
