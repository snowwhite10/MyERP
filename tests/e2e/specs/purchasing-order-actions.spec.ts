import { mkdir } from 'node:fs/promises';
import type { Page } from '@playwright/test';
import { chooseLanguage, chooseTheme, expect, test } from '../support/fixtures';

/**
 * 介面 4.1 採購單送審／刪除／取消（WU-PUR-006f1，REQ_IO_A4.1.4）：模擬後端保存狀態，每個操作同時斷言畫面訊息與
 * 後端實際收到／保存的資料（版本、原因、狀態、列表是否仍有該單）。
 */
type Status = 'Draft' | 'PendingApproval' | 'Approved' | 'Rejected' | 'Cancelled';
interface Order { status: Status; version: number; deleted: boolean; cancelReason: string | null }
interface Backend { order: Order; posts: { path: string; body: unknown }[]; deletes: string[]; next: { status: number; body: object } | null }

const line = { id: 11, lineNo: 1, itemId: 77, requisitionLineId: 44, awardId: null, quantity: 5, unitPrice: 20, taxRateId: 3, taxRatePercent: 5,
  originalNet: 100, originalTax: 5, originalGross: 105, baseNet: 100, baseTax: 5, baseGross: 105, requisitionId: 4, requisitionDocumentNo: 'PR-0004',
  requisitionLineNo: 2, requisitionQuantity: 10, requisitionOrderedQuantity: 0, requisitionRemainingQuantity: 10 };

async function capture(page: Page, name: string): Promise<void> {
  const directory = '../../tests/qa/PUR/screenshots';
  await mkdir(directory, { recursive: true });
  await expect(page.locator('.ant-spin-spinning')).toHaveCount(0);
  await page.screenshot({ path: `${directory}/4.1-actions-${name}.png`, fullPage: true, animations: 'disabled' });
}

async function mockOrder(page: Page, status: Status): Promise<Backend> {
  const backend: Backend = { order: { status, version: 3, deleted: false, cancelReason: null }, posts: [], deletes: [], next: null };
  const summary = () => ({ id: 1, documentNo: 'PO-2026-1', businessDate: '2026-11-20', supplierId: 5, status: backend.order.status,
    currency: 'TWD', baseGross: 105, lineCount: 1, version: backend.order.version });
  const detail = () => ({ ...summary(), rfqId: null, rfqDocumentNo: null, baseCurrencyCode: 'TWD', exchangeRate: 1, baseNet: 100, baseTax: 5,
    remark: null, lines: [line], approvalHistory: [] });
  await page.route('**/api/v1/pur/purchase-orders**', async route => {
    const request = route.request();
    const url = new URL(request.url());
    if (request.method() === 'POST' || request.method() === 'DELETE') {
      if (request.method() === 'POST') backend.posts.push({ path: url.pathname, body: request.postDataJSON() });
      else backend.deletes.push(url.search);
      if (backend.next) { const reply = backend.next; backend.next = null; return route.fulfill({ status: reply.status, json: reply.body }); }
      const order = backend.order;
      if (request.method() === 'DELETE') { order.deleted = true; return route.fulfill({ status: 204, body: '' }); }
      order.version++;
      if (url.pathname.endsWith('/submit')) order.status = 'PendingApproval';
      else { order.status = 'Cancelled'; order.cancelReason = (request.postDataJSON() as { reason: string }).reason; }
      return route.fulfill({ json: { id: 1, documentNo: 'PO-2026-1', status: order.status, version: order.version, approval: { status: 'Pending' } } });
    }
    if (/\/purchase-orders\/1$/.test(url.pathname)) {
      return backend.order.deleted ? route.fulfill({ status: 404, json: { errorCode: 'PUR.ORDER.NOT_FOUND' } }) : route.fulfill({ json: detail() });
    }
    const items = backend.order.deleted ? [] : [summary()];
    return route.fulfill({ json: { items, totalCount: items.length, page: 1, pageSize: 20 } });
  });
  return backend;
}
async function openOrder(page: Page): Promise<void> {
  await page.goto('/pur/purchase-orders');
  await page.getByRole('button', { name: 'PO-2026-1' }).click();
  await expect(page.getByTestId('pur-order-detail')).toBeVisible();
}

