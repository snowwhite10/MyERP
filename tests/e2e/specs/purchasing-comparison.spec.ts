import { mkdir } from 'node:fs/promises';
import { E2E_AUTHORIZATION, chooseLanguage, chooseTheme, expect, test } from '../support/fixtures';

const comparison = { rfqId: 17, comparisonDate: '2026-10-23', baseCurrencyCode: 'TWD', lines: [
  { rfqLineId: 170, lineNo: 1, quantity: 2, offers: [
    { quotationId: 31, supplierId: 7, quotationLineId: 51, unitPrice: 10.123456, currencyCode: 'USD',
      taxRateId: 9, isComparable: true, unavailableReason: null, exchangeRate: 2, exchangeRateId: 42,
      exchangeRateFrom: '2026-10-01', exchangeRateTo: '2026-10-31', taxRatePercent: 5,
      taxRateFrom: '2026-01-01', taxRateTo: null, baseNet: 40.49, baseTax: 2.02,
      baseGross: 42.51, status: 'Active' },
    { quotationId: 32, supplierId: 8, quotationLineId: 52, unitPrice: 9.5, currencyCode: 'EUR',
      taxRateId: 9, isComparable: false, unavailableReason: 'MD.EXCHANGE_RATE.NOT_FOUND',
      exchangeRate: null, exchangeRateId: null, exchangeRateFrom: null, exchangeRateTo: null,
      taxRatePercent: null, taxRateFrom: null, taxRateTo: null, baseNet: null, baseTax: null,
      baseGross: null, status: 'Active' },
  ] },
  { rfqLineId: 171, lineNo: 2, quantity: 1, offers: [] },
] };
const awardContext = { rfqId: 17, documentNo: 'RFQ-0017', status: 'Sent', version: 3, awards: [] as unknown[] };

test.beforeEach(async ({ page }) => {
  await page.route('**/api/v1/pur/rfqs/17/award-context', route => route.fulfill({ json: awardContext }));
});

async function capture(page: import('@playwright/test').Page, name: string): Promise<void> {
  const directory = '../../tests/qa/PUR/screenshots';
  await mkdir(directory, { recursive: true });
  await page.screenshot({ path: `${directory}/4.15-${name}.png`, fullPage: true, animations: 'disabled' });
}

