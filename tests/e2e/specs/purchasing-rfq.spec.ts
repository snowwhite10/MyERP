import { mkdir } from 'node:fs/promises';
import type { Page } from '@playwright/test';
import { E2E_AUTHORIZATION, chooseLanguage, chooseTheme, expect, test } from '../support/fixtures';

async function capture(page: Page, state: string): Promise<void> {
  const directory = '../../tests/qa/PUR/screenshots';
  await mkdir(directory, { recursive: true });
  await expect(page.locator('.ant-spin-spinning')).toHaveCount(0);
  await page.screenshot({ path: `${directory}/4.11-${state}.png`, fullPage: true });
}

const row = { id: 17, documentNo: 'RFQ-0017', businessDate: '2026-10-20', replyDeadline: '2026-10-31',
  ownerId: 4, status: 'Sent', lineCount: 1, invitationCount: 1, version: 2 };
const detail = { ...row, remark: 'Keep original terms', sentAt: '2026-10-21T09:00:00', createdAt: '2026-10-20T08:00:00',
  lines: [{ id: 170, lineNo: 1, itemId: 11, requisitionLineId: 44, requiredDate: '2026-11-10', quantity: 1.123456, remark: null }],
  invitations: [{ id: 171, supplierId: 7, supplierCodeSnapshot: 'SUP-7', supplierNameSnapshot: 'Saved supplier', sentAt: '2026-10-21T09:00:00' }] };

