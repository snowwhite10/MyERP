import { mkdir } from 'node:fs/promises';
import { chooseLanguage, chooseTheme, expect, test } from '../support/fixtures';

/** WU-SD-006c3a (ADR-174): read-only credit preview of the saved order on Create 2.5 / Edit 2.6 (REQ_IO_A2.5.21, A2.6.18). */
type Page = import('@playwright/test').Page;

async function capture(page: Page, name: string): Promise<void> {
  const path = '../../tests/qa/SD/screenshots';
  await mkdir(path, { recursive: true });
  await page.screenshot({ path: `${path}/${name}.png`, fullPage: true, animations: 'disabled' });
}

const money = (value: number) => value.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 4 });
const credit = (orderId: number, overrides: object = {}) => ({ customerId: 7, orderId, status: 'WithinLimit', reasonCode: null,
  evaluationDate: '2028-02-29', baseCurrency: 'TWD', limit: 5000, postedOpenAr: 1200, otherCommitments: 800, candidate: 105,
  headroom: 2895, excess: null, limitVersion: 3, requiresRecheck: true, ...overrides });

test('SD-ORDER-CREDIT-E2E-001 create: credit preview appears only after the saved receipt and matches the saved order', async ({ page }) => {
  const checks: string[] = [];
  const saved = { id: 81, documentNo: 'SO-0081', status: 'Draft', customerId: 7, customerCode: 'C-7', customerName: 'Customer Seven',
    businessDate: '2028-02-29', currencyCode: 'TWD', sourceQuotationId: null,
    lines: [{ id: 1, lineNo: 1, itemId: 9, itemCode: 'I-9', itemName: 'Item Nine' }] };
  let writes = 0;
  await page.route('**/api/v1/sal/document-options/customers**', route => route.fulfill({ json: {
    items: [{ id: 7, code: 'C-7', name: 'Customer Seven', isActive: true, reviewStatus: 'Approved' }], totalCount: 1 } }));
  await page.route('**/api/v1/sal/document-options/items**', route => route.fulfill({ json: {
    items: [{ id: 9, code: 'I-9', name: 'Item Nine', isActive: true }], totalCount: 1 } }));
  await page.route('**/api/v1/sal/document-options/output-tax-rates**', route => route.fulfill({ json: { items:
    [{ id: 4, code: 'OUT', name: 'Output VAT', ratePercent: 5 }], totalCount: 1, page: 1, pageSize: 20 } }));
  await page.route('**/api/v1/sal/price-rules/quote**', route => route.fulfill({ json: { asOf: '2028-02-29', ruleId: 5,
    ruleVersion: 1, itemId: 9, customerId: null, currencyCode: 'TWD', unitPrice: 100, discountCeiling: 10,
    validFrom: '2028-01-01', validTo: '2028-12-31' } }));
  await page.route('**/api/v1/sal/orders**', route => {
    if (route.request().method() === 'POST') { writes++; return route.fulfill({ status: 201, json: saved }); }
    if (route.request().url().endsWith('/81/created')) return route.fulfill({ json: saved });
    return route.fulfill({ json: { items: [], totalCount: 0 } });
  });
  await page.route('**/api/v1/sal/credit-checks/**', route => { checks.push(route.request().url()); return route.fulfill({ json: credit(81) }); });

  await page.goto('/sal/orders/new');
  await expect(page.getByTestId('sd-order-create')).toBeVisible();
  await expect(page.getByTestId('sd-order-saved-credit')).toHaveCount(0);
  await page.getByTestId('sd-order-create-customer-pick').click();
  await page.getByText('C-7', { exact: true }).last().click();
  await page.getByTestId('erp-select-confirm').click();
  await page.getByTestId('sd-order-create-item-pick-0').click();
  await page.getByText('I-9', { exact: true }).last().click();
  await page.getByTestId('erp-select-confirm').click();
  await page.getByTestId('sd-order-create-date').fill('2028-02-29');
  await page.getByTestId('sd-order-create-tax-pick-0').click();
  await page.getByText('OUT', { exact: true }).last().click();
  await page.getByTestId('erp-select-confirm').click();
  await expect(page.getByTestId('sd-order-create-price-0')).toHaveValue('100');
  expect(checks).toEqual([]);
  await page.getByTestId('sd-order-create-save').click();
  await expect(page.getByTestId('sd-order-create-success')).toContainText('SO-0081');

  const panel = page.getByTestId('sd-order-saved-credit');
  await expect(panel).toContainText('SO-0081');
  await expect(panel.getByTestId('sd-order-credit-status')).toHaveText('額度內');
  await expect(panel).toContainText('僅供預覽已保存的版本');
  expect(writes).toBe(1);
  expect(checks.map(url => new URL(url).pathname + new URL(url).search)).toEqual(['/api/v1/sal/credit-checks/7?orderId=81']);
  await capture(page, '2.5-credit-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, '2.5-credit-dark-zh-Hant');
  await chooseTheme(page, 'light'); await chooseLanguage(page, 'en');
  await expect(panel).toContainText('Credit preview for saved order SO-0081');
  await expect(panel.getByTestId('sd-order-credit-limit')).toHaveText(money(5000));
  await expect(panel.getByTestId('sd-order-credit-headroom')).toHaveText(money(2895));
  await expect(panel).not.toContainText(/[一-鿿]|sd\.order\./);
  await capture(page, '2.5-credit-light-en');
  await chooseTheme(page, 'dark'); await capture(page, '2.5-credit-dark-en');
});

test('SD-ORDER-CREDIT-E2E-002 edit: over-limit preview with excess; recheck 500 shows the trace ID', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 500/);
  pageErrors.allowApi5xx(/\/api\/v1\/sal\/credit-checks\/7\?orderId=84$/, 500);
  let version = 1;
  let checks = 0;
  const saved = () => ({ id: 84, documentNo: 'SO-0084', status: 'Draft', version, customerId: 7, customerCode: 'C-7',
    customerName: 'Customer Seven', businessDate: '2028-02-29', currencyCode: 'TWD', sourceQuotationId: null,
    remark: version === 1 ? 'Original' : 'Bigger order', lines: [{ id: 1, lineNo: 1, itemId: 9, itemCode: 'I-9',
      itemName: 'Item Nine', quantity: 1, unitPrice: 2, discountRate: 0, taxRateId: 4, deliveredQuantity: 0, pendingQuantity: 0 }] });
  await page.route('**/api/v1/sal/orders/84', route => {
    if (route.request().method() === 'PUT') version++;
    return route.fulfill({ json: saved() });
  });
  await page.route('**/api/v1/sal/credit-checks/7?orderId=84', route => {
    checks++;
    return checks === 1
      ? route.fulfill({ json: credit(84, { status: 'OverLimit', reasonCode: 'SAL.CREDIT.LIMIT_EXCEEDED', candidate: 3200,
        headroom: -305.5, excess: 305.5 }) })
      : route.fulfill({ status: 500, json: { errorCode: 'COMMON.UNEXPECTED', traceId: 'trace-credit-panel-500' } });
  });
  await page.goto('/sal/orders/84/edit');
  await expect(page.getByTestId('sd-order-create-remark')).toHaveValue('Original');
  await expect(page.getByTestId('sd-order-saved-credit')).toHaveCount(0);
  await page.getByTestId('sd-order-create-remark').fill('Bigger order');
  await page.getByTestId('sd-order-create-save').click();
  await expect(page.getByTestId('sd-order-create-success')).toContainText('SO-0084');
  const panel = page.getByTestId('sd-order-saved-credit');
  await expect(panel.getByTestId('sd-order-credit-status')).toHaveText('超出額度');
  await expect(panel.getByTestId('sd-order-saved-credit-excess')).toContainText('305.50 TWD');
  await expect(panel.getByTestId('sd-order-saved-credit-override-hint')).toContainText('信用覆核理由');
  expect({ version, checks }).toEqual({ version: 2, checks: 1 });
  await capture(page, '2.6-credit-over-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, '2.6-credit-over-dark-zh-Hant');
  await chooseTheme(page, 'light'); await chooseLanguage(page, 'zh-Hans');
  await expect(panel.getByTestId('sd-order-saved-credit-excess')).toContainText('本订单超出可用信用');
  await capture(page, '2.6-credit-over-light-zh-Hans');
  await chooseLanguage(page, 'zh-Hant');

  await panel.getByTestId('sd-order-saved-credit-recheck').click();
  await expect(panel.getByTestId('sd-order-saved-credit-error')).toContainText('trace-credit-panel-500');
  await expect(panel.getByTestId('sd-order-saved-credit-error-hint')).toContainText('訂單本身已保存');
  await expect(panel.getByTestId('sd-order-credit-status')).toHaveCount(0);
  await expect(panel.getByTestId('sd-order-saved-credit-notice')).toHaveCount(0);
  await expect(page.getByTestId('sd-order-create-success')).toBeVisible();
  expect({ version, checks }).toEqual({ version: 2, checks: 2 });
  await capture(page, '2.6-credit-error-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, '2.6-credit-error-dark-zh-Hant');
  await chooseTheme(page, 'light'); await chooseLanguage(page, 'en');
  await expect(panel.getByTestId('sd-order-saved-credit-error-hint')).toContainText('The order itself is saved');
  await capture(page, '2.6-credit-error-light-en');
});
