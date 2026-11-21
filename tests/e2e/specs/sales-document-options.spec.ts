import { mkdir } from 'node:fs/promises';
import type { Page } from '@playwright/test';
import { chooseLanguage, chooseTheme, expect, test } from '../support/fixtures';

// WU-SD-FORM-OPTIONS (ADR-167): quotation and order pickers read the SD document-option endpoints (no MD view needed);
// the tax picker is one dialog of rates effective on the document date and is disabled, with a reason, until a date
// is entered. Screenshots: tests/qa/SD/screenshots/2.2-docopt-*, 2.5-docopt-*.

async function capture(page: Page, name: string): Promise<void> {
  const path = '../../tests/qa/SD/screenshots';
  await mkdir(path, { recursive: true });
  await page.screenshot({ path: `${path}/${name}.png`, fullPage: true, animations: 'disabled' });
}

/** Theme menus sit behind an open modal: open the dialog in each theme, capture, cancel; ends in light. */
async function dialogInBothThemes(page: Page, open: () => Promise<void>, name: string): Promise<void> {
  for (const theme of ['light', 'dark'] as const) {
    await chooseTheme(page, theme);
    await open();
    await capture(page, `${name}-${theme}-zh-Hant`);
    await page.getByTestId('erp-select-cancel').click();
    await expect(page.getByTestId('erp-select-dialog')).toHaveCount(0);
  }
  await chooseTheme(page, 'light');
}

/** Records every master-data request (none may happen) and every option request URL. */
async function mockOptions(page: Page, taxStatus = 200): Promise<{ md: string[]; taxUrls: URL[] }> {
  const seen = { md: [] as string[], taxUrls: [] as URL[] };
  page.on('request', request => { if (request.url().includes('/api/v1/md/')) seen.md.push(request.url()); });
  await page.route('**/api/v1/sal/document-options/customers**', route => route.fulfill({ json: {
    items: [{ id: 7, code: 'C-7', name: 'Customer Seven' }], totalCount: 1, page: 1, pageSize: 20 } }));
  await page.route('**/api/v1/sal/document-options/items**', route => route.fulfill({ json: {
    items: [{ id: 9, code: 'I-9', name: 'Item Nine' }], totalCount: 1, page: 1, pageSize: 20 } }));
  await page.route('**/api/v1/sal/document-options/output-tax-rates**', route => {
    seen.taxUrls.push(new URL(route.request().url()));
    if (taxStatus !== 200) return route.fulfill({ status: taxStatus, json: { status: taxStatus, title: 'Server error',
      errorCode: 'COMMON.INTERNAL', traceId: 'trace-docopt-500' } });
    const empty = route.request().url().includes('businessDate=1999-01-01');
    return route.fulfill({ json: { items: empty ? [] : [
      { id: 4, code: 'OUT', name: 'Output VAT', ratePercent: 5 },
      { id: 6, code: 'ZERO', name: 'Zero-rated export', ratePercent: 0 }], totalCount: empty ? 0 : 2, page: 1, pageSize: 20 } });
  });
  await page.route('**/api/v1/sal/price-rules/quote**', route => route.fulfill({ json: { asOf: '2028-02-29', ruleId: 5,
    ruleVersion: 1, itemId: 9, customerId: null, currencyCode: 'TWD', unitPrice: 10, discountCeiling: 0,
    validFrom: '2028-01-01', validTo: '2028-12-31' } }));
  return seen;
}

