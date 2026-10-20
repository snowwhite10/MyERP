import { mkdir, readFile } from 'node:fs/promises';
import { E2E_AUTHORIZATION, chooseTheme, expect, test } from '../support/fixtures';

const OUT = '../../tests/qa/PUR/screenshots';

const row = (id: number) => ({ id, documentNo: `REQ-${String(id).padStart(4, '0')}`,
  businessDate: '2026-10-20', requesterId: 4, status: 'Draft', lineCount: 1, version: 1,
  branchId: 2, departmentId: 3 });
const detail = (id: number) => ({ ...row(id), remark: ' +SUM(1)', createdAt: '2026-10-20T10:00:00',
  lines: [{ id: id * 10, lineNo: 1, itemId: 11, requiredDate: '2026-10-31',
    quantity: 1.123456, orderedQuantity: 0, remark: '=HYPERLINK("x")' }], approvalHistory: [] });

test('PUR-CSV-001/008 exports all filtered pages and saved lines safely', async ({ page }) => {
  const requestedPages: number[] = [];
  await page.route('**/api/v1/pur/requisitions**', route => {
    const url = new URL(route.request().url());
    const match = url.pathname.match(/\/requisitions\/(\d+)$/);
    if (match) return route.fulfill({ json: detail(Number(match[1])) });
    const pageNumber = Number(url.searchParams.get('page') ?? 1);
    requestedPages.push(pageNumber);
    const pageSize = Number(url.searchParams.get('pageSize') ?? 20);
    const start = (pageNumber - 1) * pageSize + 1;
    return route.fulfill({ json: { items: Array.from({ length: Math.max(0, Math.min(pageSize, 201 - start + 1)) },
      (_, index) => row(start + index)), totalCount: 201 } });
  });
  await page.goto('/pur/requisitions');
  await expect(page.getByTestId('pur-requisition-row-1')).toBeVisible();
  const download = page.waitForEvent('download');
  await page.getByTestId('pur-requisition-export').click();
  const csv = await readFile((await (await download).path())!, 'utf8');
  await expect(page.getByTestId('pur-requisition-notice')).toContainText('201 行 CSV');
  await mkdir(OUT, { recursive: true });
  await page.screenshot({ path: `${OUT}/4.8-exported-light-zh-Hant.png`, fullPage: true });
  await chooseTheme(page, 'dark');
  await page.screenshot({ path: `${OUT}/4.8-exported-dark-zh-Hant.png`, fullPage: true });
  expect(csv.startsWith('\uFEFF')).toBe(true);
  expect(csv).toContain('"REQ-0001"');
  expect(csv).toContain('"REQ-0201"');
  expect(csv).toContain('"\'=HYPERLINK(""x"")"');
  expect(csv.match(/"REQ-\d{4}"/g)).toHaveLength(201);
  expect(requestedPages).toEqual(expect.arrayContaining([1, 2]));
});

test('PUR-CSV-002 rejects an oversized export without a download', async ({ page }) => {
  await page.route('**/api/v1/pur/requisitions**', route => route.fulfill({ json: {
    items: [row(1)], totalCount: 10001 } }));
  await page.goto('/pur/requisitions');
  await page.getByTestId('pur-requisition-export').click();
  await expect(page.getByTestId('pur-requisition-error')).toContainText('10000 行上限');
  await expect(page.getByTestId('pur-requisition-export')).toBeEnabled();
  await mkdir(OUT, { recursive: true });
  await page.screenshot({ path: `${OUT}/4.8-export-limit-light-zh-Hant.png`, fullPage: true });
  await chooseTheme(page, 'dark');
  await page.screenshot({ path: `${OUT}/4.8-export-limit-dark-zh-Hant.png`, fullPage: true });
  await expect(page.getByTestId('pur-requisition-notice')).toHaveCount(0);
});

