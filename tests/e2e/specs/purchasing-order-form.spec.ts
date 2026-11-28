import { mkdir } from 'node:fs/promises';
import type { Page } from '@playwright/test';
import { chooseLanguage, chooseTheme, expect, test } from '../support/fixtures';

/**
 * 介面 4.2 採購單建立（WU-PUR-006f2b，REQ_IO_A4.2.1～3）：模擬後端選項與保存，每個情境同時斷言畫面訊息與
 * 後端實際收到／保存的資料（body 精確十進位文字、保存筆數、拒絕時零保存）。
 */
interface Saved { id: number; documentNo: string; lines: object[] }
interface Backend { posts: Record<string, any>[]; saved: Saved[]; next: { status: number; body: object } | null; contextDates: string[] }

const SAVED = { id: 9, documentNo: 'PO-202802-0001', status: 'Draft', version: 1, supplierId: 5, businessDate: '2028-02-29', currency: 'TWD',
  baseCurrencyCode: 'TWD', exchangeRate: 1, baseNet: 250.3086, baseTax: 12.5154, baseGross: 262.824, remark: '急件',
  lines: [{ id: 90, lineNo: 1, itemId: 7, requisitionLineId: 44, quantity: 2.5, unitPrice: 100.123456, taxRateId: 3, taxRatePercent: 5,
    baseNet: 250.3086, baseTax: 12.5154, baseGross: 262.824 }] };

async function capture(page: Page, name: string): Promise<void> {
  const directory = '../../tests/qa/PUR/screenshots';
  await mkdir(directory, { recursive: true });
  await expect(page.getByTestId('erp-select-dialog')).toHaveCount(0);
  await expect(page.locator('.ant-spin-spinning')).toHaveCount(0);
  await expect(page.locator('.cdk-overlay-backdrop-showing')).toHaveCount(0);
  await page.waitForTimeout(250);
  await page.screenshot({ path: `${directory}/4.2-${name}.png`, fullPage: true, animations: 'disabled' });
}

async function mockBackend(page: Page): Promise<Backend> {
  const backend: Backend = { posts: [], saved: [], next: null, contextDates: [] };
  const page1 = (items: object[]) => ({ items, totalCount: items.length, page: 1, pageSize: 10 });
  await page.route('**/api/v1/pur/purchase-orders**', async route => {
    const request = route.request();
    const url = new URL(request.url());
    const path = url.pathname;
    if (path.endsWith('/supplier-options')) return route.fulfill({ json: page1([{ id: 5, code: 'SUP5', name: '大同五金', isActive: true, isApproved: true }]) });
    if (path.endsWith('/item-options')) return route.fulfill({ json: page1([{ id: 7, code: 'ITM7', name: '不鏽鋼螺絲', isActive: true }]) });
    if (path.endsWith('/requisition-options')) return route.fulfill({ json: page1([{ id: 44, requisitionId: 4, documentNo: 'PR-0004', lineNo: 2,
      itemId: 7, requiredDate: '2028-03-10', quantity: 5.123456, orderedQuantity: 2, remainingQuantity: 3.123456 }]) });
    if (path.endsWith('/create-context')) {
      backend.contextDates.push(url.searchParams.get('businessDate') ?? '');
      return route.fulfill({ json: { businessDate: url.searchParams.get('businessDate'), currencies: [{ code: 'TWD', name: '新台幣' }],
        taxRates: [{ id: 3, code: 'VAT5', name: '進項 5%', ratePercent: 5 }, { id: 4, code: 'ZERO', name: '零稅率', ratePercent: 0 }] } });
    }
    if (request.method() === 'POST') {
      backend.posts.push(request.postDataJSON());
      if (backend.next) { const reply = backend.next; backend.next = null; return route.fulfill({ status: reply.status, json: reply.body }); }
      backend.saved.push(SAVED);
      return route.fulfill({ status: 201, json: SAVED });
    }
    if (/\/purchase-orders\/9$/.test(path)) return route.fulfill({ json: { ...SAVED, rfqId: null, rfqDocumentNo: null, approvalHistory: [],
      lines: SAVED.lines.map(l => ({ ...l, awardId: null, originalNet: l.baseNet, originalTax: l.baseTax, originalGross: l.baseGross,
        requisitionId: 4, requisitionDocumentNo: 'PR-0004', requisitionLineNo: 2, requisitionQuantity: 5.123456, requisitionOrderedQuantity: 2,
        requisitionRemainingQuantity: 3.123456 })) } });
    const items = backend.saved.map(s => ({ id: s.id, documentNo: s.documentNo, businessDate: '2028-02-29', supplierId: 5, status: 'Draft',
      currency: 'TWD', baseGross: 262.824, lineCount: 1, version: 1 }));
    return route.fulfill({ json: { items, totalCount: items.length, page: 1, pageSize: 20 } });
  });
  return backend;
}

async function pick(page: Page, opener: ReturnType<Page['getByTestId']>, rowId: number): Promise<void> {
  await opener.click();
  await page.getByTestId(`erp-select-row-${rowId}`).click();
  await page.getByTestId('erp-select-confirm').click();
  await expect(page.getByTestId('erp-select-dialog')).toHaveCount(0);
}

