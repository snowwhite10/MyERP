import { E2E_AUTHORIZATION, expect, test } from '../support/fixtures';

test('幣別：本位幣標示、新增、更換本位幣被鎖定、刪除提示與公司切換', async ({ page, pageErrors }) => {
  pageErrors.allow(/status of 409/);
  const rows: Record<string, Record<string, unknown>[]> = { '1': [
    { id: 1, code: 'TWD', name: '新台幣', symbol: 'NT$', decimalPlaces: 0, isBase: true, isActive: true, version: 1 },
    { id: 2, code: 'USD', name: '美元', symbol: '$', decimalPlaces: 2, isBase: false, isActive: true, version: 1 }], '2': [] };
  const writes: { method: string; body: unknown; url: string }[] = [];
  await page.route('**/api/v1/md/currencies**', async (route) => {
    const request = route.request(); const company = request.headers()['x-company-id'] ?? '1';
    const data = rows[company] ??= [];
    if (request.method() === 'GET') {
      const keyword = new URL(request.url()).searchParams.get('keyword')?.toLowerCase() ?? '';
      const items = data.filter((row) => [row['code'], row['name']].some((value) => String(value).toLowerCase().includes(keyword)));
      return route.fulfill({ json: { items, totalCount: items.length, page: 1, pageSize: 20 } });
    }
    writes.push({ method: request.method(), body: request.postDataJSON(), url: request.url() });
    if (request.method() === 'POST') {
      const row = { ...(request.postDataJSON() as Record<string, unknown>), id: 3, isActive: true, version: 1 };
      data.push(row);
      return route.fulfill({ status: 201, json: row });
    }
    if (request.method() === 'DELETE') return route.fulfill({ status: 409, contentType: 'application/problem+json',
      json: { status: 409, errorCode: 'MD.CURRENCY.IN_USE' } });
    return route.fulfill({ status: 409, contentType: 'application/problem+json',
      json: { status: 409, errorCode: 'MD.CURRENCY.BASE_LOCKED' } });
  });
  await page.goto('/md/currencies');
  await expect(page.getByTestId('md-currency-base-TWD')).toBeVisible();
  await expect(page.getByTestId('md-currency-rates-TWD')).toHaveCount(0);
  await expect(page.getByTestId('md-currency-delete-TWD')).toHaveCount(0);
  await page.getByTestId('md-currency-search').fill('美');
  await page.getByTestId('md-currency-search-button').click();
  await expect(page.getByTestId('md-currency-row-TWD')).toHaveCount(0);
  await page.getByTestId('md-currency-search').fill('');
  await page.getByTestId('md-currency-search-button').click();

  await page.getByTestId('md-currency-create').click();
  await page.getByTestId('md-currency-code').fill('jp');
  await page.getByTestId('md-currency-name').fill('日圓');
  await page.locator('.ant-modal-footer .ant-btn-primary').click();
  await expect(page.getByText('請輸入三個英文字母的 ISO 4217 代碼')).toBeVisible();
  await page.getByTestId('md-currency-code').fill('jpy');
  await page.getByTestId('md-currency-decimals').fill('0');
  await page.locator('.ant-modal-footer .ant-btn-primary').click();
  await expect(page.getByTestId('md-currency-row-JPY')).toBeVisible();
  expect(writes[0]).toMatchObject({ method: 'POST', body: { code: 'JPY', name: '日圓', symbol: null, decimalPlaces: 0, isBase: false } });

  await page.getByTestId('md-currency-edit-TWD').click();
  await expect(page.getByTestId('md-currency-is-base')).toBeDisabled();
  await expect(page.getByTestId('md-currency-base-locked')).toBeVisible();
  await page.locator('.ant-modal-footer .ant-btn:not(.ant-btn-primary)').click();
  await page.getByTestId('md-currency-edit-USD').click();
  await page.getByTestId('md-currency-is-base').check();
  await page.locator('.ant-modal-footer .ant-btn-primary').click();
  await expect(page.getByTestId('md-currency-save-error')).toContainText('已有匯率，不可更換本位幣');
  expect(writes[1]).toMatchObject({ method: 'PUT', body: { isBase: true, isActive: true, version: 1 } });
  await page.locator('.ant-modal-footer .ant-btn:not(.ant-btn-primary)').click();

  await page.getByTestId('md-currency-delete-USD').click();
  await page.getByTestId('erp-message-ok').click();
  await expect(page.getByTestId('md-currency-error')).toContainText('此幣別已有匯率');
  expect(writes[2]!.url).toContain('/currencies/2?version=1');

  await page.getByTestId('layout-company-select').click();
  await page.locator('nz-option-item', { hasText: 'HK01 Hong Kong' }).click();
  await expect(page.getByTestId('md-currency-row-TWD')).toHaveCount(0);
});