test('PUR-RFQ-070 filters saved RFQs and clears stale detail on empty and failed results', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 500/);
  pageErrors.allowApi5xx(/\/api\/v1\/pur\/rfqs/, 500);
  const queries: string[] = [];
  await page.route('**/api/v1/pur/rfqs**', route => {
    const url = new URL(route.request().url());
    if (url.pathname.endsWith('/17')) return route.fulfill({ json: detail });
    queries.push(url.search);
    if (url.searchParams.get('keyword') === 'fail') return route.fulfill({ status: 500, json: { traceId: 'rfq-trace' } });
    const items = url.searchParams.get('keyword') === 'missing' ? [] : [row];
    return route.fulfill({ json: { items, totalCount: items.length } });
  });
  await page.goto('/pur/rfqs');
  await expect(page.getByTestId('pur-rfq-row-17')).toContainText('RFQ-0017');
  await capture(page, 'normal-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'normal-dark-zh-Hant');
  await chooseTheme(page, 'light');
  await chooseLanguage(page, 'en'); await capture(page, 'normal-light-en');
  await chooseLanguage(page, 'zh-Hans'); await capture(page, 'normal-light-zh-Hans');
  await chooseLanguage(page, 'zh-Hant');
  await page.getByRole('button', { name: 'RFQ-0017' }).click();
  await expect(page.getByTestId('pur-rfq-detail')).toContainText('Saved supplier');
  await expect(page.getByTestId('pur-rfq-detail')).toContainText('1.123456');
  await expect(page.getByTestId('pur-rfq-detail')).toContainText('2026-10-21 09:00');
  await capture(page, 'detail-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'detail-dark-zh-Hant');
  await chooseTheme(page, 'light');
  await page.getByTestId('pur-rfq-search').fill('missing');
  await page.getByTestId('pur-rfq-filter').click();
  await expect(page.getByTestId('pur-rfq-detail')).toHaveCount(0);
  await expect(page.getByTestId('pur-rfq-table')).toContainText('沒有符合的詢價單');
  await capture(page, 'no-results-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'no-results-dark-zh-Hant');
  await chooseTheme(page, 'light');
  await page.getByTestId('pur-rfq-search').fill('fail');
  await page.getByTestId('pur-rfq-filter').click();
  await expect(page.getByTestId('pur-rfq-error')).toContainText('rfq-trace');
  await capture(page, 'server-error-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'server-error-dark-zh-Hant');
  expect(queries.some(query => query.includes('keyword=missing'))).toBe(true);
});

test('PUR-RFQ-071 rejects inverted date filters before sending a query', async ({ page }) => {
  let requests = 0;
  await page.route('**/api/v1/pur/rfqs**', route => { requests++; return route.fulfill({ json: { items: [row], totalCount: 1 } }); });
  await page.goto('/pur/rfqs');
  await expect(page.getByTestId('pur-rfq-row-17')).toBeVisible();
  const before = requests;
  await page.getByLabel('起始日').fill('2026-10-22');
  await page.getByLabel('截止日').fill('2026-10-21');
  await page.getByTestId('pur-rfq-filter').click();
  await expect(page.getByTestId('pur-rfq-error')).toContainText('起始日不可晚於截止日');
  await expect(page.locator('.filters [role="alert"]')).toContainText('起始日不可晚於截止日');
  await capture(page, 'validation-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'validation-dark-zh-Hant');
  expect(requests).toBe(before);
  await page.getByLabel('起始日').fill('');
  await page.getByLabel('截止日').fill('');
  await page.getByLabel('請購來源').fill('0');
  await page.getByTestId('pur-rfq-filter').click();
  await expect(page.locator('.filters [role="alert"]')).toContainText('請購行 ID 必須是正整數');
  expect(requests).toBe(before);
});

test('PUR-RFQ-072 clears company detail and distinguishes an empty list', async ({ page }) => {
  await page.route('**/api/v1/pur/rfqs**', route => {
    const url = new URL(route.request().url());
    if (url.pathname.endsWith('/17')) return route.fulfill({ json: detail });
    const items = route.request().headers()['x-company-id'] === '2' ? [] : [row];
    return route.fulfill({ json: { items, totalCount: items.length } });
  });
  await page.goto('/pur/rfqs');
  await page.getByRole('button', { name: 'RFQ-0017' }).click();
  await expect(page.getByTestId('pur-rfq-detail')).toBeVisible();
  await page.getByTestId('layout-company-select').click();
  await page.locator('nz-option-item', { hasText: 'HK01 Hong Kong' }).click();
  await page.getByRole('heading', { name: '詢價單' }).click();
  await expect(page.getByTestId('pur-rfq-detail')).toHaveCount(0);
  await expect(page.getByTestId('pur-rfq-table')).toContainText('尚無詢價單');
  await capture(page, 'empty-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'empty-dark-zh-Hant');
});

test('PUR-RFQ-075 restores shareable filters from the URL', async ({ page }) => {
  const queries: string[] = [];
  await page.route('**/api/v1/pur/rfqs**', route => {
    queries.push(new URL(route.request().url()).search);
    return route.fulfill({ json: { items: [row], totalCount: 21 } });
  });
  await page.goto('/pur/rfqs?keyword=RFQ-0017&status=Sent&page=2');
  await expect(page.getByTestId('pur-rfq-search')).toHaveValue('RFQ-0017');
  await expect(page.getByTestId('pur-rfq-row-17')).toBeVisible();
  expect(queries.some(query => query.includes('keyword=RFQ-0017') && query.includes('status=Sent') && query.includes('page=2'))).toBe(true);
  await page.getByTestId('pur-rfq-search').fill('RFQ-NEW');
  await page.getByTestId('pur-rfq-filter').click();
  await expect(page).toHaveURL(/keyword=RFQ-NEW/);
  await page.getByTestId('pur-rfq-reset').click();
  await expect(page.getByTestId('pur-rfq-search')).toHaveValue('');
  await expect(page).not.toHaveURL(/keyword=/);
});

test('PUR-RFQ-072 keeps draft and closed requests read only', async ({ page }) => {
  await page.route('**/api/v1/pur/rfqs**', route => route.fulfill({ json: { items: [
    { ...row, id: 18, documentNo: 'RFQ-0018', status: 'Draft' },
    { ...row, id: 19, documentNo: 'RFQ-0019', status: 'Closed' }
  ], totalCount: 2 } }));
  await page.goto('/pur/rfqs');
  await expect(page.getByTestId('pur-rfq-row-18')).toContainText('草稿');
  await expect(page.getByTestId('pur-rfq-row-19')).toContainText('已關閉');
  await expect(page.locator('[data-testid^="pur-rfq-row-"] button')).toHaveCount(2);
});

test('PUR-RFQ-073 explains network failure and can retry without stale data', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: net::ERR_FAILED/);
  let fail = true;
  await page.route('**/api/v1/pur/rfqs**', route => {
    if (fail) return route.abort('failed');
    return route.fulfill({ json: { items: [row], totalCount: 1 } });
  });
  await page.goto('/pur/rfqs');
  await expect(page.getByTestId('pur-rfq-error')).toContainText('無法連線到伺服器');
  await expect(page.getByTestId('pur-rfq-table')).toContainText('載入失敗');
  await capture(page, 'network-error-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'network-error-dark-zh-Hant');
  fail = false;
  await page.getByTestId('pur-rfq-filter').click();
  await expect(page.getByTestId('pur-rfq-row-17')).toContainText('RFQ-0017');
  await expect(page.getByTestId('pur-rfq-error')).toHaveCount(0);
});

test('PUR-RFQ-074 explains a detail that was removed or left the data scope', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 404/);
  let stale = true;
  await page.route('**/api/v1/pur/rfqs**', route => {
    if (new URL(route.request().url()).pathname.endsWith('/17')) {
      stale = false;
      return route.fulfill({ status: 404, json: { errorCode: 'PUR.RFQ.NOT_FOUND' } });
    }
    return route.fulfill({ json: { items: stale ? [row] : [], totalCount: stale ? 1 : 0 } });
  });
  await page.goto('/pur/rfqs');
  await page.getByRole('button', { name: 'RFQ-0017' }).click();
  await expect(page.getByTestId('pur-rfq-error')).toContainText('找不到這張詢價單');
  await expect(page.getByTestId('pur-rfq-detail')).toHaveCount(0);
  await expect(page.getByTestId('pur-rfq-row-17')).toHaveCount(0);
  await capture(page, 'not-found-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'not-found-dark-zh-Hant');
});

test.describe('restricted authorization', () => {
  test.use({ authorization: { ...E2E_AUTHORIZATION, isFullAccess: false, permissions: [] } });
  test('PUR-RFQ-072 denies users without RFQ VIEW permission', async ({ page }) => {
    await page.goto('/pur/rfqs');
    await expect(page).toHaveURL(/\/forbidden/);
    await capture(page, 'forbidden-light-zh-Hant');
    await chooseTheme(page, 'dark'); await capture(page, 'forbidden-dark-zh-Hant');
  });
});