async function fillValid(page: Page): Promise<void> {
  const form = page.getByTestId('pur-po-form');
  await pick(page, form.getByTestId('pur-po-form-supplier'), 5);
  await form.getByTestId('pur-po-form-date').fill('2028-02-29');
  await expect(form.getByTestId('pur-po-form-currency')).toHaveValue('TWD');
  await pick(page, form.getByTestId('pur-po-form-item-0'), 7);
  await pick(page, form.getByTestId('pur-po-form-source-0'), 44);
  await form.getByTestId('pur-po-form-quantity-0').fill('2.5');
  await form.getByTestId('pur-po-form-price-0').fill('100.123456');
  await form.getByTestId('pur-po-form-tax-0').selectOption({ label: 'VAT5 · 進項 5% (5%)' });
  await form.getByTestId('pur-po-form-remark').fill('急件');
}

test('PUR-PO-UI-E09 New from the list saves one exact draft and opens its saved detail', async ({ page }) => {
  const backend = await mockBackend(page);
  await page.goto('/pur/purchase-orders');
  await expect(page.getByTestId('pur-order-new')).toHaveText('新增採購單');
  await page.getByTestId('pur-order-new').click();
  const form = page.getByTestId('pur-po-form');
  await expect(form.getByRole('heading', { name: '新增採購單' })).toBeVisible();
  await expect(form.getByTestId('pur-po-form-context-hint')).toContainText('請先選擇業務日期');
  await capture(page, 'normal-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'normal-dark-zh-Hant'); await chooseTheme(page, 'light');

  await fillValid(page);
  expect(backend.contextDates).toEqual(['2028-02-29']);
  await expect(form.getByTestId('pur-po-form-source-detail-0')).toHaveText('PR-0004／第 2 行 · 請購 5.123456，已轉 2，餘量 3.123456');
  await form.getByTestId('pur-po-form-quantity-0').fill('3.2');
  await expect(form.getByTestId('pur-po-form-source-over-0')).toContainText('超過請購餘量 3.123456');
  await capture(page, 'filled-over-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'filled-over-dark-zh-Hant'); await chooseTheme(page, 'light');
  await form.getByTestId('pur-po-form-quantity-0').fill('2.5');
  await expect(form.getByTestId('pur-po-form-source-over-0')).toHaveCount(0);

  await form.getByTestId('pur-po-form-save').click();
  await expect(page.getByText('採購單 PO-202802-0001 已儲存為草稿，共 1 行。')).toBeVisible();
  await expect(page.getByTestId('pur-order-detail')).toContainText('PO-202802-0001');
  await expect(page.getByTestId('pur-order-detail')).toContainText('PR-0004');
  expect(backend.posts).toEqual([{ supplierId: 5, businessDate: '2028-02-29', remark: '急件', lines: [{ lineNo: 1, itemId: 7,
    requisitionLineId: 44, quantity: '2.5', unitPrice: '100.123456', currencyCode: 'TWD', taxRateId: 3 }] }]);
  expect(backend.saved).toHaveLength(1);
  await capture(page, 'success-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'success-dark-zh-Hant');
});

test('PUR-PO-UI-E10 validation, 422 and 500 explain why and save nothing', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of (422|500)/);
  pageErrors.allowApi5xx(/\/api\/v1\/pur\/purchase-orders$/, 500);
  const backend = await mockBackend(page);
  await page.goto('/pur/purchase-orders/new');
  const form = page.getByTestId('pur-po-form');
  await form.getByTestId('pur-po-form-save').click();
  await expect(form.getByTestId('pur-po-form-error')).toContainText('部分欄位需要修正，採購單未儲存。');
  await expect(form.getByTestId('pur-po-form-supplier-error')).toHaveText('請選擇供應商。');
  await expect(form.getByTestId('pur-po-form-quantity-error-0')).toContainText('大於 0');
  expect(backend.posts).toHaveLength(0);
  await capture(page, 'validation-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'validation-dark-zh-Hant'); await chooseTheme(page, 'light');

  await fillValid(page);
  backend.next = { status: 422, body: { errorCode: 'MD.SUPPLIER.NOT_APPROVED', traceId: 't-422' } };
  await form.getByTestId('pur-po-form-save').click();
  await expect(form.getByTestId('pur-po-form-error')).toContainText('供應商須啟用並核准後才可用於交易。');
  await expect(form.getByTestId('pur-po-form-server-supplierId')).toHaveText('供應商須啟用並核准後才可用於交易。');
  await expect(form.getByTestId('pur-po-form-error')).toContainText('採購單未儲存，資料未變更。');
  await capture(page, '422-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, '422-dark-zh-Hant'); await chooseTheme(page, 'light');

  backend.next = { status: 500, body: { traceId: 'po-trace-500' } };
  await form.getByTestId('pur-po-form-save').click();
  await expect(form.getByTestId('pur-po-form-error')).toContainText('po-trace-500');
  await expect(form.getByTestId('pur-po-form-quantity-0')).toHaveValue('2.5');
  expect(backend.posts).toHaveLength(2);
  expect(backend.saved).toHaveLength(0);
  await capture(page, '500-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, '500-dark-zh-Hant'); await chooseTheme(page, 'light');

  await chooseLanguage(page, 'en');
  await expect(form.getByRole('heading', { name: 'New purchase order' })).toBeVisible();
  await capture(page, 'filled-light-en');
  await chooseLanguage(page, 'zh-Hans');
  await expect(form.getByRole('heading', { name: '新增采购单' })).toBeVisible();
  await capture(page, 'filled-light-zh-Hans');
});