test('PUR-CSV-003/004/007 cancels pending details and reports failure without a partial download', async ({ page, pageErrors }) => {
  let fail = false;
  pageErrors.allowApi5xx(/\/api\/v1\/pur\/requisitions\/1$/, 500);
  pageErrors.allow(/^console: Failed to load resource: the server responded with a status of 500 \(Internal Server Error\)$/);
  await page.route('**/api/v1/pur/requisitions**', async route => {
    const url = new URL(route.request().url());
    if (url.pathname.endsWith('/1')) {
      if (fail) return route.fulfill({ status: 500, json: { errorCode: 'COMMON.SERVER.ERROR', traceId: '00-pur-export' } });
      await new Promise(resolve => setTimeout(resolve, 1200));
      return route.fulfill({ json: detail(1) }).catch(() => {});
    }
    return route.fulfill({ json: { items: [row(1)], totalCount: 1 } });
  });
  await page.goto('/pur/requisitions');
  await page.getByTestId('pur-requisition-export').click();
  await expect(page.getByTestId('pur-requisition-cancel')).toBeVisible();
  await page.getByTestId('pur-requisition-cancel').click();
  await expect(page.getByTestId('pur-requisition-notice')).toContainText('匯出已取消');
  fail = true;
  await page.getByTestId('pur-requisition-export').click();
  await expect(page.getByTestId('pur-requisition-error')).toContainText('00-pur-export');
  await expect(page.getByTestId('pur-requisition-export')).toBeEnabled();
  await mkdir(OUT, { recursive: true });
  await page.screenshot({ path: `${OUT}/4.8-export-error-light-zh-Hant.png`, fullPage: true });
  await chooseTheme(page, 'dark');
  await page.screenshot({ path: `${OUT}/4.8-export-error-dark-zh-Hant.png`, fullPage: true });
  await expect(page.getByTestId('pur-requisition-notice')).toHaveCount(0);
});

test('PUR-CSV-002 warns when saved documents have no lines', async ({ page }) => {
  let downloads = 0;
  page.on('download', () => downloads++);
  await page.route('**/api/v1/pur/requisitions**', route => {
    const url = new URL(route.request().url());
    return route.fulfill({ json: url.pathname.endsWith('/1') ? { ...detail(1), lines: [] }
      : { items: [row(1)], totalCount: 1 } });
  });
  await page.goto('/pur/requisitions');
  await page.getByTestId('pur-requisition-export').click();
  await expect(page.getByTestId('pur-requisition-notice')).toContainText('沒有可匯出的明細');
  expect(downloads).toBe(0);
});

test('PUR-CSV-003 rejects duplicated records across pages', async ({ page }) => {
  let downloads = 0;
  page.on('download', () => downloads++);
  await page.route('**/api/v1/pur/requisitions**', route => route.fulfill({ json: {
    items: [row(1)], totalCount: 2 } }));
  await page.goto('/pur/requisitions');
  await page.getByTestId('pur-requisition-export').click();
  await expect(page.getByTestId('pur-requisition-error')).toContainText('資料已變更');
  expect(downloads).toBe(0);
});

test('PUR-CSV-003 changing the applied filter cancels a pending export', async ({ page }) => {
  let downloads = 0;
  page.on('download', () => downloads++);
  await page.route('**/api/v1/pur/requisitions**', async route => {
    const url = new URL(route.request().url());
    if (url.pathname.endsWith('/1')) {
      await new Promise(resolve => setTimeout(resolve, 1000));
      return route.fulfill({ json: detail(1) }).catch(() => {});
    }
    return route.fulfill({ json: url.searchParams.get('keyword') === 'missing'
      ? { items: [], totalCount: 0 } : { items: [row(1)], totalCount: 1 } });
  });
  await page.goto('/pur/requisitions');
  await page.getByTestId('pur-requisition-export').click();
  await expect(page.getByTestId('pur-requisition-cancel')).toBeVisible();
  await page.getByTestId('pur-requisition-search').fill('missing');
  await page.getByTestId('pur-requisition-filter').click();
  await expect(page.getByTestId('pur-requisition-table')).toContainText('目前條件沒有符合的請購單');
  await expect(page.getByTestId('pur-requisition-cancel')).toHaveCount(0);
  expect(downloads).toBe(0);
});

