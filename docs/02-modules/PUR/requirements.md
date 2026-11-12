# PUR 採購需求

WU-PUR-008a39 補足 REQ_F4114.8.2～3 的離線寫入交接驗收：共同 Draft EF 計畫完成全圖預檢與追蹤列旗標套用後，須明確交回待新增持有列、完整既有追蹤列（含零變更歷史列）及實際變更列。新建、改回本張歷史鍵、原號修訂的三組集合均須完整且保留實體參照；預檢失敗不交回部分結果。SQL 寫入者在 008a 交易中接入；需求版本不變。

WU-PUR-008a38 補足 REQ_F4114.8.2～3 的離線套用驗收：共同 Draft EF 計畫須先核對完整表頭與行（包括修訂版本），再核對所有號碼持有列（包括零變更歷史列及新增鍵），之後才按順序套用追蹤列旗標。任一後段預檢失敗時，先前追蹤列保持原值；真實 SQL 交易及競態仍由 008a 驗收，需求版本不變。

WU-PUR-008a37 補足 REQ_F4114.8.2～3 的離線保存前驗收：共同 EF 計畫建立後、套用持有列變更前，須以原已驗行計畫核對表頭身份、Draft 狀態、版本、行數及逐行來源／價稅；完整持有列快照包括零變更的保留鍵亦須核對身份、鍵、活動狀態及版本，新增鍵亦須核對。任一漂移拒絕，不返回可保存結果。真實 SQL 鎖、交易與 HTTP 仍由 008a 驗收；需求版本不變。

WU-PUR-008a34 補足 REQ_F4114.8.2～3 的離線 EF 組裝驗收：由 008a33 已驗表頭與行計畫建立獨立的 Draft 發票實體圖，保留指定發票 ID、公司及來源參照、行號與三種總額；修訂保留期望版本供後續併發寫入使用。錯置公司／發票參照、重複行號、空行或不一致總額均拒絕，審計欄位由持久化攔截器蓋章。此切片不執行 SQL；需求版本不變。

WU-PUR-008a33 補足 REQ_F4114.8.2～3 的 Draft 行保存映射驗收：新建由寫入者指定正數發票 ID，修訂須與已驗保存 ID 及期望版本一致；表頭公司、供應商、訂單、原幣、日期、號碼及三種總額，與每行公司、發票 ID、行號、來源、量及價稅，均由同一重驗保存意圖形成唯讀寫入計畫。行按行號穩定排序，同來源可有多行；錯誤 ID 不輸出計畫。此切片只驗離線映射，真實 SQL 交易、HTTP、權限、稽核和競態仍屬 008a；需求版本不變。

WU-PUR-008a31 補足 REQ_F4114.8.2 的持有列保存驗收：已驗 Draft 新建須使用寫入者指派的正數發票 ID，產生一筆與已驗公司、供應商、正規號碼鍵一致的活動持有列；修訂只接受與已保存發票 ID 相同的寫入身份，從已驗舊鍵及新鍵產生停用、回啟歷史鍵或插入新鍵的順序。持有列快照含他張歷史鍵衝突、同張身份錯置、缺活動鍵或重複鍵時整單拒絕。此為離線意圖，真實 SQL 交易、權限及 HTTP 保存仍屬 008a；需求版本不變。

WU-PUR-007a3 補足 REQ_F4114.2.3～4 的 SUBMIT／SYS 駁回數量轉移離線驗收：Draft／Rejected 提交按已驗保存行聚合，同一訂單行升冪輸出待審量原值、目標值與差額；PendingApproval 同快照重驗必須零變更，駁回僅可從 PendingApproval 精確釋回本單預留。任一來源、狀態、已佔量或本單預留不一致，整單不輸出計畫。WU-PUR-007a6 要求提交／駁回只以請求公司、收貨單 ID 定位，狀態、日期、訂單及行須來自保存快照，逐來源本單預留證據與保存量不符時整單拒絕。WU-PUR-007a4 補足最終核准的離線驗收：PendingApproval 本單預留精確等於保存行合量才可轉為已收量；Posted 重放須逐來源已過帳證據相符且零變更。實際版本、權限、SQL 鎖內重驗與 SYS／INV 同交易留 007a／007b；需求版本不變。

## WU-PUR-008d 三單比對及應付契約設計基線（尚未實作）

WU-PUR-008a19 補足 REQ_F4114.8.2 的整單金額驗收：Draft 驗證成功時，從同一批已驗行逐行累加未稅、稅額和含稅額，返回三種原幣總額供保存；任何行錯誤或總額超出 SQL 金額上限時不得返回結果。總額不得從客戶端另傳或由未驗清單另算。此為離線契約，真實保存仍屬 008a，需求版本不變。

WU-PUR-008a18 補足 REQ_F4114.8.2 的 Draft 驗證結果：保存前驗證須複製整張有效發票行並以唯讀集合返回；呼叫者修改原始清單或將結果轉型成可寫集合後，均不可改變已驗證的行快照。此為離線輸入契約；真實保存、權限及來源重驗仍屬 008a，需求版本不變。

WU-PUR-008a16 補足 REQ_F4114.8.2 的 Draft 行原幣價稅輸入：每行單價非負且最多六位小數、低於 DECIMAL(19,6) 上限；稅率在 0～100 且最多四位小數；未稅、稅額、含稅額各非負、最多四位小數且低於 DECIMAL(19,4) 上限，整單各欄總額亦不得超限。任一行或總額無效整單拒絕，不產生待保存輸入。此為離線形狀驗證，與訂單權威價稅比對及 SQL／HTTP 保存留 008a；需求版本不變。
WU-PUR-008a17 補足 REQ_F4114.8.2 的 Draft 金額一致性：逐行未稅額須等於數量乘發票單價四位 AwayFromZero 取整，稅額須等於已取整未稅額乘稅率除百再四位取整，含稅額須等於未稅加稅。任何不一致或計算溢位整單拒絕；訂單權威價稅差異仍在 MATCH 判定，需求版本不變。

