import { mkdir } from 'node:fs/promises';
import { chooseLanguage, chooseTheme, expect, test } from '../support/fixtures';

// Sales outbound detail actions (WU-SD-004c2b2a, ADR-150): submit, withdraw, delete Draft, line 422, INV stock refusal, 500;
// INV refusals naming several delivery lines (WU-SD-004c2b2d, ADR-154).
type Status = 'Draft' | 'Pending' | 'Rejected' | 'Posted';
const saved = (status: Status = 'Draft', version = 3) => ({ id: 61, documentNo: 'DH-0061', orderId: 81, orderDocumentNo: 'SO-0081',
  customerId: 7, customerCode: 'C-7', customerName: 'Saved customer', businessDate: '2026-11-21', status, remark: null,
  approvalInstanceId: status === 'Pending' ? 501 : null, postedAt: status === 'Posted' ? '2026-11-21T09:30:00' : null, version,
  approvalHistory: status === 'Pending' ? [{ instanceId: 501, status: 'Pending', submittedAt: '2026-11-21T09:00:00',
    steps: [{ stepNo: 1, name: 'Manager', status: 'Pending', actedBy: null, actedAt: null, comment: null }] }] : [],
  lines: [
    { id: 71, lineNo: 1, orderLineId: 91, orderLineNo: 1, itemId: 9, itemCode: 'ITEM-9', itemName: 'Lot item', warehouseId: 4,
      locationId: 12, quantity: 2, tracking: [], invMovementId: status === 'Posted' ? 9001 : null },
    { id: 72, lineNo: 2, orderLineId: 93, orderLineNo: 3, itemId: 11, itemCode: 'ITEM-11', itemName: 'Plain item', warehouseId: 4,
      locationId: null, quantity: 2, tracking: [], invMovementId: status === 'Posted' ? 9002 : null }] });
const list = (status: Status | null, version = 3) => ({ items: status ? [{ id: 61, documentNo: 'DH-0061', orderId: 81,
  orderDocumentNo: 'SO-0081', customerId: 7, customerName: 'Saved customer', businessDate: '2026-11-21', status, lineCount: 2,
  totalQuantity: 4, version }] : [], totalCount: status ? 1 : 0, page: 1, pageSize: 20 });

async function capture(page: import('@playwright/test').Page, name: string): Promise<void> {
  const path = '../../tests/qa/SD/screenshots';
  await mkdir(path, { recursive: true });
  await page.screenshot({ path: `${path}/2.7-action-${name}.png`, fullPage: true, animations: 'disabled' });
}

