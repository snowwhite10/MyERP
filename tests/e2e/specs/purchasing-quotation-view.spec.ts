import { mkdir } from 'node:fs/promises';
import type { Page } from '@playwright/test';
import { E2E_AUTHORIZATION, chooseLanguage, chooseTheme, expect, test } from '../support/fixtures';

const quotation = { id: 31, rfqId: 17, supplierId: 7, status: 'Withdrawn', currentRevisionNo: 2,
  quoteDate: '2026-10-21', validUntil: '2026-10-31', version: 3, isExpired: true };
const detail = { ...quotation, revisions: [
  { id: 41, revisionNo: 1, quoteDate: '2026-10-20', validUntil: '2026-10-25', remark: 'Original offer',
    lines: [{ id: 51, rfqLineId: 170, unitPrice: 1.123456, currencyCode: 'TWD', taxRateId: 9 }] },
  { id: 42, revisionNo: 2, quoteDate: '2026-10-21', validUntil: '2026-10-31', remark: 'Revised offer',
    lines: [{ id: 52, rfqLineId: 170, unitPrice: 2.123456, currencyCode: 'USD', taxRateId: 10 }] },
] };

async function capture(page: Page, state: string): Promise<void> {
  const directory = '../../tests/qa/PUR/screenshots';
  await mkdir(directory, { recursive: true });
  await expect(page.locator('.ant-spin-spinning')).toHaveCount(0);
  await expect(page.locator('.cdk-overlay-backdrop-showing')).toHaveCount(0);
  await page.waitForTimeout(250);
  await page.screenshot({ path: `${directory}/4.14-${state}.png`, fullPage: true, animations: 'disabled' });
}

test('PUR-QUOTE-UI-001 lists saved offers and every immutable revision in three languages and themes',
  async ({ page }) => {
    const requests: string[] = [];
    await page.route('**/api/v1/pur/rfqs/17/quotations**', route => {
      const url = new URL(route.request().url()); requests.push(url.pathname);
      return route.fulfill({ json: url.pathname.endsWith('/31') ? detail : { items: [quotation], totalCount: 1 } });
    });
    await page.goto('/pur/rfqs/17/quotations');
    await expect(page.getByTestId('pur-quotation-row-31')).toContainText('已撤回');
    await expect(page.getByTestId('pur-quotation-row-31')).toContainText('已過期');
    await capture(page, 'normal-light-zh-Hant');
    await chooseTheme(page, 'dark'); await capture(page, 'normal-dark-zh-Hant');
    await chooseTheme(page, 'light');
    await chooseLanguage(page, 'en'); await capture(page, 'normal-light-en');
    await chooseLanguage(page, 'zh-Hans'); await capture(page, 'normal-light-zh-Hans');
    await chooseLanguage(page, 'zh-Hant');
    await page.getByTestId('pur-quotation-row-31').getByRole('button').click();
    const saved = page.getByTestId('pur-quotation-detail');
    await expect(saved).toContainText('Original offer');
    await expect(saved).toContainText('Revised offer');
    await expect(saved).toContainText('1.123456');
    await expect(saved).toContainText('2.123456');
    await expect(saved).toContainText('USD');
    await capture(page, 'detail-light-zh-Hant');
    await chooseTheme(page, 'dark'); await capture(page, 'detail-dark-zh-Hant');
    expect(requests).toEqual(['/api/v1/pur/rfqs/17/quotations', '/api/v1/pur/rfqs/17/quotations/31']);
  });

test('PUR-QUOTE-UI-002 validates filters, shares paging and clears stale detail on empty or failed data',
  async ({ page, pageErrors }) => {
    pageErrors.allow(/Failed to load resource: the server responded with a status of 500/);
    pageErrors.allowApi5xx(/\/api\/v1\/pur\/rfqs\/17\/quotations/, 500);
    const queries: URL[] = [];
    await page.route('**/api/v1/pur/rfqs/17/quotations**', route => {
      const url = new URL(route.request().url());
      if (url.pathname.endsWith('/31')) return route.fulfill({ json: detail });
      queries.push(url);
      if (url.searchParams.get('status') === 'Awarded')
        return route.fulfill({ status: 500, json: { traceId: 'quotation-trace' } });
      const items = url.searchParams.get('supplierId') === '8' ? [] : [quotation];
      return route.fulfill({ json: { items, totalCount: items.length } });
    });
    await page.goto('/pur/rfqs/17/quotations');
    await expect(page.getByTestId('pur-quotation-row-31')).toBeVisible();
    const before = queries.length;
    await page.getByTestId('pur-quotation-supplier').fill('0');
    await page.getByTestId('pur-quotation-search').click();
    await expect(page.getByTestId('pur-quotation-error')).toContainText('正整數');
    await expect(page.getByTestId('pur-quotation-supplier')).toBeFocused();
    await capture(page, 'validation-light-zh-Hant');
    await chooseTheme(page, 'dark'); await capture(page, 'validation-dark-zh-Hant');
    await chooseTheme(page, 'light');
    expect(queries).toHaveLength(before);
    await page.getByTestId('pur-quotation-supplier').fill('8');
    await page.getByTestId('pur-quotation-search').click();
    await expect(page.getByTestId('pur-quotation-table')).toContainText('沒有符合的報價');
    await expect(page).toHaveURL(/supplierId=8/);
    await capture(page, 'no-results-light-zh-Hant');
    await chooseTheme(page, 'dark'); await capture(page, 'no-results-dark-zh-Hant');
    await chooseTheme(page, 'light');
    await page.getByTestId('pur-quotation-supplier').fill('');
    await page.getByTestId('pur-quotation-status').selectOption('Awarded');
    await page.getByTestId('pur-quotation-search').click();
    await expect(page.getByTestId('pur-quotation-error')).toContainText('quotation-trace');
    await expect(page.getByTestId('pur-quotation-detail')).toHaveCount(0);
    await capture(page, 'server-error-light-zh-Hant');
    await chooseTheme(page, 'dark'); await capture(page, 'server-error-dark-zh-Hant');
    expect(queries.at(-1)?.searchParams.get('status')).toBe('Awarded');
  });

