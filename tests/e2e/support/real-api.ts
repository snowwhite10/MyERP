import { expect, request } from '@playwright/test';

/** Tokens returned by POST /api/v1/sys/auth/login (the shape AuthService stores). */
export type RealApiTokens = { accessToken: string; accessTokenExpiresAt: string; refreshToken: string;
  refreshTokenExpiresAt: string; tokenType: string };

export const MASTER_CODE = '123456';

/**
 * Logs in against the real API with the Development master captcha. The login endpoint allows 5 requests per IP per
 * minute (MASTER_PROMPT section 7); when the real-API specs together exceed it, this waits for the Retry-After the
 * server sends and tries again, so the limit stays in force instead of being configured away.
 */
export async function realApiLogin(apiUrl: string, username: string, password: string): Promise<RealApiTokens> {
  const anonymous = await request.newContext({ baseURL: apiUrl });
  try {
    for (let attempt = 1; ; attempt++) {
      const captcha = await anonymous.get('/api/v1/sys/auth/captcha');
      if (captcha.status() === 200) {
        const { captchaId } = await captcha.json();
        const login = await anonymous.post('/api/v1/sys/auth/login', {
          data: { username, password, captchaId, captchaCode: MASTER_CODE },
        });
        if (login.status() !== 429 || attempt >= 4) {
          expect(login.status(), await login.text()).toBe(200);
          return await login.json() as RealApiTokens;
        }
        await waitRetryAfter(login.headers()['retry-after']);
      } else {
        expect(captcha.status(), await captcha.text()).toBe(429);
        expect(attempt).toBeLessThan(4);
        await waitRetryAfter(captcha.headers()['retry-after']);
      }
    }
  } finally {
    await anonymous.dispose();
  }
}

async function waitRetryAfter(header: string | undefined): Promise<void> {
  const seconds = Math.min(65, Math.max(1, Number(header) || 60));
  await new Promise(resolve => setTimeout(resolve, seconds * 1000 + 250));
}
