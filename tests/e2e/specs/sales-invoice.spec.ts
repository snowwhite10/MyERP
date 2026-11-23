import { mkdir } from 'node:fs/promises';
import { Page, Route } from '@playwright/test';
import { chooseLanguage, chooseTheme, E2E_AUTHORIZATION, expect, test } from '../support/fixtures';

// Sales Invoice View Interface 2.14 and Issue Interface 2.15 (WU-SD-007b3a, ADR-182): mocked API flows.
const summary = { id: 5, documentNo: 'SI-0005', orderId: 81, orderDocumentNo: 'SO-0081', customerId: 7,
  customerName: 'Saved customer', businessDate: '2026-11-23', currencyCode: 'USD', baseCurrencyCode: 'TWD',
  originalGross: 1050.0001, baseGross: 32550.0031, postedAt: '2026-11-23T14:30:12', fiJournalId: 901, lineCount: 2,
  version: 2 };
const sourceSummary = { id: 81, documentNo: 'SO-0081', customerId: 7, customerCode: 'C-7', customerName: 'Saved customer',
  businessDate: '2026-11-18', currencyCode: 'USD', openLineCount: 2, version: 4 };
const source = { id: 81, documentNo: 'SO-0081', customerId: 7, customerCode: 'C-7', customerName: 'Saved customer',
  businessDate: '2026-11-18', currencyCode: 'USD', baseCurrencyCode: 'TWD', exchangeRate: 31.00000001, version: 4,
  lines: [
    { deliveryId: 61, deliveryDocumentNo: 'DH-0061', deliveryDate: '2026-11-20', deliveryLineId: 71, deliveryLineNo: 1,
      orderLineId: 91, orderLineNo: 1, itemId: 9, itemCode: 'ITEM-9', itemName: 'Lot item', taxRatePercent: 5,
      postedQuantity: 12.000001, invoicedQuantity: 2, remainingQuantity: 10.000001 },
    { deliveryId: 62, deliveryDocumentNo: 'DH-0062', deliveryDate: '2026-11-22', deliveryLineId: 72, deliveryLineNo: 1,
      orderLineId: 92, orderLineNo: 2, itemId: 10, itemCode: 'ITEM-10', itemName: 'Plain item', taxRatePercent: 0,
      postedQuantity: 2, invoicedQuantity: 0, remainingQuantity: 2 }] };

interface IssueBody { requestKey: string; businessDate: string; lines: { lineNo: number; deliveryLineId: number; quantity: number }[] }

/** The saved invoice for an ISSUE body: echoes the key, date, lines and exact quantities the form checks (ADR-179). */
function detailFor(body: IssueBody, raw: string) {
  const quantities = [...raw.matchAll(/"quantity":([\d.]+)/g)].map(match => match[1]);
  return `{"id":5,"documentNo":"SI-0005","requestKey":${JSON.stringify(body.requestKey)},"orderId":81,"orderDocumentNo":"SO-0081",` +
    `"customerId":7,"customerCode":"C-7","customerName":"Saved customer","businessDate":${JSON.stringify(body.businessDate)},` +
    `"currencyCode":"USD","baseCurrencyCode":"TWD","exchangeRate":31.00000001,"status":"Posted","postedAt":"2026-11-23T14:30:12",` +
    `"fiSalesSourceId":301,"fiReceivableId":401,"fiJournalId":901,"originalNet":1000.0001,"originalTax":50,"originalGross":1050.0001,` +
    `"baseNet":31000.0031,"baseTax":1550,"baseGross":32550.0031,"version":2,"lines":[` +
    body.lines.map((line, index) => `{"id":${110 + index},"lineNo":${line.lineNo},"deliveryId":${60 + index + 1},` +
      `"deliveryDocumentNo":"DH-006${index + 1}","deliveryLineId":${line.deliveryLineId},"orderLineId":${91 + index},` +
      `"orderLineNo":${index + 1},"itemId":${9 + index},"itemCode":"ITEM-${9 + index}","itemName":"Item ${index + 1}",` +
      `"taxRateId":${index + 1},"taxRatePercent":${index ? 0 : 5},"quantity":${quantities[index]},"originalNet":500,` +
      `"originalTax":25,"originalGross":525,"baseNet":15500,"baseTax":775,"baseGross":16275}`).join(',') + ']}';
}