test('PUR-QUOTE-UI-002 ignores delayed results and explains a disconnected list',
  async ({ page, pageErrors }) => {
    pageErrors.allow(/Failed to load resource|net::ERR_FAILED/);
    let releaseOld: (() => void) | undefined;
    const oldPending = new Promise<void>(resolve => { releaseOld = resolve; });
    await page.route('**/api/v1/pur/rfqs/17/quotations**', async route => {
      const url = new URL(route.request().url());
      if (url.searchParams.get('status') === 'Active') return route.abort();
      if (url.searchParams.get('supplierId') === '8') {
        await oldPending;
        return route.fulfill({ json: { items: [quotation], totalCount: 1 } });
      }
      return route.fulfill({ json: { items: url.searchParams.get('supplierId') === '9' ? [] : [quotation],
        totalCount: url.searchParams.get('supplierId') === '9' ? 0 : 1 } });
    });
    await page.goto('/pur/rfqs/17/quotations');
    await expect(page.getByTestId('pur-quotation-row-31')).toBeVisible();
    await page.getByTestId('pur-quotation-supplier').fill('8');
    await page.getByTestId('pur-quotation-search').click();
    await page.getByTestId('pur-quotation-supplier').fill('9');
    await page.getByTestId('pur-quotation-search').click();
    await expect(page.getByTestId('pur-quotation-table')).toContainText('沒有符合的報價');
    releaseOld!();
    await expect(page.getByTestId('pur-quotation-row-31')).toHaveCount(0);
    await page.getByTestId('pur-quotation-status').selectOption('Active');
    await page.getByTestId('pur-quotation-search').click();
    await expect(page.getByTestId('pur-quotation-error')).toBeVisible();
    await expect(page.getByTestId('pur-quotation-row-31')).toHaveCount(0);
    await capture(page, 'network-error-light-zh-Hant');
    await chooseTheme(page, 'dark'); await capture(page, 'network-error-dark-zh-Hant');
  });

test('PUR-QUOTE-UI-002 rejects an invalid validity date from a shared URL without querying', async ({ page }) => {
  let requests = 0;
  await page.route('**/api/v1/pur/rfqs/17/quotations**', route => {
    requests++; return route.fulfill({ json: { items: [], totalCount: 0 } });
  });
  await page.goto('/pur/rfqs/17/quotations?validOn=2026-02-30');
  await expect(page.getByTestId('pur-quotation-error')).toBeVisible();
  expect(requests).toBe(0);
});

test.describe('quotation VIEW authorization', () => {
  test.use({ authorization: { ...E2E_AUTHORIZATION, isFullAccess: false,
    permissions: [{ code: 'PUR.RFQ.VIEW', dataScope: 'Company' }] } });
  test('PUR-QUOTE-UI-003 rejects direct navigation without QUOTATION.VIEW', async ({ page }) => {
    let requests = 0;
    await page.route('**/api/v1/pur/rfqs/17/quotations**', route => {
      requests++; return route.fulfill({ json: { items: [quotation], totalCount: 1 } });
    });
    await page.goto('/pur/rfqs/17/quotations');
    await expect(page).toHaveURL(/\/forbidden/);
    expect(requests).toBe(0);
  });
});