WU-PUR-008a14 補足 REQ_F4114.8.2 的 Draft 發票行保存前形狀驗收：整單必須有 1～1000 行，每行正數且不重複的行號、正數收貨行 ID、正數六位小數且小於 DECIMAL(19,6) 上限的基本單位數量；同張發票可多行引用同一收貨來源，後續由 037b 聚合驗餘量。任一行無效整單拒絕，不輸出部分有效行。這是離線規則；008a 的 SQL／HTTP 保存仍須在權威來源和權限檢查下執行，需求版本不變。

WU-PUR-008a13 補足 REQ_F4114.8.2／8.6 的離線重複判定：同公司、同供應商的正規化號碼如已由另一張發票使用，即使該發票軟刪也回衝突；編輯本張發票沿用原號可通過，其他公司或供應商可使用同號。權威快照身份或既存鍵失效時拒絕。此純規則不保證資料庫讀取完整性或併發唯一性；008a 須以真實 SQL 唯一索引驗收，需求版本不變。

WU-PUR-008a12 補足 REQ_F4114.8.2 的供應商發票號驗收：保存前去除外圍空白、以 Unicode NFKC 正規化並轉大寫形成永久唯一鍵；空白、控制／格式字元、非法 UTF-16，或顯示值／正規化鍵超過 80 個 UTF-16 字元的號碼拒絕。實際同公司同供應商重複（含軟刪）、併發唯一約束及 HTTP 錯誤仍由 008a 驗收；需求版本不變。

WU-PUR-008a35 補足 REQ_F4114.8.2～3 的離線持有列驗收：已重驗 Draft 保存意圖指定公司、供應商及發票 ID，完整持有列快照產生依序新增活動鍵、停用舊鍵或回啟本張歷史鍵的 EF 實體變更；不同發票的活動或歷史佔號回衝突，失效身份、重複持有列 ID 或計畫後變動拒絕。真實交易、SQL 唯一索引、權限與 HTTP 仍由 008a 驗收；需求版本不變。

WU-PUR-008a11 補足 REQ_F4114.8.5 的 POST 離線組裝驗收：請求公司與發票 ID 須與已保存快照一致；複製保存行及接受差異證據後，整單預檢、版本化指紋及待送 FI 的來源資料由同一份快照派生。任一身份、來源、狀態或差異驗證失敗不得返回 FI 資料；Posted 重放只輸出零數量變更與同來源指紋。FI 真實契約與同交易由 008b 驗收；需求版本不變。

WU-PUR-008a10 補足 REQ_F4114.8.5 的來源快照冪等驗收：以保存的公司／發票／供應商／原幣／業務日、逐行收貨來源與價稅基準、已接受差異的原因及差額形成 `PUR-INVOICE-POST-v1:` 版本化 SHA-256 指紋。行與差異決定的載入順序不得影響結果，任何實質快照或覆核原因改變須改變指紋；非法身份、行或過期差異證據拒絕產生。FI 來源鍵比對及同交易仍由 008b 驗收；需求版本不變。

WU-PUR-008a9 補足 REQ_F4114.8.5 的 POST 離線整單預檢：只接受已保存 Matched 或可證明本單已過帳量的 Posted 重放；由保存行重建來源請求、重新驗證採購單／收貨身份與價稅基準，所有當前差異必須逐行有與差異內容完全一致的已保存 Accept 決定，最後產生預留轉已開票量計畫。差異過期、來源失效或任一量不足整單拒絕。FI 同交易、SQL 鎖、HTTP、版本及持久化仍由 008a／008b 驗收；需求版本不變。

WU-PUR-008a8 補足 REQ_F4114.8.5 的 POST 數量轉移離線驗收：Matched 發票按收貨行合量，要求本單預留精確等於保存量，計畫將預留歸零且已開票量增加同量；Posted 重放須有本單已過帳量的權威證據且零變更。任一來源不足、缺漏、重複或舊值不一致整單拒絕。FI 同交易、SQL 鎖、HTTP、版本及持久化仍由 008a／008b 驗收；需求版本不變。

WU-PUR-008a7 補足 REQ_F4114.8.3～4 的離線預留轉移驗收：MATCH 成功按收貨行聚合為目標預留，重驗同一快照不重複增加；Disputed 全行接受保留原預留，任一拒絕後精確釋回。來源缺漏、重複、量不足或原預留不符整單拒絕。此切片不建立 SQL 鎖、端點或持久化；需求版本不變。

採購發票引用同公司 Approved 採購單與 Posted 收貨行。發票可先存 Draft；MATCH 對訂單、實收淨量及發票逐行比對，解決差異後才經 FI 公開契約產生應付。此設計對應 BR-PUR-036～043、PUR-MATCH-001～028；008d 沒有表、端點、畫面或產品測試。

WU-PUR-008a2 的離線整單餘量決策實現 REQ_F4114.8.2～3 的「同單重複來源合量、任一行不足整單不匹配」驗收條件；來源權威、SQL 鎖、HTTP 與保存狀態仍由 008a 驗收。此切片不更改需求版本或介面／資料結構。

WU-PUR-008a3 的離線來源身份預檢補足 REQ_F4114.8.2～3：已保存發票須對應同公司、供應商、採購單和原幣的 Approved 訂單；每個已請求收貨行須是同訂單的 Posted、未刪來源，物料與所屬訂單行一致。交易服務仍須在 SQL 鎖內從權威資料組裝身份，並與餘量預檢一起執行；本切片不更改需求版本或介面／資料結構。

WU-PUR-008a4 的離線價稅決策補足 REQ_F4114.8.3：以已保存訂單單價與稅率按發票數量形成四位原幣逐行基準，逐項記錄單價、稅率、未稅、稅額及含稅差額，任一差異整單不可直接匹配；不重估當日 MD 匯率。SQL 鎖內權威讀取與保存差異仍由 008a 驗收；需求版本及介面／資料結構不變。