const savedDetail = detailFor({ requestKey: 'saved-key', businessDate: '2026-11-23', lines: [
  { lineNo: 1, deliveryLineId: 71, quantity: 10.000001 }, { lineNo: 2, deliveryLineId: 72, quantity: 2 }] },
'"quantity":10.000001,"quantity":2');

async function capture(page: Page, name: string): Promise<void> {
  const path = '../../tests/qa/SD/screenshots';
  await mkdir(path, { recursive: true });
  await page.screenshot({ path: `${path}/${name}.png`, fullPage: true, animations: 'disabled' });
}

interface InvoiceApi { lists: URL[]; posts: { raw: string; body: IssueBody }[]; sourceReads: number;
  issue: (route: Route, raw: string, body: IssueBody) => Promise<void>; current: typeof source; issued: boolean }

/** One handler for the invoice resource, dispatching by path (list, detail, source orders, ISSUE). */
async function routeInvoices(page: Page): Promise<InvoiceApi> {
  const api: InvoiceApi = { lists: [], posts: [], sourceReads: 0, current: source, issued: false,
    issue: (route, raw, body) => { api.issued = true;
      return route.fulfill({ status: 201, contentType: 'application/json', body: detailFor(body, raw) }); } };
  await page.route('**/api/v1/sal/invoices**', route => {
    const request = route.request(); const url = new URL(request.url());
    if (request.method() === 'POST') {
      const raw = request.postData() ?? ''; const body = JSON.parse(raw) as IssueBody;
      api.posts.push({ raw, body });
      return api.issue(route, raw, body);
    }
    if (url.pathname.endsWith('/source-orders')) return route.fulfill({ json: { items: [sourceSummary], totalCount: 1, page: 1, pageSize: 10 } });
    if (url.pathname.endsWith('/source-orders/81')) { api.sourceReads++; return route.fulfill({ json: api.current }); }
    if (url.pathname.endsWith('/invoices/5')) return route.fulfill({ contentType: 'application/json', body: savedDetail });
    api.lists.push(url);
    const items = api.issued ? [summary] : [];
    return route.fulfill({ json: { items, totalCount: items.length, page: 1, pageSize: 20 } });
  });
  return api;
}

async function chooseOrder(page: Page): Promise<void> {
  await page.getByTestId('sd-invoice-issue-order-pick').click();
  await expect(page.getByTestId('erp-select-description')).toContainText('只列出已核准');
  await page.getByText('SO-0081', { exact: true }).last().click();
  await page.getByTestId('erp-select-confirm').click();
  await expect(page.getByTestId('sd-invoice-issue-order')).toHaveText('SO-0081 · C-7 · Saved customer');
}

