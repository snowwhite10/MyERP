import { mkdir } from 'node:fs/promises';
import type { Page } from '@playwright/test';
import { chooseLanguage, chooseTheme, E2E_AUTHORIZATION, expect, test } from '../support/fixtures';

/**
 * 介面 4.3 採購單修訂（WU-PUR-006f3，REQ_IO_A4.3.1～2）：模擬後端保存資料與 PUT，每個情境同時斷言畫面訊息與後端實際
 * 收到／保存的資料（版本、決標行原行號、駁回回草稿；409／422 零保存）。
 */
interface Backend { puts: { version: string | null; body: Record<string, any> }[]; order: Record<string, any>;
  next: { status: number; body: object } | null; getNext: { status: number; body: object } | null }

const manual = { id: 91, lineNo: 1, itemId: 7, requisitionLineId: 44, awardId: null, quantity: 2.5, unitPrice: 100.123456, taxRateId: 3,
  taxRatePercent: 5, originalNet: 250.3086, originalTax: 12.5154, originalGross: 262.824, baseNet: 250.3086, baseTax: 12.5154,
  baseGross: 262.824, requisitionId: 4, requisitionDocumentNo: 'PR-0004', requisitionLineNo: 2, requisitionQuantity: 5.123456,
  requisitionOrderedQuantity: 2, requisitionRemainingQuantity: 3.123456 };
const award = { ...manual, id: 92, lineNo: 2, itemId: 8, requisitionLineId: null, awardId: 70, quantity: 4, unitPrice: 12.5, taxRateId: 6,
  taxRatePercent: 10, requisitionId: null, requisitionDocumentNo: null, requisitionLineNo: null, requisitionQuantity: null,
  requisitionOrderedQuantity: null, requisitionRemainingQuantity: null };
const ORDER = { id: 9, documentNo: 'PO-202802-0009', status: 'Rejected', version: 3, supplierId: 5, rfqId: 12, rfqDocumentNo: 'RFQ-12',
  businessDate: '2028-02-29', currency: 'TWD', baseCurrencyCode: 'TWD', exchangeRate: 1, baseNet: 300.3086, baseTax: 17.5154,
  baseGross: 317.824, remark: '首版', lines: [manual, award], approvalHistory: [] };

async function capture(page: Page, name: string): Promise<void> {
  const directory = '../../tests/qa/PUR/screenshots';
  await mkdir(directory, { recursive: true });
  await expect(page.locator('.cdk-overlay-backdrop-showing')).toHaveCount(0);
  await page.waitForTimeout(250);
  await page.screenshot({ path: `${directory}/4.3-${name}.png`, fullPage: true, animations: 'disabled' });
}

async function mockBackend(page: Page): Promise<Backend> {
  const backend: Backend = { puts: [], order: structuredClone(ORDER), next: null, getNext: null };
  await page.route('**/api/v1/pur/purchase-orders**', async route => {
    const request = route.request();
    const url = new URL(request.url());
    const path = url.pathname;
    if (path.endsWith('/create-context')) return route.fulfill({ json: { businessDate: url.searchParams.get('businessDate'),
      currencies: [{ code: 'TWD', name: '新台幣' }], taxRates: [{ id: 3, code: 'VAT5', name: '進項 5%', ratePercent: 5 }] } });
    if (request.method() === 'PUT') {
      backend.puts.push({ version: url.searchParams.get('version'), body: request.postDataJSON() });
      if (backend.next) { const reply = backend.next; backend.next = null; return route.fulfill({ status: reply.status, json: reply.body }); }
      const body = request.postDataJSON();
      backend.order = { ...backend.order, status: 'Draft', version: backend.order.version + 1, remark: body.remark,
        lines: body.lines.map((l: any) => l.lineNo === 2 ? award : { ...manual, lineNo: l.lineNo, quantity: Number(l.quantity) }) };
      return route.fulfill({ json: { ...backend.order, lines: backend.order.lines.map((l: any) => ({ id: l.id, lineNo: l.lineNo })) } });
    }
    if (/\/purchase-orders\/9(\/edit)?$/.test(path)) {
      if (backend.getNext) { const reply = backend.getNext; backend.getNext = null; return route.fulfill({ status: reply.status, json: reply.body }); }
      return route.fulfill({ json: backend.order });
    }
    const o = backend.order;
    return route.fulfill({ json: { items: [{ id: 9, documentNo: o.documentNo, businessDate: o.businessDate, supplierId: 5, status: o.status,
      currency: 'TWD', baseGross: o.baseGross, lineCount: o.lines.length, version: o.version }], totalCount: 1, page: 1, pageSize: 20 } });
  });
  return backend;
}

