import { mkdir } from 'node:fs/promises';
import type { Page } from '@playwright/test';
import { E2E_AUTHORIZATION, chooseLanguage, chooseTheme, expect, test } from '../support/fixtures';

const base = { id: 17, documentNo: 'RFQ-0017', businessDate: '2026-10-20', replyDeadline: '2026-10-31',
  ownerId: 4, status: 'Draft' as 'Draft' | 'Sent' | 'Closed', lineCount: 1, invitationCount: 1, version: 2 };
const details = { ...base, remark: null, sentAt: null, createdAt: '2026-10-20T08:00:00',
  lines: [{ id: 170, lineNo: 1, itemId: 11, requisitionLineId: null,
    requiredDate: '2026-11-10', quantity: 1, remark: null }],
  invitations: [{ id: 171, supplierId: 7, supplierCodeSnapshot: 'SUP-7',
    supplierNameSnapshot: 'Saved supplier', sentAt: null }] };

async function capture(page: Page, state: string): Promise<void> {
  const directory = '../../tests/qa/PUR/screenshots';
  await mkdir(directory, { recursive: true });
  await page.waitForTimeout(350);
  await page.screenshot({ path: `${directory}/4.11-action-${state}.png`, fullPage: true });
}

async function openDetail(page: Page): Promise<void> {
  await page.goto('/pur/rfqs');
  await page.getByRole('button', { name: 'RFQ-0017' }).click();
  await expect(page.getByTestId('pur-rfq-detail')).toBeVisible();
}

test('PUR-RFQ-086/087 sends then closes with saved versions and explicit confirmation', async ({ page }) => {
  let saved = { ...details }; const mutations: string[] = [];
  await page.route('**/api/v1/pur/rfqs**', route => {
    const url = new URL(route.request().url());
    if (route.request().method() === 'POST') {
      mutations.push(`${url.pathname}?version=${url.searchParams.get('version')}`);
      saved = { ...saved, status: saved.status === 'Draft' ? 'Sent' : 'Closed', version: saved.version + 1 };
      return route.fulfill({ json: saved });
    }
    if (url.pathname.endsWith('/17')) return route.fulfill({ json: saved });
    return route.fulfill({ json: { items: [saved], totalCount: 1 } });
  });
  await openDetail(page);
  await capture(page, 'draft-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'draft-dark-zh-Hant');
  await chooseTheme(page, 'light');
  await chooseLanguage(page, 'en'); await capture(page, 'draft-light-en');
  await chooseLanguage(page, 'zh-Hans'); await capture(page, 'draft-light-zh-Hans');
  await chooseLanguage(page, 'zh-Hant');
  await page.getByTestId('pur-rfq-send').click();
  await expect(page.getByTestId('erp-message-content')).toContainText('不會寄出電郵');
  await capture(page, 'send-confirm-light-zh-Hant');
  await page.getByTestId('erp-message-cancel').click();
  expect(mutations).toEqual([]);
  await expect(page.getByTestId('pur-rfq-row-17')).toContainText('草稿');
  await chooseTheme(page, 'dark');
  await page.getByTestId('pur-rfq-send').click();
  await capture(page, 'send-confirm-dark-zh-Hant');
  await page.getByTestId('erp-message-cancel').click();
  await chooseTheme(page, 'light');
  await page.getByTestId('pur-rfq-send').click();
  await page.getByTestId('erp-message-ok').click();
  await expect(page.getByTestId('pur-rfq-row-17')).toContainText('已發送');
  await expect(page.getByTestId('pur-rfq-detail')).toContainText('已發送');
  await expect(page.locator('.ant-message')).toContainText('RFQ-0017 已發送');
  await capture(page, 'sent-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'sent-dark-zh-Hant');
  await chooseTheme(page, 'light');
  await page.getByTestId('pur-rfq-close').click();
  await expect(page.getByTestId('erp-message-content')).toContainText('不再接受新報價');
  await page.getByTestId('erp-message-ok').click();
  await expect(page.getByTestId('pur-rfq-row-17')).toContainText('已關閉');
  await expect(page.getByTestId('pur-rfq-detail')).toContainText('已關閉');
  await expect(page.getByTestId('pur-rfq-close')).toHaveCount(0);
  await expect(page.locator('.ant-message')).toContainText('RFQ-0017 已關閉');
  await capture(page, 'closed-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'closed-dark-zh-Hant');
  expect(mutations).toEqual(['/api/v1/pur/rfqs/17/send?version=2', '/api/v1/pur/rfqs/17/close?version=3']);
  expect(saved.status).toBe('Closed');
  expect(saved.version).toBe(4);
});

test('PUR-RFQ-087/090 refreshes after a stale version and preserves the reason', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 409/);
  let saved = { ...details }; let posts = 0;
  await page.route('**/api/v1/pur/rfqs**', route => {
    const url = new URL(route.request().url());
    if (route.request().method() === 'POST') {
      posts++; saved = { ...saved, status: 'Sent', version: 3 };
      return route.fulfill({ status: 409, json: { errorCode: 'COMMON.CONCURRENCY.CONFLICT' } });
    }
    if (url.pathname.endsWith('/17')) return route.fulfill({ json: saved });
    return route.fulfill({ json: { items: [saved], totalCount: 1 } });
  });
  await openDetail(page);
  await page.getByTestId('pur-rfq-send').click(); await page.getByTestId('erp-message-ok').click();
  await expect(page.getByTestId('pur-rfq-error')).toContainText('資料已被他人修改');
  await expect(page.getByTestId('pur-rfq-detail')).toHaveCount(0);
  await expect(page.getByTestId('pur-rfq-row-17')).toContainText('已發送');
  await capture(page, 'conflict-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'conflict-dark-zh-Hant');
  expect(posts).toBe(1);
});

test('PUR-RFQ-090 shows a server trace and keeps the saved draft', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 500/);
  pageErrors.allowApi5xx(/\/api\/v1\/pur\/rfqs\/17\/send/, 500);
  await page.route('**/api/v1/pur/rfqs**', route => {
    const url = new URL(route.request().url());
    if (route.request().method() === 'POST') return route.fulfill({ status: 500, json: { traceId: 'rfq-send-trace' } });
    if (url.pathname.endsWith('/17')) return route.fulfill({ json: details });
    return route.fulfill({ json: { items: [details], totalCount: 1 } });
  });
  await openDetail(page);
  await page.getByTestId('pur-rfq-send').click(); await page.getByTestId('erp-message-ok').click();
  await expect(page.getByTestId('pur-rfq-error')).toContainText('rfq-send-trace');
  await expect(page.getByTestId('pur-rfq-row-17')).toContainText('草稿');
  await expect(page.getByTestId('pur-rfq-send')).toBeEnabled();
  await capture(page, 'server-error-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'server-error-dark-zh-Hant');
});

test('PUR-RFQ-088/090 explains a rejected send without changing the draft', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 422/);
  let posts = 0;
  await page.route('**/api/v1/pur/rfqs**', route => {
    const url = new URL(route.request().url());
    if (route.request().method() === 'POST') { posts++;
      return route.fulfill({ status: 422, json: { errorCode: 'PUR.RFQ.NOT_SENDABLE' } }); }
    if (url.pathname.endsWith('/17')) return route.fulfill({ json: details });
    return route.fulfill({ json: { items: [details], totalCount: 1 } });
  });
  await openDetail(page);
  await page.getByTestId('pur-rfq-send').click(); await page.getByTestId('erp-message-ok').click();
  await expect(page.getByTestId('pur-rfq-error')).toContainText('只有草稿詢價單可發送');
  await expect(page.getByTestId('pur-rfq-row-17')).toContainText('草稿');
  await expect(page.getByTestId('pur-rfq-detail')).toContainText('草稿');
  await capture(page, 'rejected-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'rejected-dark-zh-Hant');
  expect(posts).toBe(1);
});

