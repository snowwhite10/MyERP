import { mkdir } from 'node:fs/promises';
import type { Locator, Page } from '@playwright/test';
import { expectErrorBorder, expectReadableText } from '../support/contrast';
import { chooseTheme, expect, test } from '../support/fixtures';
import { mockAuthApi } from '../support/mock-auth-api';

// WU-UI-ERRCOLOR2：MD／SYS／登入／驗證碼／PUR 欄位錯誤文字不得用狀態標籤底色 `--erp-status-error`，
// 亮暗模式都要達 AA 4.5:1；已觸碰且無效的輸入框標紅框。
const out = '../../docs/02-modules/SYS/screenshots';

async function capture(target: Page | Locator, name: string): Promise<void> {
  await mkdir(out, { recursive: true });
  await target.screenshot({ path: `${out}/${name}.png` });
}

test.describe('登入頁錯誤', () => {
  test.use({ signedIn: false });

  for (const theme of ['light', 'dark'] as const) {
    test(`UI-ERRCOLOR2-LOGIN 必填錯誤清楚可讀、欄位標紅（${theme}）`, async ({ page }) => {
      await page.emulateMedia({ colorScheme: theme });
      const api = await mockAuthApi(page);
      await page.goto('/login');
      await expect(page.getByTestId('captcha-image')).toBeVisible();
      await page.getByTestId('login-submit').click();
      await expect(page.locator('.erp-login__field-error')).toHaveCount(3);
      await expectReadableText(page.locator('.erp-login__field-error'));
      await expectErrorBorder(page.getByTestId('login-username'));
      await expectErrorBorder(page.getByTestId('login-password'));
      await expectErrorBorder(page.getByTestId('captcha-input'));
      expect(api.loginBodies).toEqual([]);
      await capture(page.getByTestId('login-page'), `errcolor2-login-validation-${theme}-zh-Hant`);
    });
  }

  for (const theme of ['light', 'dark'] as const) {
    test(`UI-ERRCOLOR2-CAPTCHA 驗證碼載入失敗文字清楚可讀（${theme}）`, async ({ page, pageErrors }) => {
      pageErrors.allow(/status of 500/);
      pageErrors.allowApi5xx(/\/api\/v1\/sys\/auth\/captcha$/, 500);
      await page.emulateMedia({ colorScheme: theme });
      await page.route('**/api/v1/sys/auth/captcha', (route) => route.fulfill({ status: 500, json: { traceId: 'trace-captcha' } }));
      await page.goto('/login');
      await expect(page.getByTestId('captcha-error')).toBeVisible();
      await expectReadableText(page.getByTestId('captcha-error'));
    });
  }
});

test('UI-ERRCOLOR2-MD 客戶分類表單深色模式錯誤可讀、欄位標紅且不寫入', async ({ page }) => {
  const writes: string[] = [];
  await page.route('**/api/v1/md/customer-categories**', (route) => {
    if (route.request().method() !== 'GET') writes.push(route.request().method());
    return route.fulfill({ json: { items: [], page: 1, pageSize: 20, totalCount: 0 } });
  });
  await page.goto('/md/customer-categories');
  await chooseTheme(page, 'dark');
  await page.getByTestId('md-category-create').click();
  await page.locator('.ant-modal-footer .ant-btn-primary').click();
  await expect(page.locator('.category-form [role="alert"]')).toHaveCount(2);
  await expectReadableText(page.locator('.category-form [role="alert"]'));
  await expectErrorBorder(page.getByTestId('md-category-code'));
  await expectErrorBorder(page.getByTestId('md-category-name'));
  await page.getByTestId('md-category-code').fill('NEW');
  await expect(page.locator('.category-form [role="alert"]')).toHaveCount(1);
  await expectErrorBorder(page.getByTestId('md-category-code'), false);
  expect(writes).toEqual([]);
  await capture(page.locator('.ant-modal-content'), 'errcolor2-md-category-validation-dark-zh-Hant');
});

test('UI-ERRCOLOR2-SYS 用戶表單亮色模式密碼不一致錯誤可讀且不寫入', async ({ page }) => {
  const writes: string[] = [];
  await page.route('**/api/v1/sys/users**', (route) => {
    if (route.request().method() !== 'GET') writes.push(route.request().method());
    return route.fulfill({ json: { items: [], page: 1, pageSize: 20, totalCount: 0 } });
  });
  await page.goto('/sys/users');
  await page.getByTestId('sys-user-create').click();
  await page.getByTestId('sys-user-username').fill('Kowloon');
  await page.getByTestId('sys-user-password').fill('Str0ng!Passw0rd');
  await page.getByTestId('sys-user-confirm').fill('other');
  await page.locator('.ant-modal-footer .ant-btn-primary').click();
  await expect(page.getByTestId('sys-user-form')).toContainText('兩次輸入的密碼不一致');
  await expectReadableText(page.locator('.erp-user-form p[role="alert"]'));
  expect(writes).toEqual([]);
});

for (const theme of ['light', 'dark'] as const) {
  test(`UI-ERRCOLOR2-PUR 請購單欄位錯誤可讀且欄位標紅（${theme}）`, async ({ page }) => {
    let writes = 0;
    await page.route('**/api/v1/pur/requisitions**', (route) => { writes++; return route.fulfill({ status: 500 }); });
    await page.goto('/pur/requisitions/new');
    if (theme === 'dark') await chooseTheme(page, 'dark');
    await page.getByTestId('pur-form-remark').fill('x'.repeat(501));
    await page.getByTestId('pur-form-save').click();
    const errors = page.locator('.requisition-form p.field-error[role="alert"]');
    await expect(errors.first()).toBeVisible();
    await expectReadableText(errors);
    await expectErrorBorder(page.getByTestId('pur-form-remark'));
    expect(writes).toBe(0);
  });
}
