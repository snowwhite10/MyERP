import { mkdir } from 'node:fs/promises';
import { chooseLanguage, chooseTheme, expect, test } from '../support/fixtures';

/** WU-SD-005b2b: the order form looks up the dated price rule; the rule price equals the price the test keeps, so no override. */
async function mockQuote(page: import('@playwright/test').Page, unitPrice: number): Promise<void> {
  await page.route('**/api/v1/sal/price-rules/quote**', route => route.fulfill({ json: { asOf: '2028-02-29', ruleId: 5,
    ruleVersion: 1, itemId: 9, customerId: null, currencyCode: 'TWD', unitPrice, discountCeiling: 10,
    validFrom: '2028-01-01', validTo: '2028-12-31' } }));
}

/** WU-SD-003c5c：訂單 View / Create / Edit 串連 QA，斷言畫面數字與模擬伺服器實際保存的資料一致。 */
async function capture(page: import('@playwright/test').Page, name: string): Promise<void> {
  const path = '../../tests/qa/SD/screenshots';
  await mkdir(path, { recursive: true });
  await page.screenshot({ path: `${path}/${name}.png`, fullPage: true, animations: 'disabled' });
}


/** 表單頁可能已捲動，先回頂部再切回 zh-Hant 淺色，避免頂欄下拉點空。 */
async function resetLocale(page: import('@playwright/test').Page): Promise<void> {
  await page.evaluate(() => window.scrollTo(0, 0));
  await chooseLanguage(page, 'zh-Hant');
  await chooseTheme(page, 'light');
}
interface StoredLine { id: number; lineNo: number; itemId: number; quantity: number; unitPrice: number;
  discountRate: number; taxRateId: number }

