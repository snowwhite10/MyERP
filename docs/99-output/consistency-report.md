# 一致性檢查報告

> 由 `tools/docgen` 自動產生（MASTER_PROMPT 15.8），不得手動修改。產生日期：2026-10-16；資料庫：localhost\MSSQLSERVER2022/myerp。

## 摘要

| 項目 | 數量 |
|---|---|
| 功能需求 REQ_F | 271 |
| 非功能需求 REQ_NF | 33 |
| 資料結構 REQ_DS43 | 72 |
| 介面 | 109 |
| 介面欄位 REQ_IO_A | 419 |
| 測試案例 | 469 |
| BlueWhale 對照項目 | 460 |
| Error | 0 |
| Warning | 64 |
| Info | 0 |

**結果：通過（沒有 Error）。**

## Error（0）

無。

## Warning（64）

| 代碼 | 說明 | 位置 |
|---|---|---|
| MISSING_SOURCE | MyERP_SRS.docx: 46 narrative source file(s) not written yet: docs/05-doc-sources/srs/nda.md, docs/05-doc-sources/srs/ch1-1-purpose.md, docs/05-doc-sources/srs/ch1-2-scope.md, docs/05-doc-sources/srs/ch1-3-definitions.md, docs/05-doc-sources/srs/ch1-4-intended-audience.md, docs/05-doc-sources/srs/ch1-5-references.md, docs/05-doc-sources/srs/ch1-6-roles.md, docs/05-doc-sources/srs/ch1-7-overview.md, docs/05-doc-sources/srs/ch2-1-product-perspective.md, docs/05-doc-sources/srs/ch2-2-02-sales.md, docs/05-doc-sources/srs/ch2-2-03-production.md, docs/05-doc-sources/srs/ch2-2-04-purchase.md, docs/05-doc-sources/srs/ch2-2-05-inventory.md, docs/05-doc-sources/srs/ch2-2-06-accounting.md, docs/05-doc-sources/srs/ch2-2-07-report.md, docs/05-doc-sources/srs/ch2-2-10-controlling.md, docs/05-doc-sources/srs/ch2-2-11-quality.md, docs/05-doc-sources/srs/ch2-2-12-maintenance.md, docs/05-doc-sources/srs/ch2-2-13-crm.md, docs/05-doc-sources/srs/ch2-2-14-human-resources.md, docs/05-doc-sources/srs/ch2-2-15-project.md, docs/05-doc-sources/srs/ch2-2-16-supply-chain.md, docs/05-doc-sources/srs/ch2-3-user-classes.md, docs/05-doc-sources/srs/ch2-4-1-operating-system.md, docs/05-doc-sources/srs/ch2-4-2-hardware.md, docs/05-doc-sources/srs/ch2-4-3-tools.md, docs/05-doc-sources/srs/ch2-5-constraints.md, docs/05-doc-sources/srs/ch2-6-1-assumptions.md, docs/05-doc-sources/srs/ch2-6-2-dependencies.md, docs/05-doc-sources/srs/ch3-1-interview.md, docs/05-doc-sources/srs/ch3-2-1-economic.md, docs/05-doc-sources/srs/ch3-2-2-operational.md, docs/05-doc-sources/srs/ch3-2-3-technical.md, docs/05-doc-sources/srs/ch4-1-intro.md, docs/05-doc-sources/srs/ch4-2-1-performance.md, docs/05-doc-sources/srs/ch4-2-2-security.md, docs/05-doc-sources/srs/ch4-2-3-quality.md, docs/05-doc-sources/srs/ch4-2-4-availability.md, docs/05-doc-sources/srs/ch4-2-5-testability.md, docs/05-doc-sources/srs/ch4-3-intro.md, docs/05-doc-sources/srs/ch4-4-intro.md, docs/05-doc-sources/srs/ch4-6-1-scrum.md, docs/05-doc-sources/srs/ch4-6-2-timeline.md, docs/05-doc-sources/srs/ch4-6-3-human-resources.md, docs/05-doc-sources/srs/ch4-6-4-budget.md, docs/05-doc-sources/srs/ch4-6-5-product-design.md. | - |
| MISSING_SOURCE | MyERP_SDS.docx: 12 narrative source file(s) not written yet: docs/05-doc-sources/sds/ch1-1-purpose.md, docs/05-doc-sources/sds/ch1-2-scope.md, docs/05-doc-sources/sds/ch1-3-definitions.md, docs/05-doc-sources/sds/ch1-4-intended-audience.md, docs/05-doc-sources/sds/ch1-5-references.md, docs/05-doc-sources/sds/ch1-6-overview.md, docs/05-doc-sources/sds/ch2-1-goals.md, docs/05-doc-sources/sds/ch2-2-development-methods.md, docs/05-doc-sources/sds/ch2-3-architecture-strategies.md, docs/05-doc-sources/sds/ch3-1-system-architecture.md, docs/05-doc-sources/sds/ch3-2-security-architecture.md, docs/05-doc-sources/sds/ch3-3-software-architecture.md. | - |
| PARITY_OPEN | BlueWhale parity (Function): 170 of 222 entries are not done yet. | - |
| PARITY_OPEN | BlueWhale parity (Interface): 108 of 150 entries are not done yet. | - |
| PARITY_OPEN | BlueWhale parity (Table): 62 of 88 entries are not done yet. | - |
| TRACE_GAP | 'REQ_F4115.21.1' has no data structure, interface. | docs/02-modules/INV/requirements.md:131 |
| TRACE_GAP | 'REQ_F4115.21.2' has no data structure, interface. | docs/02-modules/INV/requirements.md:132 |
| TRACE_GAP | 'REQ_F4115.21.3' has no interface. | docs/02-modules/INV/requirements.md:133 |
| TRACE_GAP | 'REQ_F4115.22.1' has no interface. | docs/02-modules/INV/requirements.md:147 |
| TRACE_GAP | 'REQ_F4115.22.2' has no interface. | docs/02-modules/INV/requirements.md:148 |
| TRACE_GAP | 'REQ_F4115.22.3' has no interface. | docs/02-modules/INV/requirements.md:149 |
| TRACE_GAP | 'REQ_F4115.20.1' has no data structure. | docs/02-modules/INV/requirements.md:192 |
| TRACE_GAP | 'REQ_F4115.20.2' has no data structure. | docs/02-modules/INV/requirements.md:193 |
| TRACE_GAP | 'REQ_F4115.20.3' has no data structure. | docs/02-modules/INV/requirements.md:194 |
| TRACE_GAP | 'REQ_F4115.22.4' has no interface. | docs/02-modules/INV/requirements.md:24 |
| TRACE_GAP | 'REQ_F4115.22.5' has no interface. | docs/02-modules/INV/requirements.md:25 |
| TRACE_GAP | 'REQ_F4118.34.1' has no data structure, interface. | docs/02-modules/MD/requirements.md:11 |
| TRACE_GAP | 'REQ_F4118.36.5' has no interface. | docs/02-modules/MD/requirements.md:205 |
| TRACE_GAP | 'REQ_F4118.39.5' has no interface. | docs/02-modules/MD/requirements.md:243 |
| TRACE_GAP | 'REQ_F4118.14.5' has no interface. | docs/02-modules/MD/requirements.md:260 |
| TRACE_GAP | 'REQ_F4118.5.5' has no interface. | docs/02-modules/MD/requirements.md:282 |
| TRACE_GAP | 'REQ_F4118.12.5' has no interface. | docs/02-modules/MD/requirements.md:287 |
| TRACE_GAP | 'REQ_F4118.7.5' has no interface. | docs/02-modules/MD/requirements.md:310 |
| TRACE_GAP | 'REQ_F4118.2.7' has no data structure. | docs/02-modules/MD/requirements.md:359 |
| TRACE_GAP | 'REQ_F4118.1.7' has no data structure. | docs/02-modules/MD/requirements.md:56 |
| TRACE_GAP | 'REQ_F4118.26.1' has no data structure, interface. | docs/02-modules/SYS/requirements.md:110 |
| TRACE_GAP | 'REQ_F4118.26.2' has no data structure, interface. | docs/02-modules/SYS/requirements.md:111 |
| TRACE_GAP | 'REQ_F4118.26.3' has no data structure, interface. | docs/02-modules/SYS/requirements.md:112 |
| TRACE_GAP | 'REQ_F4118.26.4' has no data structure, interface. | docs/02-modules/SYS/requirements.md:113 |
| TRACE_GAP | 'REQ_F4118.26.5' has no data structure, interface. | docs/02-modules/SYS/requirements.md:114 |
| TRACE_GAP | 'REQ_F4118.26.6' has no data structure, interface. | docs/02-modules/SYS/requirements.md:115 |
| TRACE_GAP | 'REQ_F4118.26.7' has no data structure, interface. | docs/02-modules/SYS/requirements.md:116 |
| TRACE_GAP | 'REQ_F4118.27.1' has no interface. | docs/02-modules/SYS/requirements.md:128 |
| TRACE_GAP | 'REQ_F4118.27.3' has no interface. | docs/02-modules/SYS/requirements.md:130 |
| TRACE_GAP | 'REQ_F4118.27.5' has no interface. | docs/02-modules/SYS/requirements.md:132 |
| TRACE_GAP | 'REQ_F4118.29.4' has no interface. | docs/02-modules/SYS/requirements.md:149 |
| TRACE_GAP | 'REQ_F4118.28.5' has no interface. | docs/02-modules/SYS/requirements.md:171 |
| TRACE_GAP | 'REQ_F4118.30.1' has no interface. | docs/02-modules/SYS/requirements.md:186 |
| TRACE_GAP | 'REQ_F4118.30.2' has no interface. | docs/02-modules/SYS/requirements.md:187 |
| TRACE_GAP | 'REQ_F4118.30.3' has no interface. | docs/02-modules/SYS/requirements.md:188 |
| TRACE_GAP | 'REQ_F4118.31.1' has no data structure. | docs/02-modules/SYS/requirements.md:200 |
| TRACE_GAP | 'REQ_F4118.31.2' has no data structure. | docs/02-modules/SYS/requirements.md:201 |
| TRACE_GAP | 'REQ_F4118.31.3' has no data structure. | docs/02-modules/SYS/requirements.md:202 |
| TRACE_GAP | 'REQ_F4118.31.4' has no data structure. | docs/02-modules/SYS/requirements.md:203 |
| TRACE_GAP | 'REQ_F4118.31.5' has no data structure. | docs/02-modules/SYS/requirements.md:204 |
| TRACE_GAP | 'REQ_F4118.31.6' has no data structure. | docs/02-modules/SYS/requirements.md:205 |
| TRACE_GAP | 'REQ_F4118.32.4' has no interface. | docs/02-modules/SYS/requirements.md:220 |
| TRACE_GAP | 'REQ_F4118.32.5' has no data structure, interface. | docs/02-modules/SYS/requirements.md:221 |
| TRACE_GAP | 'REQ_F4118.32.6' has no data structure. | docs/02-modules/SYS/requirements.md:222 |
| TRACE_GAP | 'REQ_F4118.17.3' has no interface. | docs/02-modules/SYS/requirements.md:236 |
| TRACE_GAP | 'REQ_F4118.18.3' has no interface. | docs/02-modules/SYS/requirements.md:252 |
| TRACE_GAP | 'REQ_F4118.19.3' has no interface. | docs/02-modules/SYS/requirements.md:266 |
| TRACE_GAP | 'REQ_F4118.33.1' has no interface. | docs/02-modules/SYS/requirements.md:278 |
| TRACE_GAP | 'REQ_F4111.1.4' has no data structure. | docs/02-modules/SYS/requirements.md:41 |
| TRACE_GAP | 'REQ_F4111.1.5' has no data structure. | docs/02-modules/SYS/requirements.md:42 |
| TRACE_GAP | 'REQ_F4118.23.3' has no interface. | docs/02-modules/SYS/requirements.md:56 |
| TRACE_GAP | 'REQ_F4118.23.4' has no interface. | docs/02-modules/SYS/requirements.md:57 |
| TRACE_GAP | 'REQ_F4118.24.1' has no data structure. | docs/02-modules/SYS/requirements.md:73 |
| TRACE_GAP | 'REQ_F4118.24.2' has no data structure, interface. | docs/02-modules/SYS/requirements.md:74 |
| TRACE_GAP | 'REQ_F4118.24.3' has no data structure, interface. | docs/02-modules/SYS/requirements.md:75 |
| TRACE_GAP | 'REQ_F4118.24.4' has no data structure, interface. | docs/02-modules/SYS/requirements.md:76 |
| TRACE_GAP | 'REQ_F4118.24.5' has no data structure. | docs/02-modules/SYS/requirements.md:77 |
| TRACE_GAP | 'REQ_F4118.25.5' has no interface. | docs/02-modules/SYS/requirements.md:93 |
| TRACE_GAP | 'REQ_F4118.25.7' has no interface. | docs/02-modules/SYS/requirements.md:95 |

## Info（0）

無。