test('SD-DOCOPT-UI-101 quotation: tax picker waits for the date, then lists the rates of that date in one dialog', async ({ page }) => {
  const seen = await mockOptions(page);
  await page.goto('/sal/quotations/new');
  await expect(page.getByTestId('sd-quote-create-tax-pick-0')).toBeDisabled();
  await expect(page.getByTestId('sd-quote-create-tax-hint-0')).toHaveText('請先輸入單據日期，才能選擇該日有效的稅率。');
  await capture(page, '2.2-docopt-tax-needs-date-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, '2.2-docopt-tax-needs-date-dark-zh-Hant');
  await chooseTheme(page, 'light');

  await page.getByTestId('sd-quote-create-customer-pick').click();
  await expect(page.getByTestId('erp-select-description')).toHaveText('只列出已啟用且已核准的客戶。');
  await page.getByText('C-7', { exact: true }).last().click();
  await page.getByTestId('erp-select-confirm').click();
  await expect(page.getByTestId('sd-quote-create-customer')).toHaveText('C-7 · Customer Seven');
  await page.getByTestId('sd-quote-create-item-pick-0').click();
  await expect(page.getByTestId('erp-select-description')).toHaveText('只列出已啟用的物料。');
  await page.getByText('I-9', { exact: true }).last().click();
  await page.getByTestId('erp-select-confirm').click();

  await page.getByTestId('sd-quote-create-date').fill('2028-02-29');
  await expect(page.getByTestId('sd-quote-create-tax-hint-0')).toHaveCount(0);
  const openTax = async () => {
    await page.getByTestId('sd-quote-create-tax-pick-0').click();
    await expect(page.getByTestId('erp-select-description')).toHaveText('只列出在單據日期有效的銷項稅率。');
    await expect(page.getByTestId('erp-select-table')).toContainText('Zero-rated export');
    await expect(page.getByTestId('erp-select-table')).toContainText('5%');
    expect(seen.taxUrls.at(-1)!.searchParams.get('businessDate')).toBe('2028-02-29');
  };
  await dialogInBothThemes(page, openTax, '2.2-docopt-tax-dialog');
  await openTax();
  await page.getByText('OUT', { exact: true }).last().click();
  await page.getByTestId('erp-select-confirm').click();
  await expect(page.getByTestId('erp-select-dialog')).toHaveCount(0);
  await expect(page.getByText('OUT · 5%', { exact: true })).toBeVisible();
  expect(seen.md, 'the quotation form never calls MD endpoints').toEqual([]);

  await chooseLanguage(page, 'en');
  await page.getByTestId('sd-quote-create-date').fill('');
  await expect(page.getByTestId('sd-quote-create-tax-hint-0')).toHaveText(
    'Enter the document date first to choose a tax rate effective on that date.');
  await capture(page, '2.2-docopt-tax-needs-date-light-en');
  await chooseLanguage(page, 'zh-Hans');
  await expect(page.getByTestId('sd-quote-create-tax-hint-0')).toHaveText('请先输入单据日期，才能选择该日有效的税率。');
  await capture(page, '2.2-docopt-tax-needs-date-light-zh-Hans');
  await chooseLanguage(page, 'zh-Hant');
});

test('SD-DOCOPT-UI-102 order: no rate on the date is an empty list and nothing is chosen', async ({ page }) => {
  const seen = await mockOptions(page);
  await page.goto('/sal/orders/new');
  await expect(page.getByTestId('sd-order-create-tax-pick-0')).toBeDisabled();
  await expect(page.getByTestId('sd-order-create-tax-hint-0')).toBeVisible();
  await page.getByTestId('sd-order-create-date').fill('1999-01-01');
  await dialogInBothThemes(page, async () => {
    await page.getByTestId('sd-order-create-tax-pick-0').click();
    await expect(page.getByTestId('erp-select-table')).toContainText('暫無資料');
    await expect(page.getByTestId('erp-select-confirm')).toBeDisabled();
  }, '2.5-docopt-tax-empty');
  await expect(page.getByText('尚未選擇稅率').first()).toBeVisible();
  expect(seen.md).toEqual([]);
});

test('SD-DOCOPT-UI-103 order: the tax option list failing with 500 is explained with its trace ID', async ({ page, pageErrors }) => {
  pageErrors.allow(/status of 500/);
  pageErrors.allowApi5xx(/\/api\/v1\/sal\/document-options\/output-tax-rates/, 500);
  await mockOptions(page, 500);
  await page.goto('/sal/orders/new');
  await page.getByTestId('sd-order-create-date').fill('2028-02-29');
  await dialogInBothThemes(page, async () => {
    await page.getByTestId('sd-order-create-tax-pick-0').click();
    await expect(page.getByTestId('erp-select-error')).toBeVisible();
    await expect(page.getByTestId('erp-select-trace')).toContainText('trace-docopt-500');
  }, '2.5-docopt-tax-500');
});
