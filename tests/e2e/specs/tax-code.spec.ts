import { E2E_AUTHORIZATION, expect, test } from '../support/fixtures';

test('稅別：類型與今日稅率、新增、修改、刪除提示與公司切換', async ({ page, pageErrors }) => {
  pageErrors.allow(/status of 409/);
  const rows: Record<string, Record<string, unknown>[]> = { '1': [
    { id: 1, code: 'VAT5', name: '應稅 5%', taxType: 'Both', currentRatePercent: 5, isActive: true, version: 1 },
    { id: 2, code: 'FREE', name: '免稅', taxType: 'Output', currentRatePercent: null, isActive: true, version: 1 }], '2': [] };
  const writes: { method: string; body: unknown; url: string }[] = [];
  await page.route('**/api/v1/md/tax-codes**', async (route) => {
    const request = route.request(); const company = request.headers()['x-company-id'] ?? '1';
    const data = rows[company] ??= [];
    if (request.method() === 'GET') {
      const keyword = new URL(request.url()).searchParams.get('keyword')?.toLowerCase() ?? '';
      const items = data.filter((row) => [row['code'], row['name']].some((value) => String(value).toLowerCase().includes(keyword)));
      return route.fulfill({ json: { items, totalCount: items.length, page: 1, pageSize: 20 } });
    }
    writes.push({ method: request.method(), body: request.postDataJSON(), url: request.url() });
    if (request.method() === 'POST') {
      const row = { ...(request.postDataJSON() as Record<string, unknown>), id: 3, currentRatePercent: null, isActive: true, version: 1 };
      data.push(row);
      return route.fulfill({ status: 201, json: row });
    }
    if (request.method() === 'PUT') {
      const row = data.find((x) => request.url().endsWith(`/tax-codes/${String(x['id'])}`))!;
      Object.assign(row, request.postDataJSON(), { version: 2 });
      return route.fulfill({ json: row });
    }
    return route.fulfill({ status: 409, contentType: 'application/problem+json', json: { status: 409, errorCode: 'MD.TAX_CODE.IN_USE' } });
  });
  await page.goto('/md/tax-codes');
  await expect(page.getByTestId('md-tax-code-title')).toHaveText('稅別與稅率');
  await expect(page.getByTestId('md-tax-code-row-VAT5')).toContainText('銷售與採購');
  await expect(page.getByTestId('md-tax-code-rate-VAT5')).toHaveText('5%');
  await expect(page.getByTestId('md-tax-code-rate-FREE')).toHaveText('今日無稅率');
  await page.getByTestId('md-tax-code-search').fill('免');
  await page.getByTestId('md-tax-code-search-button').click();
  await expect(page.getByTestId('md-tax-code-row-VAT5')).toHaveCount(0);
  await page.getByTestId('md-tax-code-search').fill('');
  await page.getByTestId('md-tax-code-search-button').click();

  await page.getByTestId('md-tax-code-create').click();
  await page.getByTestId('md-tax-code-code').fill('zero rate');
  await page.getByTestId('md-tax-code-name').fill('零稅率');
  await page.locator('.ant-modal-footer .ant-btn-primary').click();
  await expect(page.getByText('請輸入 1–30 位英文字母、數字、- 或 _。')).toBeVisible();
  await page.getByTestId('md-tax-code-code').fill('zero');
  await page.getByTestId('md-tax-code-type').selectOption('Output');
  await page.locator('.ant-modal-footer .ant-btn-primary').click();
  await expect(page.getByTestId('md-tax-code-row-ZERO')).toContainText('銷項（銷售）');
  expect(writes[0]).toMatchObject({ method: 'POST', body: { code: 'ZERO', name: '零稅率', taxType: 'Output' } });

  await page.getByTestId('md-tax-code-edit-FREE').click();
  await expect(page.getByTestId('md-tax-code-code')).toBeDisabled();
  await page.getByTestId('md-tax-code-active').uncheck();
  await page.locator('.ant-modal-footer .ant-btn-primary').click();
  await expect(page.getByTestId('md-tax-code-row-FREE')).toContainText('停用');
  expect(writes[1]).toMatchObject({ method: 'PUT', body: { name: '免稅', taxType: 'Output', isActive: false, version: 1 } });

  await page.getByTestId('md-tax-code-delete-VAT5').click();
  await page.getByTestId('erp-message-ok').click();
  await expect(page.getByTestId('md-tax-code-error')).toContainText('此稅別已有稅率');
  expect(writes[2]!.url).toContain('/tax-codes/1?version=1');

  await page.getByTestId('layout-company-select').click();
  await page.locator('nz-option-item', { hasText: 'HK01 Hong Kong' }).click();
  await expect(page.getByTestId('md-tax-code-row-VAT5')).toHaveCount(0);
});