WU-PUR-008a5 補足 REQ_F4114.8.4 的離線驗收：只有 Disputed 的已保存價稅差異可逐行決定；每個有差異的行須恰有一筆接受或拒絕與 1～500 字原因。任一拒絕使整單返回 Draft，全部接受才可轉 Matched；無差異行不得捏造決定。SQL 鎖內權威、權限、版本、決定人／時間、預留釋回與持久化仍由 008a 驗收；需求版本不變。

WU-PUR-008a6 補足 REQ_F4114.8.3 的離線整單驗收：Draft／Rejected 發票的保存行先驗請求形狀，再驗訂單／收貨身份、聚合可開票量，最後驗逐行價稅；任一來源不足只回按來源排序的完整不足清單，不回部分價稅結果；無差異為 Matched，有差異為 Disputed。交易服務須在同一鎖內提供全部權威快照並保存結果，仍待 008a。需求版本、資料結構與介面不變。

| Sub-function No. | Sub-function |
|---|---|
| 4.8 | Purchase Invoice and Three-Way Match |

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F4114.8.1 | 1.0 | System should enable user to view purchase invoices and matching results. | Developer | VIEW 只列目前公司及 SYS 資料範圍內的發票；詳情顯示保存行、訂單／收貨／退貨來源、已匹配與可開票量、價稅差異、解決理由及 FI 應付來源狀態；跨公司或範圍外 404。 |
| REQ_F4114.8.2 | 1.0 | System should enable user to create and edit purchase invoices. | Developer | CREATE／EDIT 只保存同公司同一供應商、單一已核准採購單的 Draft，1～1000 行；供應商發票號在公司與供應商內永久唯一。每行選 Posted 收貨行及正數六位基本單位數量，不超該行扣已 Posted 退貨、待審退貨預留與其他已匹配發票量。原幣與稅率來源按訂單快照；客戶端不得修改來源成本、公司或 FI 狀態。舊版本 409；已 Posted 不可編輯。 |
| REQ_F4114.8.3 | 1.0 | System should enable user to match purchase invoices against orders and receipts. | Developer | MATCH 按固定順序鎖來源行，整單比較供應商、物料、幣別、數量、訂單單價、稅率與原幣價稅額；逐行回傳差異類型、金額及來源。數量超實收淨量不得覆核；價格／稅額超允差先成 Disputed，不建立 FI 應付。預設允差為零；只在經版本化設定及授權時接受非零允差。 |
| REQ_F4114.8.4 | 1.0 | System should enable user to resolve purchase invoice differences. | Developer | 有 RESOLVE 權限者可帶原因及目前版本接受或拒絕可覆核的價格／稅額差異；拒絕回 Draft 供修訂，接受保留原輸入、基準、差額、決定人／時間，不更改採購單或收貨。數量、公司、來源或供應商不符不可接受。沒有任何差異時 MATCH 可直接 Posted；有差異須逐行解決後另以 POST 動作最終過帳。 |
| REQ_F4114.8.5 | 1.0 | System should enable user to post matched purchase invoices to accounts payable. | Developer | POST 重新鎖並重驗淨實收量、其他已開票量、版本與差異決定；在同一 SQL 交易中以 FI 公開契約按保存發票來源建立應付、增加 PUR 已開票量及狀態 Posted、寫審計。FI 不可用或任一行失敗整單回滾；相同來源／快照重試不重複應付，不同快照 409。未匹配、Disputed 或 Rejected 不可過帳。 |
| REQ_F4114.8.6 | 1.0 | System should enable user to void draft purchase invoices. | Developer | DELETE 只可按版本軟刪 Draft／Rejected，保留供應商發票號及稽核；Matched／Disputed／Posted 不可刪。Posted 應付調整走後續 FI 信用票據，不改原發票或 FI 分錄。 |

變更記錄：2026-10-24T12:20:56+08:00 WU-PUR-008d 新增 4.8.1～6，位於已分配的 4.7 之後；REQ_DS43 待 008a migration 真實建表時分配，REQ_IO_A 待 008c 實際畫面時分配。

## WU-PUR-007d 收貨、採購入庫與退貨設計基線（尚未實作）

WU-PUR-007a2 補足 REQ_F4114.2.2／2.4 的來源與可收量離線驗收：從已驗證草稿的同來源多行聚合量，要求同公司、同採購單、未刪且 Approved 的訂單及一對一訂單行快照；每行核准、已過帳及待審量必須有效，`本單聚合量 <= 核准量 - 已過帳量 - 待審量`。缺漏、重複、額外、失效或跨公司來源及超量整單拒絕。此切片只處理純規則，007a 仍須於 SQL 鎖內重讀權威資料、處理版本／權限與保存；需求版本不變。

沿用 BlueWhale 4.2 Purchase Inbound 六項 ID；退貨是 MyERP 新增的 4.7。收貨以已核准採購單的逐行未收量為上限，只有入庫單的 SYS 最終核准才經 INV 公開契約增加庫存。退貨只引用已過帳入庫行，須有新的 INV 專用退貨出庫契約；現有 `PostSalesOutboundAsync` 不適用。本設計對應 BR-PUR-028～035、PUR-RECV-001～028。007d 沒有建表、端點、畫面或產品測試。