test('PUR-COMPARE-UI-001 shows sorted live cost evidence and every ineligible reason in three languages', async ({ page }) => {
  const methods: string[] = [];
  await page.route('**/api/v1/pur/rfqs/17/comparison', route => {
    methods.push(route.request().method()); return route.fulfill({ json: comparison });
  });
  await page.goto('/pur/rfqs/17/comparison');
  await expect(page.getByTestId('pur-comparison-line-1')).toContainText('42.51 TWD');
  await expect(page.getByTestId('pur-comparison-offer-31')).toContainText('2026-10-01');
  await expect(page.getByTestId('pur-comparison-offer-31')).toContainText('42');
  await expect(page.getByTestId('pur-comparison-offer-32')).toContainText('此日期沒有有效匯率');
  await expect(page.getByTestId('pur-comparison-offer-32')).not.toContainText('42.51');
  await expect(page.getByTestId('pur-comparison-no-offers')).toBeVisible();
  await capture(page, 'normal-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'normal-dark-zh-Hant');
  await chooseTheme(page, 'light');
  await chooseLanguage(page, 'en'); await capture(page, 'normal-light-en');
  await expect(page.getByTestId('pur-comparison-offer-31').getByText('42.51 TWD')).toBeInViewport();
  await chooseLanguage(page, 'zh-Hans'); await capture(page, 'normal-light-zh-Hans');
  expect(methods).toEqual(['GET']);
});

test('PUR-COMPARE-UI-002 clears stale cost on empty, business rule and server failures', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 422/);
  pageErrors.allow(/Failed to load resource: the server responded with a status of 500/);
  pageErrors.allowApi5xx(/\/api\/v1\/pur\/rfqs\/17\/comparison/, 500);
  let mode = 'normal';
  await page.route('**/api/v1/pur/rfqs/17/comparison', route => {
    if (mode === 'empty') return route.fulfill({ json: { ...comparison, lines: [] } });
    if (mode === 'draft') return route.fulfill({ status: 422, json: { errorCode: 'PUR.RFQ.NOT_SENT' } });
    if (mode === 'server') return route.fulfill({ status: 500, json: { traceId: 'compare-trace' } });
    return route.fulfill({ json: comparison });
  });
  await page.goto('/pur/rfqs/17/comparison');
  await expect(page.getByTestId('pur-comparison-offer-31')).toBeVisible();
  mode = 'empty'; await page.getByTestId('pur-comparison-refresh').click();
  await expect(page.getByTestId('pur-comparison-no-lines')).toBeVisible();
  await capture(page, 'empty-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'empty-dark-zh-Hant');
  await chooseTheme(page, 'light');
  mode = 'draft'; await page.getByTestId('pur-comparison-refresh').click();
  await expect(page.getByTestId('pur-comparison-error')).toContainText('只有已發送');
  await expect(page.getByTestId('pur-comparison-offer-31')).toHaveCount(0);
  await capture(page, 'rule-error-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'rule-error-dark-zh-Hant');
  await chooseTheme(page, 'light');
  mode = 'server'; await page.getByTestId('pur-comparison-refresh').click();
  await expect(page.getByTestId('pur-comparison-error')).toContainText('compare-trace');
  await capture(page, 'server-error-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'server-error-dark-zh-Hant');
});

test('PUR-COMPARE-UI-004 switches company before showing new costs and explains network failure', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource|net::ERR_FAILED/);
  const companyHeaders: string[] = [];
  let disconnected = false;
  await page.route('**/api/v1/pur/rfqs/17/comparison', route => {
    companyHeaders.push(route.request().headers()['x-company-id'] ?? '');
    if (disconnected) return route.abort();
    return route.fulfill({ json: route.request().headers()['x-company-id'] === '2'
      ? { ...comparison, lines: [] } : comparison });
  });
  await page.goto('/pur/rfqs/17/comparison');
  await expect(page.getByTestId('pur-comparison-offer-31')).toBeVisible();
  await page.getByTestId('layout-company-select').click();
  await page.locator('nz-option-item', { hasText: 'HK01 Hong Kong' }).click();
  await expect(page.getByTestId('pur-comparison-no-lines')).toBeVisible();
  await expect(page.getByTestId('pur-comparison-offer-31')).toHaveCount(0);
  await capture(page, 'company-empty-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'company-empty-dark-zh-Hant');
  await chooseTheme(page, 'light');
  disconnected = true; await page.getByTestId('pur-comparison-refresh').click();
  await expect(page.getByTestId('pur-comparison-error')).toBeVisible();
  await expect(page.getByTestId('pur-comparison-no-lines')).toHaveCount(0);
  await capture(page, 'network-error-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'network-error-dark-zh-Hant');
  expect(companyHeaders).toContain('2');
});

test.describe('comparison permission boundary', () => {
  test.use({ authorization: { ...E2E_AUTHORIZATION, isFullAccess: false, permissions: [
    { code: 'PUR.RFQ.VIEW', dataScope: 'Company' },
  ] } });
  test('PUR-COMPARE-UI-004 denies direct navigation without COMPARE before requesting data', async ({ page }) => {
    let calls = 0;
    await page.route('**/api/v1/pur/rfqs/17/comparison', route => { calls++; return route.fulfill({ json: comparison }); });
    await page.goto('/pur/rfqs/17/comparison');
    await expect(page).toHaveURL(/\/forbidden/);
    expect(calls).toBe(0);
  });
});

test('PUR-AWARD-UI-001 saves one reasoned award, then reads the permanent snapshot after refresh', async ({ page }) => {
  let saved: Record<string, unknown> | null = null;
  const writes: Record<string, unknown>[] = [];
  await page.route('**/api/v1/pur/rfqs/17/comparison', route => route.fulfill({ json: comparison }));
  await page.route('**/api/v1/pur/rfqs/17/award-context', route => route.fulfill({ json: {
    ...awardContext, version: saved ? 4 : 3, awards: saved ? [saved] : [] } }));
  await page.route('**/api/v1/pur/rfqs/17/awards', async route => {
    const input = route.request().postDataJSON() as Record<string, unknown>;
    writes.push(input); saved = { id: 90, rfqLineId: input['rfqLineId'], quotationId: input['quotationId'],
      supplierId: 7, reason: input['reason'], quantity: 2, unitPrice: 10.123456, currencyCode: 'USD',
      baseCurrencyCode: 'TWD', taxRatePercent: 5, exchangeRate: 2, exchangeRateId: 42,
      exchangeRateFrom: '2026-10-01', exchangeRateTo: '2026-10-31', comparisonDate: '2026-10-23',
      baseNet: 40.49, baseTax: 2.02, baseGross: 42.51 };
    await route.fulfill({ status: 201, json: saved });
  });
  await page.goto('/pur/rfqs/17/comparison');
  await page.getByTestId('pur-award-select-31').click();
  await page.getByTestId('pur-award-save').click();
  await expect(page.getByText('請填寫 1 至 500 字決標理由。')).toBeVisible();
  expect(writes).toHaveLength(0);
  await page.getByTestId('pur-award-reason').fill('交期與含稅成本較佳');
  await page.getByTestId('pur-award-save').click();
  await expect(page.getByTestId('erp-message-dialog')).toContainText('決標後不可覆寫');
  await page.getByTestId('erp-message-ok').click();
  await expect(page.getByTestId('pur-award-snapshot-1')).toContainText('交期與含稅成本較佳');
  await expect(page.getByTestId('pur-award-snapshot-1')).toContainText('42.51 TWD');
  expect(writes).toEqual([{ version: 3, rfqLineId: 170, quotationId: 31, reason: '交期與含稅成本較佳' }]);
  await capture(page, 'award-success-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'award-success-dark-zh-Hant');
  await page.reload();
  await expect(page.getByTestId('pur-award-snapshot-1')).toContainText('交期與含稅成本較佳');
  await expect(page.getByTestId('pur-award-select-31')).toHaveCount(0);
  expect(writes).toHaveLength(1);
});

test('PUR-AWARD-UI-003/004/007 explains conflicts and server failures without claiming success', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 409/);
  pageErrors.allow(/Failed to load resource: the server responded with a status of 422/);
  pageErrors.allow(/Failed to load resource: the server responded with a status of 500/);
  pageErrors.allowApi5xx(/\/api\/v1\/pur\/rfqs\/17\/awards/, 500);
  let status = 409;
  let version = 3;
  let writes = 0;
  await page.route('**/api/v1/pur/rfqs/17/comparison', route => route.fulfill({ json: comparison }));
  await page.route('**/api/v1/pur/rfqs/17/award-context', route => route.fulfill({ json: { ...awardContext, version } }));
  await page.route('**/api/v1/pur/rfqs/17/awards', route => {
    writes++; version++;
    return route.fulfill({ status, json: { errorCode: status === 422 ? 'PUR.RFQ.LINE_ALREADY_AWARDED' :
      status === 409 ? 'COMMON.CONCURRENCY.CONFLICT' : undefined,
      traceId: status === 500 ? 'award-trace' : undefined } });
  });
  await page.goto('/pur/rfqs/17/comparison');
  for (const code of [409, 422, 500]) {
    status = code;
    await page.getByTestId('pur-award-select-31').click();
    await page.getByTestId('pur-award-reason').fill('價格及交期合適');
    await page.getByTestId('pur-award-save').click();
    await page.getByTestId('erp-message-ok').click();
    await expect(page.getByTestId('pur-comparison-error')).toBeVisible();
    await expect(page.getByTestId('pur-award-notice')).toHaveCount(0);
    await expect(page.getByTestId('pur-award-snapshot-1')).toHaveCount(0);
    if (code === 422) await expect(page.getByTestId('pur-comparison-error')).toContainText('已決標');
    if (code === 500) await expect(page.getByTestId('pur-comparison-error')).toContainText('award-trace');
    await capture(page, `award-${code}-light-zh-Hant`);
    await chooseTheme(page, 'dark'); await capture(page, `award-${code}-dark-zh-Hant`);
    await chooseTheme(page, 'light');
    if (code === 500) await page.getByTestId('pur-comparison-refresh').click();
  }
  expect(writes).toBe(3);
});

test.describe('read-only comparison role', () => {
  test.use({ authorization: { ...E2E_AUTHORIZATION, isFullAccess: false, permissions: [
    { code: 'PUR.RFQ.COMPARE', dataScope: 'Company' },
  ] } });
  test('PUR-AWARD-UI-005 reads saved evidence but never offers an award action', async ({ page }) => {
    await page.route('**/api/v1/pur/rfqs/17/comparison', route => route.fulfill({ json: comparison }));
    await page.goto('/pur/rfqs/17/comparison');
    await expect(page.getByTestId('pur-comparison-offer-31')).toBeVisible();
    await expect(page.getByTestId('pur-award-select-31')).toHaveCount(0);
    await capture(page, 'award-readonly-light-zh-Hant');
    await chooseTheme(page, 'dark'); await capture(page, 'award-readonly-dark-zh-Hant');
  });
});

for (const [label, reason] of [['1 character', '價'], ['500 characters', '理'.repeat(500)]] as const) {
  test(`PUR-AWARD-UI-002 accepts a ${label} reason and saves it unchanged`, async ({ page }) => {
    let saved: Record<string, unknown> | null = null;
    const writes: Record<string, unknown>[] = [];
    await page.route('**/api/v1/pur/rfqs/17/comparison', route => route.fulfill({ json: comparison }));
    await page.route('**/api/v1/pur/rfqs/17/award-context', route => route.fulfill({ json: {
      ...awardContext, version: saved ? 4 : 3, awards: saved ? [saved] : [] } }));
    await page.route('**/api/v1/pur/rfqs/17/awards', async route => {
      const input = route.request().postDataJSON() as Record<string, unknown>;
      writes.push(input);
      saved = { id: 91, rfqLineId: 170, quotationId: 31, supplierId: 7, reason: input['reason'], quantity: 2,
        unitPrice: 10.123456, currencyCode: 'USD', baseCurrencyCode: 'TWD', taxRatePercent: 5, exchangeRate: 2,
        exchangeRateId: 42, exchangeRateFrom: '2026-10-01', exchangeRateTo: '2026-10-31',
        comparisonDate: '2026-10-23', baseNet: 40.49, baseTax: 2.02, baseGross: 42.51 };
      await route.fulfill({ status: 201, json: saved });
    });
    await page.goto('/pur/rfqs/17/comparison');
    await page.getByTestId('pur-award-select-31').click();
    // 501 字無法輸入：maxlength 截至 500，送出的正是 500 字。
    await page.getByTestId('pur-award-reason').fill(reason.length === 500 ? reason + '超' : reason);
    await expect(page.getByTestId('pur-award-reason')).toHaveValue(reason);
    await page.getByTestId('pur-award-save').click();
    await page.getByTestId('erp-message-ok').click();
    await expect(page.getByTestId('pur-award-notice')).toBeVisible();
    await expect(page.getByTestId('pur-award-snapshot-1')).toContainText(reason);
    expect(writes).toEqual([{ version: 3, rfqLineId: 170, quotationId: 31, reason }]);
    if (reason.length === 500) {
      await capture(page, 'award-reason500-light-zh-Hant');
      await chooseTheme(page, 'dark'); await capture(page, 'award-reason500-dark-zh-Hant');
    }
  });
}

test('PUR-AWARD-UI-007 explains field validation, timeout and disconnect without claiming success', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource|net::ERR_/);
  let failure: 400 | 'timedout' | 'internetdisconnected' = 400;
  let writes = 0;
  await page.route('**/api/v1/pur/rfqs/17/comparison', route => route.fulfill({ json: comparison }));
  await page.route('**/api/v1/pur/rfqs/17/awards', route => {
    writes++;
    if (failure === 400) return route.fulfill({ status: 400, json: { errorCode: 'COMMON.REQUEST.VALIDATION_FAILED',
      errors: { reason: ['理由不得超過 500 字'] } } });
    return route.abort(failure);
  });
  await page.goto('/pur/rfqs/17/comparison');
  await page.getByTestId('pur-award-select-31').click();
  await page.getByTestId('pur-award-reason').fill('價格及交期合適');
  for (const mode of [400, 'timedout', 'internetdisconnected'] as const) {
    failure = mode;
    await page.getByTestId('pur-award-save').click();
    await page.getByTestId('erp-message-ok').click();
    const error = page.getByTestId('pur-comparison-error');
    await expect(error).toContainText(mode === 400 ? '輸入資料不正確' : '無法連線到伺服器');
    if (mode === 400) await expect(error).toContainText('reason: 理由不得超過 500 字');
    await expect(page.getByTestId('pur-award-notice')).toHaveCount(0);
    await expect(page.getByTestId('pur-award-snapshot-1')).toHaveCount(0);
    // 失敗後保留表單與理由，用戶修正或重試時不必重打。
    await expect(page.getByTestId('pur-award-reason')).toHaveValue('價格及交期合適');
    await expect(page.getByTestId('pur-award-save')).toBeEnabled();
    await capture(page, `award-${mode}-light-zh-Hant`);
    await chooseTheme(page, 'dark'); await capture(page, `award-${mode}-dark-zh-Hant`);
    await chooseTheme(page, 'light');
  }
  await chooseLanguage(page, 'en');
  await expect(page.getByTestId('pur-comparison-error')).not.toContainText('無法連線');
  await page.getByTestId('pur-comparison-refresh').click();
  await expect(page.getByTestId('pur-comparison-error')).toHaveCount(0);
  await expect(page.getByTestId('pur-award-form')).toHaveCount(0);
  await expect(page.getByTestId('pur-award-select-31')).toBeVisible();
  expect(writes).toBe(3);
});

