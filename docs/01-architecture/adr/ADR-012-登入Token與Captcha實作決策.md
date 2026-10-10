# ADR-012：登入、Token 與 Captcha 實作決策

- 日期：2026-10-10
- 狀態：採用
- 工作單元：WU-SYS-003

## 背景

MASTER_PROMPT 第 7 節要求 JWT Access 15 分鐘、Refresh 7 天且每次使用輪替、存 Redis、登出即失效；Captcha 萬用碼只在 Development 生效。簽章金鑰不得提交到 Git。本機 `localhost:6379` 有 Redis 相容服務可用。

## 決定

1. **Token**：Access Token 用 HS256（`Microsoft.IdentityModel.JsonWebTokens`），`sub` = 用戶 Id，`MapInboundClaims = false`，ClockSkew 30 秒。時間取自 `IClock`。
2. **Refresh Token**：32 bytes 隨機值（Base64Url）；Redis 只存其 SHA-256 雜湊作鍵，值為用戶 Id。取用與條件刪除用 Lua 腳本（不依賴 Redis 6.2 的 `GETDEL`），保證並發重放只有一個成功。登出用「值相符才刪除」，防止撤銷他人 Token。
3. **登出即失效**：本輪撤銷 Refresh Token；已簽發的 Access Token 仍有效至最多 15 分鐘。是否加入 jti 黑名單（Redis 失敗時放行，符合 REQ_NF424.4）留待前端整合時評估。
4. **簽章金鑰**：`Security:Jwt:SigningKey` 由 user-secrets 或環境變數提供，至少 32 bytes；非 Development 缺少即啟動失敗。Development 未設定時於程序內產生臨時金鑰（重啟後需重新登入），記一筆 Information。
5. **Captcha**：數字答案存 Redis（預設 5 位、120 秒），驗證一次即作廢；圖片以 SVG data URI 回傳，不引入圖形套件。萬用碼由 `IValidateOptions` + `ValidateOnStart` 檢查：非 Development 有設定即寫 Error 並拒絕啟動。萬用碼稽核日誌待 WU-SYS-007 `sys_audit_log` 建立後補寫。
6. **401 錯誤**：共用核心新增 `UnauthenticatedException`（`ErpErrorKind.Unauthenticated` → 401）。

## 後果

後端可完整登入／刷新／登出；SVG Captcha 防 OCR 能力有限，配合帳號鎖定與 WU-SYS-005 的每 IP 限流。Redis 不可用時無法登入或刷新，但已簽發 Access Token 仍有效，`/health/ready` 回 503。