test('稅率與試算：按日期篩選、重疊提示、新增、修改、刪除；停用稅別不可新增；試算含稅金額', async ({ page, pageErrors }) => {
  pageErrors.allow(/status of 409|status of 422/);
  const rates: Record<string, unknown>[] = [
    { id: 7, taxCodeId: 1, effectiveFrom: '2026-01-01', effectiveTo: '2026-03-31', ratePercent: 5, version: 1 }];
  const writes: { method: string; body: unknown; url: string }[] = [];
  let listCalls = 0;
  await page.route('**/api/v1/md/tax-codes?**', (route) => { listCalls++; return route.fulfill({ json: { items: [
    { id: 1, code: 'VAT5', name: '應稅 5%', taxType: 'Both', currentRatePercent: 5, isActive: true, version: 1 },
    { id: 2, code: 'OLD', name: '舊稅別', taxType: 'Input', currentRatePercent: null, isActive: false, version: 1 }], totalCount: 2 } }); });
  await page.route('**/api/v1/md/tax-codes/*/rates**', async (route) => {
    const request = route.request();
    if (request.method() === 'GET') {
      const on = new URL(request.url()).searchParams.get('on');
      const items = request.url().includes('/tax-codes/1/') ? rates.filter((x) => !on
        || (String(x['effectiveFrom']) <= on && (x['effectiveTo'] === null || String(x['effectiveTo']) >= on))) : [];
      return route.fulfill({ json: { items, totalCount: items.length, page: 1, pageSize: 10 } });
    }
    writes.push({ method: request.method(), body: request.postDataJSON(), url: request.url() });
    if (request.method() === 'POST') {
      const body = request.postDataJSON() as { effectiveFrom: string };
      if (body.effectiveFrom <= '2026-03-31') return route.fulfill({ status: 409, contentType: 'application/problem+json',
        json: { status: 409, errorCode: 'MD.TAX_RATE.OVERLAP' } });
      rates.unshift({ id: 8, taxCodeId: 1, ...body, version: 1 });
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
  await page.route('**/api/v1/md/tax-rates/calculate', async (route) => {
    const body = route.request().postDataJSON() as { direction: string; amount: number };
    writes.push({ method: 'CALC', body, url: route.request().url() });
    if (body.direction === 'Input') return route.fulfill({ status: 422, contentType: 'application/problem+json',
      json: { status: 422, errorCode: 'MD.TAX_RATE.NOT_FOUND' } });
    return route.fulfill({ json: { ratePercent: 5, net: 100, tax: 5, gross: 105 } });
  });
  await page.goto('/md/tax-codes');
  await page.getByTestId('md-tax-code-rates-VAT5').click();
  await expect(page.getByTestId('md-tax-rate-row-2026-01-01')).toContainText('5%');
  const before = listCalls;

  await page.getByTestId('md-tax-rate-from').fill('2026-03-01');
  await page.getByTestId('md-tax-rate-percent').fill('5');
  await page.getByTestId('md-tax-rate-save').click();
  await expect(page.getByTestId('md-tax-rate-error')).toContainText('期間與此稅別的其他稅率重疊');
  await page.getByTestId('md-tax-rate-from').fill('2026-04-01');
  await page.getByTestId('md-tax-rate-percent').fill('100.5');
  await page.getByTestId('md-tax-rate-save').click();
  await expect(page.getByTestId('md-tax-rate-percent-error')).toBeVisible();
  await page.getByTestId('md-tax-rate-percent').fill('5.5');
  await page.getByTestId('md-tax-rate-save').click();
  await expect(page.getByTestId('md-tax-rate-row-2026-04-01')).toContainText('持續有效');
  expect(writes.at(-1)).toMatchObject({ method: 'POST', body: { effectiveFrom: '2026-04-01', effectiveTo: null, ratePercent: 5.5 } });
  await expect.poll(() => listCalls).toBeGreaterThan(before);

  await page.getByTestId('md-tax-rate-on').fill('2026-02-15');
  await page.getByTestId('md-tax-rate-on').dispatchEvent('change');
  await expect(page.getByTestId('md-tax-rate-row-2026-04-01')).toHaveCount(0);
  await page.getByTestId('md-tax-rate-clear').click();
  await expect(page.getByTestId('md-tax-rate-row-2026-04-01')).toBeVisible();

  await page.getByTestId('md-tax-rate-edit-2026-01-01').click();
  await page.getByTestId('md-tax-rate-to').fill('2026-03-15');
  await page.getByTestId('md-tax-rate-save').click();
  await expect(page.getByTestId('md-tax-rate-row-2026-01-01')).toContainText('2026-03-15');
  expect(writes.at(-1)).toMatchObject({ method: 'PUT', body: { effectiveFrom: '2026-01-01', effectiveTo: '2026-03-15', ratePercent: 5, version: 1 } });

  await page.getByTestId('md-tax-rate-delete-2026-01-01').click();
  await page.getByTestId('erp-message-ok').click();
  await expect(page.getByTestId('md-tax-rate-row-2026-01-01')).toHaveCount(0);
  expect(writes.at(-1)!.url).toContain('/tax-codes/1/rates/7?version=2');

  await page.keyboard.press('Escape');
  await expect(page.getByTestId('md-tax-rate-dialog')).toHaveCount(0);
  await page.getByTestId('md-tax-code-rates-OLD').click();
  await expect(page.getByTestId('md-tax-rate-inactive')).toBeVisible();
  await expect(page.getByTestId('md-tax-rate-form')).toHaveCount(0);
  await page.keyboard.press('Escape');
  await expect(page.getByTestId('md-tax-rate-dialog')).toHaveCount(0);

  await page.getByTestId('md-tax-code-calc-VAT5').click();
  await page.getByTestId('md-tax-calc-date').fill('2026-02-01');
  await page.getByTestId('md-tax-calc-currency').fill('twd');
  await page.getByTestId('md-tax-calc-amount').fill('105');
  await page.getByTestId('md-tax-calc-includes').check();
  await page.getByTestId('md-tax-calc-submit').click();
  await expect(page.getByTestId('md-tax-calc-net')).toHaveText('100');
  await expect(page.getByTestId('md-tax-calc-tax')).toHaveText('5');
  await expect(page.getByTestId('md-tax-calc-gross')).toHaveText('105');
  expect(writes.at(-1)).toMatchObject({ method: 'CALC', body: { taxCode: 'VAT5', direction: 'Output', date: '2026-02-01',
    amount: 105, priceIncludesTax: true, currencyCode: 'TWD' } });
  await page.getByTestId('md-tax-calc-direction').selectOption('Input');
  await page.getByTestId('md-tax-calc-submit').click();
  await expect(page.getByTestId('md-tax-calc-error')).toContainText('該日期沒有適用稅率');
  await expect(page.getByTestId('md-tax-calc-result')).toHaveCount(0);
});

test.describe('稅別只讀權限', () => {
  test.use({ authorization: { ...E2E_AUTHORIZATION, isFullAccess: false,
    permissions: [{ code: 'MD.TAX_CODE.VIEW', dataScope: 'Company' }] } });
  test('隱藏寫入按鈕，稅率只可查看，仍可試算', async ({ page }) => {
    await page.route('**/api/v1/md/tax-codes?**', (route) => route.fulfill({ json: { items: [
      { id: 1, code: 'VAT5', name: '應稅 5%', taxType: 'Both', currentRatePercent: 5, isActive: true, version: 1 }], totalCount: 1 } }));
    await page.route('**/api/v1/md/tax-codes/1/rates**', (route) => route.fulfill({ json: { items: [
      { id: 7, taxCodeId: 1, effectiveFrom: '2026-01-01', effectiveTo: null, ratePercent: 5, version: 1 }], totalCount: 1 } }));
    await page.goto('/md/tax-codes');
    await expect(page.getByTestId('md-tax-code-title')).toBeVisible();
    await expect(page.getByTestId('md-tax-code-create')).toHaveCount(0);
    await expect(page.getByTestId('md-tax-code-edit-VAT5')).toHaveCount(0);
    await expect(page.getByTestId('md-tax-code-delete-VAT5')).toHaveCount(0);
    await expect(page.getByTestId('md-tax-code-calc-VAT5')).toBeVisible();
    await page.getByTestId('md-tax-code-rates-VAT5').click();
    await expect(page.getByTestId('md-tax-rate-row-2026-01-01')).toBeVisible();
    await expect(page.getByTestId('md-tax-rate-form')).toHaveCount(0);
    await expect(page.getByTestId('md-tax-rate-edit-2026-01-01')).toHaveCount(0);
  });
});
