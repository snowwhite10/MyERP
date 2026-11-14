import { mkdir } from 'node:fs/promises';
import type { Locator } from '@playwright/test';
import { expect, test } from '../support/fixtures';

// WU-UI-ERRFOCUS：MD／SYS 表單送出驗證失敗時焦點移到第一個錯誤欄位，
// 錯誤訊息以 aria-describedby 關聯欄位（共用指令 erpFormErrors）。
const out = '../../docs/02-modules/SYS/screenshots';

/** 欄位標示無效，且 aria-describedby 指向顯示中的錯誤訊息。 */
async function expectDescribedError(field: Locator, message: string | RegExp): Promise<void> {
  await expect(field).toHaveAttribute('aria-invalid', 'true');
  const ids = ((await field.getAttribute('aria-describedby')) ?? '').split(/\s+/).filter(Boolean);
  expect(ids.length).toBeGreaterThan(0);
  const described = field.page().locator(ids.map((id) => `[id="${id}"]`).join(', '));
  await expect(described).toHaveAttribute('role', 'alert');
  await expect(described).toHaveText(message);
}

test('UI-ERRFOCUS-E01 客戶分類：確定鈕送出聚焦代碼，補代碼後再送出聚焦名稱，不寫入', async ({ page }) => {
  const writes: string[] = [];
  await page.route('**/api/v1/md/customer-categories**', (route) => {
    if (route.request().method() !== 'GET') writes.push(route.request().method());
    return route.fulfill({ json: { items: [], page: 1, pageSize: 20, totalCount: 0 } });
  });
  await page.goto('/md/customer-categories');
  await page.getByTestId('md-category-create').click();
  const code = page.getByTestId('md-category-code');
  const name = page.getByTestId('md-category-name');

  await page.locator('.ant-modal-footer .ant-btn-primary').click();
  await expect(code).toBeFocused();
  await expectDescribedError(code, /英數字/);
  await expectDescribedError(name, /名稱/);

  await code.fill('NEW');
  await expect(code).not.toHaveAttribute('aria-invalid', 'true');
  await expect(code).not.toHaveAttribute('aria-describedby', /erp-field-error/);
  await page.locator('.ant-modal-footer .ant-btn-primary').click();
  await expect(name).toBeFocused();
  await expectDescribedError(name, /名稱/);
  expect(writes).toEqual([]);
  await mkdir(out, { recursive: true });
  await page.locator('.ant-modal-content').screenshot({ path: `${out}/errfocus-md-category-name-focused-light-zh-Hant.png` });
});

test('UI-ERRFOCUS-E02 用戶：空白送出聚焦帳號；密碼不一致聚焦確認密碼並關聯訊息，不寫入', async ({ page }) => {
  const writes: string[] = [];
  await page.route('**/api/v1/sys/users**', (route) => {
    if (route.request().method() !== 'GET') writes.push(route.request().method());
    return route.fulfill({ json: { items: [], page: 1, pageSize: 20, totalCount: 0 } });
  });
  await page.goto('/sys/users');
  await page.getByTestId('sys-user-create').click();
  const ok = page.locator('.ant-modal-footer .ant-btn-primary');

  await ok.click();
  await expect(page.getByTestId('sys-user-username')).toBeFocused();
  await expect(page.getByTestId('sys-user-username')).toHaveAttribute('aria-invalid', 'true');

  await page.getByTestId('sys-user-username').fill('Kowloon');
  await page.getByTestId('sys-user-password').fill('Str0ng!Passw0rd');
  await page.getByTestId('sys-user-confirm').fill('other');
  await ok.click();
  const confirm = page.getByTestId('sys-user-confirm');
  await expect(confirm).toBeFocused();
  await expectDescribedError(confirm, '兩次輸入的密碼不一致');
  await expect(page.getByTestId('sys-user-password')).not.toHaveAttribute('aria-invalid', 'true');
  expect(writes).toEqual([]);
  await page.locator('.ant-modal-content').screenshot({ path: `${out}/errfocus-sys-user-mismatch-light-zh-Hant.png` });
});

test('UI-ERRFOCUS-E03 表單有效時照常保存，不殘留錯誤標示', async ({ page }) => {
  const bodies: unknown[] = [];
  await page.route('**/api/v1/md/customer-categories**', (route) => {
    if (route.request().method() === 'POST') {
      bodies.push(route.request().postDataJSON());
      return route.fulfill({ status: 201, json: { id: 1, code: 'NEW', name: '新分類', isActive: true, version: 1 } });
    }
    return route.fulfill({ json: { items: [], page: 1, pageSize: 20, totalCount: 0 } });
  });
  await page.goto('/md/customer-categories');
  await page.getByTestId('md-category-create').click();
  await page.locator('.ant-modal-footer .ant-btn-primary').click();
  await expect(page.getByTestId('md-category-code')).toBeFocused();
  await page.getByTestId('md-category-code').fill('NEW');
  await page.getByTestId('md-category-name').fill('新分類');
  await page.locator('.ant-modal-footer .ant-btn-primary').click();
  await expect(page.getByTestId('md-category-form')).toBeHidden();
  expect(bodies).toEqual([{ code: 'NEW', name: '新分類' }]);
});
