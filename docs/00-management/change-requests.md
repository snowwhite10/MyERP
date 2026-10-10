# 對 MASTER_PROMPT / Runner 的修改建議

> AI 不得修改 MASTER_PROMPT.md 與 Runner 核心檔。建議寫在這裡，由人手決定是否採納。

| CR | 日期 | 對象（章節 / 檔案） | 建議 | 理由 | 人手決定 |
|---|---|---|---|---|---|
| CR-000 | 2026-10-09 | MASTER_PROMPT 第 4 節 | 本機 Redis 為 Microsoft Open Tech Redis on Windows 3.2.100（2016），只可使用 Redis 3.2 支援的指令；所有 key 加前綴 myerp:；正式環境須用 Redis 6+ 或 Memurai | 本機無管理員權限，Memurai 安裝失敗 | 已採納 |