| Sub-function No. | Sub-function |
|---|---|
| 4.2 | Purchase Inbound |
| 4.7 | Purchase Return |

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F4114.2.1 | 1.0 | System should enable user to view purchase inbound documents. | Developer | VIEW 只列目前公司及 SYS 資料範圍內的單據，依單號、日期、採購單與狀態穩定分頁；詳情顯示已保存行、批序號、審批歷史、已收／未收量及 INV 過帳結果，跨公司或範圍外回 404。列印重新讀保存資料；CSV 最多 10000 行、無部分檔。 |
| REQ_F4114.2.2 | 1.0 | System should enable user to create purchase inbound documents. | Developer | CREATE 只從同公司已核准採購單建立 1～1000 行草稿，按訂單行選擇倉庫／儲位及批序號；基本單位正數六位量不得超過逐行可收量，成本使用採購單已保存原幣價格、稅率與匯率換算後的本位幣未稅成本快照。草稿不寫 INV。 |
| REQ_F4114.2.3 | 1.0 | System should enable user to edit purchase inbound documents. | Developer | EDIT 帶版本整組修訂 Draft／Rejected 的數量、倉儲與追蹤；來源採購單、供應商、物料及成本來源不可改。待審／已過帳不得修改，舊版 409；修改後重新驗可收量。 |
| REQ_F4114.2.4 | 1.0 | System should enable user to approve purchase inbound documents. | Developer | SUBMIT 原子鎖定採購單行並預留全部可收量、建立 SYS 審批；只有 SYS 最終核准同交易把入庫單標為 Posted 並以已保存明細呼叫 INV `PostPurchaseInboundAsync`。零步驟流程也遵守同一交易。失敗全部回滾；重放相同快照不得重複入庫。 |
| REQ_F4114.2.5 | 1.0 | System should enable user to reject purchase inbound documents. | Developer | SYS 核准人可駁回 PendingApproval 入庫單；同交易釋回預留量，保留歷史與意見，不呼叫 INV。重複／過時決定不得二次釋回。 |
| REQ_F4114.2.6 | 1.0 | System should enable user to delete purchase inbound documents. | Developer | DELETE 僅可帶版本軟刪 Draft；Rejected 須先修訂回 Draft。PendingApproval 與 Posted 不可刪，Posted 只能經退貨沖回且 INV 原異動不修改。 |
| REQ_F4114.7.1 | 1.0 | System should enable user to view purchase returns. | Developer | VIEW 可追溯原 Posted 入庫行、可退量、保存的退貨狀態、批序號、審批和出庫異動；只見目前公司與授權範圍。 |
| REQ_F4114.7.2 | 1.0 | System should enable user to create and edit purchase returns. | Developer | 從同公司 Posted 入庫行建立 Draft；1～1000 行、每行正數六位量，不超原入庫行扣除已預留／已過帳退貨量；保留原入庫成本與批序號來源。僅 Draft／Rejected 可帶版本修訂或草稿軟刪，無庫存副作用。 |
| REQ_F4114.7.3 | 1.0 | System should enable user to approve or reject purchase returns. | Developer | SUBMIT 原子預留可退量並送 SYS；最終核准同交易呼叫新的 INV 採購退貨出庫契約，以原入庫 movement 的本位幣單位成本沖回。庫存或追蹤不足整單失敗；駁回釋回預留，不寫 INV。不得借用銷售出庫來源或直接改 INV 表。 |

變更記錄：2026-10-24 WU-PUR-007d 首次固定 4.2.1～6 與新增 4.7.1～3；前者沿用 BlueWhale ID，後者追加且不重排既有子功能。REQ_DS43 於 migration 真實建表時分配，REQ_IO_A 於實際前端畫面時分配。

## WU-PUR-006d 採購單設計基線（尚未實作）

BlueWhale 的 4.1 採購單六項需求沿用原編號。人工建立與 ADR-070 的決標轉單草稿共用同一採購單生命週期；後者保留不可變決標來源與商業快照。提交才鎖定同公司已核准請購行並消耗剩餘量；詢價與轉單草稿都不預留。需求對應 BR-PUR-022～027、介面 4.1～4.3、PUR-PO-001～024。006d 只固定規格，沒有宣稱下表功能已完成。

| Sub-function No. | Sub-function |
|---|---|
| 4.1 | Purchase Order |

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F4114.1.1 | 1.0 | System should enable user to view purchase orders. | Developer | VIEW 只列目前公司及 SYS 動作資料範圍內的訂單；依單號、日期、供應商、狀態及請購來源穩定分頁。詳情顯示保存行、決標來源、請購原量／已轉量／餘量、版本與 SYS 審批歷史；他公司或範圍外 404。另持 EXPORT 權限才可匯出完整安全 CSV；列印重新讀取已保存詳情。 |
| REQ_F4114.1.2 | 1.0 | System should enable user to create purchase orders. | Developer | CREATE 可人工建立 1～1000 行草稿；CONVERT 建立的草稿沿用 REQ_F4114.6.3。SYS 同交易發永久唯一單號；供應商、物料、幣別、進項稅率與可選請購來源須在目前公司有效，正數六位基本單位數量、非負六位單價、逐行四位金額；來源請購必須已核准且物料相同。草稿不消耗請購量、不寫 INV。 |
| REQ_F4114.1.3 | 1.0 | System should enable user to edit purchase orders. | Developer | EDIT 只可帶目前版本修改草稿或駁回單的可編輯表頭與人工行；決標轉單行的來源、供應商、數量與價格快照不可改。駁回的純轉單訂單可只修訂備註後回草稿。修訂後重新驗人工行 MD 與請購引用並重算金額；待審、已核准、已取消不可修改；舊版本 409。 |
| REQ_F4114.1.4 | 1.0 | System should enable user to approve purchase orders. | Developer | SUBMIT 只接受草稿及目前版本，按請購行鎖定並檢查全部來源的累計已轉量不超核准量，在同一交易增加已轉量與建立 SYS 審批實例；零步驟立即核准。人工最終核准只經 SYS 審批 API，同交易更新訂單狀態；重複提交／決定不重複消耗。核准不寫 INV，採購入庫另行過帳。 |
| REQ_F4114.1.5 | 1.0 | System should enable user to reject purchase orders. | Developer | SYS 核准人可帶意見駁回待審訂單；審批決定、訂單狀態與全部請購預占量的釋回同交易完成。保留歷史，修訂後可重送；重複駁回或已核准不得再釋回。 |
| REQ_F4114.1.6 | 1.0 | System should enable user to delete purchase orders. | Developer | DELETE 只可按版本軟刪未提交草稿；已提交、已核准、已有入庫引用均不可刪。核准且未入庫訂單以獨立 CANCEL 動作取消並原子釋回請購量；已入庫不得取消，須走後續退貨流程。刪除或取消後永久保留單號與決標來源連結，不允許重轉同一決標行。 |

