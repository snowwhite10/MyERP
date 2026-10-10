// 測試專用：登入 Session 範例資料。
import { AuthSession, AuthTokens, AUTH_SESSION_KEY } from '../app/core/auth/auth.service';
import { PREFERENCE_KEY_PREFIX } from '../app/core/storage/preference-storage';

/** 測試基準時間：Refresh Token 範例 7 天後到期。 */
export const TEST_NOW = Date.parse('2026-10-10T12:00:00+08:00');

export const STORED_SESSION_KEY = PREFERENCE_KEY_PREFIX + AUTH_SESSION_KEY;

export function testTokens(suffix = '1', refreshTokenExpiresAt = '2026-10-17T12:00:00+08:00'): AuthTokens {
  return {
    accessToken: `access-${suffix}`,
    accessTokenExpiresAt: '2026-10-10T12:15:00+08:00',
    refreshToken: `refresh-${suffix}`,
    refreshTokenExpiresAt,
    tokenType: 'Bearer',
  };
}

/**
 * 直接寫入 localStorage，模擬已登入（AuthService 建立時讀取）。
 * 預設 Refresh Token 遠期到期，讓使用真實時間的元件測試不會因日期過去而失效。
 */
export function seedSession(session: Partial<AuthSession> = {}): void {
  localStorage.setItem(
    STORED_SESSION_KEY,
    JSON.stringify({ ...testTokens('1', '2099-12-31T00:00:00+08:00'), username: 'admin', ...session }),
  );
}