test('SD-INV-E2E-001 lists saved invoices and shows the posted detail with FI references in three languages and two themes', async ({ page }) => {
  const api = await routeInvoices(page); api.issued = true;
  await page.goto('/sal/invoices');
  const row = page.getByTestId('sd-invoice-row-5');
  await expect(row).toContainText('SI-0005');
  await expect(row).toContainText('USD 1,050.0001');
  await expect(row).toContainText('TWD 32,550.0031');
  await expect(row).toContainText('901');
  await expect(page.getByTestId('sd-invoice-notice')).toContainText('無法修改');
  await capture(page, '2.14-normal-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, '2.14-normal-dark-zh-Hant');
  await chooseTheme(page, 'light'); await chooseLanguage(page, 'en');
  await expect(page.getByTestId('sd-invoice-table')).toContainText('Invoice number');
  await capture(page, '2.14-normal-light-en');
  await chooseLanguage(page, 'zh-Hans'); await capture(page, '2.14-normal-light-zh-Hans');
  await chooseLanguage(page, 'zh-Hant');

  await page.getByTestId('sd-invoice-open-5').click();
  const detail = page.getByTestId('sd-invoice-detail');
  await expect(page.getByTestId('sd-invoice-detail-status')).toHaveText('已過帳');
  await expect(page.getByTestId('sd-invoice-detail-customer')).toHaveText('C-7 · Saved customer');
  await expect(page.getByTestId('sd-invoice-detail-rate')).toHaveText('31.00000001');
  await expect(page.getByTestId('sd-invoice-fi-journal')).toHaveText('901');
  await expect(page.getByTestId('sd-invoice-fi-receivable')).toHaveText('401');
  await expect(page.getByTestId('sd-invoice-fi-source')).toHaveText('301');
  await expect(page.getByTestId('sd-invoice-totals')).toContainText('TWD 32,550.0031');
  await expect(page.getByTestId('sd-invoice-line-110')).toContainText('10.000001');
  await expect(detail).toContainText('2026-11-23 14:30');
  await capture(page, '2.14-detail-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, '2.14-detail-dark-zh-Hant');
  await chooseLanguage(page, 'en');
  await expect(page.getByTestId('sd-invoice-detail-status')).toHaveText('Posted');
  await capture(page, '2.14-detail-dark-en');
  await chooseLanguage(page, 'zh-Hans'); await capture(page, '2.14-detail-dark-zh-Hans');

  // Customer chip filter is written to the URL and sent to the server.
  await chooseLanguage(page, 'zh-Hant'); await chooseTheme(page, 'light');
  await page.getByTestId('sd-invoice-filter-customer-5').click();
  await expect(page).toHaveURL(/customerId=7/);
  expect(api.lists.at(-1)?.searchParams.get('customerId')).toBe('7');
  await expect(page.getByTestId('sd-invoice-customer-filter')).toContainText('Saved customer');
});

test('SD-INV-E2E-002 separates empty, no-result, invalid dates, missing invoice and server error with trace and retry', async ({ page, pageErrors }) => {
  pageErrors.allowApi5xx(/\/api\/v1\/sal\/invoices/, 500);
  pageErrors.allow(/Failed to load resource: the server responded with a status of (404|500)/);
  let mode: 'empty' | 'saved' | 'failure' = 'empty';
  const lists: URL[] = [];
  await page.route('**/api/v1/sal/invoices**', route => {
    const url = new URL(route.request().url());
    if (url.pathname.endsWith('/5')) return route.fulfill({ status: 404, json: { errorCode: 'SAL.INVOICE.NOT_FOUND' } });
    lists.push(url);
    if (mode === 'failure') return route.fulfill({ status: 500, json: { traceId: 'trace-invoice-500' } });
    const items = mode === 'saved' && !url.searchParams.get('keyword') ? [summary] : [];
    return route.fulfill({ json: { items, totalCount: items.length, page: 1, pageSize: 20 } });
  });
  await page.goto('/sal/invoices');
  await expect(page.getByTestId('sd-invoice-table')).toContainText('尚無發票');
  await capture(page, '2.14-empty-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, '2.14-empty-dark-zh-Hant');

  mode = 'saved';
  await page.getByTestId('sd-invoice-keyword').fill('SI-404');
  await page.getByTestId('sd-invoice-search').click();
  await expect(page.getByTestId('sd-invoice-table')).toContainText('沒有符合篩選條件的發票');
  await expect(page).toHaveURL(/keyword=SI-404/);
  await capture(page, '2.14-no-results-dark-zh-Hant');

  const reads = lists.length;
  await page.getByTestId('sd-invoice-keyword').fill('');
  await page.getByTestId('sd-invoice-from').fill('2026-11-30');
  await page.getByTestId('sd-invoice-to').fill('2026-11-01');
  await page.getByTestId('sd-invoice-search').click();
  await expect(page.getByTestId('sd-invoice-validation')).toContainText('開始日期不可晚於結束日期');
  expect(lists.length).toBe(reads);
  await capture(page, '2.14-invalid-dates-dark-zh-Hant');

  await page.getByTestId('sd-invoice-reset').click();
  await page.getByTestId('sd-invoice-open-5').click();
  await expect(page.getByTestId('sd-invoice-detail-error')).toContainText('找不到發票');
  await expect(page.getByTestId('sd-invoice-detail')).toHaveCount(0);
  await capture(page, '2.14-detail-404-dark-zh-Hant');

  mode = 'failure';
  await page.getByTestId('sd-invoice-search').click();
  await expect(page.getByTestId('sd-invoice-error')).toContainText('trace-invoice-500');
  await expect(page.getByTestId('sd-invoice-table')).toContainText('無法載入發票');
  await expect(page.getByTestId('sd-invoice-table')).not.toContainText('SI-0005');
  await capture(page, '2.14-server-error-dark-zh-Hant');
  await chooseTheme(page, 'light'); await capture(page, '2.14-server-error-light-zh-Hant');
  mode = 'saved';
  await page.getByTestId('sd-invoice-retry').click();
  await expect(page.getByTestId('sd-invoice-row-5')).toBeVisible();
});

test('SD-INV-E2E-003 issues an invoice from the list, sends exact quantities and opens the posted detail', async ({ page }) => {
  const api = await routeInvoices(page);
  await page.goto('/sal/invoices');
  await page.getByTestId('sd-invoice-new').click();
  await expect(page).toHaveURL(/\/sal\/invoices\/new$/);
  await expect(page.getByTestId('sd-invoice-issue-intro')).toContainText('立即過帳到應收帳款與總帳');
  await capture(page, '2.15-empty-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, '2.15-empty-dark-zh-Hant'); await chooseTheme(page, 'light');

  await chooseOrder(page);
  await expect(page.getByTestId('sd-invoice-issue-rate')).toHaveText('31.00000001');
  await expect(page.getByTestId('sd-invoice-issue-remaining-71')).toHaveText('10.000001');
  await expect(page.getByTestId('sd-invoice-issue-quantity-71')).toHaveValue('10.000001');
  await expect(page.getByTestId('sd-invoice-issue-count')).toHaveText('已選 2 / 2 行');
  await capture(page, '2.15-normal-light-zh-Hant');
  await chooseLanguage(page, 'en'); await expect(page.getByTestId('sd-invoice-issue-count')).toHaveText('2 of 2 lines selected');
  await capture(page, '2.15-normal-light-en');
  await chooseLanguage(page, 'zh-Hans'); await capture(page, '2.15-normal-light-zh-Hans');
  await chooseTheme(page, 'dark'); await capture(page, '2.15-normal-dark-zh-Hans');
  await chooseTheme(page, 'light'); await chooseLanguage(page, 'zh-Hant');

  // Client validation: date before the latest chosen outbound date, quantity above the remaining one.
  await page.getByTestId('sd-invoice-issue-date').fill('2026-11-21');
  await page.getByTestId('sd-invoice-issue-quantity-72').fill('2.5');
  await page.getByTestId('sd-invoice-issue-submit').click();
  await expect(page.getByTestId('sd-invoice-issue-date-error')).toContainText('2026-11-22');
  await expect(page.getByTestId('sd-invoice-issue-quantity-error-72')).toContainText('不可超過未開票數量 2');
  await expect(page.getByTestId('sd-invoice-issue-errors')).toContainText('2 個問題');
  await expect(page.getByTestId('sd-invoice-issue-date')).toBeFocused();
  expect(api.posts).toHaveLength(0);
  await capture(page, '2.15-validation-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, '2.15-validation-dark-zh-Hant'); await chooseTheme(page, 'light');

  await page.getByTestId('sd-invoice-issue-date').fill('2026-11-23');
  await page.getByTestId('sd-invoice-issue-quantity-72').fill('2');
  await page.getByTestId('sd-invoice-issue-submit').click();
  await expect(page.getByTestId('sd-invoice-detail-title')).toHaveText('SI-0005');
  await expect(page).toHaveURL(/\/sal\/invoices$/); // `?open=5` opens the detail once and is then dropped
  await expect(page.getByText('發票 SI-0005 已開立並過帳。')).toBeVisible();
  await expect(page.getByTestId('sd-invoice-detail-title')).toBeFocused();
  await expect(page.getByTestId('sd-invoice-fi-journal')).toHaveText('901');
  await expect(page.getByTestId('sd-invoice-row-5')).toContainText('SI-0005');
  await capture(page, '2.15-success-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, '2.15-success-dark-zh-Hant');

  // Exactly one POST: exact decimal literal, no amount/customer/state fields, a UUID request key.
  expect(api.posts).toHaveLength(1);
  const { raw, body } = api.posts[0];
  expect(raw).toContain('"quantity":10.000001');
  expect(body.requestKey).toMatch(/^[0-9a-f-]{36}$/);
  expect(body.businessDate).toBe('2026-11-23');
  expect(body.lines).toEqual([{ lineNo: 1, deliveryLineId: 71, quantity: 10.000001 }, { lineNo: 2, deliveryLineId: 72, quantity: 2 }]);
  expect(Object.keys(body).sort()).toEqual(['businessDate', 'lines', 'requestKey']);
});

test('SD-INV-E2E-004 marks the line of a 422 BALANCE_EXCEEDED, reloads the balance and retries with a new key', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 422/);
  const api = await routeInvoices(page);
  api.issue = (route, raw, body) => {
    if (api.posts.length === 1) return route.fulfill({ status: 422, contentType: 'application/problem+json',
      body: JSON.stringify({ status: 422, errorCode: 'SAL.INVOICE.BALANCE_EXCEEDED', lineNo: 1, remaining: 4.5 }) });
    api.issued = true;
    return route.fulfill({ status: 201, contentType: 'application/json', body: detailFor(body, raw) });
  };
  await page.goto('/sal/invoices/new');
  await chooseOrder(page);
  await page.getByTestId('sd-invoice-issue-date').fill('2026-11-23');
  await page.getByTestId('sd-invoice-issue-submit').click();
  await expect(page.getByTestId('sd-invoice-issue-error')).toContainText('數量超過出貨明細的未開票數量');
  await expect(page.getByTestId('sd-invoice-issue-line-error-71')).toHaveText('此行最多只可再開票 4.5。');
  await expect(page.getByTestId('sd-invoice-issue-quantity-71')).toBeFocused();
  await expect(page).toHaveURL(/\/sal\/invoices\/new$/);
  await capture(page, '2.15-balance-exceeded-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, '2.15-balance-exceeded-dark-zh-Hant');
  await chooseLanguage(page, 'en');
  await expect(page.getByTestId('sd-invoice-issue-line-error-71')).toHaveText('Only 4.5 can still be invoiced on this line.');
  await capture(page, '2.15-balance-exceeded-dark-en');
  await chooseLanguage(page, 'zh-Hant'); await chooseTheme(page, 'light');

  // Another invoice took 5.500001 meanwhile: reload keeps the selection and caps the client check at the new balance.
  api.current = { ...source, lines: [{ ...source.lines[0], invoicedQuantity: 7.500001, remainingQuantity: 4.5 }, source.lines[1]] };
  await page.getByTestId('sd-invoice-issue-reload').click();
  await expect(page.getByTestId('sd-invoice-issue-remaining-71')).toHaveText('4.5');
  await expect(page.getByTestId('sd-invoice-issue-error')).toHaveCount(0);
  expect(api.sourceReads).toBe(2);
  await page.getByTestId('sd-invoice-issue-submit').click();
  await expect(page.getByTestId('sd-invoice-issue-quantity-error-71')).toContainText('4.5');
  expect(api.posts).toHaveLength(1);
  await page.getByTestId('sd-invoice-issue-quantity-71').fill('4.5');
  await page.getByTestId('sd-invoice-issue-submit').click();
  await expect(page.getByTestId('sd-invoice-detail-title')).toHaveText('SI-0005');
  await expect(page).toHaveURL(/\/sal\/invoices$/); // `?open=5` opens the detail once and is then dropped
  expect(api.posts).toHaveLength(2);
  expect(api.posts[1].body.requestKey).not.toBe(api.posts[0].body.requestKey);
  expect(api.posts[1].raw).toContain('"quantity":4.5');
});

test('SD-INV-E2E-005 locks the form after a network failure and resends the same request, which replays without reposting', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: net::ERR_FAILED/);
  const api = await routeInvoices(page);
  api.issue = (route, raw, body) => {
    if (api.posts.length === 1) return route.abort('failed');
    api.issued = true;
    return route.fulfill({ status: 200, contentType: 'application/json', body: detailFor(body, raw) });
  };
  await page.goto('/sal/invoices/new');
  await chooseOrder(page);
  await page.getByTestId('sd-invoice-issue-date').fill('2026-11-23');
  await page.getByTestId('sd-invoice-issue-submit').click();
  await expect(page.getByTestId('sd-invoice-issue-uncertain')).toContainText('發票可能已開立');
  await expect(page.getByTestId('sd-invoice-issue-submit')).toBeDisabled();
  await expect(page.getByTestId('sd-invoice-issue-order-pick')).toBeDisabled();
  await expect(page.getByTestId('sd-invoice-issue-quantity-71')).toBeDisabled();
  await expect(page.getByTestId('sd-invoice-issue-check')).toBeVisible();
  await capture(page, '2.15-uncertain-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, '2.15-uncertain-dark-zh-Hant');
  await chooseLanguage(page, 'en'); await expect(page.getByTestId('sd-invoice-issue-resend')).toHaveText(/Resend safely/);
  await capture(page, '2.15-uncertain-dark-en');
  await chooseLanguage(page, 'zh-Hant'); await chooseTheme(page, 'light');

  await page.getByTestId('sd-invoice-issue-resend').click();
  await expect(page.getByTestId('sd-invoice-detail-title')).toHaveText('SI-0005');
  await expect(page).toHaveURL(/\/sal\/invoices$/); // `?open=5` opens the detail once and is then dropped
  await expect(page.getByText('此請求先前已開立發票 SI-0005，本次沒有重複過帳。')).toBeVisible();
  expect(api.posts).toHaveLength(2);
  expect(api.posts[1].raw).toBe(api.posts[0].raw);
});

test.describe('issue without view permission', () => {
  test.use({ authorization: { ...E2E_AUTHORIZATION, isFullAccess: false,
    permissions: [{ code: 'SAL.INVOICE.ISSUE', dataScope: 'Company' }] } });

  test('SD-INV-E2E-006 keeps the receipt on the page when the user cannot view invoices', async ({ page }) => {
    const api = await routeInvoices(page);
    await page.goto('/sal/invoices/new');
    await expect(page.getByTestId('sd-invoice-issue-back')).toHaveCount(0);
    await chooseOrder(page);
    await page.getByTestId('sd-invoice-issue-date').fill('2026-11-23');
    await page.getByTestId('sd-invoice-issue-submit').click();
    await expect(page.getByTestId('sd-invoice-issue-success')).toContainText('發票 SI-0005 已開立並過帳。');
    await expect(page.getByTestId('sd-invoice-issue-receipt')).toHaveText('SI-0005 · SO-0081 · USD 1,050.0001 · TWD 32,550.0031');
    await expect(page).toHaveURL(/\/sal\/invoices\/new$/);
    expect(api.lists).toHaveLength(0);
    await capture(page, '2.15-receipt-no-view-light-zh-Hant');
    await chooseTheme(page, 'dark'); await capture(page, '2.15-receipt-no-view-dark-zh-Hant');
    await page.getByTestId('sd-invoice-issue-another').click();
    await expect(page.getByTestId('sd-invoice-issue-order')).toHaveText('尚未選擇訂單');
  });
});