變更記錄：2026-10-24 WU-PUR-006d 首次固定 4.1.1～6 的驗收條件。取消作為 4.1.6 的明確受權延伸動作，不新增 BlueWhale 功能編號；正式操作須在後續子單元驗收。

WU-PUR-005f3e 將 REQ_F4114.5.2 由 1.0 升至 1.1：表單讀取改用 QUOTE 專屬最小保存上下文及目前公司／報價日主檔選項，消除對 RFQ.VIEW／MD.VIEW 的額外角色依賴；保存時仍重新驗引用。其餘需求 ID 不變。

WU-PUR-005f2b 依既有 REQ_F4114.4.4／5：4.11 詳情只對草稿顯示 SEND、已發送顯示 CLOSE，分別檢查動作權限；確認框說明發送不寄電郵、關閉後不收新報價。帶保存版本提交，確認取消不發請求，處理中不能重複提交；成功後狀態與版本採伺服器回應，409／404 清除過時詳情並保留錯誤原因，其他失敗不顯示成功。需求 ID 與版本不變。

## WU-PUR-005a 詢價、供應商報價與比價設計基線

本工作單元固定契約，實體、API 與畫面依 WU-PUR-005b～f 交付。詢價是向受邀供應商索取價格的商業文件，不會預留請購量或改動庫存。對應 ADR-070、BR-PUR-014～020、介面 4.11～4.15、PUR-RFQ-001～018。

WU-PUR-005f1 依 REQ_F4114.4.1 交付唯讀列表／保存詳情。進頁須有 `PUR.RFQ.VIEW`，篩選錯誤不發請求；篩選與頁碼可從 URL 還原／分享。查詢、換公司或列表失敗關閉舊詳情，詳情 404 重查列表並保留具體原因，5xx 顯示 traceId。原需求 ID／版本不變；發送、關閉與轉單不在此切片。

WU-PUR-005f2a2 依既有 REQ_F4114.4.2／3：建立與修訂使用 RFQ CREATE／EDIT 授權的 MD 選項；EDIT 詳情及已保存引用只按 EDIT 動作資料範圍讀，只有 EDIT 而沒有 VIEW 仍可修訂。已保存的停用／未核准引用顯示原代碼及不可用原因，提交時仍由後端拒絕。前端建立／修訂表單、三語亮暗及模擬 E2E 已交付；請購來源選擇器顯示同公司已核准且有餘量的行，保存實際行 ID，已保存的失效來源仍顯示原單號及原因。SQL 服務停止，真實 SQL／受權 HTTP 與後端全量未驗；本單元未結案，需求 ID 與版本不變。

WU-PUR-005f2a2 來源選擇器按目前公司、物料及單號分頁，變更物料清除舊來源，後端保存仍重驗狀態與餘量。此為既有 4.4.2 驗收條件的介面實作，需求 ID／版本不變；SQL 服務未恢復前不宣稱真實資料庫驗收通過。

| Sub-function No. | Sub-function |
|---|---|
| 4.4 | Request for Quotation |
| 4.5 | Supplier Quotation |
| 4.6 | Quotation Comparison |

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F4114.4.1 | 1.1 | System should enable user to view requests for quotation. | Developer | 只顯示目前公司及 SYS VIEW 資料範圍內未軟刪詢價；單號、日期、狀態、請購來源篩選後穩定分頁；詳情含保存行、受邀供應商、版本及發送快照。範圍外／他公司回 404。另有 EXPORT 權限者可按已套用篩選下載最多 10000 行已保存明細的 UTF-8 安全 CSV；0 行、資料變動、取消或任一請求失敗不下載部分檔。列印須重新讀取已保存詳情。 |
| REQ_F4114.4.2 | 1.0 | System should enable user to create requests for quotation. | Developer | CREATE 建立草稿，SYS 同交易發公司單號；1～1000 個 MD 有效物料行、1～50 個不同的有效 MD 供應商、正數六位數量、回覆期限不得早於單據日；可選同公司已核准請購行作來源，詢價數量不超過其未轉單量；整單失敗回滾。 |
| REQ_F4114.4.3 | 1.0 | System should enable user to edit requests for quotation. | Developer | EDIT 僅可按版本整組替換草稿，舊版本 409；發送或關閉後不得改動商業條件。 |
| REQ_F4114.4.4 | 1.0 | System should enable user to send requests for quotation. | Developer | SEND 重新驗 MD 物料及供應商與請購來源，保存發送時的行及受邀名單快照；只可發送一次，無外部 email 承諾。 |
| REQ_F4114.4.5 | 1.0 | System should enable user to close requests for quotation. | Developer | CLOSE 將已發送詢價關閉，阻止新的供應商報價；保留原詢價與報價歷史，不自動產生採購單。 |
| REQ_F4114.4.6 | 1.0 | System should enable user to delete requests for quotation. | Developer | DELETE 僅可按版本軟刪草稿；發送後或已有報價不得刪除；單號永久保留。 |
| REQ_F4114.5.1 | 1.1 | System should enable user to view supplier quotations. | Developer | VIEW 按詢價、供應商、有效期與狀態查詢目前公司報價；詳情列出逐行價格、幣別、稅別、版本與修訂歷史，按目前業務日回傳當前修訂是否過期；範圍外及他公司 404。 |
| REQ_F4114.5.2 | 1.1 | System should enable user to record supplier quotations. | Developer | QUOTE 僅為已發送、未關閉詢價的受邀供應商錄入報價；有 QUOTE 但沒有 RFQ.VIEW／MD.VIEW 的用戶仍可在其資料範圍取得最小已保存詢價行、受邀供應商及目前公司有效幣別與報價日進項稅率選項。每行報價的物料與基本單位數量取發送快照，單價非負且最多六位小數；有效期不得早於報價日。寫入時重驗 MD 引用，原子保存，不接受非受邀或重複供應商當前有效報價。 |
| REQ_F4114.5.3 | 1.0 | System should enable user to revise supplier quotations. | Developer | QUOTE 按版本修訂未決標、未過期報價；保留舊版不可覆寫，已決標或已轉單報價不可修訂；舊版本 409。 |
| REQ_F4114.5.4 | 1.1 | System should enable user to withdraw supplier quotations. | Developer | QUOTE 按目前版本撤回未決標、未過期報價；舊版本 409，重複撤回或已決標／過期 422。撤回後不得參與比價，歷史仍可查閱，不影響其他供應商報價。 |
| REQ_F4114.6.1 | 1.1 | System should enable user to compare supplier quotations. | Developer | COMPARE 對已發送或已關閉詢價逐行顯示所有目前報價，包含撤回、過期及未生效項；逐行展示原幣單價、進項稅率、有效匯率及來源期間、本位幣未稅／稅額／含稅成本；不可比項明示原因且不顯示比較金額，不把不同幣別原價直接排序。畫面只以已驗收 GET 取得即時資料，錯誤或換公司清除舊成本。WU-PUR-005f3c1 只交付唯讀畫面。 |
| REQ_F4114.6.2 | 1.0 | System should enable user to select supplier quotations. | Developer | AWARD 可逐行選擇一個合格供應商報價，保存選擇理由與價格／匯率／稅額快照；重複決標或詢價關閉回 422，版本衝突 409。 |
| REQ_F4114.6.3 | 1.1 | System should enable user to convert selected quotations into a purchase order. | Developer | CONVERT 只接受同一已發送詢價、同一供應商及同一原幣的 1～1000 個不同已決標未轉行；按目前詢價版本原子建立一張採購單草稿、永久單號及不可重用來源連結，保留決標商務快照。空集合／重複 ID 拒絕，任何行衝突整批回滾。請購已轉量由 WU-PUR-006 的訂單提交流程消耗；轉單不改庫存。成功提示顯示已保存單號及行數。 |