test('SD-DEL-ACT-030 submits a Draft and withdraws it again from the list detail in three languages and two themes', async ({ page }) => {
  let current = saved('Draft');
  const bodies: string[] = [];
  await page.route('**/api/v1/sal/deliveries**', route => {
    const url = new URL(route.request().url());
    if (route.request().method() === 'POST') {
      bodies.push(`${url.pathname.split('/').pop()} ${route.request().postData()}`);
      current = url.pathname.endsWith('/submit') ? saved('Pending', current.version + 1) : saved('Draft', current.version + 1);
      return route.fulfill({ json: { ...current, approvalHistory: null } });
    }
    if (url.pathname.endsWith('/61')) return route.fulfill({ json: current });
    return route.fulfill({ json: list(current.status, current.version) });
  });
  await page.goto('/sal/deliveries');
  await page.getByTestId('sd-delivery-open-61').click();
  await expect(page.getByTestId('sd-delivery-withdraw')).toHaveCount(0);
  await capture(page, 'draft-light-zh-Hant');
  await page.getByTestId('sd-delivery-submit').click();
  await expect(page.getByTestId('erp-message-title')).toContainText('DH-0061');
  await capture(page, 'submit-confirm-light-zh-Hant');
  await page.getByTestId('erp-message-ok').click();
  await expect(page.getByTestId('sd-delivery-action-notice')).toContainText('已送審，待審批（版本 4）');
  await expect(page.getByTestId('sd-delivery-row-61')).toContainText('審批中');
  await expect(page.getByTestId('sd-delivery-detail-status')).toContainText('審批中');
  await expect(page.getByTestId('sd-delivery-approval-history')).toContainText('501');
  expect(bodies).toEqual(['submit {"version":3}']);
  await capture(page, 'submitted-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'submitted-dark-zh-Hant'); await chooseTheme(page, 'light');
  await chooseLanguage(page, 'en');
  await expect(page.getByTestId('sd-delivery-action-notice')).toContainText('pending approval (version 4)');
  await capture(page, 'submitted-light-en');
  await chooseLanguage(page, 'zh-Hans');
  await expect(page.getByTestId('sd-delivery-action-notice')).toContainText('已送审，待审批（版本 4）');
  await capture(page, 'submitted-light-zh-Hans');
  await chooseLanguage(page, 'zh-Hant');
  await page.getByTestId('sd-delivery-withdraw').click();
  await page.getByTestId('erp-message-ok').click();
  await expect(page.getByTestId('sd-delivery-action-notice')).toContainText('已撤回為草稿（版本 5）');
  await expect(page.getByTestId('sd-delivery-row-61')).toContainText('草稿');
  await expect(page.getByTestId('sd-delivery-submit')).toBeVisible();
  expect(bodies).toEqual(['submit {"version":3}', 'withdraw {"version":4}']);
  await capture(page, 'withdrawn-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'withdrawn-dark-zh-Hant');
});

test('SD-DEL-ACT-031 line 422, INV stock and 500 refusals change nothing; zero-step posting and Draft delete are reported', async ({ page, pageErrors }) => {
  pageErrors.allowApi5xx(/\/api\/v1\/sal\/deliveries/, 500);
  pageErrors.allow(/Failed to load resource: the server responded with a status of (422|500)/);
  let current: ReturnType<typeof saved> | null = saved('Draft');
  let deleteUrl = '';
  const replies: { status: number; json: object }[] = [
    { status: 422, json: { status: 422, errorCode: 'SAL.DELIVERY.TRACKING_REQUIRED', lineNo: 2, traceId: 't-422' } },
    { status: 422, json: { status: 422, errorCode: 'INV.STOCK.INSUFFICIENT', traceId: 't-stock' } },
    { status: 500, json: { status: 500, traceId: 'trace-sd-act-500' } }];
  await page.route('**/api/v1/sal/deliveries**', route => {
    const url = new URL(route.request().url());
    const method = route.request().method();
    if (method === 'POST') {
      const reply = replies.shift();
      if (reply) return route.fulfill(reply);
      current = saved('Posted', 4);
      return route.fulfill({ json: { ...current, approvalHistory: null } });
    }
    if (method === 'DELETE') { deleteUrl = route.request().url(); current = null; return route.fulfill({ status: 204 }); }
    if (url.pathname.endsWith('/61')) return current ? route.fulfill({ json: current }) : route.fulfill({ status: 404, json: { errorCode: 'SAL.DELIVERY.NOT_FOUND' } });
    return route.fulfill({ json: list(current?.status ?? null, current?.version) });
  });
  await page.goto('/sal/deliveries');
  await page.getByTestId('sd-delivery-open-61').click();
  await page.getByTestId('sd-delivery-submit').click();
  await page.getByTestId('erp-message-ok').click();
  await expect(page.getByTestId('sd-delivery-line-error-72')).toContainText('批號或序號');
  await expect(page.getByTestId('sd-delivery-action-error')).toContainText('第 2 行');
  await expect(page.getByTestId('sd-delivery-action-unchanged')).toContainText('仍為草稿');
  await expect(page.getByTestId('sd-delivery-tracking-72')).toHaveText('未選取');
  await expect(page.getByTestId('sd-delivery-detail-status')).toContainText('草稿');
  await capture(page, 'line-422-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'line-422-dark-zh-Hant'); await chooseTheme(page, 'light');
  await page.getByTestId('sd-delivery-submit').click();
  await page.getByTestId('erp-message-ok').click();
  await expect(page.getByTestId('sd-delivery-action-error')).toContainText('庫存不足');
  await expect(page.getByTestId('sd-delivery-action-stock-hint')).toContainText('倉庫、儲位');
  await expect(page.getByTestId('sd-delivery-line-error-72')).toHaveCount(0);
  await capture(page, 'stock-422-light-zh-Hant');
  await page.getByTestId('sd-delivery-submit').click();
  await page.getByTestId('erp-message-ok').click();
  await expect(page.getByTestId('sd-delivery-action-stale')).toContainText('無法確認操作結果');
  await expect(page.getByTestId('sd-delivery-action-error')).toContainText('trace-sd-act-500');
  await expect(page.getByTestId('sd-delivery-submit')).toBeDisabled();
  await expect(page.getByTestId('sd-delivery-action-notice')).toHaveCount(0);
  await capture(page, 'server-error-light-zh-Hant');
  await page.getByTestId('sd-delivery-action-refresh').click();
  await expect(page.getByTestId('sd-delivery-action-stale')).toHaveCount(0);
  await page.getByTestId('sd-delivery-submit').click();
  await page.getByTestId('erp-message-ok').click();
  await expect(page.getByTestId('sd-delivery-action-notice')).toContainText('無需審批，已直接過帳並扣減庫存');
  await expect(page.getByTestId('sd-delivery-movement-71')).toContainText('9001');
  await expect(page.getByTestId('sd-delivery-row-61')).toContainText('已過帳');
  await capture(page, 'posted-light-zh-Hant');
  current = saved('Draft', 5);
  await page.getByTestId('sd-delivery-open-61').click();
  await page.getByTestId('sd-delivery-delete').click();
  await expect(page.getByTestId('erp-message-title')).toContainText('DH-0061');
  await capture(page, 'delete-confirm-light-zh-Hant');
  await page.getByTestId('erp-message-ok').click();
  await expect(page.getByTestId('sd-delivery-action-notice')).toContainText('草稿 DH-0061 已刪除');
  expect(new URL(deleteUrl).searchParams.get('version')).toBe('5');
  await expect(page.getByTestId('sd-delivery-row-61')).toHaveCount(0);
  await capture(page, 'deleted-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'deleted-dark-zh-Hant');
});

test('SD-DEL-ACT-032 INV stock refusals mark every delivery line they name (ADR-154)', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 422/);
  const replies = [
    { status: 422, json: { status: 422, errorCode: 'INV.STOCK.INSUFFICIENT', lineNo: 1, lineNos: [1, 2], traceId: 't-many' } },
    { status: 422, json: { status: 422, errorCode: 'INV.SERIAL.NOT_AVAILABLE', lineNo: 2, lineNos: [2], traceId: 't-one' } }];
  await page.route('**/api/v1/sal/deliveries**', route => {
    const url = new URL(route.request().url());
    if (route.request().method() === 'POST') return route.fulfill(replies.shift()!);
    if (url.pathname.endsWith('/61')) return route.fulfill({ json: saved('Draft') });
    return route.fulfill({ json: list('Draft') });
  });
  await page.goto('/sal/deliveries');
  await page.getByTestId('sd-delivery-open-61').click();
  await page.getByTestId('sd-delivery-submit').click();
  await page.getByTestId('erp-message-ok').click();
  await expect(page.getByTestId('sd-delivery-action-error')).toContainText('第 1、2 行');
  await expect(page.getByTestId('sd-delivery-line-error-71')).toContainText('庫存不足');
  await expect(page.getByTestId('sd-delivery-line-error-72')).toContainText('庫存不足');
  await expect(page.getByTestId('sd-delivery-line-71')).toHaveClass(/failed/);
  await expect(page.getByTestId('sd-delivery-line-72')).toHaveClass(/failed/);
  await expect(page.getByTestId('sd-delivery-action-stock-hint')).toContainText('倉庫、儲位');
  await expect(page.getByTestId('sd-delivery-action-unchanged')).toContainText('仍為草稿');
  await capture(page, 'stock-lines-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'stock-lines-dark-zh-Hant'); await chooseTheme(page, 'light');
  await chooseLanguage(page, 'en');
  await expect(page.getByTestId('sd-delivery-action-error')).toContainText('lines 1, 2');
  await capture(page, 'stock-lines-light-en');
  await chooseLanguage(page, 'zh-Hans');
  await expect(page.getByTestId('sd-delivery-action-error')).toContainText('第 1、2 行');
  await capture(page, 'stock-lines-light-zh-Hans');
  await chooseLanguage(page, 'zh-Hant');
  await page.getByTestId('sd-delivery-submit').click();
  await page.getByTestId('erp-message-ok').click();
  await expect(page.getByTestId('sd-delivery-action-error')).toContainText('第 2 行');
  await expect(page.getByTestId('sd-delivery-line-error-72')).toBeVisible();
  await expect(page.getByTestId('sd-delivery-line-error-71')).toHaveCount(0);
  await capture(page, 'serial-line-light-zh-Hant');
});
