import { mkdir } from 'node:fs/promises';
import type { Page } from '@playwright/test';
import { E2E_AUTHORIZATION, chooseLanguage, chooseTheme, expect, test } from '../support/fixtures';

const row = { id: 7, documentNo: 'REQ-0007', businessDate: '2026-10-20', requesterId: 4,
  status: 'Draft', lineCount: 1, version: 1, branchId: 2, departmentId: 3 };
const detail = { ...row, remark: 'Urgent', createdAt: '2026-10-20T10:00:00',
  lines: [{ id: 70, lineNo: 1, itemId: 11, requiredDate: '2026-10-31', quantity: 1.123456,
    orderedQuantity: 0, remark: 'Line note' }], approvalHistory: [{ instanceId: 79, status: 'Cancelled',
    submittedAt: '2026-10-19T10:10:00', steps: [{ stepNo: 1, name: 'Manager', status: 'Skipped',
      actedBy: null, actedAt: null, comment: 'Previous attempt' }] }, { instanceId: 80, status: 'Rejected',
    submittedAt: '2026-10-20T10:10:00', steps: [{ stepNo: 1, name: 'Manager', status: 'Rejected',
      actedBy: 8, actedAt: '2026-10-20T10:20:00', comment: 'Check quantity' }] }] };

async function capture(page: Page, name: string): Promise<void> {
  const directory = '../../tests/qa/PUR/screenshots';
  await mkdir(directory, { recursive: true });
  await expect(page.locator('.ant-spin-spinning')).toHaveCount(0);
  await page.screenshot({ path: `${directory}/${name}.png`, fullPage: true });
}