詢價三表已於 WU-PUR-005b 落地並分配 REQ_DS43.163～165；報價三表於 005d 分配 166～168，決標快照於 005e3 分配 169。採購單草稿的完整編輯、送審及取消在 WU-PUR-006；本需求群只規定最小可保存轉單草稿與來源唯一性。變更時遞增需求版本並記錄原因。

變更記錄：WU-PUR-005e4 將 REQ_F4114.6.3 從 1.0 更新至 1.1，固定明確行集合、單幣別、版本及來源永久唯一的驗收條件；資料表及端點待實作驗收。WU-PUR-005f2c 將 REQ_F4114.4.1 從 1.0 更新至 1.1，加入已套用篩選的安全 CSV 與保存詳情列印；對應 BR-PUR-021、REQ_IO_A4.11.4～5、PUR-RFQ-092～098。WU-PUR-005f3c1 將 REQ_F4114.6.1 從 1.0 更新至 1.1，對齊已驗收 GET 的歷史不可比項、稅率／匯率來源及唯讀畫面錯誤清除條件。

## WU-PUR-000 請購設計基線

WU-PUR-001 交付 REQ_F4114.3.1～6 共用前置條件：PUR 組件與獨立 Contracts 載入、請購 VIEW／CREATE／EDIT／DELETE／SUBMIT（及後續 CSV 的 EXPORT）權限宣告、無公司上下文拒絕、他公司單據隱藏為 404。WU-PUR-002a 已落地資料表與資料庫約束；002b1 完成 3.1 唯讀列表／詳情的保存資料部分。002b2a 加入分店／部門歸屬及 SYS 授權資料範圍，審批歷史待 003；建立、修改與刪除待 002b2 後續切片。

BlueWhale 已占用子功能 4.1 採購單、4.2 採購入庫；MyERP 請購追加 4.3，不更動既有編號。本切片固定契約，產品功能待 WU-PUR-001～004。

| Sub-function No. | Sub-function |
|---|---|
| 4.3 | Purchase Requisition |

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F4114.3.1 | 1.2 | System should enable user to view purchase requisitions. | Developer | `PUR.REQUISITION.VIEW` 僅列目前公司及 SYS 授權的本人／部門／分店／全公司範圍；依單號、業務日期、狀態、請購人穩定分頁；詳情呈現保存行、版本、SYS 審批歷史與可轉採購餘量；範圍外及他公司 ID 回 404。另持 EXPORT 權限可按目前篩選匯出所有保存明細，UTF-8 CSV 公式安全，最多 10000 行；取消、資料變動或任一請求失敗不得下載部分檔。 |
| REQ_F4114.3.2 | 1.1 | System should enable user to create purchase requisitions. | Developer | `PUR.REQUISITION.CREATE` 建立草稿並取 SYS 公司單號；1–1000 行、物料與基本單位正數六位數量、需求日期不得早於單據日；只驗本公司有效 MD 引用，不寫 INV 異動或預留庫存。請購人、分店、部門從 SYS 目前授權取得。 |
| REQ_F4114.3.3 | 1.0 | System should enable user to edit purchase requisitions. | Developer | `PUR.REQUISITION.EDIT` 以版本整組替換草稿或已駁回單；駁回修訂回草稿並保留歷史；過期版本 409，待審、已核准、已關閉不可修改。 |
| REQ_F4114.3.4 | 1.0 | System should enable user to approve purchase requisitions. | Developer | `PUR.REQUISITION.SUBMIT` 送 SYS；核准人僅經 SYS 審批 API 決定；最終核准與請購狀態同交易完成，核准量作為後續採購單可引用上限；重複決定不重複釋放數量。 |
| REQ_F4114.3.5 | 1.0 | System should enable user to reject purchase requisitions. | Developer | SYS 核准人可帶意見駁回待審單，保存歷史，不產生採購單或庫存異動；修訂後可重送。 |
| REQ_F4114.3.6 | 1.0 | System should enable user to delete purchase requisitions. | Developer | `PUR.REQUISITION.DELETE` 僅按版本軟刪草稿；已駁回未修訂、待審、已核准及已被引用單據不可刪；保留單號、稽核與歷史。 |