test('SD-ORDER-QA-001 created and edited order shows the same saved values on list, detail and edit', async ({ page }) => {
  test.setTimeout(180_000);
  const store: { order: null | { version: number; remark: string; lines: StoredLine[] } } = { order: null };
  const writes: string[] = [];
  const detail = () => {
    const order = store.order!;
    const lines = order.lines.map(line => {
      const net = Math.round(line.quantity * line.unitPrice * 100) / 100;
      const tax = Math.round(net * 5) / 100;
      return { ...line, itemCode: 'I-9', itemName: 'Item Nine', taxRatePercent: 5,
        deliveredQuantity: 0, pendingQuantity: 0, remainingQuantity: line.quantity,
        originalNet: net, originalTax: tax, originalGross: net + tax };
    });
    const sum = (key: 'originalNet' | 'originalTax' | 'originalGross') => lines.reduce((total, line) => total + line[key], 0);
    return { id: 101, documentNo: 'SO-0101', customerId: 7, customerCode: 'C-7', customerName: 'Customer Seven',
      businessDate: '2028-02-29', status: 'Draft', version: order.version, currencyCode: 'TWD', baseCurrencyCode: 'TWD',
      exchangeRate: 1, sourceQuotationId: null, remark: order.remark, approvalInstanceId: null, approvalHistory: [],
      originalNet: sum('originalNet'), originalTax: sum('originalTax'), originalGross: sum('originalGross'),
      baseNet: sum('originalNet'), baseTax: sum('originalTax'), baseGross: sum('originalGross'), lines };
  };
  const store_ = (draft: { remark: string; lines: Omit<StoredLine, 'id'>[] }, version: number) => {
    store.order = { version, remark: draft.remark, lines: draft.lines.map((line, index) => ({ ...line, id: index + 1 })) };
  };
  await page.route('**/api/v1/sal/document-options/customers**', route => route.fulfill({ json: {
    items: [{ id: 7, code: 'C-7', name: 'Customer Seven', isActive: true, reviewStatus: 'Approved' }], totalCount: 1 } }));
  await page.route('**/api/v1/sal/document-options/items**', route => route.fulfill({ json: {
    items: [{ id: 9, code: 'I-9', name: 'Item Nine', isActive: true }], totalCount: 1 } }));
  await page.route('**/api/v1/sal/document-options/output-tax-rates**', route => route.fulfill({ json: { items:
    [{ id: 4, code: 'OUT', name: 'Output VAT', ratePercent: 5 }], totalCount: 1, page: 1, pageSize: 20 } }));
  await page.route('**/api/v1/sal/orders**', route => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    if (request.method() === 'POST') {
      writes.push('POST'); store_(JSON.parse(request.postData() ?? '{}'), 1);
      return route.fulfill({ status: 201, json: detail() });
    }
    if (request.method() === 'PUT') {
      const body = JSON.parse(request.postData() ?? '{}');
      writes.push(`PUT v${body.version}`);
      if (body.version !== store.order!.version) return route.fulfill({ status: 409, json: { errorCode: 'COMMON.CONCURRENCY.CONFLICT' } });
      store_(body.draft, store.order!.version + 1);
      return route.fulfill({ json: detail() });
    }
    if (path.endsWith('/101') || path.endsWith('/101/created')) return route.fulfill({ json: detail() });
    const items = store.order ? [detail()] : [];
    return route.fulfill({ json: { items, totalCount: items.length, page: 1, pageSize: 20 } });
  });

  await mockQuote(page, 12.5);
  await page.goto('/sal/orders/new');
  await page.getByTestId('sd-order-create-customer-pick').click();
  await page.getByText('C-7', { exact: true }).last().click();
  await page.getByTestId('erp-select-confirm').click();
  await page.getByTestId('sd-order-create-date').fill('2028-02-29');
  await page.getByTestId('sd-order-create-remark').fill('QA first save');
  await page.getByTestId('sd-order-create-item-pick-0').click();
  await page.getByText('I-9', { exact: true }).last().click();
  await page.getByTestId('erp-select-confirm').click();
  await page.getByTestId('sd-order-create-quantity-0').fill('100');
  await page.getByTestId('sd-order-create-price-0').fill('12.5');
  await page.getByTestId('sd-order-create-tax-pick-0').click();
  await page.getByText('OUT', { exact: true }).last().click();
  await page.getByTestId('erp-select-confirm').click();
  await page.getByTestId('sd-order-create-save').click();
  await expect(page.getByTestId('sd-order-create-success')).toContainText('SO-0101');
  await expect(page.getByTestId('sd-order-create-receipt')).toContainText('Customer Seven');
  expect(store.order!.lines).toEqual([{ id: 1, lineNo: 1, itemId: 9, quantity: 100, unitPrice: 12.5, discountRate: 0, taxRateId: 4,
    pricingRuleId: 5, pricingRuleVersion: 1 }]);
  for (const language of ['en', 'zh-Hans'] as const) {
    await chooseLanguage(page, language);
    await expect(page.getByTestId('sd-order-create-success')).not.toContainText(/orderCreate\.|undefined/);
    await chooseTheme(page, 'light'); await capture(page, `2.5-success-light-${language}`);
    await chooseTheme(page, 'dark'); await capture(page, `2.5-success-dark-${language}`);
  }
  await resetLocale(page);

  // View：列表與明細顯示伺服器保存的值（1250 + 5% = 1312.5）。
  await page.goto('/sal/orders');
  const row = page.getByTestId('sd-order-row-101');
  await expect(row).toContainText('SO-0101');
  await expect(row).toContainText('Customer Seven');
  await expect(row).toContainText('2028-02-29');
  await expect(row).toContainText('草稿');
  await expect(row).toContainText('1,312.50');
  await row.getByRole('button').click();
  await expect(page.getByTestId('sd-order-detail')).toContainText('QA first save');
  await expect(page.getByTestId('sd-order-line-1')).toContainText('I-9 · Item Nine');
  await expect(page.getByTestId('sd-order-line-1')).toContainText('100');
  await expect(page.getByTestId('sd-order-line-1')).toContainText('1,312.50');
  await expect(page.getByTestId('sd-order-detail')).toContainText('1,312.50');
  for (const language of ['en', 'zh-Hans'] as const) {
    await chooseLanguage(page, language);
    await expect(page.getByTestId('sd-order-detail')).not.toContainText(/sd\.order|undefined/);
    await chooseTheme(page, 'light'); await capture(page, `2.4-qa-detail-light-${language}`);
    await chooseTheme(page, 'dark'); await capture(page, `2.4-qa-detail-dark-${language}`);
  }
  await resetLocale(page);

  // Edit：從列表進入，表單載入保存值；改數量與備註後保存，列表與明細同步更新。
  await page.getByTestId('sd-order-edit-101').click();
  await expect(page.getByTestId('sd-order-create-remark')).toHaveValue('QA first save');
  await expect(page.getByTestId('sd-order-create-quantity-0')).toHaveValue('100');
  await expect(page.getByTestId('sd-order-create-price-0')).toHaveValue('12.5');
  await chooseTheme(page, 'dark');
  for (const language of ['en', 'zh-Hans'] as const) {
    await chooseLanguage(page, language);
    await capture(page, `2.6-normal-dark-${language}`);
  }
  await resetLocale(page);
  await page.getByTestId('sd-order-create-quantity-0').fill('120');
  await page.getByTestId('sd-order-create-remark').fill('QA second save');
  await page.getByTestId('sd-order-create-save').click();
  await expect(page.getByTestId('sd-order-create-success')).toContainText('SO-0101');
  expect(writes).toEqual(['POST', 'PUT v1']);
  expect(store.order).toMatchObject({ version: 2, remark: 'QA second save', lines: [{ quantity: 120, unitPrice: 12.5 }] });
  for (const language of ['en', 'zh-Hans'] as const) {
    await chooseLanguage(page, language);
    await expect(page.getByTestId('sd-order-create-success')).not.toContainText(/orderEdit\.|orderCreate\.|undefined/);
    await chooseTheme(page, 'light'); await capture(page, `2.6-success-light-${language}`);
    await chooseTheme(page, 'dark'); await capture(page, `2.6-success-dark-${language}`);
  }
  await resetLocale(page);

  // 1500 + 5% = 1575；版本 2。
  await page.goto('/sal/orders');
  await expect(page.getByTestId('sd-order-row-101')).toContainText('1,575.00');
  await page.getByTestId('sd-order-row-101').getByRole('button').click();
  await expect(page.getByTestId('sd-order-detail')).toContainText('QA second save');
  await expect(page.getByTestId('sd-order-line-1')).toContainText('120');
  await expect(page.getByTestId('sd-order-detail')).toContainText('1,575.00');
  await chooseTheme(page, 'dark');
  for (const language of ['en', 'zh-Hans'] as const) {
    await chooseLanguage(page, language);
    await capture(page, `2.4-normal-dark-${language}`);
  }
  await resetLocale(page);
  // Create 頁 zh-Hans／en 深色正常狀態。
  await page.goto('/sal/orders/new');
  await expect(page.getByTestId('sd-order-create')).toBeVisible();
  await chooseTheme(page, 'dark');
  for (const language of ['en', 'zh-Hans'] as const) {
    await chooseLanguage(page, language);
    await capture(page, `2.5-normal-dark-${language}`);
  }
  expect(writes).toEqual(['POST', 'PUT v1']);
});
