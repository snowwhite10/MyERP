import { mkdir } from 'node:fs/promises';
import type { Page } from '@playwright/test';
import { E2E_AUTHORIZATION, chooseLanguage, chooseTheme, expect, test } from '../support/fixtures';

const saved = { id: 7, documentNo: 'REQ-0007', businessDate: '2028-02-29', requesterId: 4,
  status: 'Draft', lineCount: 1, version: 1, branchId: 2, departmentId: 3, remark: 'Saved',
  createdAt: '2028-02-29T10:00:00', lines: [{ id: 70, lineNo: 1, itemId: 11,
    requiredDate: '2028-03-01', quantity: 1.123456, orderedQuantity: 0, remark: null }], approvalHistory: [] };
async function capture(page: Page, name: string): Promise<void> {
  const directory = '../../tests/qa/PUR/screenshots';
  await mkdir(directory, { recursive: true });
  await expect(page.getByTestId('erp-select-dialog')).toHaveCount(0);
  await expect(page.locator('.ant-spin-spinning')).toHaveCount(0);
  await expect(page.locator('.cdk-overlay-backdrop-showing')).toHaveCount(0);
  await page.waitForTimeout(250);
  await page.screenshot({ path: `${directory}/${name}.png`, fullPage: true });
}

test('PUR-UI-002/005 creates with an authorized MD item and exact quantity', async ({ page }) => {
  let persisted: typeof saved | null = null; let requestBody: any;
  await page.route('**/api/v1/pur/requisitions**', route => {
    const url = new URL(route.request().url());
    if (url.pathname.endsWith('/item-options')) return route.fulfill({ json: { items: [
      { id: 11, code: 'ITEM11', name: 'Bolt', isActive: true }], totalCount: 1 } });
    if (route.request().method() === 'POST') { requestBody = route.request().postDataJSON(); persisted = saved;
      return route.fulfill({ status: 201, json: saved }); }
    return route.fulfill({ json: { items: persisted ? [persisted] : [], totalCount: persisted ? 1 : 0 } });
  });
  await page.goto('/pur/requisitions/new');
  const form = page.getByTestId('pur-requisition-form');
  await expect(form).toBeVisible();
  await capture(page, '4.9-normal-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, '4.9-normal-dark-zh-Hant');
  await chooseTheme(page, 'light');
  await form.getByTestId('pur-form-date').fill('2028-02-29');
  await form.getByRole('button', { name: '選擇物料' }).click();
  await expect(page.getByTestId('erp-select-row-11')).toBeVisible();
  await page.getByTestId('erp-select-row-11').click(); await page.getByTestId('erp-select-confirm').click();
  await form.getByTestId('pur-form-line-0').getByLabel('需求日期').fill('2028-03-01');
  await form.getByTestId('pur-form-line-0').getByLabel('請購量').fill('1.123456');
  await form.getByTestId('pur-form-save').click();
  await expect(page.getByTestId('pur-requisition-row-7')).toContainText('REQ-0007');
  expect(requestBody.lines[0].quantity).toBe('1.123456');
  expect(requestBody).not.toHaveProperty('requesterId');
  expect(persisted?.lines[0].quantity).toBe(1.123456);
  await capture(page, '4.9-success-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, '4.9-success-dark-zh-Hant');
});

test('PUR-UI-002/014 shows inline errors and does not create invalid data', async ({ page }) => {
  let writes = 0;
  await page.route('**/api/v1/pur/requisitions**', route => {
    if (route.request().method() === 'POST') writes++;
    return route.fulfill({ json: { items: [], totalCount: 0 } });
  });
  await page.goto('/pur/requisitions/new');
  await page.getByTestId('pur-form-save').click();
  await expect(page.getByTestId('pur-form-error')).toContainText('請修正標示的欄位');
  await expect(page.getByTestId('pur-form-line-0')).toContainText('請選擇物料');
  expect(writes).toBe(0);
  await capture(page, '4.9-validation-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, '4.9-validation-dark-zh-Hant');
  await chooseTheme(page, 'light'); await chooseLanguage(page, 'en');
  await expect(page.getByRole('heading', { name: 'Create requisition' })).toBeVisible();
  await capture(page, '4.9-validation-light-en');
  await chooseLanguage(page, 'zh-Hans');
  await expect(page.getByRole('heading', { name: '建立请购单' })).toBeVisible();
  await capture(page, '4.9-validation-light-zh-Hans');
});

async function fillValidDraft(page: Page): Promise<void> {
  const form = page.getByTestId('pur-requisition-form');
  await form.getByTestId('pur-form-date').fill('2028-02-29');
  await form.getByRole('button', { name: '選擇物料' }).click();
  await page.getByTestId('erp-select-row-11').click();
  await page.getByTestId('erp-select-confirm').click();
  await form.getByTestId('pur-form-line-0').getByLabel('需求日期').fill('2028-03-01');
  await form.getByTestId('pur-form-line-0').getByLabel('請購量').fill('1.123456');
}

for (const issue of [
  { name: 'bad-request', status: 400, code: 'COMMON.REQUEST.VALIDATION_FAILED', message: '輸入資料不正確' },
  { name: 'inactive', status: 422, code: 'MD.ITEM.INACTIVE', message: '此物料已停用' },
  { name: 'server-error', status: 500, code: '', message: 'qa-pur-form-trace' },
] as const) {
  test(`PUR-UI-014 explains ${issue.name} without claiming a save`, async ({ page, pageErrors }) => {
    pageErrors.allow(new RegExp(`Failed to load resource: the server responded with a status of ${issue.status}`));
    if (issue.status === 500) pageErrors.allowApi5xx(/\/api\/v1\/pur\/requisitions$/, 500);
    let savedCount = 0;
    await page.route('**/api/v1/pur/requisitions**', route => {
      const url = new URL(route.request().url());
      if (url.pathname.endsWith('/item-options')) return route.fulfill({ json: { items: [
        { id: 11, code: 'ITEM11', name: 'Bolt', isActive: true }], totalCount: 1 } });
      if (route.request().method() === 'POST') return route.fulfill({ status: issue.status,
        json: issue.status === 500 ? { traceId: 'qa-pur-form-trace' } : {
          errorCode: issue.code, errors: { 'lines[0].itemId': ['Item is inactive'] } } });
      return route.fulfill({ json: { items: Array(savedCount).fill(saved), totalCount: savedCount } });
    });
    await page.goto('/pur/requisitions/new'); await fillValidDraft(page);
    await page.getByTestId('pur-form-save').click();
    await expect(page.getByTestId('pur-form-error')).toContainText(issue.message);
    if (issue.status === 400) {
      await expect(page.getByTestId('pur-form-error')).toContainText('第 1 行物料');
      await expect(page.getByTestId('pur-form-error')).not.toContainText('Item is inactive');
      await expect(page.getByTestId('pur-form-line-0').getByRole('alert')).toContainText('物料已停用');
    }
    await expect(page.getByTestId('pur-form-success')).toHaveCount(0);
    expect(savedCount).toBe(0);
    await capture(page, `4.9-${issue.name}-light-zh-Hant`);
    await chooseTheme(page, 'dark'); await capture(page, `4.9-${issue.name}-dark-zh-Hant`);
    if (issue.status === 400) {
      await chooseLanguage(page, 'en');
      await expect(page.getByTestId('pur-form-line-0').getByRole('alert')).toContainText('Item inactive');
      await capture(page, '4.9-bad-request-dark-en');
    }
  });
}

test('PUR-UI-014 reports a disconnected save and retains fields', async ({ page, pageErrors }) => {
  pageErrors.allow(/net::ERR_/);
  await page.route('**/api/v1/pur/requisitions**', route => {
    if (new URL(route.request().url()).pathname.endsWith('/item-options')) return route.fulfill({ json: {
      items: [{ id: 11, code: 'ITEM11', name: 'Bolt', isActive: true }], totalCount: 1 } });
    if (route.request().method() === 'POST') return route.abort('internetdisconnected');
    return route.fulfill({ json: { items: [], totalCount: 0 } });
  });
  await page.goto('/pur/requisitions/new'); await fillValidDraft(page);
  await page.getByTestId('pur-form-save').click();
  await expect(page.getByTestId('pur-form-error')).toContainText('網路');
  await expect(page.getByTestId('pur-form-line-0').getByLabel('請購量')).toHaveValue('1.123456');
  await capture(page, '4.9-network-error-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, '4.9-network-error-dark-zh-Hant');
});

test('PUR-UI-014 reports a timed out save without reporting success', async ({ page, pageErrors }) => {
  pageErrors.allow(/net::ERR_/);
  await page.route('**/api/v1/pur/requisitions**', route => {
    if (new URL(route.request().url()).pathname.endsWith('/item-options')) return route.fulfill({ json: {
      items: [{ id: 11, code: 'ITEM11', name: 'Bolt', isActive: true }], totalCount: 1 } });
    if (route.request().method() === 'POST') return route.abort('timedout');
    return route.fulfill({ json: { items: [], totalCount: 0 } });
  });
  await page.goto('/pur/requisitions/new'); await fillValidDraft(page);
  await page.getByTestId('pur-form-save').click();
  await expect(page.getByTestId('pur-form-error')).toContainText('無法連線');
  await expect(page.getByTestId('pur-form-success')).toHaveCount(0);
  await capture(page, '4.9-timeout-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, '4.9-timeout-dark-zh-Hant');
});

test('PUR-UI-013 blocks editing a pending document', async ({ page }) => {
  let writes = 0;
  await page.route('**/api/v1/pur/requisitions**', route => {
    if (route.request().method() === 'PUT') writes++;
    if (new URL(route.request().url()).pathname.endsWith('/saved-item-options')) return route.fulfill({ json: [
      { id: 11, code: 'ITEM11', name: 'Bolt', isActive: true }] });
    return route.fulfill({ json: { ...saved, status: 'PendingApproval' } });
  });
  await page.goto('/pur/requisitions/7/edit');
  await expect(page.getByTestId('pur-form-save')).toBeDisabled();
  await expect(page.getByTestId('pur-form-save')).toHaveAttribute('title', /只有草稿或已駁回/);
  await expect(page.getByTestId('pur-form-line-0').getByRole('button', { name: '移除' })).toHaveAttribute('title', /只有草稿或已駁回/);
  expect(writes).toBe(0);
  await capture(page, '4.10-pending-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, '4.10-pending-dark-zh-Hant');
});

test('PUR-UI-004 warns before leaving an unsaved form', async ({ page }) => {
  await page.goto('/pur/requisitions/new');
  await page.getByTestId('pur-form-remark').fill('Not saved');
  await page.getByRole('link', { name: '返回請購列表' }).click();
  await expect(page.getByText('尚未儲存，確定離開？')).toBeVisible();
  await page.getByRole('button', { name: '繼續編輯' }).click();
  await expect(page.getByTestId('pur-form-remark')).toHaveValue('Not saved');
});

test('PUR-UI-004/012/014 revises a rejected document and keeps it on conflict', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 409/);
  let writes = 0;
  await page.route('**/api/v1/pur/requisitions**', route => {
    if (route.request().method() === 'PUT') { writes++; return route.fulfill({ status: 409,
      json: { errorCode: 'COMMON.CONCURRENCY.CONFLICT' } }); }
    if (new URL(route.request().url()).pathname.endsWith('/saved-item-options')) return route.fulfill({ json: [
      { id: 11, code: 'ITEM11', name: 'Bolt', isActive: false }] });
    return route.fulfill({ json: { ...saved, status: 'Rejected' } });
  });
  await page.goto('/pur/requisitions/7/edit');
  const form = page.getByTestId('pur-requisition-form');
  await expect(form.getByTestId('pur-form-date')).toHaveValue('2028-02-29');
  await expect(form).toContainText('ITEM11 · Bolt');
  await expect(form).toContainText('物料已停用');
  await capture(page, '4.10-normal-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, '4.10-normal-dark-zh-Hant');
  await chooseTheme(page, 'light');
  await form.getByTestId('pur-form-remark').fill('Changed');
  await form.getByTestId('pur-form-save').click();
  await expect(page.getByTestId('pur-form-error')).toContainText('資料已被他人修改');
  await expect(page.getByTestId('pur-form-reload')).toBeVisible();
  expect(writes).toBe(1);
  await expect(form.getByTestId('pur-form-remark')).toHaveValue('Changed');
  await capture(page, '4.10-conflict-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, '4.10-conflict-dark-zh-Hant');
  await page.getByTestId('pur-form-reload').click();
  await expect(page.getByText('目前未保存的修改會遺失。')).toBeVisible();
  await page.getByRole('button', { name: '取消' }).click();
  await expect(form.getByTestId('pur-form-remark')).toHaveValue('Changed');
});

test.describe('PUR-UI-012 reader', () => {
  test.use({ authorization: { ...E2E_AUTHORIZATION, isFullAccess: false,
    permissions: [{ code: 'PUR.REQUISITION.VIEW', dataScope: 'Company' }] } });
  test('cannot enter create or edit routes', async ({ page }) => {
    await page.goto('/pur/requisitions/new');
    await expect(page.getByTestId('error-page-403')).toBeVisible();
    await page.goto('/pur/requisitions/7/edit');
    await expect(page.getByTestId('error-page-403')).toBeVisible();
  });
});

test.describe('PUR-FORM-005 editor without VIEW', () => {
  test.use({ authorization: { ...E2E_AUTHORIZATION, isFullAccess: false,
    permissions: [{ code: 'PUR.REQUISITION.EDIT', dataScope: 'Company' }] } });
  test('loads, saves and stays on the edit route', async ({ page }) => {
    let persisted = { ...saved, remark: 'Saved' };
    await page.route('**/api/v1/pur/requisitions**', route => {
      const path = new URL(route.request().url()).pathname;
      if (path.endsWith('/saved-item-options')) return route.fulfill({ json: [
        { id: 11, code: 'ITEM11', name: 'Bolt', isActive: true }] });
      if (route.request().method() === 'PUT') { persisted = { ...persisted,
        remark: (route.request().postDataJSON() as { remark: string }).remark, version: 2 };
        return route.fulfill({ json: persisted }); }
      if (path.endsWith('/edit')) return route.fulfill({ json: persisted });
      return route.fulfill({ status: 403, json: { errorCode: 'COMMON.PERMISSION.DENIED' } });
    });
    await page.goto('/pur/requisitions/7/edit');
    await expect(page.getByTestId('pur-form-remark')).toHaveValue('Saved');
    await page.getByTestId('pur-form-remark').fill('Editor change');
    await page.getByTestId('pur-form-save').click();
    await expect(page.getByTestId('pur-form-success')).toContainText('REQ-0007');
    await expect(page).toHaveURL(/\/pur\/requisitions\/7\/edit$/);
    expect(persisted.remark).toBe('Editor change');
  });
});
