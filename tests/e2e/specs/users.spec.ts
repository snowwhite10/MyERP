import { Page } from '@playwright/test';
import { chooseLanguage, E2E_AUTHORIZATION, expect, openHome, test } from '../support/fixtures';

// 介面 8.63（REQ_F4118.23.5–8）：用戶維護頁。API 以 page.route 模擬（ADR-013）。

interface User {
  id: number;
  username: string;
  isActive: boolean;
  isLocked: boolean;
  passwordChangedAt: string;
  isPasswordExpired: boolean;
  version: number;
}

interface Call {
  method: string;
  path: string;
  company: string;
  body: unknown;
}

const STRONG = 'Str0ng!Passw0rd';

const user = (id: number, username: string, overrides: Partial<User> = {}): User => ({
  id,
  username,
  isActive: true,
  isLocked: false,
  passwordChangedAt: '2026-10-01T09:15:00+08:00',
  isPasswordExpired: false,
  version: 1,
  ...overrides,
});

/** 模擬用戶 API；資料按 X-Company-Id 分開存放，模擬重複帳號、版本衝突、自我停用及密碼政策。 */
async function mockUserApi(page: Page, data: Record<string, User[]>): Promise<Call[]> {
  const calls: Call[] = [];
  await page.route('**/api/v1/sys/users**', async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    const company = request.headers()['x-company-id'] ?? '';
    const rows = (data[company] ??= []);
    const body = request.postDataJSON() as Record<string, unknown> | null;
    calls.push({ method: request.method(), path: url.pathname, company, body });
    const segments = url.pathname.split('/');
    const id = Number(segments[5]);
    const row = rows.find((r) => r.id === id);
    const weak = (password: unknown) => typeof password !== 'string' || password.length < 12;

    if (request.method() === 'GET') {
      const keyword = (url.searchParams.get('keyword') ?? '').toLowerCase();
      const items = rows
        .filter((r) => !keyword || r.username.toLowerCase().includes(keyword))
        .sort((a, b) => a.username.localeCompare(b.username) * (url.searchParams.get('sort') === '-username' ? -1 : 1));
      await route.fulfill({ json: { items, page: 1, pageSize: 20, totalCount: items.length } });
    } else if (url.pathname.endsWith('/api/v1/sys/users')) {
      const username = String(body?.['username']);
      if (Object.values(data).flat().some((r) => r.username.toLowerCase() === username.toLowerCase())) {
        await route.fulfill({ status: 409, json: { status: 409, errorCode: 'SYS.USER.DUPLICATE' } });
      } else if (weak(body?.['password'])) {
        await route.fulfill({ status: 400, json: { status: 400, errorCode: 'COMMON.REQUEST.VALIDATION_FAILED' } });
      } else {
        const created = user(100 + rows.length, username, { passwordChangedAt: '2026-10-11T00:30:00+08:00' });
        rows.push(created);
        await route.fulfill({ status: 201, json: created });
      }
    } else if (!row) {
      await route.fulfill({ status: 404, json: { status: 404, errorCode: 'SYS.USER.NOT_FOUND' } });
    } else if (segments[6] === 'reset-password') {
      if (weak(body?.['password'])) {
        await route.fulfill({ status: 400, json: { status: 400, errorCode: 'COMMON.REQUEST.VALIDATION_FAILED' } });
        return;
      }
      Object.assign(row, { isLocked: false, isPasswordExpired: false, passwordChangedAt: '2026-10-11T00:31:00+08:00' });
      await route.fulfill({ status: 204 });
    } else if (segments[6] === 'unlock') {
      row.isLocked = false;
      await route.fulfill({ status: 204 });
    } else {
      if (row.username === 'admin' && body?.['isActive'] === false) {
        await route.fulfill({ status: 422, json: { status: 422, errorCode: 'SYS.USER.SELF_DEACTIVATION' } });
        return;
      }
      if (body?.['version'] !== row.version) {
        await route.fulfill({ status: 409, json: { status: 409, errorCode: 'COMMON.CONCURRENCY.CONFLICT' } });
        return;
      }
      Object.assign(row, { isActive: body?.['isActive'], version: row.version + 1 });
      await route.fulfill({ json: row });
    }
  });
  return calls;
}