test('PUR-PO-UI-E06 SUBMIT confirms, a short requisition refuses the whole order with line reasons, then a retry is saved', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 422/);
  const backend = await mockOrder(page, 'Draft');
  await openOrder(page);
  await expect(page.getByTestId('pur-order-submit')).toHaveText('送審');
  await expect(page.getByTestId('pur-order-delete')).toBeVisible();
  await expect(page.getByTestId('pur-order-cancel')).toHaveCount(0);
  await capture(page, 'draft-light-zh-Hant');
  await chooseTheme(page, 'dark');
  await page.getByTestId('pur-order-submit').click();
  await expect(page.getByRole('dialog')).toContainText('PO-2026-1');
  await expect(page.getByRole('dialog')).toContainText('整張單都不會送出');
  await capture(page, 'submit-confirm-dark-zh-Hant');
  await page.getByRole('dialog').getByRole('button', { name: '取消', exact: true }).click();
  await expect(page.getByRole('dialog')).toHaveCount(0);
  expect(backend.posts).toHaveLength(0);
  await chooseTheme(page, 'light');

  backend.next = { status: 422, body: { errorCode: 'PUR.ORDER.REQUISITION_INSUFFICIENT',
    shortages: [{ requisitionLineId: 44, orderLineNos: [1], requested: 5, available: 2.5 }] } };
  await page.getByTestId('pur-order-submit').click();
  await page.getByRole('dialog').getByRole('button', { name: '送審', exact: true }).click();
  await expect(page.getByTestId('pur-order-action-error')).toContainText('可用的核准數量不足');
  await expect(page.getByTestId('pur-order-action-details')).toHaveText('請購行 44（訂單行 1）：需求 5，可用 2.5');
  await expect(page.getByTestId('pur-order-detail-status')).toHaveText('草稿');
  expect(backend.order.status).toBe('Draft');
  await capture(page, 'submit-refused-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'submit-refused-dark-zh-Hant');
  await chooseTheme(page, 'light');

  await page.getByTestId('pur-order-submit').click();
  await page.getByRole('dialog').getByRole('button', { name: '送審', exact: true }).click();
  await expect(page.getByText('採購單 PO-2026-1 已送審，等待審批。')).toBeVisible();
  // 畫面與保存資料一致：送出的是畫面上的保存版本，重新讀取後的狀態／版本與後端相同。
  expect(backend.posts.at(-1)).toEqual({ path: '/api/v1/pur/purchase-orders/1/submit', body: { version: 3 } });
  expect(backend.order).toMatchObject({ status: 'PendingApproval', version: 4 });
  await expect(page.getByTestId('pur-order-detail-status')).toHaveText('待審批');
  await expect(page.getByTestId('pur-order-row-1')).toContainText('待審批');
  await expect(page.getByTestId('pur-order-action-hint')).toContainText('等待審批中');
  await expect(page.getByTestId('pur-order-submit')).toHaveCount(0);
  await expect(page.getByTestId('pur-order-action-error')).toHaveCount(0);
  await capture(page, 'submitted-light-zh-Hant');
  await chooseLanguage(page, 'en');
  await expect(page.getByTestId('pur-order-action-hint')).toContainText('Waiting for approval');
  await capture(page, 'submitted-light-en');
});

test('PUR-PO-UI-E07 CANCEL requires a reason, confirms the release and saves the trimmed reason', async ({ page }) => {
  const backend = await mockOrder(page, 'Approved');
  await openOrder(page);
  await expect(page.getByTestId('pur-order-submit')).toHaveCount(0);
  await page.getByTestId('pur-order-cancel').click();
  await expect(page.getByTestId('pur-order-cancel-reason')).toBeFocused();
  await page.getByTestId('pur-order-cancel-confirm').click();
  await expect(page.getByTestId('pur-order-cancel-reason-error')).toHaveText('請輸入取消原因。');
  expect(backend.posts).toHaveLength(0);
  await capture(page, 'cancel-validation-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'cancel-validation-dark-zh-Hant');
  await chooseTheme(page, 'light');
  await page.getByTestId('pur-order-cancel-reason').fill('  供應商停產  ');
  await expect(page.getByTestId('pur-order-cancel-reason-error')).toHaveCount(0);
  await page.getByTestId('pur-order-cancel-confirm').click();
  await expect(page.getByRole('dialog')).toContainText('請購數量釋回給其他訂單使用');
  await capture(page, 'cancel-confirm-light-zh-Hant');
  await page.getByRole('dialog').getByRole('button', { name: '取消訂單', exact: true }).click();
  await expect(page.getByText('採購單 PO-2026-1 已取消，請購數量已釋回。')).toBeVisible();
  expect(backend.posts.at(-1)).toEqual({ path: '/api/v1/pur/purchase-orders/1/cancel', body: { version: 3, reason: '供應商停產' } });
  expect(backend.order).toMatchObject({ status: 'Cancelled', version: 4, cancelReason: '供應商停產' });
  await expect(page.getByTestId('pur-order-detail-status')).toHaveText('已取消');
  await expect(page.getByTestId('pur-order-row-1')).toContainText('已取消');
  await expect(page.getByTestId('pur-order-action-hint')).toHaveText('此訂單已取消，不能再做任何操作。');
  await expect(page.getByTestId('pur-order-cancel-form')).toHaveCount(0);
  await capture(page, 'cancelled-light-zh-Hant');
  await chooseLanguage(page, 'zh-Hans');
  await expect(page.getByTestId('pur-order-action-hint')).toHaveText('此订单已取消，不能再做任何操作。');
  await chooseTheme(page, 'dark'); await capture(page, 'cancelled-dark-zh-Hans');
});

test('PUR-PO-UI-E08 a rejected order explains why it cannot be resubmitted; DELETE removes it from the saved list', async ({ page }) => {
  const backend = await mockOrder(page, 'Rejected');
  await openOrder(page);
  await expect(page.getByTestId('pur-order-submit')).toBeDisabled();
  await expect(page.getByTestId('pur-order-action-hint')).toHaveText('已駁回的訂單須先修訂並儲存，才可再次送審。');
  await capture(page, 'rejected-light-zh-Hant');
  await chooseLanguage(page, 'en');
  await page.getByTestId('pur-order-delete').click();
  await expect(page.getByRole('dialog')).toContainText('never reused');
  await capture(page, 'delete-confirm-light-en');
  await page.getByRole('dialog').getByRole('button', { name: 'Delete', exact: true }).click();
  await expect(page.getByText('Purchase order PO-2026-1 was deleted.')).toBeVisible();
  expect(backend.deletes).toEqual(['?version=3']);
  expect(backend.order.deleted).toBe(true);
  await expect(page.getByTestId('pur-order-detail')).toHaveCount(0);
  await expect(page.getByTestId('pur-order-row-1')).toHaveCount(0);
  await expect(page.getByTestId('pur-order-table')).toContainText('No purchase orders yet.');
  await capture(page, 'deleted-light-en');
});