test('PUR-RFQ-090 names a supplier that became unavailable before sending', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 422/);
  await page.route('**/api/v1/pur/rfqs**', route => {
    const url = new URL(route.request().url());
    if (route.request().method() === 'POST') return route.fulfill({ status: 422,
      json: { errorCode: 'MD.SUPPLIER.NOT_APPROVED' } });
    if (url.pathname.endsWith('/17')) return route.fulfill({ json: details });
    return route.fulfill({ json: { items: [details], totalCount: 1 } });
  });
  await openDetail(page);
  await page.getByTestId('pur-rfq-send').click(); await page.getByTestId('erp-message-ok').click();
  await expect(page.getByTestId('pur-rfq-error')).toContainText('受邀供應商已停用或未核准');
  await expect(page.getByTestId('pur-rfq-row-17')).toContainText('草稿');
  await capture(page, 'supplier-unavailable-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'supplier-unavailable-dark-zh-Hant');
});

test.describe('VIEW only', () => {
  test.use({ authorization: { ...E2E_AUTHORIZATION, isFullAccess: false,
    permissions: [{ code: 'PUR.RFQ.VIEW', dataScope: 'Company' }] } });
  test('PUR-RFQ-089 hides lifecycle actions without action permissions', async ({ page }) => {
    await page.route('**/api/v1/pur/rfqs**', route =>
      route.fulfill({ json: new URL(route.request().url()).pathname.endsWith('/17')
        ? details : { items: [details], totalCount: 1 } }));
    await openDetail(page);
    await expect(page.getByTestId('pur-rfq-send')).toHaveCount(0);
    await expect(page.getByTestId('pur-rfq-close')).toHaveCount(0);
    await capture(page, 'view-only-light-zh-Hant');
    await chooseTheme(page, 'dark'); await capture(page, 'view-only-dark-zh-Hant');
  });
});