test('PUR-CSV-006 exports every saved status without changing data', async ({ page }) => {
  const statuses = ['Draft', 'PendingApproval', 'Rejected', 'Approved', 'Closed'];
  let writes = 0;
  await page.route('**/api/v1/pur/requisitions**', route => {
    if (route.request().method() !== 'GET') writes++;
    const url = new URL(route.request().url());
    const match = url.pathname.match(/\/requisitions\/(\d+)$/);
    const index = match ? Number(match[1]) - 1 : -1;
    return route.fulfill({ json: match ? { ...detail(index + 1), status: statuses[index] }
      : { items: statuses.map((status, item) => ({ ...row(item + 1), status })), totalCount: 5 } });
  });
  await page.goto('/pur/requisitions');
  const download = page.waitForEvent('download');
  await page.getByTestId('pur-requisition-export').click();
  const csv = await readFile((await (await download).path())!, 'utf8');
  await expect(page.getByTestId('pur-requisition-notice')).toContainText('5 行 CSV');
  for (const status of statuses) expect(csv).toContain(`"${status}"`);
  expect(writes).toBe(0);
});

for (const status of [400, 409, 422]) {
  test(`PUR-CSV-007 reports detail HTTP ${status} without a partial download`, async ({ page, pageErrors }) => {
    let downloads = 0;
    page.on('download', () => downloads++);
    const reason = status === 400 ? 'Bad Request' : status === 409 ? 'Conflict' : 'Unprocessable Entity';
    pageErrors.allow(new RegExp(`^console: Failed to load resource: the server responded with a status of ${status} \\(${reason}\\)$`));
    await page.route('**/api/v1/pur/requisitions**', route => {
      const url = new URL(route.request().url());
      return url.pathname.endsWith('/1')
        ? route.fulfill({ status, json: { errorCode: status === 409 ? 'COMMON.CONCURRENCY.CONFLICT'
          : status === 422 ? 'PUR.REQUISITION.NOT_EDITABLE' : 'COMMON.VALIDATION.ERROR' } })
        : route.fulfill({ json: { items: [row(1)], totalCount: 1 } });
    });
    await page.goto('/pur/requisitions');
    await page.getByTestId('pur-requisition-export').click();
    await expect(page.getByTestId('pur-requisition-error')).toBeVisible();
    await expect(page.getByTestId('pur-requisition-export')).toBeEnabled();
    expect(downloads).toBe(0);
  });
}

for (const reason of ['failed', 'timedout'] as const) {
  test(`PUR-CSV-007 reports ${reason} without a partial download`, async ({ page, pageErrors }) => {
    let downloads = 0;
    page.on('download', () => downloads++);
    pageErrors.allow(new RegExp(`^console: Failed to load resource: net::ERR_${reason === 'failed' ? 'FAILED' : 'TIMED_OUT'}$`));
    await page.route('**/api/v1/pur/requisitions**', route => {
      const url = new URL(route.request().url());
      return url.pathname.endsWith('/1') ? route.abort(reason)
        : route.fulfill({ json: { items: [row(1)], totalCount: 1 } });
    });
    await page.goto('/pur/requisitions');
    await page.getByTestId('pur-requisition-export').click();
    await expect(page.getByTestId('pur-requisition-error')).toBeVisible();
    await expect(page.getByTestId('pur-requisition-export')).toBeEnabled();
    expect(downloads).toBe(0);
  });
}

test.describe('PUR-CSV-005 export permission', () => {
  test.use({ authorization: { ...E2E_AUTHORIZATION, isFullAccess: false,
    permissions: [{ code: 'PUR.REQUISITION.VIEW', dataScope: 'Company' }] } });
  test('shows saved rows but hides the export action', async ({ page }) => {
    await page.route('**/api/v1/pur/requisitions**', route => route.fulfill({ json: {
      items: [row(1)], totalCount: 1 } }));
    await page.goto('/pur/requisitions');
    await expect(page.getByTestId('pur-requisition-row-1')).toBeVisible();
    await expect(page.getByTestId('pur-requisition-export')).toHaveCount(0);
  });
});
