import { Page } from '@playwright/test';
import { chooseLanguage, expect, openHome, test } from '../support/fixtures';

interface Param { key: string; module: string; group: string; type: string; value: string; defaultValue: string; isDefault: boolean;
  minimum: number | null; maximum: number | null; maxLength: number; options: string[]; version: number; }
const initial = (): Param[] => [
  { key: 'SYS.QUANTITY_DECIMALS', module: 'SYS', group: 'precision', type: 'WholeNumber', value: '2', defaultValue: '2',
    isDefault: true, minimum: 0, maximum: 4, maxLength: 11, options: [], version: 0 },
  { key: 'SYS.UNIT_PRICE_DECIMALS', module: 'SYS', group: 'precision', type: 'WholeNumber', value: '5', defaultValue: '4',
    isDefault: false, minimum: 0, maximum: 6, maxLength: 11, options: [], version: 3 },
];

/** 模擬參數 API；`conflict` 為 true 時 PUT 回 409。回傳每次 PUT 的 body 與 GET 的 X-Company-Id。 */
async function mockApi(page: Page, rows: Param[], options: { conflict?: boolean } = {}) {
  const puts: { items: { key: string; value: string; version: number }[] }[] = [];
  const gets: string[] = [];
  await page.route('**/api/v1/sys/parameters**', async route => {
    const req = route.request();
    if (req.method() === 'GET') {
      gets.push(req.headers()['x-company-id'] ?? '');
      await route.fulfill({ json: rows });
      return;
    }
    const body = req.postDataJSON();
    puts.push(body);
    if (options.conflict) {
      await route.fulfill({ status: 409, json: { errorCode: 'COMMON.CONCURRENCY.CONFLICT' } });
      return;
    }
    for (const item of body.items) {
      const row = rows.find(r => r.key === item.key)!;
      Object.assign(row, { value: item.value, isDefault: item.value === row.defaultValue, version: row.version + 1 });
    }
    await route.fulfill({ json: rows });
  });
  return { puts, gets };
}

async function openParameters(page: Page): Promise<void> {
  await openHome(page);
  await page.getByTestId('layout-menu-sys').click();
  await page.getByTestId('layout-menu-sys-parameters').click();
  await expect(page.getByTestId('param-row-SYS.QUANTITY_DECIMALS')).toBeVisible();
}

test('SYS-PARAM-005 系統參數：三語名稱、只保存修改項目、恢復預設', async ({ page }) => {
  const rows = initial(); const api = await mockApi(page, rows);
  await openParameters(page);
  await expect(page.getByTestId('param-row-SYS.QUANTITY_DECIMALS')).toContainText('數量小數位數');
  await expect(page.getByTestId('param-default-SYS.UNIT_PRICE_DECIMALS')).toContainText('預設值：4');
  await expect(page.getByTestId('param-save')).toBeDisabled();

  await page.getByTestId('param-input-SYS.QUANTITY_DECIMALS').fill('9');
  await expect(page.getByTestId('param-error-SYS.QUANTITY_DECIMALS')).toContainText('0 至 4');
  await expect(page.getByTestId('param-save')).toBeDisabled();
  await page.getByTestId('param-input-SYS.QUANTITY_DECIMALS').fill('3');
  await page.getByTestId('param-restore-SYS.UNIT_PRICE_DECIMALS').click();
  await expect(page.getByTestId('param-input-SYS.UNIT_PRICE_DECIMALS')).toHaveValue('4');
  await page.getByTestId('param-save').click();
  await expect(page.getByTestId('param-notice')).toBeVisible();
  expect(api.puts).toEqual([{ items: [
    { key: 'SYS.QUANTITY_DECIMALS', value: '3', version: 0 },
    { key: 'SYS.UNIT_PRICE_DECIMALS', value: '4', version: 3 },
  ] }]);
  await expect(page.getByTestId('param-restore-SYS.UNIT_PRICE_DECIMALS')).toHaveCount(0);

  await chooseLanguage(page, 'en');
  await expect(page.getByTestId('param-row-SYS.QUANTITY_DECIMALS')).toContainText('Quantity decimal places');
  await chooseLanguage(page, 'zh-Hans');
  await expect(page.getByTestId('param-row-SYS.UNIT_PRICE_DECIMALS')).toContainText('单价小数位数');
});

test('SYS-PARAM-006 系統參數：版本衝突重新載入、離開前確認、切換公司重新查詢', async ({ page, pageErrors }) => {
  pageErrors.allow(/status of 409/);
  const api = await mockApi(page, initial(), { conflict: true });
  await openParameters(page);
  await page.getByTestId('param-input-SYS.QUANTITY_DECIMALS').fill('1');
  await page.getByTestId('param-save').click();
  await expect(page.getByTestId('param-save-error')).toBeVisible();
  await expect(page.getByTestId('param-input-SYS.QUANTITY_DECIMALS')).toHaveValue('1');

  // 有未保存修改時離開要確認；取消則留在本頁
  await page.getByTestId('layout-menu-sys-ip-whitelist').click();
  await page.getByTestId('erp-message-cancel').click();
  await expect(page).toHaveURL(/\/sys\/parameters$/);

  await page.getByTestId('param-conflict-reload').click();
  await expect(page.getByTestId('param-input-SYS.QUANTITY_DECIMALS')).toHaveValue('2');
  await expect(page.getByTestId('param-save-error')).toHaveCount(0);
  expect(api.gets).toEqual(['1', '1']);

  await page.getByTestId('layout-company-select').click();
  await page.locator('nz-option-item', { hasText: 'HK01 Hong Kong' }).click();
  await expect.poll(() => api.gets.at(-1)).toBe('2');
});
