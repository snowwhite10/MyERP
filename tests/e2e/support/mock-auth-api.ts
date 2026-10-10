import { Page, Route } from '@playwright/test';

/**
 * 以 page.route 模擬 `/api/v1/sys/auth/*`（ADR-013）：E2E 站台只有靜態前端。
 * 行為依 api.md「登入與 Token」：Captcha 一次即作廢；萬用碼 123456；帳號不分大小寫。
 * 後端真實行為由 MyERP.IntegrationTests 的 13 個登入整合測試覆蓋。
 */
export const MASTER_CODE = '123456';
export const VALID_USER = { username: 'admin', password: 'Passw0rd!2026' };

const SVG = Buffer.from(
  '<svg xmlns="http://www.w3.org/2000/svg" width="120" height="40"><rect width="120" height="40" fill="#f6f6f6"/>' +
    '<text x="20" y="28" font-size="22" fill="#333">4 7 1 9 3</text></svg>',
).toString('base64');

export interface MockAuthApi {
  captchaRequests: number;
  loginBodies: Record<string, string>[];
  logoutBodies: Record<string, string>[];
}

function problem(route: Route, status: number, errorCode: string): Promise<void> {
  return route.fulfill({
    status,
    contentType: 'application/problem+json',
    body: JSON.stringify({ type: 'about:blank', title: errorCode, status, errorCode }),
  });
}

function tokens(suffix: number): Record<string, string> {
  return {
    accessToken: `mock-access-${suffix}`,
    accessTokenExpiresAt: '2099-12-31T00:15:00+08:00',
    refreshToken: `mock-refresh-${suffix}`,
    refreshTokenExpiresAt: '2099-12-31T00:00:00+08:00',
    tokenType: 'Bearer',
  };
}

export async function mockAuthApi(page: Page): Promise<MockAuthApi> {
  const state: MockAuthApi = { captchaRequests: 0, loginBodies: [], logoutBodies: [] };
  const issued = new Set<string>();
  let counter = 0;

  await page.route('**/api/v1/sys/auth/captcha', (route) => {
    state.captchaRequests++;
    const captchaId = `captcha-${state.captchaRequests}`;
    issued.add(captchaId);
    return route.fulfill({
      status: 200,
      headers: { 'Cache-Control': 'no-store' },
      contentType: 'application/json',
      body: JSON.stringify({ captchaId, image: `data:image/svg+xml;base64,${SVG}`, length: 5, expiresInSeconds: 120 }),
    });
  });

  await page.route('**/api/v1/sys/auth/login', (route) => {
    const body = route.request().postDataJSON() as Record<string, string>;
    state.loginBodies.push(body);
    const captchaValid = issued.delete(body['captchaId']) && body['captchaCode'] === MASTER_CODE;
    if (!captchaValid) {
      return problem(route, 400, 'SYS.AUTH.CAPTCHA_INVALID');
    }
    if (body['username']?.toLowerCase() !== VALID_USER.username || body['password'] !== VALID_USER.password) {
      return problem(route, 401, 'SYS.AUTH.INVALID_CREDENTIALS');
    }
    return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(tokens(++counter)) });
  });

  await page.route('**/api/v1/sys/auth/logout', (route) => {
    state.logoutBodies.push(route.request().postDataJSON() as Record<string, string>);
    return route.fulfill({ status: 204 });
  });

  return state;
}
