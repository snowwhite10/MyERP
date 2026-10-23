import { mkdir, readFile } from 'node:fs/promises';
import { chooseLanguage, chooseTheme, E2E_AUTHORIZATION, expect, test } from '../support/fixtures';

const OUT = '../../tests/qa/PUR/screenshots';
const row = (id: number) => ({ id, documentNo: `RFQ-${String(id).padStart(4, '0')}`,
  businessDate: '2024-02-29', replyDeadline: '2024-03-31', ownerId: 4,
  status: 'Sent', lineCount: 1, invitationCount: 1, version: 2 });
const detail = (id: number) => ({ ...row(id), remark: null, sentAt: '2024-02-29T10:00:00',
  createdAt: '2024-02-29T09:00:00', lines: [{ id: id * 10, lineNo: 1, itemId: 11,
    requisitionLineId: null, requiredDate: '2024-03-31', quantity: 1.123456, remark: '=SUM(1)' }],
  invitations: [{ id: id * 10 + 1, supplierId: 7, supplierCodeSnapshot: '@SUP',
    supplierNameSnapshot: 'Saved supplier', sentAt: '2024-02-29T10:00:00' }] });

async function capture(page: import('@playwright/test').Page, name: string): Promise<void> {
  await mkdir(OUT, { recursive: true });
  await page.screenshot({ path: `${OUT}/4.11-export-${name}.png`, fullPage: true });
}