test('PUR-QUOTE-UI-003 clears company data and rejects a deleted or out-of-scope detail',
  async ({ page, pageErrors }) => {
    pageErrors.allow(/Failed to load resource: the server responded with a status of 404/);
    let missing = false;
    await page.route('**/api/v1/pur/rfqs/17/quotations**', route => {
      const url = new URL(route.request().url());
      if (url.pathname.endsWith('/31')) return route.fulfill(missing
        ? { status: 404, json: { errorCode: 'PUR.QUOTATION.NOT_FOUND' } } : { json: detail });
      const items = route.request().headers()['x-company-id'] === '2' ? [] : [quotation];
      return route.fulfill({ json: { items, totalCount: items.length } });
    });
    await page.goto('/pur/rfqs/17/quotations');
    await page.getByTestId('pur-quotation-row-31').getByRole('button').click();
    await expect(page.getByTestId('pur-quotation-detail')).toBeVisible();
    await page.getByTestId('layout-company-select').click();
    await page.locator('nz-option-item', { hasText: 'HK01 Hong Kong' }).click();
    await page.getByRole('heading', { name: /供應商報價/ }).click();
    await expect(page.getByTestId('pur-quotation-detail')).toHaveCount(0);
    await expect(page.getByTestId('pur-quotation-table')).toContainText('尚無供應商報價');
    await capture(page, 'empty-light-zh-Hant');
    await chooseTheme(page, 'dark'); await capture(page, 'empty-dark-zh-Hant');
    await chooseTheme(page, 'light');
    await page.getByTestId('layout-company-select').click();
    await page.locator('nz-option-item', { hasText: 'TW01 Taipei' }).click();
    await page.getByRole('heading', { name: /供應商報價/ }).click();
    missing = true;
    await page.getByTestId('pur-quotation-row-31').getByRole('button').click();
    await expect(page.getByTestId('pur-quotation-detail')).toHaveCount(0);
    await expect(page.getByTestId('pur-quotation-error')).toContainText('報價不存在');
    await capture(page, 'not-found-light-zh-Hant');
    await chooseTheme(page, 'dark'); await capture(page, 'not-found-dark-zh-Hant');
  });

test('PUR-QUOTE-UI-003 clears prior company rows even when a draft filter is invalid', async ({ page }) => {
  await page.route('**/api/v1/pur/rfqs/17/quotations**', route =>
    route.fulfill({ json: new URL(route.request().url()).pathname.endsWith('/31')
      ? detail : { items: [quotation], totalCount: 1 } }));
  await page.goto('/pur/rfqs/17/quotations');
  await page.getByTestId('pur-quotation-row-31').getByRole('button').click();
  await expect(page.getByTestId('pur-quotation-detail')).toBeVisible();
  await page.getByTestId('pur-quotation-supplier').fill('0');
  await page.getByTestId('layout-company-select').click();
  await page.locator('nz-option-item', { hasText: 'HK01 Hong Kong' }).click();
  await expect(page.getByTestId('pur-quotation-row-31')).toHaveCount(0);
  await expect(page.getByTestId('pur-quotation-detail')).toHaveCount(0);
  await expect(page.getByTestId('pur-quotation-error')).toContainText('正整數');
});

test('PUR-QUOTE-UI-004 opens the authorized quotation view from saved RFQ detail', async ({ page }) => {
  const rfq = { id: 17, documentNo: 'RFQ-0017', businessDate: '2026-10-20', replyDeadline: '2026-10-31',
    ownerId: 4, status: 'Sent', lineCount: 1, invitationCount: 1, version: 2,
    remark: null, sentAt: '2026-10-21T09:00:00', createdAt: '2026-10-20T08:00:00',
    lines: [{ id: 170, lineNo: 1, itemId: 11, requisitionLineId: null,
      requiredDate: '2026-11-10', quantity: 1, remark: null }],
    invitations: [{ id: 171, supplierId: 7, supplierCodeSnapshot: 'SUP-7',
      supplierNameSnapshot: 'Saved supplier', sentAt: '2026-10-21T09:00:00' }] };
  await page.route('**/api/v1/pur/rfqs**', route => {
    const url = new URL(route.request().url());
    if (url.pathname.endsWith('/17')) return route.fulfill({ json: rfq });
    if (url.pathname.endsWith('/quotations')) return route.fulfill({ json: { items: [quotation], totalCount: 1 } });
    return route.fulfill({ json: { items: [rfq], totalCount: 1 } });
  });
  await page.goto('/pur/rfqs');
  await page.getByRole('button', { name: 'RFQ-0017' }).click();
  await expect(page.getByTestId('pur-rfq-quotations')).toBeVisible();
  await page.screenshot({ path: '../../tests/qa/PUR/screenshots/4.11-quotation-entry-light-zh-Hant.png', fullPage: true });
  await chooseTheme(page, 'dark');
  await page.screenshot({ path: '../../tests/qa/PUR/screenshots/4.11-quotation-entry-dark-zh-Hant.png', fullPage: true });
  await page.getByTestId('pur-rfq-quotations').click();
  await expect(page).toHaveURL(/\/pur\/rfqs\/17\/quotations/);
  await expect(page.getByTestId('pur-quotation-row-31')).toBeVisible();
});
