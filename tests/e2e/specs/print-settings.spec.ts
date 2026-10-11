import { Page } from '@playwright/test';
import { chooseLanguage, expect, openHome, test } from '../support/fixtures';

interface Param { key: string; module: string; group: string; type: string; value: string; defaultValue: string; isDefault: boolean;
  minimum: number | null; maximum: number | null; maxLength: number; options: string[]; version: number; }
const text = (name: string, maxLength: number): Param => ({ key: `SYS.PRINT_${name}`, module: 'SYS', group: 'print', type: 'Text',
  value: '', defaultValue: '', isDefault: true, minimum: null, maximum: null, maxLength, options: [], version: 0 });
const flag = (name: string): Param => ({ key: `SYS.PRINT_${name}`, module: 'SYS', group: 'print', type: 'Boolean',
  value: 'false', defaultValue: 'false', isDefault: true, minimum: null, maximum: null, maxLength: 5, options: [], version: 0 });
const initial = (): Param[] => [
  { key: 'SYS.QUANTITY_DECIMALS', module: 'SYS', group: 'precision', type: 'WholeNumber', value: '2', defaultValue: '2',
    isDefault: true, minimum: 0, maximum: 4, maxLength: 11, options: [], version: 0 },
  text('ADDRESS', 200), text('COMPANY_NAME', 100), text('FAX', 30), text('PHONE', 30), text('POSTAL_CODE', 10),
  text('PURCHASE_ORDER_FOOTER', 500), text('SALES_DELIVERY_FOOTER', 500), flag('SHOW_LOGO'), flag('SHOW_STAMP'),
];

/** 模擬參數 API（列印設定沿用同一 API，ADR-027）；回傳每次 PUT 的 body。 */
async function mockApi(page: Page, rows: Param[]) {
  const puts: { items: { key: string; value: string; version: number }[] }[] = [];
  await page.route('**/api/v1/sys/parameters**', async route => {
    const req = route.request();
    if (req.method() === 'GET') {
      await route.fulfill({ json: rows });
      return;
    }
    const body = req.postDataJSON();
    puts.push(body);
    for (const item of body.items) {
      const row = rows.find(r => r.key === item.key)!;
      Object.assign(row, { value: item.value, isDefault: item.value === row.defaultValue, version: row.version + 1 });
    }
    await route.fulfill({ json: rows });
  });
  return { puts };
}

test('SYS-PRINT-004 列印設定：抬頭預覽、頁尾多行備註、只保存 print 分組的修改、三語', async ({ page }) => {
  const api = await mockApi(page, initial());
  await openHome(page);
  await page.getByTestId('layout-menu-sys').click();
  await page.getByTestId('layout-menu-sys-print-settings').click();
  await expect(page.getByRole('heading', { name: '列印設定' })).toBeVisible();
  await expect(page.getByTestId('param-row-SYS.PRINT_COMPANY_NAME')).toContainText('列印公司名稱');
  await expect(page.getByTestId('param-row-SYS.QUANTITY_DECIMALS')).toHaveCount(0);
  // 公司名稱空白時預覽目前公司名稱
  await expect(page.getByTestId('print-preview-name')).toHaveText('Taipei');

  await page.getByTestId('param-input-SYS.PRINT_COMPANY_NAME').fill('Example Trading Co.');
  await page.getByTestId('param-input-SYS.PRINT_PHONE').fill('02-1234-5678');
  const footer = page.getByTestId('param-input-SYS.PRINT_SALES_DELIVERY_FOOTER');
  await expect(footer).toHaveJSProperty('tagName', 'TEXTAREA');
  await footer.fill('貨品請當面點清。\n謝謝惠顧。');
  await page.getByTestId('param-input-SYS.PRINT_SHOW_STAMP').click();
  await expect(page.getByTestId('print-preview-name')).toHaveText('Example Trading Co.');
  await expect(page.getByTestId('print-preview-phone')).toHaveText('電話 02-1234-5678');
  await expect(page.getByTestId('print-preview-stamp')).toBeVisible();

  await page.getByTestId('param-save').click();
  await expect(page.getByTestId('param-notice')).toBeVisible();
  expect(api.puts).toEqual([{ items: [
    { key: 'SYS.PRINT_COMPANY_NAME', value: 'Example Trading Co.', version: 0 },
    { key: 'SYS.PRINT_PHONE', value: '02-1234-5678', version: 0 },
    { key: 'SYS.PRINT_SALES_DELIVERY_FOOTER', value: '貨品請當面點清。\n謝謝惠顧。', version: 0 },
    { key: 'SYS.PRINT_SHOW_STAMP', value: 'true', version: 0 },
  ] }]);

  await chooseLanguage(page, 'en');
  await expect(page.getByRole('heading', { name: 'Print settings' })).toBeVisible();
  await expect(page.getByTestId('param-row-SYS.PRINT_SALES_DELIVERY_FOOTER')).toContainText('Sales delivery note footer');
  await chooseLanguage(page, 'zh-Hans');
  await expect(page.getByTestId('param-row-SYS.PRINT_POSTAL_CODE')).toContainText('邮政编码');

  // 系統參數頁不重複顯示列印分組
  await page.getByTestId('layout-menu-sys-parameters').click();
  await expect(page.getByTestId('param-row-SYS.QUANTITY_DECIMALS')).toBeVisible();
  await expect(page.getByTestId('param-row-SYS.PRINT_COMPANY_NAME')).toHaveCount(0);
});