變更記錄：WU-PUR-002b2a 將 REQ_F4114.3.1 由 1.0 提至 1.1，明定 SYS 授權資料範圍及範圍外 404；資料表新增歸屬欄位供查詢，不改需求 ID。WU-PUR-002b2b 將 3.2 提至 1.1，明定建立歸屬來自 SYS 授權。WU-PUR-005d4 將 5.1、5.4 提至 1.1，明定即時計算過期旗標與撤回版本／狀態守衛；過期不寫回歷史狀態。

WU-PUR-004d 將 3.1 由 1.1 提至 1.2，加入完整安全 CSV、10000 明細行上限、取消及整檔失敗契約；對應 BR-PUR-013、介面 REQ_IO_A4.8.5 與 PUR-CSV-001～008。

WU-PUR-002b2c 依現有 3.3／3.6 驗收條件交付後端 PUT／DELETE；沒有修改需求語義或版本。追溯補充：BR-PUR-010、PUR-MUT-001～005，審批與前端交互仍由後續單元實作。

WU-PUR-003a 交付 3.4 的送審部分：僅草稿可帶目前版本送審；送審前重新驗證全部 MD 物料；SYS 審批實例與請購狀態在同一交易保存。無適用審批步驟時立即成為已核准；失敗時請購與審批實例均不得留下變更。最終人工決定、歷史及稽核由 003b 接續。對應 BR-PUR-011、PUR-SUB-001～008。

WU-PUR-003b 交付 3.1、3.4、3.5 的人工決定與歷史部分：SYS 最終核准／駁回在同一交易更新 PUR 請購；GET 詳情先經 VIEW 授權及資料範圍檢查，再讀取同公司所有輪次與逐步意見。請購不在待審狀態時決定回 409 並回滾 SYS 結果；駁回後修訂重送保留前次意見。對應 BR-PUR-012、PUR-APR-001～008；MD 邊界與資料範圍／併發補強留 003c。

WU-PUR-003c 完成 3.4 的邊界驗收：零／負／舊版本、非草稿及軟刪單不建立審批；送審以真實 MD 物料重新檢查停用與軟刪狀態，失敗後原版本可重試；本人／分店／部門／公司範圍及無權路徑保護；雙連線送審與最終決定最多一次成功，稽核、PUR／SYS 狀態及 INV 零異動一致。對應 PUR-APR-009～014，沒有變更既有需求語義與版本。前端提示仍由 004 驗收。

WU-PUR-004a 對 3.1～3.4、3.6 建立前端 API 契約：列表篩選和分頁由伺服器處理；建立／修改只送業務日、備註及重新編號的行，不送可偽造的請購人或組織歸屬；修改／刪除以查詢版本、送審以 JSON 版本呼叫；數量保留六位十進位文字，經真實 HTTP 驗證可保存。前端輸入先驗日期、1～1000 行、正數且小於 10^13 的六位數量及 500 字備註；後端仍為最終驗證者。004a 沒有可見畫面，介面 4.8～4.10 由後續切片交付。

WU-PUR-004b 交付 3.1、3.4～3.6 的介面 4.8 列表／已保存詳情：依 VIEW 權限進頁，伺服器分頁與篩選，顯示所有 SYS 審批輪次；只在草稿顯示送審／刪除，刪除需確認，列印前重取保存詳情。載入新列表時關閉舊詳情，避免空結果仍顯示前次單據；MD 停用／刪除回應顯示具體翻譯原因。建立與修訂畫面仍待 004c，安全 CSV 待 004d，沒有變更需求 ID 或版本。

WU-PUR-004c 交付 3.2～3.3 的介面 4.9／4.10：CREATE／EDIT 路由守衛、公司啟用 MD 物料分頁選擇、已保存物料名稱與停用標記、業務日／需求日／六位數量／備註逐欄提示及 1～1000 行整單保存。EDIT 專用詳情依 EDIT 資料範圍，沒有 VIEW 權限亦可載入可修訂單據。已駁回與草稿可修訂，待審／核准／關閉顯示不可修訂原因；舊版本與 API 拒絕不宣稱成功、原輸入保留；未存離開要確認。後端選項端點走 MD 公開契約，不直接讀 MD 表。沒有變更需求 ID 或版本；安全 CSV 仍待 004d。

追溯：`business-rules.md` BR-PUR-001～009、`data-structure.md` REQ_DS43.161～162、`api.md`、`ui-spec.md` 介面 4.8～4.10，以及 `test-plan.md` PUR-CREATE-001～006、PUR-SCHEMA-001～003、PUR-SCOPE-001～003、PUR-REQ-001～018。建立草稿已落地；修改／刪除、審批與前端案例仍待測。
WU-PUR-008a15 補足 REQ_F4114.8.2 的 Draft 保存輸入邊界：供應商／採購單 ID 必須為正，業務日不可為空值，原幣代碼須為三個 ASCII 字母並轉大寫；同一次驗證連同供應商發票號正規化及整張行清單形狀，不通過即不產生待保存輸入。此為離線純規則；跨公司、權威來源、版本、資料庫唯一性及 HTTP 保存仍屬 008a，需求版本不變。