async function saveDialog(page: Page): Promise<void> {
  await page.locator('.ant-modal-footer .ant-btn-primary').click();
}

test.describe('用戶維護（全權限）', () => {
  test('由選單進入用戶頁，新增用戶：重複帳號、弱密碼與成功', async ({ page, pageErrors }) => {
    pageErrors.allow(/status of (409|400)/);
    const calls = await mockUserApi(page, { '1': [user(1, 'admin')], '2': [user(2, 'kowloon')] });
    await openHome(page);

    await page.getByTestId('layout-menu-sys').click();
    await page.getByTestId('layout-menu-sys-users').click();
    await expect(page).toHaveURL(/\/sys\/users$/);
    await expect(page.getByTestId('sys-user-row-admin')).toContainText('2026-10-01 09:15');
    expect(calls.find((c) => c.method === 'GET')?.company).toBe('1');

    await page.getByTestId('sys-user-create').click();
    // 確認密碼不一致：前端擋下，不送出
    await page.getByTestId('sys-user-username').fill('Kowloon');
    await page.getByTestId('sys-user-password').fill(STRONG);
    await page.getByTestId('sys-user-confirm').fill('other');
    await saveDialog(page);
    await expect(page.getByTestId('sys-user-form')).toContainText('兩次輸入的密碼不一致');
    expect(calls.some((c) => c.method === 'POST')).toBe(false);

    // 帳號在他公司已存在（不分大小寫）→ 409，密碼欄位清空
    await page.getByTestId('sys-user-confirm').fill(STRONG);
    await saveDialog(page);
    await expect(page.getByTestId('sys-user-save-error')).toContainText('此帳號已被使用');
    await expect(page.getByTestId('sys-user-password')).toHaveValue('');

    await page.getByTestId('sys-user-username').fill(' mary.chan ');
    await page.getByTestId('sys-user-password').fill('short');
    await page.getByTestId('sys-user-confirm').fill('short');
    await saveDialog(page);
    await expect(page.getByTestId('sys-user-save-error')).toContainText('密碼不符合密碼政策');

    await page.getByTestId('sys-user-password').fill(STRONG);
    await page.getByTestId('sys-user-confirm').fill(STRONG);
    await saveDialog(page);
    await expect(page.getByTestId('sys-user-row-mary.chan')).toBeVisible();
    await expect(page.getByTestId('sys-user-notice')).toContainText('已新增用戶 mary.chan');
    expect(calls.filter((c) => c.method === 'POST').at(-1)?.body).toEqual({ username: 'mary.chan', password: STRONG });
  });

  test('停用與重新啟用、不可停用自己、解鎖、重設密碼', async ({ page, pageErrors }) => {
    pageErrors.allow(/status of 422/);
    const calls = await mockUserApi(page, {
      '1': [
        user(1, 'admin'),
        user(2, 'bob', { version: 4 }),
        user(3, 'carol', { isLocked: true, isPasswordExpired: true }),
      ],
    });
    await page.goto('/sys/users');

    const status = page.getByTestId('sys-user-row-bob').locator('nz-tag').first();
    await expect(status).toHaveText('啟用');
    await page.getByTestId('sys-user-toggle-bob').click();
    await expect(status).toHaveText('停用');
    await expect(page.getByTestId('sys-user-toggle-bob')).toHaveText('啟用');
    await expect(page.getByTestId('sys-user-notice')).toContainText('已停用用戶 bob');
    expect(calls.find((c) => c.method === 'PUT')?.body).toEqual({ isActive: false, version: 4 });
    await page.getByTestId('sys-user-toggle-bob').click();
    await expect(status).toHaveText('啟用');
    await expect(page.getByTestId('sys-user-toggle-bob')).toHaveText('停用');
    expect(calls.filter((c) => c.method === 'PUT').at(-1)?.body).toEqual({ isActive: true, version: 5 });

    await page.getByTestId('sys-user-toggle-admin').click();
    await expect(page.getByTestId('sys-user-action-error')).toContainText('不能停用自己的帳號');

    await expect(page.getByTestId('sys-user-locked-carol')).toBeVisible();
    await expect(page.getByTestId('sys-user-expired-carol')).toBeVisible();
    await page.getByTestId('sys-user-unlock-carol').click();
    await expect(page.getByTestId('sys-user-locked-carol')).toBeHidden();
    await expect(page.getByTestId('sys-user-unlock-carol')).toHaveCount(0);
    await expect(page.getByTestId('sys-user-action-error')).toHaveCount(0);

    await page.getByTestId('sys-user-reset-carol').click();
    await expect(page.locator('.ant-modal-title')).toContainText('重設 carol 的密碼');
    await expect(page.getByTestId('sys-user-username')).toHaveCount(0);
    await page.getByTestId('sys-user-password').fill(STRONG);
    await page.getByTestId('sys-user-confirm').fill(STRONG);
    await page.getByTestId('sys-user-confirm').press('Enter');
    await expect(page.getByTestId('sys-user-notice')).toContainText('已重設 carol 的密碼');
    await expect(page.getByTestId('sys-user-expired-carol')).toBeHidden();
    expect(calls.find((c) => c.path.endsWith('/3/reset-password'))?.body).toEqual({ password: STRONG });
  });

  test('切換公司後只顯示新公司的用戶；英文介面', async ({ page }) => {
    const calls = await mockUserApi(page, { '1': [user(1, 'admin')], '2': [user(2, 'kowloon')] });
    await page.goto('/sys/users');
    await expect(page.getByTestId('sys-user-row-admin')).toBeVisible();

    await page.getByTestId('layout-company-select').click();
    await page.locator('nz-option-item', { hasText: 'HK01 Hong Kong' }).click();
    await expect(page.getByTestId('sys-user-row-kowloon')).toBeVisible();
    await expect(page.getByTestId('sys-user-row-admin')).toBeHidden();
    expect(calls.filter((c) => c.method === 'GET').at(-1)?.company).toBe('2');

    await chooseLanguage(page, 'en');
    await expect(page.getByTestId('sys-user-title')).toHaveText('Users');
    await expect(page.getByTestId('sys-user-reset-kowloon')).toHaveText(/Reset password/);
  });
});