test('匯率：按日期篩選、新增、重疊提示、修改、刪除；停用幣別不可新增', async ({ page, pageErrors }) => {
  pageErrors.allow(/status of 409/);
  const rates: Record<string, unknown>[] = [
    { id: 7, currencyId: 2, effectiveFrom: '2026-01-01', effectiveTo: '2026-03-31', rate: 31.5, version: 1 }];
  const writes: { method: string; body: unknown; url: string }[] = [];
  await page.route('**/api/v1/md/currencies?**', (route) => route.fulfill({ json: { items: [
    { id: 1, code: 'TWD', name: '新台幣', symbol: 'NT$', decimalPlaces: 0, isBase: true, isActive: true, version: 1 },
    { id: 2, code: 'USD', name: '美元', symbol: '$', decimalPlaces: 2, isBase: false, isActive: true, version: 1 },
    { id: 3, code: 'CNY', name: '人民幣', symbol: '¥', decimalPlaces: 2, isBase: false, isActive: false, version: 1 }], totalCount: 3 } }));
  await page.route('**/api/v1/md/currencies/*/rates**', async (route) => {
    const request = route.request();
    if (request.method() === 'GET') {
      const on = new URL(request.url()).searchParams.get('on');
      const items = request.url().includes('/currencies/2/') ? rates.filter((x) => !on
        || (String(x['effectiveFrom']) <= on && (x['effectiveTo'] === null || String(x['effectiveTo']) >= on))) : [];
      return route.fulfill({ json: { items, totalCount: items.length, page: 1, pageSize: 10 } });
    }
    writes.push({ method: request.method(), body: request.postDataJSON(), url: request.url() });
    if (request.method() === 'POST') {
      const body = request.postDataJSON() as { effectiveFrom: string };
      if (body.effectiveFrom <= '2026-03-31') return route.fulfill({ status: 409, contentType: 'application/problem+json',
        json: { status: 409, errorCode: 'MD.EXCHANGE_RATE.OVERLAP' } });
      rates.unshift({ id: 8, currencyId: 2, ...body, version: 1 });
      return route.fulfill({ status: 201, json: rates[0] });
    }
    if (request.method() === 'PUT') {
      const row = rates.find((x) => x['id'] === 7)!;
      Object.assign(row, request.postDataJSON(), { version: 2 });
      return route.fulfill({ json: row });
    }
    rates.splice(rates.findIndex((x) => x['id'] === 7), 1);
    return route.fulfill({ status: 204 });
  });
  await page.goto('/md/currencies');
  await page.getByTestId('md-currency-rates-USD').click();
  await expect(page.getByTestId('md-rate-row-2026-01-01')).toContainText('31.5');

  await page.getByTestId('md-rate-from').fill('2026-03-01');
  await page.getByTestId('md-rate-rate').fill('32');
  await page.getByTestId('md-rate-save').click();
  await expect(page.getByTestId('md-rate-error')).toContainText('期間與此幣別的其他匯率重疊');
  await page.getByTestId('md-rate-from').fill('2026-04-01');
  await page.getByTestId('md-rate-rate').fill('1.123456789');
  await page.getByTestId('md-rate-save').click();
  await expect(page.getByTestId('md-rate-rate-error')).toBeVisible();
  await page.getByTestId('md-rate-rate').fill('32.12345678');
  await page.getByTestId('md-rate-save').click();
  await expect(page.getByTestId('md-rate-row-2026-04-01')).toContainText('持續有效');
  expect(writes.at(-1)).toMatchObject({ method: 'POST', body: { effectiveFrom: '2026-04-01', effectiveTo: null, rate: 32.12345678 } });

  await page.getByTestId('md-rate-on').fill('2026-02-15');
  await page.getByTestId('md-rate-on').dispatchEvent('change');
  await expect(page.getByTestId('md-rate-row-2026-04-01')).toHaveCount(0);
  await expect(page.getByTestId('md-rate-row-2026-01-01')).toBeVisible();
  await page.getByTestId('md-rate-clear').click();
  await expect(page.getByTestId('md-rate-row-2026-04-01')).toBeVisible();

  await page.getByTestId('md-rate-edit-2026-01-01').click();
  await page.getByTestId('md-rate-to').fill('2026-03-15');
  await page.getByTestId('md-rate-rate').fill('31.25');
  await page.getByTestId('md-rate-save').click();
  await expect(page.getByTestId('md-rate-row-2026-01-01')).toContainText('31.25');
  expect(writes.at(-1)).toMatchObject({ method: 'PUT', body: { effectiveFrom: '2026-01-01', effectiveTo: '2026-03-15', rate: 31.25, version: 1 } });

  await page.getByTestId('md-rate-delete-2026-01-01').click();
  await page.getByTestId('erp-message-ok').click();
  await expect(page.getByTestId('md-rate-row-2026-01-01')).toHaveCount(0);
  expect(writes.at(-1)!.url).toContain('/currencies/2/rates/7?version=2');

  await page.keyboard.press('Escape');
  await expect(page.getByTestId('md-rate-dialog')).toHaveCount(0);
  await page.getByTestId('md-currency-rates-CNY').click();
  await expect(page.getByTestId('md-rate-inactive')).toBeVisible();
  await expect(page.getByTestId('md-rate-form')).toHaveCount(0);
});