test('PUR-PO-UI-E11 Revise from the detail saves the rejected order with its version, keeps the award line and returns it to Draft', async ({ page }) => {
  const backend = await mockBackend(page);
  await page.goto('/pur/purchase-orders?id=9');
  await expect(page.getByTestId('pur-order-edit')).toBeVisible();
  await capture(page, 'detail-edit-button-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'detail-edit-button-dark-zh-Hant'); await chooseTheme(page, 'light');
  await page.getByTestId('pur-order-edit').click();
  const form = page.getByTestId('pur-po-form');
  await expect(form.getByTestId('pur-po-form-title')).toHaveText('修訂採購單 PO-202802-0009');
  await expect(form.getByTestId('pur-po-form-rejected')).toContainText('儲存修訂後會回到草稿');
  await expect(form.getByTestId('pur-po-form-award-locked')).toContainText('供應商與幣別沿用決標快照');
  await expect(form.getByTestId('pur-po-form-award-1')).toContainText('物料 #8 · 數量 4 · 單價 12.5 · 稅率 10%');
  await expect(form.getByTestId('pur-po-form-source-detail-0')).toHaveText('PR-0004／第 2 行 · 請購 5.123456，已轉 2，餘量 3.123456');
  await expect(form.getByTestId('pur-po-form-remove-1')).toBeDisabled();
  await capture(page, 'normal-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'normal-dark-zh-Hant'); await chooseTheme(page, 'light');
  await chooseLanguage(page, 'en');
  await expect(form.getByTestId('pur-po-form-title')).toContainText('Revise purchase order');
  await capture(page, 'normal-light-en');
  await chooseLanguage(page, 'zh-Hans');
  await expect(form.getByTestId('pur-po-form-title')).toContainText('修订采购单');
  await capture(page, 'normal-light-zh-Hans');
  await chooseLanguage(page, 'zh-Hant');

  await form.getByTestId('pur-po-form-quantity-0').fill('3');
  await form.getByTestId('pur-po-form-remark').fill('第二版');
  await form.getByTestId('pur-po-form-save').click();
  await expect(page.getByText('採購單 PO-202802-0009 修訂已儲存並回到草稿，共 2 行；請重新送審。')).toBeVisible();
  await expect(page.getByTestId('pur-order-detail-status')).toContainText('草稿');
  expect(backend.puts).toEqual([{ version: '3', body: { supplierId: 5, businessDate: '2028-02-29', remark: '第二版', lines: [
    { lineNo: 1, itemId: 7, requisitionLineId: 44, quantity: '3', unitPrice: '100.123456', currencyCode: 'TWD', taxRateId: 3 },
    { lineNo: 2, itemId: 8, requisitionLineId: null, quantity: '4', unitPrice: '12.5', currencyCode: 'TWD', taxRateId: 6 }] } }]);
  expect(backend.order.status).toBe('Draft');
  expect(backend.order.version).toBe(4);
  await capture(page, 'success-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'success-dark-zh-Hant');
});

test('PUR-PO-UI-E14 revision 400 fields, locked award 422 and 500 retain honest saved state', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of (400|422|500)/);
  pageErrors.allowApi5xx(/\/api\/v1\/pur\/purchase-orders\/9\?version=3$/, 500);
  const backend = await mockBackend(page);
  await page.goto('/pur/purchase-orders/9/edit');
  const form = page.getByTestId('pur-po-form');
  await form.getByTestId('pur-po-form-quantity-0').fill('3');
  backend.next = { status: 400, body: { errorCode: 'COMMON.VALIDATION', errors: { 'lines[0].quantity': ['invalid'], remark: ['invalid'] } } };
  await form.getByTestId('pur-po-form-save').click();
  await expect(form.getByTestId('pur-po-form-server-quantity-0')).toBeVisible();
  await expect(form.getByTestId('pur-po-form-error')).toContainText('採購單未儲存，資料未變更。');
  await expect(form.getByTestId('pur-po-form-quantity-0')).toHaveValue('3');
  expect(backend.order.version).toBe(3);
  await capture(page, '400-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, '400-dark-zh-Hant'); await chooseTheme(page, 'light');

  backend.next = { status: 422, body: { errorCode: 'PUR.ORDER.AWARD_LINE_LOCKED' } };
  await form.getByTestId('pur-po-form-save').click();
  await expect(form.getByTestId('pur-po-form-error')).toContainText('決標行的物料、來源、數量、單價及稅率不可修改');
  await expect(form.getByTestId('pur-po-form-quantity-0')).toHaveValue('2.5');
  expect(backend.order.version).toBe(3);
  await capture(page, 'award-422-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'award-422-dark-zh-Hant'); await chooseTheme(page, 'light');

  await form.getByTestId('pur-po-form-remark').fill('故障前輸入');
  backend.next = { status: 500, body: { traceId: 'po-edit-500' } };
  await form.getByTestId('pur-po-form-save').click();
  await expect(form.getByTestId('pur-po-form-error')).toContainText('po-edit-500');
  await expect(form.getByTestId('pur-po-form-remark')).toHaveValue('故障前輸入');
  expect(backend.order.remark).toBe('首版');
  expect(backend.puts.map(p => p.version)).toEqual(['3', '3', '3']);
  await capture(page, '500-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, '500-dark-zh-Hant');
});

test('PUR-PO-UI-E15 missing revision displays 404 and never offers save', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 404/);
  const backend = await mockBackend(page);
  backend.getNext = { status: 404, body: { errorCode: 'PUR.ORDER.NOT_FOUND' } };
  await page.goto('/pur/purchase-orders/9/edit');
  const form = page.getByTestId('pur-po-form');
  await expect(form.getByTestId('pur-po-form-load-error')).toBeVisible();
  await expect(form.getByTestId('pur-po-form-save')).toHaveCount(0);
  expect(backend.puts).toHaveLength(0);
  await capture(page, '404-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, '404-dark-zh-Hant');
});

test.describe('PUR-PO-UI-E16 EDIT without VIEW', () => {
  test.use({ authorization: { ...E2E_AUTHORIZATION, isFullAccess: false,
    permissions: [{ code: 'PUR.ORDER.EDIT', dataScope: 'Company' }] } });

  test('saves the revision, stays on the edit page and reloads the saved version', async ({ page }) => {
    const backend = await mockBackend(page);
    await page.goto('/pur/purchase-orders/9/edit');
    const form = page.getByTestId('pur-po-form');
    await form.getByTestId('pur-po-form-remark').fill('唯編輯權限修訂');
    await form.getByTestId('pur-po-form-save').click();
    await expect(form.getByTestId('pur-po-form-success')).toContainText('PO-202802-0009');
    await expect(page).toHaveURL(/\/pur\/purchase-orders\/9\/edit$/);
    await expect(form.getByTestId('pur-po-form-remark')).toHaveValue('唯編輯權限修訂');
    expect(backend.order.version).toBe(4);
    expect(backend.order.remark).toBe('唯編輯權限修訂');
    await capture(page, 'no-view-success-light-zh-Hant');
    await chooseTheme(page, 'dark'); await capture(page, 'no-view-success-dark-zh-Hant');
  });
});

test('PUR-PO-UI-E12 409 keeps the input and reloads on confirmation; 422 NOT_EDITABLE explains and shows the order read-only', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of (409|422)/);
  const backend = await mockBackend(page);
  await page.goto('/pur/purchase-orders/9/edit');
  const form = page.getByTestId('pur-po-form');
  await form.getByTestId('pur-po-form-quantity-0').fill('9');
  backend.next = { status: 409, body: { errorCode: 'COMMON.CONCURRENCY.CONFLICT' } };
  await form.getByTestId('pur-po-form-save').click();
  await expect(form.getByTestId('pur-po-form-error')).toContainText('採購單未儲存，資料未變更。');
  await expect(form.getByTestId('pur-po-form-quantity-0')).toHaveValue('9');
  expect(backend.order.version).toBe(3);
  await capture(page, '409-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, '409-dark-zh-Hant'); await chooseTheme(page, 'light');
  await form.getByTestId('pur-po-form-reload').click();
  await page.getByRole('button', { name: '重新載入保存資料' }).last().click();
  await expect(form.getByTestId('pur-po-form-quantity-0')).toHaveValue('2.5');
  await expect(form.getByTestId('pur-po-form-error')).toHaveCount(0);

  backend.next = { status: 422, body: { errorCode: 'PUR.ORDER.NOT_EDITABLE' } };
  backend.order = { ...backend.order, status: 'Approved', version: 5 };
  await form.getByTestId('pur-po-form-save').click();
  await expect(form.getByTestId('pur-po-form-error')).toContainText('只有草稿或已駁回的採購單可修訂');
  await expect(form.getByTestId('pur-po-form-not-editable')).toContainText('此採購單目前為「已核准」');
  await expect(form.getByTestId('pur-po-form-save')).toHaveCount(0);
  expect(backend.puts.map(p => p.version)).toEqual(['3', '3']);
  await capture(page, '422-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, '422-dark-zh-Hant'); await chooseTheme(page, 'light');
  await chooseLanguage(page, 'en');
  await expect(form.getByTestId('pur-po-form-not-editable')).toContainText('This order is Approved.');
  await capture(page, '422-light-en');
  await chooseLanguage(page, 'zh-Hans');
  await expect(form.getByTestId('pur-po-form-not-editable')).toContainText('此采购单目前为“已批准”');
  await capture(page, '422-light-zh-Hans');
});
