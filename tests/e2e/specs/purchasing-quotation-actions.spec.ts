import { mkdir } from 'node:fs/promises';
import type { Page } from '@playwright/test';
import { E2E_AUTHORIZATION, chooseLanguage, chooseTheme, expect, test } from '../support/fixtures';

const rfq = { id: 17, documentNo: 'RFQ-0017', businessDate: '2028-02-29', replyDeadline: '2028-03-31',
  ownerId: 4, status: 'Sent', version: 2, remark: null, sentAt: '2028-02-29T09:00:00', createdAt: '2028-02-29T08:00:00',
  lines: [{ id: 170, lineNo: 1, itemId: 11, requisitionLineId: null, requiredDate: '2028-03-15', quantity: 1, remark: null }],
  invitations: [{ id: 171, supplierId: 7, supplierCodeSnapshot: 'SUP-7', supplierNameSnapshot: 'Saved supplier',
    sentAt: '2028-02-29T09:00:00' }] };
type Saved = { id: number; rfqId: number; supplierId: number; status: string; currentRevisionNo: number;
  version: number; isExpired: boolean; revisions: { id: number; revisionNo: number; quoteDate: string;
    validUntil: string; remark: string | null; lines: { id: number; rfqLineId: number; unitPrice: number;
      currencyCode: string; taxRateId: number }[] }[] };

async function capture(page: Page, state: string): Promise<void> {
  await mkdir('../../tests/qa/PUR/screenshots', { recursive: true });
  await expect(page.locator('.ant-spin-spinning')).toHaveCount(0);
  await page.screenshot({ path: `../../tests/qa/PUR/screenshots/4.14-${state}.png`, fullPage: true, animations: 'disabled' });
}