test.describe('幣別只讀權限', () => {
  test.use({ authorization: { ...E2E_AUTHORIZATION, isFullAccess: false,
    permissions: [{ code: 'MD.CURRENCY.VIEW', dataScope: 'Company' }] } });
  test('隱藏寫入按鈕，匯率只可查看', async ({ page }) => {
    await page.route('**/api/v1/md/currencies?**', (route) => route.fulfill({ json: { items: [
      { id: 2, code: 'USD', name: '美元', symbol: '$', decimalPlaces: 2, isBase: false, isActive: true, version: 1 }], totalCount: 1 } }));
    await page.route('**/api/v1/md/currencies/2/rates**', (route) => route.fulfill({ json: { items: [
      { id: 7, currencyId: 2, effectiveFrom: '2026-01-01', effectiveTo: null, rate: 31.5, version: 1 }], totalCount: 1 } }));
    await page.goto('/md/currencies');
    await expect(page.getByTestId('md-currency-title')).toBeVisible();
    await expect(page.getByTestId('md-currency-create')).toHaveCount(0);
    await expect(page.getByTestId('md-currency-edit-USD')).toHaveCount(0);
    await expect(page.getByTestId('md-currency-delete-USD')).toHaveCount(0);
    await page.getByTestId('md-currency-rates-USD').click();
    await expect(page.getByTestId('md-rate-row-2026-01-01')).toBeVisible();
    await expect(page.getByTestId('md-rate-form')).toHaveCount(0);
    await expect(page.getByTestId('md-rate-edit-2026-01-01')).toHaveCount(0);
  });
});