test('PUR-AWARD-UI-006 closed RFQ shows why no award can be made and never posts', async ({ page }) => {
  let writes = 0;
  await page.route('**/api/v1/pur/rfqs/17/comparison', route => route.fulfill({ json: comparison }));
  await page.route('**/api/v1/pur/rfqs/17/award-context', route => route.fulfill({ json: { ...awardContext, status: 'Closed' } }));
  await page.route('**/api/v1/pur/rfqs/17/awards', route => { writes++; return route.fulfill({ status: 201, json: {} }); });
  await page.goto('/pur/rfqs/17/comparison');
  await expect(page.getByTestId('pur-comparison-offer-31')).toContainText('42.51');
  await expect(page.getByTestId('pur-award-select-31')).toHaveCount(0);
  await expect(page.getByTestId('pur-comparison-line-1')).toContainText('詢價已不在可決標狀態。');
  await capture(page, 'award-closed-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'award-closed-dark-zh-Hant');
  await chooseLanguage(page, 'en');
  await expect(page.getByTestId('pur-comparison-line-1')).toContainText('This RFQ is no longer open for award.');
  await chooseLanguage(page, 'zh-Hans');
  await expect(page.getByTestId('pur-comparison-line-1')).toContainText('询价已不在可决标状态。');
  expect(writes).toBe(0);
});

const convertibleAward = (id: number, lineId: number, orderId: number | null = null) => ({
  id, rfqLineId: lineId, quotationId: 31, supplierId: 7, reason: 'Best cost', quantity: 2,
  unitPrice: 10.123456, currencyCode: 'USD', baseCurrencyCode: 'TWD', taxRatePercent: 5,
  exchangeRate: 2, exchangeRateId: 42, exchangeRateFrom: '2026-10-01', exchangeRateTo: '2026-10-31',
  comparisonDate: '2026-10-23', baseNet: 40.49, baseTax: 2.02, baseGross: 42.51,
  purchaseOrderId: orderId, purchaseOrderNo: orderId ? 'PO20280229001' : null
});

test('PUR-RFQ-067 converts selected awards and verifies saved order number and line count', async ({ page }) => {
  const awards = [convertibleAward(90, 170), convertibleAward(91, 171)];
  const writes: unknown[] = []; let saved = false;
  await page.route('**/api/v1/pur/rfqs/17/comparison', route => route.fulfill({ json: comparison }));
  await page.route('**/api/v1/pur/rfqs/17/award-context', route => route.fulfill({ json: {
    ...awardContext, version: saved ? 4 : 3, awards: saved ? awards.map(a => ({ ...a,
      purchaseOrderId: 55, purchaseOrderNo: 'PO20280229001' })) : awards } }));
  await page.route('**/api/v1/pur/rfqs/17/orders', route => {
    writes.push(route.request().postDataJSON()); saved = true;
    return route.fulfill({ status: 201, json: { orderId: 55, documentNo: 'PO20280229001',
      status: 'Draft', lineCount: 2, awardIds: [90, 91], rfqVersion: 4 } });
  });
  await page.goto('/pur/rfqs/17/comparison');
  await expect(page.getByTestId('pur-convert-0')).toContainText('2 行');
  await page.getByTestId('pur-convert-0').click();
  await expect(page.getByTestId('erp-message-dialog')).toContainText('2 行');
  expect(writes).toHaveLength(0);
  await page.getByTestId('erp-message-ok').click();
  await expect(page.getByTestId('pur-award-notice')).toContainText('PO20280229001，共 2 行');
  await expect(page.getByTestId('pur-convert-all-done')).toBeVisible();
  await expect(page.getByTestId('pur-converted-list')).toContainText('PO20280229001');
  expect(writes).toEqual([{ version: 3, supplierId: 7, awardIds: [90, 91] }]);
  await capture(page, 'convert-success-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'convert-success-dark-zh-Hant');
  await chooseTheme(page, 'light'); await chooseLanguage(page, 'en');
  await expect(page.getByTestId('pur-convert-all-done')).toContainText('already been converted');
  await capture(page, 'convert-success-light-en');
  await chooseLanguage(page, 'zh-Hans');
  await expect(page.getByTestId('pur-convert-all-done')).toContainText('全部已决标行');
  await capture(page, 'convert-success-light-zh-Hans');
});

test('PUR-RFQ-067 reports a changed line and all-converted status without partial success', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 422/);
  let awards = [convertibleAward(90, 170), convertibleAward(91, 171)]; let writes = 0;
  await page.route('**/api/v1/pur/rfqs/17/comparison', route => route.fulfill({ json: comparison }));
  await page.route('**/api/v1/pur/rfqs/17/award-context', route => route.fulfill({ json: {
    ...awardContext, version: writes ? 4 : 3, awards } }));
  await page.route('**/api/v1/pur/rfqs/17/orders', route => {
    writes++; awards = [convertibleAward(90, 170, 55), convertibleAward(91, 171)];
    return route.fulfill({ status: 422, json: { errorCode: 'PUR.RFQ.AWARD_ALREADY_CONVERTED',
      detail: 'Award 90 already converted.' } });
  });
  await page.goto('/pur/rfqs/17/comparison');
  await page.getByTestId('pur-convert-0').click(); await page.getByTestId('erp-message-ok').click();
  await expect(page.getByTestId('pur-comparison-error')).toContainText('選取行已轉單');
  await expect(page.getByTestId('pur-converted-90')).toContainText('PO20280229001');
  await expect(page.getByTestId('pur-convert-0')).toContainText('1 行');
  await expect(page.getByTestId('pur-award-notice')).toHaveCount(0);
  await capture(page, 'convert-conflict-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'convert-conflict-dark-zh-Hant');
  awards = awards.map(a => ({ ...a, purchaseOrderId: 55, purchaseOrderNo: 'PO20280229001' }));
  await page.getByTestId('pur-comparison-refresh').click();
  await expect(page.getByTestId('pur-convert-all-done')).toBeVisible();
  await expect(page.getByTestId('pur-convert-0')).toHaveCount(0);
  expect(writes).toBe(1);
});

test('PUR-RFQ-067 shows no-awards and closed states without sending a conversion', async ({ page }) => {
  let writes = 0; let mode: 'empty' | 'closed' = 'empty';
  await page.route('**/api/v1/pur/rfqs/17/comparison', route => route.fulfill({ json: comparison }));
  await page.route('**/api/v1/pur/rfqs/17/award-context', route => route.fulfill({ json: {
    ...awardContext, status: mode === 'closed' ? 'Closed' : 'Sent',
    awards: mode === 'closed' ? [convertibleAward(90, 170)] : [] } }));
  await page.route('**/api/v1/pur/rfqs/17/orders', route => { writes++; return route.fulfill({ status: 201, json: {} }); });
  await page.goto('/pur/rfqs/17/comparison');
  await expect(page.getByTestId('pur-convert-no-awards')).toContainText('尚無已決標行');
  await capture(page, 'convert-empty-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'convert-empty-dark-zh-Hant');
  mode = 'closed'; await page.getByTestId('pur-comparison-refresh').click();
  await expect(page.getByTestId('pur-convert-not-open')).toContainText('不能再轉單');
  await expect(page.getByTestId('pur-convert-0')).toHaveCount(0);
  expect(writes).toBe(0);
});

test('PUR-RFQ-067 refuses empty selection and 400/409/422/500/timeout/disconnect without success', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource|net::ERR_/);
  pageErrors.allowApi5xx(/\/api\/v1\/pur\/rfqs\/17\/orders/, 500);
  const awards = [convertibleAward(90, 170)];
  let mode: 400 | 409 | 422 | 500 | 'timedout' | 'internetdisconnected' = 400;
  let writes = 0;
  await page.route('**/api/v1/pur/rfqs/17/comparison', route => route.fulfill({ json: comparison }));
  await page.route('**/api/v1/pur/rfqs/17/award-context', route => route.fulfill({ json: {
    ...awardContext, awards } }));
  await page.route('**/api/v1/pur/rfqs/17/orders', route => {
    writes++;
    if (typeof mode === 'string') return route.abort(mode);
    return route.fulfill({ status: mode, json: mode === 400
      ? { errorCode: 'COMMON.REQUEST.VALIDATION_FAILED', errors: { awardIds: ['Choose at least one award'] } }
      : mode === 422 ? { errorCode: 'PUR.RFQ.AWARD_ALREADY_CONVERTED' }
        : mode === 500 ? { traceId: 'convert-trace' } : { errorCode: 'COMMON.CONCURRENCY.CONFLICT' } });
  });
  await page.goto('/pur/rfqs/17/comparison');
  await page.getByTestId('pur-convert-check-90').uncheck();
  await expect(page.getByTestId('pur-convert-0')).toBeDisabled();
  await expect(page.getByTestId('pur-convert-none-0')).toContainText('請至少選擇一行');
  expect(writes).toBe(0);
  await page.getByTestId('pur-convert-check-90').check();
  for (const [index, failure] of ([400, 409, 422, 500, 'timedout', 'internetdisconnected'] as const).entries()) {
    mode = failure;
    await page.getByTestId('pur-convert-0').click(); await page.getByTestId('erp-message-ok').click();
    await expect.poll(() => writes, { message: `${failure} must reach the conversion API` }).toBe(index + 1);
    await expect(page.getByTestId('pur-comparison-error')).toBeVisible();
    if (failure === 400) await expect(page.getByTestId('pur-comparison-error')).toContainText('awardIds');
    if (failure === 500) await expect(page.getByTestId('pur-comparison-error')).toContainText('convert-trace');
    await expect(page.getByTestId('pur-award-notice')).toHaveCount(0);
    await expect(page.getByTestId('pur-converted-list')).toHaveCount(0);
    if (failure === 500) {
      await capture(page, 'convert-server-error-light-zh-Hant');
      await chooseTheme(page, 'dark'); await capture(page, 'convert-server-error-dark-zh-Hant');
      await chooseTheme(page, 'light');
    }
  }
  expect(writes).toBe(6);
});