test('PUR-UI-008/009/013/015 filters and shows saved lines and all approval attempts', async ({ page }) => {
  const queries: string[] = [];
  await page.route('**/api/v1/pur/requisitions**', route => {
    const url = new URL(route.request().url());
    if (url.pathname.endsWith('/7')) return route.fulfill({ json: detail });
    queries.push(url.search);
    return route.fulfill({ json: { items: url.searchParams.has('keyword') && url.searchParams.get('keyword') === 'missing'
      ? [] : [row], totalCount: url.searchParams.get('keyword') === 'missing' ? 0 : 1 } });
  });
  await page.goto('/pur/requisitions');
  await expect(page.getByTestId('pur-requisition-row-7')).toBeVisible();
  await capture(page, '4.8-normal-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, '4.8-normal-dark-zh-Hant');
  await chooseTheme(page, 'light');
  await page.getByTestId('pur-requisition-row-7').getByRole('button', { name: 'REQ-0007' }).click();
  await expect(page.getByTestId('pur-requisition-detail')).toContainText('1.123456');
  await expect(page.getByTestId('pur-requisition-detail')).toContainText('Previous attempt');
  await expect(page.getByTestId('pur-requisition-detail')).toContainText('Check quantity');
  await capture(page, '4.8-detail-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, '4.8-detail-dark-zh-Hant');
  await chooseTheme(page, 'light');
  const count = queries.length;
  await page.getByLabel('起始日').fill('2026-10-21');
  await page.getByLabel('截止日').fill('2026-10-20');
  await page.getByTestId('pur-requisition-filter').click();
  await expect(page.getByTestId('pur-requisition-error')).toContainText('起始日不可晚於截止日');
  expect(queries).toHaveLength(count);
  await page.getByLabel('起始日').fill('2026-10-20');
  await page.getByTestId('pur-requisition-search').fill('missing');
  await page.getByTestId('pur-requisition-filter').click();
  await expect(page.getByTestId('pur-requisition-table')).toContainText('目前條件沒有符合的請購單');
  await expect(page.getByTestId('pur-requisition-detail')).toHaveCount(0);
  await expect.poll(() => queries.some(query => query.includes('keyword=missing'))).toBe(true);
  await capture(page, '4.8-no-results-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, '4.8-no-results-dark-zh-Hant');
  await chooseTheme(page, 'light');
  await chooseLanguage(page, 'en');
  await expect(page.getByRole('heading', { name: 'Purchase requisitions' })).toBeVisible();
  await capture(page, '4.8-no-results-light-en');
  await chooseLanguage(page, 'zh-Hans');
  await expect(page.getByRole('heading', { name: '请购单' })).toBeVisible();
  await capture(page, '4.8-no-results-light-zh-Hans');
  await page.getByTestId('pur-requisition-search').fill('');
  await page.getByTestId('pur-requisition-filter').click();
  await expect(page.getByTestId('pur-requisition-row-7')).toBeVisible();
  await capture(page, '4.8-normal-light-zh-Hans');
  await chooseLanguage(page, 'en');
  await expect(page.getByTestId('pur-requisition-row-7')).toBeVisible();
  await capture(page, '4.8-normal-light-en');
});

test('PUR-UI-010/011/014 keeps the persisted row after version conflict', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 409/);
  let status = 'Draft';
  await page.route('**/api/v1/pur/requisitions**', route => {
    const url = new URL(route.request().url());
    if (url.pathname.endsWith('/submit')) return route.fulfill({ status: 409,
      json: { errorCode: 'COMMON.CONCURRENCY.CONFLICT' } });
    if (url.pathname.endsWith('/7')) return route.fulfill({ json: { ...detail, status } });
    return route.fulfill({ json: { items: [{ ...row, status }], totalCount: 1 } });
  });
  await page.goto('/pur/requisitions');
  await page.getByTestId('pur-requisition-row-7').getByRole('button', { name: '送審' }).click();
  await expect(page.getByTestId('pur-requisition-error')).toContainText('資料已被他人修改');
  await expect(page.getByTestId('pur-requisition-row-7')).toContainText('草稿');
  await capture(page, '4.8-conflict-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, '4.8-conflict-dark-zh-Hant');
  status = 'Approved';
  await page.getByTestId('pur-requisition-filter').click();
  await expect(page.getByTestId('pur-requisition-row-7').getByRole('button', { name: '送審' })).toHaveCount(0);
  for (const blocked of ['PendingApproval', 'Rejected', 'Closed']) {
    status = blocked;
    await page.getByTestId('pur-requisition-filter').click();
    await expect(page.getByTestId('pur-requisition-row-7').getByRole('button', { name: '送審' })).toHaveCount(0);
    await expect(page.getByTestId('pur-requisition-row-7').getByRole('button', { name: '刪除' })).toHaveCount(0);
  }
});

test('PUR-UI-011/013 deletes only a persisted draft after confirmation', async ({ page }) => {
  let exists = true; let deletedVersion: string | null = null;
  await page.route('**/api/v1/pur/requisitions**', route => {
    const url = new URL(route.request().url());
    if (route.request().method() === 'DELETE') { deletedVersion = url.searchParams.get('version'); exists = false;
      return route.fulfill({ status: 204, body: '' }); }
    return route.fulfill({ json: { items: exists ? [row] : [], totalCount: exists ? 1 : 0 } });
  });
  await page.goto('/pur/requisitions');
  await page.getByTestId('pur-requisition-row-7').getByRole('button', { name: '刪除' }).click();
  await expect(page.getByTestId('pur-requisition-row-7')).toBeVisible();
  await page.getByRole('button', { name: '取消' }).click();
  expect(exists).toBe(true);
  await page.getByTestId('pur-requisition-row-7').getByRole('button', { name: '刪除' }).click();
  await page.getByRole('button', { name: '刪除' }).last().click();
  await expect(page.getByTestId('pur-requisition-row-7')).toHaveCount(0);
  expect(deletedVersion).toBe('1');
});

test('PUR-UI-011/013 shows the persisted pending state after submission', async ({ page }) => {
  let savedStatus = 'Draft'; let submittedVersion: number | null = null;
  await page.route('**/api/v1/pur/requisitions**', async route => {
    const url = new URL(route.request().url());
    if (url.pathname.endsWith('/submit')) { submittedVersion = (route.request().postDataJSON() as { version: number }).version;
      savedStatus = 'PendingApproval'; return route.fulfill({ json: { requisition: { ...detail, status: savedStatus },
        approval: { instanceId: 81, status: 'Pending', currentStepNo: 1, stepCount: 1 } } }); }
    return route.fulfill({ json: { items: [{ ...row, status: savedStatus }], totalCount: 1 } });
  });
  await page.goto('/pur/requisitions');
  await page.getByTestId('pur-requisition-row-7').getByRole('button', { name: '送審' }).click();
  await expect(page.getByTestId('pur-requisition-row-7')).toContainText('待審');
  await expect(page.getByTestId('pur-requisition-row-7').getByRole('button', { name: '送審' })).toHaveCount(0);
  expect(submittedVersion).toBe(1);
  expect(savedStatus).toBe('PendingApproval');
  await capture(page, '4.8-success-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, '4.8-success-dark-zh-Hant');
});

test('PUR-UI-009 distinguishes an empty company list', async ({ page }) => {
  await page.route('**/api/v1/pur/requisitions**', route => route.fulfill({ json: { items: [], totalCount: 0 } }));
  await page.goto('/pur/requisitions');
  await expect(page.getByTestId('pur-requisition-table')).toContainText('尚無請購單');
  await capture(page, '4.8-empty-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, '4.8-empty-dark-zh-Hant');
});

test('PUR-UI-014 explains business refusal and keeps the saved draft', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 422/);
  await page.route('**/api/v1/pur/requisitions**', route => {
    if (new URL(route.request().url()).pathname.endsWith('/submit')) return route.fulfill({ status: 422,
      json: { errorCode: 'MD.ITEM.INACTIVE' } });
    return route.fulfill({ json: { items: [row], totalCount: 1 } });
  });
  await page.goto('/pur/requisitions');
  await page.getByTestId('pur-requisition-row-7').getByRole('button', { name: '送審' }).click();
  await expect(page.getByTestId('pur-requisition-error')).toContainText('此物料已停用');
  await expect(page.getByTestId('pur-requisition-row-7')).toContainText('草稿');
  await capture(page, '4.8-refusal-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, '4.8-refusal-dark-zh-Hant');
});

test('PUR-UI-014 shows server trace and retains the saved draft', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 500/);
  pageErrors.allowApi5xx(/\/api\/v1\/pur\/requisitions\/7\/submit$/, 500);
  await page.route('**/api/v1/pur/requisitions**', route => {
    if (new URL(route.request().url()).pathname.endsWith('/submit')) return route.fulfill({ status: 500,
      json: { traceId: '00-qa-pur-01' } });
    return route.fulfill({ json: { items: [row], totalCount: 1 } });
  });
  await page.goto('/pur/requisitions');
  await page.getByTestId('pur-requisition-row-7').getByRole('button', { name: '送審' }).click();
  await expect(page.getByTestId('pur-requisition-error')).toContainText('00-qa-pur-01');
  await expect(page.getByTestId('pur-requisition-row-7')).toContainText('草稿');
  await capture(page, '4.8-server-error-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, '4.8-server-error-dark-zh-Hant');
});

test('PUR-UI-014 reports a disconnected request', async ({ page, pageErrors }) => {
  pageErrors.allow(/net::ERR_/);
  await page.route('**/api/v1/pur/requisitions**', route => {
    if (new URL(route.request().url()).pathname.endsWith('/submit')) return route.abort('internetdisconnected');
    return route.fulfill({ json: { items: [row], totalCount: 1 } });
  });
  await page.goto('/pur/requisitions');
  await page.getByTestId('pur-requisition-row-7').getByRole('button', { name: '送審' }).click();
  await expect(page.getByTestId('pur-requisition-error')).toContainText('網路');
  await expect(page.getByTestId('pur-requisition-row-7')).toContainText('草稿');
  await capture(page, '4.8-network-error-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, '4.8-network-error-dark-zh-Hant');
});

test('PUR-UI-014 explains invalid server filters and a failed list load', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 400/);
  await page.route('**/api/v1/pur/requisitions**', route => route.fulfill({ status: 400,
    json: { errorCode: 'COMMON.REQUEST.VALIDATION_FAILED' } }));
  await page.goto('/pur/requisitions');
  await expect(page.getByTestId('pur-requisition-error')).toContainText('輸入資料不正確');
  await expect(page.getByTestId('pur-requisition-table')).toContainText('載入失敗');
  await capture(page, '4.8-validation-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, '4.8-validation-dark-zh-Hant');
});

test('PUR-UI-014 treats a timed out submit as uncommitted', async ({ page, pageErrors }) => {
  pageErrors.allow(/net::ERR_/);
  await page.route('**/api/v1/pur/requisitions**', route => {
    if (new URL(route.request().url()).pathname.endsWith('/submit')) return route.abort('timedout');
    return route.fulfill({ json: { items: [row], totalCount: 1 } });
  });
  await page.goto('/pur/requisitions');
  await page.getByTestId('pur-requisition-row-7').getByRole('button', { name: '送審' }).click();
  await expect(page.getByTestId('pur-requisition-error')).toContainText('無法連線');
  await expect(page.getByTestId('pur-requisition-row-7')).toContainText('草稿');
  await capture(page, '4.8-timeout-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, '4.8-timeout-dark-zh-Hant');
});

test('PUR-UI-015 prints fresh persisted detail', async ({ page }) => {
  let detailReads = 0;
  await page.route('**/api/v1/pur/requisitions**', route => {
    if (new URL(route.request().url()).pathname.endsWith('/7')) {
      detailReads++;
      return route.fulfill({ json: { ...detail, remark: 'Saved at print time' } });
    }
    return route.fulfill({ json: { items: [row], totalCount: 1 } });
  });
  await page.goto('/pur/requisitions');
  await page.evaluate(() => { window.print = () => { document.body.dataset['printed'] = 'yes'; }; });
  await page.getByTestId('pur-requisition-row-7').getByRole('button', { name: '列印' }).click();
  await expect(page.getByTestId('pur-requisition-detail')).toContainText('Saved at print time');
  await expect(page.locator('body')).toHaveAttribute('data-printed', 'yes');
  expect(detailReads).toBe(1);
});

test('PUR-UI-015 does not print when detail retrieval fails', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 404/);
  await page.route('**/api/v1/pur/requisitions**', route => {
    if (new URL(route.request().url()).pathname.endsWith('/7')) return route.fulfill({ status: 404,
      json: { errorCode: 'PUR.REQUISITION.NOT_FOUND' } });
    return route.fulfill({ json: { items: [row], totalCount: 1 } });
  });
  await page.goto('/pur/requisitions');
  await page.evaluate(() => { window.print = () => { document.body.dataset['printed'] = 'yes'; }; });
  await page.getByTestId('pur-requisition-row-7').getByRole('button', { name: '列印' }).click();
  await expect(page.getByTestId('pur-requisition-error')).toContainText('找不到這張請購單');
  await expect(page.locator('body')).not.toHaveAttribute('data-printed', 'yes');
});

test.describe('PUR-UI-012 view only', () => {
  test.use({ authorization: { ...E2E_AUTHORIZATION, isFullAccess: false,
    permissions: [{ code: 'PUR.REQUISITION.VIEW', dataScope: 'Company' }] } });
  test('hides submission and deletion for a reader', async ({ page }) => {
    await page.route('**/api/v1/pur/requisitions**', route => route.fulfill({ json: { items: [row], totalCount: 1 } }));
    await page.goto('/pur/requisitions');
    await expect(page.getByTestId('pur-requisition-row-7')).toBeVisible();
    await expect(page.getByTestId('pur-requisition-row-7').getByRole('button', { name: '送審' })).toHaveCount(0);
    await expect(page.getByTestId('pur-requisition-row-7').getByRole('button', { name: '刪除' })).toHaveCount(0);
    await capture(page, '4.8-read-only-light-zh-Hant');
    await chooseTheme(page, 'dark'); await capture(page, '4.8-read-only-dark-zh-Hant');
  });
});

test.describe('PUR-UI-012 no view permission', () => {
  test.use({ authorization: { ...E2E_AUTHORIZATION, isFullAccess: false, permissions: [] } });
  test('shows the forbidden page without requesting requisitions', async ({ page }) => {
    let requests = 0;
    await page.route('**/api/v1/pur/requisitions**', route => { requests++; return route.abort(); });
    await page.goto('/pur/requisitions');
    await expect(page.getByTestId('error-page-403')).toBeVisible();
    expect(requests).toBe(0);
    await capture(page, '4.8-forbidden-light-zh-Hant');
    await chooseTheme(page, 'dark'); await capture(page, '4.8-forbidden-dark-zh-Hant');
  });
});