test('PUR-QUOTE-UI-005/006 creates, revises and withdraws one saved offer with immutable history', async ({ page }) => {
  let saved: Saved | null = null; const writes: { path: string; body: unknown; version: string | null }[] = [];
  await page.route('**/api/v1/pur/rfqs/17/quotation-context**', route => route.fulfill({ json: { rfq, currencies: [{ code: 'TWD', name: 'Taiwan Dollar' }], taxRates: [{ id: 9, code: 'VAT', name: 'Input VAT', ratePercent: 5 }] } }));
  await page.route('**/api/v1/pur/rfqs/17/quotations**', route => {
    const url = new URL(route.request().url()); const request = route.request();
    if (request.method() === 'GET') {
      if (url.pathname.endsWith('/31')) return route.fulfill({ json: saved });
      const items = saved ? [{ ...saved, quoteDate: saved.revisions.at(-1)!.quoteDate,
        validUntil: saved.revisions.at(-1)!.validUntil }] : [];
      return route.fulfill({ json: { items, totalCount: items.length } });
    }
    writes.push({ path: url.pathname, body: request.postDataJSON(), version: url.searchParams.get('version') });
    if (url.pathname.endsWith('/withdraw')) saved = { ...saved!, status: 'Withdrawn', version: saved!.version + 1 };
    else {
      const body = request.postDataJSON(); const revisionNo = (saved?.currentRevisionNo ?? 0) + 1;
      const revision = { id: 40 + revisionNo, revisionNo, quoteDate: body.quoteDate, validUntil: body.validUntil,
        remark: body.remark, lines: body.lines.map((line: { rfqLineId: number; unitPrice: string; currencyCode: string; taxRateId: number }) =>
          ({ id: 50 + revisionNo, rfqLineId: line.rfqLineId, unitPrice: Number(line.unitPrice),
            currencyCode: line.currencyCode, taxRateId: line.taxRateId })) };
      saved = { id: 31, rfqId: 17, supplierId: body.supplierId, status: 'Active', currentRevisionNo: revisionNo,
        version: (saved?.version ?? 0) + 1, isExpired: false, revisions: [...(saved?.revisions ?? []), revision] };
    }
    return route.fulfill({ status: writes.length === 1 ? 201 : 200, json: saved });
  });
  await page.goto('/pur/rfqs/17/quotations');
  await page.getByTestId('pur-quotation-create').click();
  await page.getByTestId('pur-quotation-save').click();
  await expect(page.getByTestId('pur-quotation-error')).toContainText('請修正');
  await expect(page.getByTestId('pur-quotation-edit-supplier')).toBeFocused();
  await capture(page, 'create-validation-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'create-validation-dark-zh-Hant');
  await chooseTheme(page, 'light');
  expect(writes).toHaveLength(0);
  await page.getByTestId('pur-quotation-edit-supplier').selectOption({ label: 'SUP-7 · Saved supplier' });
  await page.getByTestId('pur-quotation-edit-date').fill('2028-02-29');
  await page.getByTestId('pur-quotation-edit-until').fill('2028-03-31');
  await page.getByTestId('pur-quotation-price-0').fill('0.123456');
  await page.getByTestId('pur-quotation-currency-0').selectOption('TWD');
  await page.getByTestId('pur-quotation-tax-0').selectOption({ label: 'VAT · Input VAT (5%)' });
  await capture(page, 'create-normal-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'create-normal-dark-zh-Hant');
  await chooseTheme(page, 'light');
  await chooseLanguage(page, 'en'); await capture(page, 'create-normal-light-en');
  await chooseLanguage(page, 'zh-Hans'); await capture(page, 'create-normal-light-zh-Hans');
  await chooseLanguage(page, 'zh-Hant');
  await page.getByRole('link', { name: '返回詢價列表' }).click();
  await expect(page.getByText('尚未儲存')).toBeVisible();
  await page.getByRole('button', { name: '繼續編輯' }).click();
  await expect(page).toHaveURL(/\/pur\/rfqs\/17\/quotations/);
  await expect(page.getByTestId('pur-quotation-price-0')).toHaveValue('0.123456');
  await page.getByTestId('pur-quotation-save').click();
  await expect(page.getByTestId('pur-quotation-success')).toContainText('首版報價已保存');
  await expect(page.getByTestId('pur-quotation-detail')).toContainText('0.123456');
  expect(writes[0].body).toMatchObject({ supplierId: 7, lines: [{ rfqLineId: 170, unitPrice: '0.123456', currencyCode: 'TWD' }] });
  await capture(page, 'create-success-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'create-success-dark-zh-Hant'); await chooseTheme(page, 'light');
  await page.getByTestId('pur-quotation-revise').click();
  await page.getByTestId('pur-quotation-price-0').fill('2.123456');
  await page.getByTestId('pur-quotation-save').click();
  await expect(page.getByTestId('pur-quotation-success')).toContainText('新修訂已保存');
  await expect(page.getByTestId('pur-quotation-detail')).toContainText('0.123456');
  await expect(page.getByTestId('pur-quotation-detail')).toContainText('2.123456');
  expect(writes[1].version).toBe('1');
  await page.getByTestId('pur-quotation-withdraw').click();
  await expect(page.getByText('撤回這張報價？')).toBeVisible();
  await capture(page, 'withdraw-confirm-light-zh-Hant');
  await page.getByRole('button', { name: '取消' }).last().click();
  expect(writes).toHaveLength(2);
  await page.getByTestId('pur-quotation-withdraw').click();
  await page.getByRole('button', { name: '撤回報價' }).last().click();
  await expect(page.getByTestId('pur-quotation-success')).toContainText('報價已撤回');
  await expect(page.getByTestId('pur-quotation-detail')).toContainText('2.123456');
  await expect(page.getByTestId('pur-quotation-revise')).toHaveCount(0);
  expect(writes[2].version).toBe('2');
  await capture(page, 'withdraw-success-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'withdraw-success-dark-zh-Hant');
});

test.describe('quotation write authorization', () => {
  test.use({ authorization: { ...E2E_AUTHORIZATION, isFullAccess: false,
    permissions: [{ code: 'PUR.QUOTATION.VIEW', dataScope: 'Company' },
      { code: 'PUR.RFQ.VIEW', dataScope: 'Company' }] } });
  test('PUR-QUOTE-UI-009 keeps VIEW-only history read-only', async ({ page }) => {
    await page.route('**/api/v1/pur/rfqs/17/quotation-context**', route => route.fulfill({ json: { rfq, currencies: [{ code: 'TWD', name: 'Taiwan Dollar' }], taxRates: [{ id: 9, code: 'VAT', name: 'Input VAT', ratePercent: 5 }] } }));
    await page.route('**/api/v1/pur/rfqs/17/quotations**', route => route.fulfill({ json: { items: [], totalCount: 0 } }));
    await page.goto('/pur/rfqs/17/quotations');
    await expect(page.getByTestId('pur-quotation-table')).toBeVisible();
    await expect(page.getByTestId('pur-quotation-create')).toHaveCount(0);
    await capture(page, 'view-only-light-zh-Hant');
    await chooseTheme(page, 'dark'); await capture(page, 'view-only-dark-zh-Hant');
  });
});

test.describe('quotation role without RFQ view', () => {
  test.use({ authorization: { ...E2E_AUTHORIZATION, isFullAccess: false,
    permissions: [{ code: 'PUR.QUOTATION.VIEW', dataScope: 'Company' },
      { code: 'PUR.QUOTATION.QUOTE', dataScope: 'Company' }] } });
  test('PUR-QUOTE-CTX-002 allows QUOTE without RFQ.VIEW and offers saved choices', async ({ page }) => {
    const paths: string[] = [];
    await page.route('**/api/v1/pur/rfqs/17/quotation-context**', route => {
      paths.push(new URL(route.request().url()).pathname);
      return route.fulfill({ json: { rfq, currencies: [{ code: 'TWD', name: 'Taiwan Dollar' }],
        taxRates: [{ id: 9, code: 'VAT', name: 'Input VAT', ratePercent: 5 }] } });
    });
    await page.route('**/api/v1/pur/rfqs/17/quotations**', route => route.fulfill({ json: { items: [], totalCount: 0 } }));
    await page.goto('/pur/rfqs/17/quotations');
    await expect(page.getByTestId('pur-quotation-create')).toBeVisible();
    await page.getByTestId('pur-quotation-create').click();
    await expect(page.getByTestId('pur-quotation-currency-0').locator('option')).toContainText(['選擇幣別', 'TWD · Taiwan Dollar']);
    await expect(page.getByTestId('pur-quotation-tax-0').locator('option')).toContainText(['選擇進項稅率', 'VAT · Input VAT (5%)']);
    expect(paths).toEqual(['/api/v1/pur/rfqs/17/quotation-context']);
    await expect(page.getByTestId('pur-quotation-context-error')).toHaveCount(0);
    await capture(page, 'quote-no-rfq-view-light-zh-Hant');
    await chooseTheme(page, 'dark'); await capture(page, 'quote-no-rfq-view-dark-zh-Hant');
  });
  test('PUR-QUOTE-CTX-001 refreshes input rates when the quote date changes', async ({ page }) => {
    await page.route('**/api/v1/pur/rfqs/17/quotation-context**', route => {
      const date = new URL(route.request().url()).searchParams.get('quoteDate');
      const id = date === '2028-03-01' ? 10 : 9;
      return route.fulfill({ json: { rfq, currencies: [{ code: 'TWD', name: 'Taiwan Dollar' }],
        taxRates: [{ id, code: 'VAT', name: 'Input VAT', ratePercent: id === 9 ? 5 : 6 }] } });
    });
    await page.route('**/api/v1/pur/rfqs/17/quotations**', route => route.fulfill({ json: { items: [], totalCount: 0 } }));
    await page.goto('/pur/rfqs/17/quotations');
    await page.getByTestId('pur-quotation-create').click();
    await page.getByTestId('pur-quotation-edit-date').fill('2028-02-29');
    await page.getByTestId('pur-quotation-tax-0').selectOption({ label: 'VAT · Input VAT (5%)' });
    await page.getByTestId('pur-quotation-edit-date').fill('2028-03-01');
    await expect(page.getByTestId('pur-quotation-tax-0').locator('option')).toContainText([
      '選擇進項稅率', 'VAT · Input VAT (6%)']);
    await expect(page.getByTestId('pur-quotation-tax-0').locator('option:checked')).toHaveText('選擇進項稅率');
    await expect(page.getByTestId('pur-quotation-tax-0').getByRole('option',
      { name: 'VAT · Input VAT (5%)' })).toHaveCount(0);
  });
  test('PUR-QUOTE-CTX-004 clears editor access and shows a context failure trace', async ({ page, pageErrors }) => {
    pageErrors.allow(/Failed to load resource: the server responded with a status of 500/);
    pageErrors.allowApi5xx(/\/api\/v1\/pur\/rfqs\/17\/quotation-context/, 500);
    await page.route('**/api/v1/pur/rfqs/17/quotation-context**', route => route.fulfill({ status: 500,
      json: { traceId: 'quote-context-500' } }));
    await page.route('**/api/v1/pur/rfqs/17/quotations**', route => route.fulfill({ json: { items: [], totalCount: 0 } }));
    await page.goto('/pur/rfqs/17/quotations');
    await expect(page.getByTestId('pur-quotation-context-error')).toContainText('quote-context-500');
    await expect(page.getByTestId('pur-quotation-create')).toHaveCount(0);
    await capture(page, 'quote-context-error-light-zh-Hant');
    await chooseTheme(page, 'dark'); await capture(page, 'quote-context-error-dark-zh-Hant');
  });
});

test('PUR-QUOTE-UI-007/008/010 keeps a rejected revision and explains conflicts and server failures',
  async ({ page, pageErrors }) => {
    pageErrors.allow(/Failed to load resource: the server responded with a status of (409|422|500)/);
    pageErrors.allowApi5xx(/\/api\/v1\/pur\/rfqs\/17\/quotations\/31\/revisions/, 500);
    let failure = 409; let attempts = 0;
    const previous: Saved = { id: 31, rfqId: 17, supplierId: 7, status: 'Active', currentRevisionNo: 1,
      version: 2, isExpired: false, revisions: [{ id: 41, revisionNo: 1, quoteDate: '2028-02-29',
        validUntil: '2028-03-31', remark: null, lines: [{ id: 51, rfqLineId: 170, unitPrice: 1.123456,
          currencyCode: 'TWD', taxRateId: 9 }] }] };
    await page.route('**/api/v1/pur/rfqs/17/quotation-context**', route => route.fulfill({ json: { rfq, currencies: [{ code: 'TWD', name: 'Taiwan Dollar' }], taxRates: [{ id: 9, code: 'VAT', name: 'Input VAT', ratePercent: 5 }] } }));
    await page.route('**/api/v1/pur/rfqs/17/quotations**', route => {
      const url = new URL(route.request().url());
      if (route.request().method() === 'GET') return route.fulfill({ json: url.pathname.endsWith('/31') ? previous :
        { items: [{ ...previous, quoteDate: '2028-02-29', validUntil: '2028-03-31' }], totalCount: 1 } });
      attempts++;
      return route.fulfill({ status: failure, json: failure === 409
        ? { errorCode: 'COMMON.CONCURRENCY.CONFLICT' } : failure === 422
          ? { errorCode: 'PUR.QUOTATION.EXPIRED' } : { traceId: 'quote-trace-500' } });
    });
    await page.goto('/pur/rfqs/17/quotations');
    await page.getByTestId('pur-quotation-row-31').getByRole('button').click();
    await page.getByTestId('pur-quotation-revise').click();
    await page.getByTestId('pur-quotation-price-0').fill('2.123456');
    await page.getByTestId('pur-quotation-save').click();
    await expect(page.getByTestId('pur-quotation-error')).toBeVisible();
    await expect(page.getByTestId('pur-quotation-price-0')).toHaveValue('2.123456');
    await expect(page.getByTestId('pur-quotation-success')).toHaveCount(0);
    await capture(page, 'conflict-light-zh-Hant');
    await chooseTheme(page, 'dark'); await capture(page, 'conflict-dark-zh-Hant'); await chooseTheme(page, 'light');
    expect(attempts).toBe(1);
    await page.getByRole('button', { name: '重新載入' }).click();
    await expect(page.getByText('重新載入保存的報價？')).toBeVisible();
    await page.getByRole('button', { name: '重新載入' }).last().click();
    await page.getByTestId('pur-quotation-row-31').getByRole('button').click();
    await page.getByTestId('pur-quotation-revise').click();
    failure = 422;
    await page.getByTestId('pur-quotation-save').click();
    await expect(page.getByTestId('pur-quotation-error')).toContainText('已過期');
    await capture(page, 'rejected-light-zh-Hant');
    await chooseTheme(page, 'dark'); await capture(page, 'rejected-dark-zh-Hant'); await chooseTheme(page, 'light');
    failure = 500;
    await page.getByTestId('pur-quotation-save').click();
    await expect(page.getByTestId('pur-quotation-error')).toContainText('quote-trace-500');
    await expect(page.getByTestId('pur-quotation-success')).toHaveCount(0);
    await capture(page, 'server-error-write-light-zh-Hant');
    await chooseTheme(page, 'dark'); await capture(page, 'server-error-write-dark-zh-Hant');
    expect(attempts).toBe(3);
  });

test('PUR-QUOTE-UI-006/010 maps server line validation and keeps the draft on disconnection',
  async ({ page, pageErrors }) => {
    pageErrors.allow(/Failed to load resource|net::ERR_FAILED/);
    let disconnect = false; let writes = 0;
    const previous: Saved = { id: 31, rfqId: 17, supplierId: 7, status: 'Active', currentRevisionNo: 1,
      version: 2, isExpired: false, revisions: [{ id: 41, revisionNo: 1, quoteDate: '2028-02-29',
        validUntil: '2028-03-31', remark: null, lines: [{ id: 51, rfqLineId: 170, unitPrice: 1.123456,
          currencyCode: 'TWD', taxRateId: 9 }] }] };
    await page.route('**/api/v1/pur/rfqs/17/quotation-context**', route => route.fulfill({ json: { rfq, currencies: [{ code: 'TWD', name: 'Taiwan Dollar' }], taxRates: [{ id: 9, code: 'VAT', name: 'Input VAT', ratePercent: 5 }] } }));
    await page.route('**/api/v1/pur/rfqs/17/quotations**', route => {
      const url = new URL(route.request().url());
      if (route.request().method() === 'GET') return route.fulfill({ json: url.pathname.endsWith('/31') ? previous :
        { items: [{ ...previous, quoteDate: '2028-02-29', validUntil: '2028-03-31' }], totalCount: 1 } });
      writes++;
      return disconnect ? route.abort() : route.fulfill({ status: 400,
        json: { errorCode: 'COMMON.REQUEST.VALIDATION_FAILED', errors: { 'lines[0].unitPrice': ['Invalid price'] } } });
    });
    await page.goto('/pur/rfqs/17/quotations');
    await page.getByTestId('pur-quotation-row-31').getByRole('button').click();
    await page.getByTestId('pur-quotation-revise').click();
    await page.getByTestId('pur-quotation-save').click();
    await expect(page.getByTestId('pur-quotation-error')).toBeVisible();
    await expect(page.getByText('伺服器拒絕此欄位')).toBeVisible();
    await expect(page.getByTestId('pur-quotation-success')).toHaveCount(0);
    await capture(page, 'field-error-light-zh-Hant');
    await chooseTheme(page, 'dark'); await capture(page, 'field-error-dark-zh-Hant'); await chooseTheme(page, 'light');
    disconnect = true;
    await page.getByTestId('pur-quotation-save').click();
    await expect(page.getByTestId('pur-quotation-error')).toContainText('網路');
    await expect(page.getByTestId('pur-quotation-price-0')).toHaveValue('1.123456');
    await capture(page, 'network-write-light-zh-Hant');
    await chooseTheme(page, 'dark'); await capture(page, 'network-write-dark-zh-Hant');
    expect(writes).toBe(2);
  });