WU-PUR-008a20 補足 REQ_F4114.8.2 的離線組裝驗收：組裝者必須重新驗請求表頭、號碼、行價稅與總額，並對照呼叫端取得的同公司 Approved 採購單及逐來源 Posted 未刪收貨身份；來源缺漏、重複或不一致時整單拒絕，成功只輸出已驗行與總額。此切片無 SQL 鎖內讀取或保存，需求版本不變。
WU-PUR-008a21 補足 REQ_F4114.8.2～3 的離線組裝驗收：每個權威收貨來源須恰有一份與同一訂單行及物料相符的保存訂單單價／稅率；按已驗 Draft 行產生逐行原幣基準及五種差異證據，同來源多行各自保留。缺漏、重複、錯置或非法權威價格整單拒絕；合法價稅差異仍保存為 Draft 證據，不在 Draft 靜默改價或提前 MATCH。SQL／HTTP 及鎖內讀取仍屬 008a，需求版本不變。
WU-PUR-008a22 補足 REQ_F4114.8.2～3 的保存前重驗驗收：用同一交易內重讀的訂單、收貨及訂單行價稅權威，從 Draft 快照重新組裝；身份、號碼、業務日、已驗行、總額或逐行價稅差異證據與快照不一致時整單拒絕，回 `PUR.INVOICE.DRAFT_SNAPSHOT_CHANGED`。合法差異可保留並回傳新的已驗快照；此離線規則須由 008a 的 SQL／HTTP 保存服務接入，需求版本不變。
WU-PUR-008a23 補足 REQ_F4114.8.2～3 的離線保存意圖驗收：先按當前權威來源重驗 Draft 快照，再以重驗後同一公司、供應商及正規化號碼預檢永久佔用；已軟刪的其他發票仍佔號，編輯本張可保留原號。只有全數通過才輸出同一新驗快照的身份、號碼、業務日、待寫入行、總額與差異證據；任一步失敗不回部分意圖。SQL／HTTP、併發唯一索引、版本、權限、交易仍屬 008a，需求版本不變。
WU-PUR-008a24 補足 REQ_F4114.8.3 的修訂保存離線預檢：修訂須提供鎖內讀取的已保存發票表頭與正數期望版本；表頭 ID、公司、供應商、採購單及原幣須與待保存 Draft 一致，且未刪、狀態仍為 Draft、版本一致。只有通過後才以已保存發票 ID 取得本張號碼豁免，輸出相同 ID 與版本的待保存意圖；他張包括軟刪持號仍拒絕。狀態錯誤回 422、版本衝突回 409。此為離線純規則，SQL 鎖內讀取、權限、唯一索引與交易仍屬 008a，需求版本不變。

WU-PUR-008a25 補足 REQ_F4114.8.2～3 的修訂號碼驗收：已保存 Draft 表頭的正規化號碼鍵須有同公司、同供應商、同發票 ID 且未刪除的持有者紀錄；無持有者、鍵不一致、非正規鍵或範圍不符均不得形成保存意圖。保留原號與改號均先核對此證據，改號另驗新鍵無其他持有者，其他軟刪持有者仍佔號。真實 SQL 鎖內完整持有者讀取、唯一索引與交易仍由 008a 驗收；需求版本不變。

WU-PUR-008a26 補足 REQ_F4114.8.3 的修訂保存意圖驗收：同一個已驗 Draft 修訂意圖須明確輸出號碼為保留或改號，並攜帶已保存舊正規鍵與待寫入新正規鍵；只改顯示大小寫而鍵相同屬保留。新建意圖不攜帶修訂資訊。此為離線決策資料，SQL 唯一索引、鎖與持久化仍由 008a 驗收；需求版本不變。

WU-PUR-008a27 補足 REQ_F4114.8.2～3：Draft 新建與原號修訂僅佔用當前正規鍵；改號修訂除佔用新正規鍵外，須把舊正規鍵永久保留，後續新建、修訂或軟刪均不得重用。保存意圖輸出這兩個鍵供 008a 在同一交易落庫；需求版本不變。

WU-PUR-008a28 補足 REQ_F4114.8.2～3：同一發票的持有者快照可同時包含活動鍵與不同的歷史保留鍵；本張修訂可繼續使用自己的鍵，他張對任一鍵的重用仍拒絕。相同發票與鍵重複、同一發票跨公司或供應商、非正規鍵均視為失效快照。此為離線規則；SQL 交易與唯一索引仍屬 008a，需求版本不變。

WU-PUR-008a30 補足 REQ_F4114.8.2～3、8.6：Draft 改號時，已保存的活動鍵先轉歷史，再建立新活動鍵；若新鍵屬本張歷史鍵則重新啟用該列，所有其他歷史列永久保留。原號修訂不改持有表；同範圍他張佔鍵回 409。驗收以離線步驟順序、完整保留鍵及唯一索引錯誤辨識測試，SQL 同交易寫入仍屬 008a；需求版本不變。

WU-PUR-008a32 補足 REQ_F4114.8.2～3 的資料承載驗收：Draft 表頭保存公司、供應商、採購單、原幣、業務日、號碼、狀態與四位總額；逐行保存同公司發票參照、收貨來源、六位量／價及四位價稅；行號在發票內唯一，同收貨來源允許多行。可逆 migration 的離線模型測試通過；真實 SQL、權威來源、版本、授權與 HTTP 保存仍待 008a。需求版本不變。
WU-PUR-008a36 補足 REQ_F4114.8.2～3 的離線保存驗收：同一已重驗 Draft 意圖及同一指派發票 ID 須一併產生 EF 發票實體圖與號碼持有列變更，兩者的公司、供應商、發票 ID 與正規鍵一致；修訂須保留已保存版本，錯誤身份或失效／重複持有列快照整單拒絕。此切片沒有 SQL／HTTP 寫入，需求版本不變。
WU-PUR-008a40 補足 REQ_F4114.8.2～3 的離線 EF 寫入準備驗收：共同 Draft 計畫在 DbContext 中以相同實體參照追蹤全部既有號碼持有列，套用停用／回啟後標示 Modified，待新增活動鍵標示 Added；原號修訂的完整歷史列保持 Unchanged。不同參照的同 ID 已被追蹤或追蹤列有待處理變更時，須在套用前拒絕。需求版本不變；真實 SQL 同交易及 HTTP 驗收留待 008a。
WU-PUR-008a41 補足 REQ_F4114.8.2～3 的離線 EF 寫入準備驗收：新建 Draft 表頭與行均標 Added；修訂只接受同一 DbContext 中未變的既有表頭、期望版本及完整既有行參照，保留表頭追蹤身份，把舊行標 Deleted、新行標 Added。失效身份、版本、缺行或待處理變更須在號碼旗標變更前拒絕；真實 SQL 交易、identity 指派與 HTTP 仍由 008a 驗收，需求版本不變。