test('PUR-RFQ-092/098 exports all filtered saved lines with safe text and exact count', async ({ page }) => {
  const pages: number[] = [];
  await page.route('**/api/v1/pur/rfqs**', route => {
    const url = new URL(route.request().url());
    const match = url.pathname.match(/\/rfqs\/(\d+)$/);
    if (match) return route.fulfill({ json: detail(Number(match[1])) });
    const number = Number(url.searchParams.get('page') ?? 1);
    pages.push(number);
    const size = Number(url.searchParams.get('pageSize') ?? 20);
    const start = (number - 1) * size + 1;
    return route.fulfill({ json: { items: Array.from({ length: Math.max(0, Math.min(size, 201 - start + 1)) },
      (_, index) => row(start + index)), totalCount: 201 } });
  });
  await page.goto('/pur/rfqs');
  await expect(page.getByTestId('pur-rfq-row-1')).toBeVisible();
  const download = page.waitForEvent('download');
  await page.getByTestId('pur-rfq-export').click();
  const csv = await readFile((await (await download).path())!, 'utf8');
  await expect(page.getByTestId('pur-rfq-notice')).toContainText('201 行');
  expect(csv.startsWith('\uFEFF')).toBe(true);
  expect(csv.match(/"RFQ-\d{4}"/g)).toHaveLength(201);
  expect(csv).toContain('"1.123456"');
  expect(csv).toContain('"\'=SUM(1)"');
  expect(csv).toContain('"\'@SUP"');
  expect(pages).toEqual(expect.arrayContaining([1, 2]));
  await capture(page, 'saved-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'saved-dark-zh-Hant');
  await chooseTheme(page, 'light'); await chooseLanguage(page, 'en'); await capture(page, 'saved-light-en');
  await chooseLanguage(page, 'zh-Hans'); await capture(page, 'saved-light-zh-Hans');
});

test('PUR-RFQ-093/095 warns for zero rows and rejects oversize without a download', async ({ page }) => {
  let downloads = 0; page.on('download', () => downloads++);
  let oversized = false;
  await page.route('**/api/v1/pur/rfqs**', route => {
    const url = new URL(route.request().url());
    if (url.pathname.endsWith('/1')) return route.fulfill({ json: { ...detail(1),
      lines: oversized ? Array.from({ length: 10001 }, (_, index) => ({ ...detail(1).lines[0], id: index + 1 })) : [] } });
    return route.fulfill({ json: { items: [row(1)], totalCount: 1 } });
  });
  await page.goto('/pur/rfqs');
  await page.getByTestId('pur-rfq-export').click();
  await expect(page.getByTestId('pur-rfq-notice')).toContainText('沒有已保存明細');
  expect(downloads).toBe(0);
  await capture(page, 'zero-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'zero-dark-zh-Hant');
  oversized = true;
  await page.getByTestId('pur-rfq-filter').click();
  await expect(page.getByTestId('pur-rfq-export')).toBeEnabled();
  await page.getByTestId('pur-rfq-export').click();
  await expect(page.getByTestId('pur-rfq-error')).toContainText('10,000 行上限');
  expect(downloads).toBe(0);
  await chooseTheme(page, 'light'); await capture(page, 'limit-light-zh-Hant');
  await chooseTheme(page, 'dark');
  await capture(page, 'limit-dark-zh-Hant');
});

test('PUR-RFQ-094/095 cancels a pending export and rejects a stale saved version', async ({ page }) => {
  let downloads = 0; page.on('download', () => downloads++);
  let stale = false;
  await page.route('**/api/v1/pur/rfqs**', async route => {
    const url = new URL(route.request().url());
    if (url.pathname.endsWith('/1')) {
      if (!stale) await new Promise(resolve => setTimeout(resolve, 900));
      return route.fulfill({ json: { ...detail(1), version: stale ? 3 : 2 } }).catch(() => {});
    }
    return route.fulfill({ json: { items: [row(1)], totalCount: 1 } });
  });
  await page.goto('/pur/rfqs');
  await page.getByTestId('pur-rfq-export').click();
  await expect(page.getByTestId('pur-rfq-cancel-export')).toBeVisible();
  await page.getByTestId('pur-rfq-cancel-export').click();
  await expect(page.getByTestId('pur-rfq-notice')).toContainText('沒有下載檔案');
  stale = true;
  await page.getByTestId('pur-rfq-export').click();
  await expect(page.getByTestId('pur-rfq-error')).toContainText('資料已變更');
  expect(downloads).toBe(0);
  await capture(page, 'changed-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'changed-dark-zh-Hant');
});

test('PUR-RFQ-094 rejects duplicated IDs across pages without a partial download', async ({ page }) => {
  let downloads = 0; page.on('download', () => downloads++);
  await page.route('**/api/v1/pur/rfqs**', route => route.fulfill({ json: {
    items: [row(1)], totalCount: 2 } }));
  await page.goto('/pur/rfqs');
  await page.getByTestId('pur-rfq-export').click();
  await expect(page.getByTestId('pur-rfq-error')).toContainText('資料已變更');
  expect(downloads).toBe(0);
});

test('PUR-RFQ-094 changing applied filters cancels pending saved reads', async ({ page }) => {
  let downloads = 0; page.on('download', () => downloads++);
  await page.route('**/api/v1/pur/rfqs**', async route => {
    const url = new URL(route.request().url());
    if (url.pathname.endsWith('/1')) {
      await new Promise(resolve => setTimeout(resolve, 900));
      return route.fulfill({ json: detail(1) }).catch(() => {});
    }
    return route.fulfill({ json: url.searchParams.get('keyword') === 'missing'
      ? { items: [], totalCount: 0 } : { items: [row(1)], totalCount: 1 } });
  });
  await page.goto('/pur/rfqs');
  await page.getByTestId('pur-rfq-export').click();
  await expect(page.getByTestId('pur-rfq-cancel-export')).toBeVisible();
  await page.getByTestId('pur-rfq-search').fill('missing');
  await page.getByTestId('pur-rfq-filter').click();
  await expect(page.getByTestId('pur-rfq-table')).toContainText('沒有符合');
  await expect(page.getByTestId('pur-rfq-cancel-export')).toHaveCount(0);
  expect(downloads).toBe(0);
});

test('PUR-RFQ-094 leaving the page cancels a pending export', async ({ page }) => {
  let downloads = 0; page.on('download', () => downloads++);
  await page.route('**/api/v1/pur/rfqs**', async route => {
    const url = new URL(route.request().url());
    if (url.pathname.endsWith('/1')) {
      await new Promise(resolve => setTimeout(resolve, 900));
      return route.fulfill({ json: detail(1) }).catch(() => {});
    }
    return route.fulfill({ json: { items: [row(1)], totalCount: 1 } });
  });
  await page.goto('/pur/rfqs');
  await page.getByTestId('pur-rfq-export').click();
  await expect(page.getByTestId('pur-rfq-cancel-export')).toBeVisible();
  await page.goto('/');
  await expect(page.getByTestId('pur-rfq-cancel-export')).toHaveCount(0);
  await page.waitForTimeout(1000);
  expect(downloads).toBe(0);
});

test('PUR-RFQ-095/097 reports a detail 500 trace and allows retry with no partial file', async ({ page, pageErrors }) => {
  pageErrors.allowApi5xx(/\/api\/v1\/pur\/rfqs\/1$/, 500);
  pageErrors.allow(/Failed to load resource: the server responded with a status of 500/);
  let fail = true; let downloads = 0; page.on('download', () => downloads++);
  await page.route('**/api/v1/pur/rfqs**', route => {
    const url = new URL(route.request().url());
    if (url.pathname.endsWith('/1')) return fail
      ? route.fulfill({ status: 500, json: { errorCode: 'COMMON.SERVER.ERROR', traceId: '00-rfq-export' } })
      : route.fulfill({ json: detail(1) });
    return route.fulfill({ json: { items: [row(1)], totalCount: 1 } });
  });
  await page.goto('/pur/rfqs');
  await page.getByTestId('pur-rfq-export').click();
  await expect(page.getByTestId('pur-rfq-error')).toContainText('00-rfq-export');
  expect(downloads).toBe(0);
  await capture(page, 'server-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'server-dark-zh-Hant');
  fail = false;
  const download = page.waitForEvent('download');
  await page.getByTestId('pur-rfq-export').click();
  await download;
  await expect(page.getByTestId('pur-rfq-notice')).toContainText('1 行');
  expect(downloads).toBe(1);
});

test('PUR-RFQ-096 a VIEW scope denial stops export and gives an access reason', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 403/);
  let downloads = 0; page.on('download', () => downloads++);
  await page.route('**/api/v1/pur/rfqs**', route => {
    const url = new URL(route.request().url());
    if (url.pathname.endsWith('/1')) return route.fulfill({ status: 403,
      json: { errorCode: 'COMMON.PERMISSION.DENIED' } });
    return route.fulfill({ json: { items: [row(1)], totalCount: 1 } });
  });
  await page.goto('/pur/rfqs');
  await page.getByTestId('pur-rfq-export').click();
  await expect(page.getByTestId('pur-rfq-error')).toContainText('權限');
  expect(downloads).toBe(0);
  await capture(page, 'denied-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'denied-dark-zh-Hant');
});

test('PUR-RFQ-096 hides export without EXPORT permission and print reloads saved detail', async ({ page }) => {
  let reads = 0; let prints = 0;
  await page.route('**/api/v1/pur/rfqs**', route => {
    const url = new URL(route.request().url());
    if (url.pathname.endsWith('/1')) { reads++; return route.fulfill({ json: detail(1) }); }
    return route.fulfill({ json: { items: [row(1)], totalCount: 1 } });
  });
  await page.goto('/pur/rfqs');
  await page.getByRole('button', { name: 'RFQ-0001' }).click();
  await page.evaluate(() => { window.print = () => { (window as Window & { __prints?: number }).__prints =
    ((window as Window & { __prints?: number }).__prints ?? 0) + 1; }; });
  await page.getByTestId('pur-rfq-print').click();
  await expect.poll(() => page.evaluate(() => (window as Window & { __prints?: number }).__prints ?? 0)).toBe(1);
  prints = await page.evaluate(() => (window as Window & { __prints?: number }).__prints ?? 0);
  expect(reads).toBe(2); expect(prints).toBe(1);
  await capture(page, 'print-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'print-dark-zh-Hant');
  await page.emulateMedia({ media: 'print' });
  await expect(page.getByTestId('layout-sider')).toBeHidden();
  await expect(page.getByTestId('pur-rfq-table')).toBeHidden();
  expect(await page.getByTestId('pur-rfq-detail').evaluate(element => getComputedStyle(element).color))
    .toBe('rgb(0, 0, 0)');
  expect(await page.locator('body').evaluate(element => getComputedStyle(element).backgroundColor))
    .toBe('rgb(255, 255, 255)');
  await capture(page, 'print-media-dark-zh-Hant');
});

test('PUR-RFQ-097 does not print after the saved detail becomes unavailable', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 404/);
  let reads = 0;
  await page.route('**/api/v1/pur/rfqs**', route => {
    const url = new URL(route.request().url());
    if (url.pathname.endsWith('/1')) {
      reads++;
      return reads === 1 ? route.fulfill({ json: detail(1) })
        : route.fulfill({ status: 404, json: { errorCode: 'PUR.RFQ.NOT_FOUND' } });
    }
    return route.fulfill({ json: { items: [row(1)], totalCount: 1 } });
  });
  await page.goto('/pur/rfqs');
  await page.getByRole('button', { name: 'RFQ-0001' }).click();
  await page.evaluate(() => { window.print = () => { throw new Error('print must not run'); }; });
  await page.getByTestId('pur-rfq-print').click();
  await expect(page.getByTestId('pur-rfq-error')).toContainText('找不到');
  expect(reads).toBe(2);
});

test.describe('VIEW-only authorization', () => {
  test.use({ authorization: { ...E2E_AUTHORIZATION, isFullAccess: false,
    permissions: [{ code: 'PUR.RFQ.VIEW', dataScope: 'Company' }] } });
  test('PUR-RFQ-096 hides export', async ({ page }) => {
    await page.route('**/api/v1/pur/rfqs**', route => route.fulfill({ json: { items: [row(1)], totalCount: 1 } }));
    await page.goto('/pur/rfqs');
    await expect(page.getByTestId('pur-rfq-row-1')).toBeVisible();
    await expect(page.getByTestId('pur-rfq-export')).toHaveCount(0);
    await capture(page, 'view-only-light-zh-Hant');
    await chooseTheme(page, 'dark'); await capture(page, 'view-only-dark-zh-Hant');
  });
});