test('PUR-RFQ-067 does not claim success when the saved source disagrees with the 201 response', async ({ page }) => {
  let writes = 0;
  await page.route('**/api/v1/pur/rfqs/17/comparison', route => route.fulfill({ json: comparison }));
  await page.route('**/api/v1/pur/rfqs/17/award-context', route => route.fulfill({ json: {
    ...awardContext, version: writes ? 4 : 3, awards: [convertibleAward(90, 170)] } }));
  await page.route('**/api/v1/pur/rfqs/17/orders', route => {
    writes++; return route.fulfill({ status: 201, json: { orderId: 55, documentNo: 'PO20280229001',
      status: 'Draft', lineCount: 1, awardIds: [90], rfqVersion: 4 } });
  });
  await page.goto('/pur/rfqs/17/comparison');
  await page.getByTestId('pur-convert-0').click(); await page.getByTestId('erp-message-ok').click();
  await expect(page.getByTestId('pur-comparison-error')).toContainText('不能確認轉單成功');
  await expect(page.getByTestId('pur-award-notice')).toHaveCount(0);
  await expect(page.getByTestId('pur-converted-list')).toHaveCount(0);
  expect(writes).toBe(1);
});

test.describe('comparison without conversion permission', () => {
  test.use({ authorization: { ...E2E_AUTHORIZATION, isFullAccess: false, permissions: [
    { code: 'PUR.RFQ.COMPARE', dataScope: 'Company' },
  ] } });
  test('PUR-RFQ-067 reads awards without exposing a conversion action', async ({ page }) => {
    let writes = 0;
    await page.route('**/api/v1/pur/rfqs/17/comparison', route => route.fulfill({ json: comparison }));
    await page.route('**/api/v1/pur/rfqs/17/award-context', route => route.fulfill({ json: {
      ...awardContext, awards: [convertibleAward(90, 170)] } }));
    await page.route('**/api/v1/pur/rfqs/17/orders', route => { writes++; return route.fulfill({ status: 201, json: {} }); });
    await page.goto('/pur/rfqs/17/comparison');
    await expect(page.getByTestId('pur-award-snapshot-1')).toContainText('Best cost');
    await expect(page.getByTestId('pur-convert')).toHaveCount(0);
    expect(writes).toBe(0);
  });
});