test.describe('用戶維護（只可檢視與解鎖）', () => {
  test.use({
    authorization: {
      ...E2E_AUTHORIZATION,
      isFullAccess: false,
      permissions: [
        { code: 'SYS.USER.VIEW', dataScope: 'Company' },
        { code: 'SYS.USER.EDIT', dataScope: 'Company' },
      ],
    },
  });

  test('沒有新增與重設密碼按鈕；仍可解鎖', async ({ page }) => {
    await mockUserApi(page, { '1': [user(1, 'admin'), user(2, 'bob', { isLocked: true })] });
    await openHome(page);
    await page.getByTestId('layout-menu-sys').click();
    await expect(page.getByTestId('layout-menu-sys-companies')).toHaveCount(0);
    await page.getByTestId('layout-menu-sys-users').click();
    await expect(page.getByTestId('sys-user-row-bob')).toBeVisible();
    await expect(page.getByTestId('sys-user-create')).toHaveCount(0);
    await expect(page.getByTestId('sys-user-reset-bob')).toHaveCount(0);
    await page.getByTestId('sys-user-unlock-bob').click();
    await expect(page.getByTestId('sys-user-locked-bob')).toBeHidden();
  });
});

test.describe('用戶維護（無用戶權限）', () => {
  test.use({
    authorization: { ...E2E_AUTHORIZATION, isFullAccess: false, permissions: [{ code: 'SYS.COMPANY.VIEW', dataScope: 'Company' }] },
  });

  test('選單沒有用戶項目，直接進入顯示 403', async ({ page }) => {
    await openHome(page);
    await page.getByTestId('layout-menu-sys').click();
    await expect(page.getByTestId('layout-menu-sys-users')).toHaveCount(0);
    await page.goto('/sys/users');
    await expect(page.getByTestId('error-page-403')).toBeVisible();
  });
});
