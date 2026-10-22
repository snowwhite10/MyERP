import { mkdir } from 'node:fs/promises';
import type { Page } from '@playwright/test';
import { E2E_AUTHORIZATION, chooseLanguage, chooseTheme, expect, test } from '../support/fixtures';

const saved = { id: 17, documentNo: 'RFQ-0017', businessDate: '2028-02-29', replyDeadline: '2028-03-01',
  ownerId: 4, status: 'Draft', version: 1, remark: null, sentAt: null, createdAt: '2028-02-29T09:00:00',
  lines: [{ id: 170, lineNo: 1, itemId: 11, requisitionLineId: null, requiredDate: '2028-03-01', quantity: 1.123456, remark: null }],
  invitations: [{ id: 171, supplierId: 7, supplierCodeSnapshot: 'SUP-7', supplierNameSnapshot: 'Vendor', sentAt: null }] };
const item = { id: 11, code: 'ITEM11', name: 'Bolt', isActive: true };
const supplier = { id: 7, code: 'SUP-7', name: 'Vendor', isActive: true, isApproved: true };
const source = { id: 91, documentNo: 'PR-0091', lineNo: 2, itemId: 11, remainingQuantity: 3.5 };

async function capture(page: Page, name: string): Promise<void> {
  await mkdir('../../tests/qa/PUR/screenshots', { recursive: true });
  await expect(page.locator('.cdk-overlay-backdrop-showing')).toHaveCount(0);
  await expect(page.locator('.ant-spin-spinning')).toHaveCount(0);
  await page.screenshot({ path: `../../tests/qa/PUR/screenshots/${name}.png`, fullPage: true });
}
async function fillDraft(page: Page): Promise<void> {
  await page.getByTestId('pur-rfq-date').fill('2028-02-29');
  await page.getByTestId('pur-rfq-deadline').fill('2028-03-01');
  await page.getByTestId('pur-rfq-item-0').click();
  await page.getByTestId('erp-select-row-11').click();
  await page.getByTestId('erp-select-confirm').click();
  await page.getByTestId('pur-rfq-required-0').fill('2028-03-01');
  await page.getByTestId('pur-rfq-quantity-0').fill('1.123456');
  await page.getByTestId('pur-rfq-supplier').click();
  await page.getByTestId('erp-select-row-7').click();
  await page.getByTestId('erp-select-confirm').click();
}

test('PUR-RFQ-076 creates one complete draft and shows the saved record', async ({ page }) => {
  let persisted: typeof saved | null = null; let body: any;
  await page.route('**/api/v1/pur/rfqs**', route => {
    const path = new URL(route.request().url()).pathname;
    if (path.endsWith('/item-options')) return route.fulfill({ json: { items: [item], totalCount: 1 } });
    if (path.endsWith('/supplier-options')) return route.fulfill({ json: { items: [supplier], totalCount: 1 } });
    if (route.request().method() === 'POST') { body = route.request().postDataJSON(); persisted = saved;
      return route.fulfill({ status: 201, json: saved }); }
    return route.fulfill({ json: { items: persisted ? [{ ...saved, lineCount: 1, invitationCount: 1 }] : [],
      totalCount: persisted ? 1 : 0 } });
  });
  await page.goto('/pur/rfqs/new');
  await expect(page.getByTestId('pur-rfq-form')).toBeVisible();
  await capture(page, '4.12-normal-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, '4.12-normal-dark-zh-Hant');
  await chooseTheme(page, 'light'); await chooseLanguage(page, 'en');
  await capture(page, '4.12-normal-light-en');
  await chooseLanguage(page, 'zh-Hans'); await capture(page, '4.12-normal-light-zh-Hans');
  await chooseLanguage(page, 'zh-Hant');
  await fillDraft(page);
  await page.getByTestId('pur-rfq-save').click();
  await expect(page.getByTestId('pur-rfq-row-17')).toContainText('RFQ-0017');
  expect(body.lines[0].quantity).toBe('1.123456');
  expect(body.supplierIds).toEqual([7]);
  expect(persisted?.lines[0].quantity).toBe(1.123456);
  await capture(page, '4.12-success-light-zh-Hant');
});

test('PUR-RFQ-081 selects an approved requisition line and saves its actual ID', async ({ page }) => {
  let body: any = null;
  await page.route('**/api/v1/pur/rfqs**', route => {
    const path = new URL(route.request().url()).pathname;
    if (path.endsWith('/item-options')) return route.fulfill({ json: { items: [item], totalCount: 1 } });
    if (path.endsWith('/supplier-options')) return route.fulfill({ json: { items: [supplier], totalCount: 1 } });
    if (path.endsWith('/source-options')) {
      expect(new URL(route.request().url()).searchParams.get('itemId')).toBe('11');
      return route.fulfill({ json: { items: [source], totalCount: 1 } });
    }
    if (route.request().method() === 'POST') { body = route.request().postDataJSON();
      return route.fulfill({ status: 201, json: { ...saved, lines: [{ ...saved.lines[0], requisitionLineId: 91 }] } }); }
    return route.fulfill({ json: { items: body ? [{ ...saved, lineCount: 1, invitationCount: 1 }] : [], totalCount: body ? 1 : 0 } });
  });
  await page.goto('/pur/rfqs/new'); await fillDraft(page);
  await page.getByTestId('pur-rfq-source-0').click();
  await expect(page.getByTestId('erp-select-row-91')).toContainText('PR-0091');
  await page.getByTestId('erp-select-row-91').click();
  await page.getByTestId('erp-select-confirm').click();
  await expect(page.getByTestId('pur-rfq-source-0')).toContainText('PR-0091');
  await capture(page, '4.12-source-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, '4.12-source-dark-zh-Hant');
  await chooseTheme(page, 'light');
  await page.getByTestId('pur-rfq-save').click();
  await expect(page.getByTestId('pur-rfq-row-17')).toContainText('RFQ-0017');
  expect(body.lines[0].requisitionLineId).toBe(91);
});

test('PUR-RFQ-077 rejects invalid fields without writing and retains input', async ({ page }) => {
  let writes = 0;
  await page.route('**/api/v1/pur/rfqs**', route => {
    if (route.request().method() === 'POST') writes++;
    return route.fulfill({ json: { items: [], totalCount: 0 } });
  });
  await page.goto('/pur/rfqs/new');
  await page.getByTestId('pur-rfq-save').click();
  await expect(page.getByTestId('pur-rfq-form-error')).toContainText('請修正');
  await expect(page.getByTestId('pur-rfq-line-0')).toContainText('請選擇物料');
  expect(writes).toBe(0);
  await capture(page, '4.12-validation-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, '4.12-validation-dark-zh-Hant');
  await page.getByTestId('pur-rfq-date').fill('2028-02-29');
  await page.getByTestId('pur-rfq-deadline').fill('2028-02-28');
  await page.getByTestId('pur-rfq-quantity-0').fill('1.1234567');
  await page.getByTestId('pur-rfq-save').click();
  await expect(page.getByTestId('pur-rfq-form')).toContainText('回覆期限須有效');
  await expect(page.getByTestId('pur-rfq-quantity-0')).toHaveValue('1.1234567');
  expect(writes).toBe(0);
});

test('PUR-RFQ-078/080 edit requires draft and current version; stale save keeps fields', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 409/);
  let updates = 0; let savedVersion = saved;
  await page.route('**/api/v1/pur/rfqs/**', route => {
    const path = new URL(route.request().url()).pathname;
    if (path.endsWith('/saved-options')) return route.fulfill({ json: { items: [item], suppliers: [supplier] } });
    if (route.request().method() === 'PUT') { updates++;
      return route.fulfill({ status: 409, json: { errorCode: 'COMMON.CONCURRENCY.CONFLICT' } }); }
    return route.fulfill({ json: savedVersion });
  });
  await page.goto('/pur/rfqs/17/edit');
  await expect(page.getByTestId('pur-rfq-item-0')).toContainText('ITEM11');
  await expect(page.getByTestId('pur-rfq-form')).toContainText('SUP-7');
  await expect(page.getByTestId('pur-rfq-save')).toBeEnabled();
  await capture(page, '4.13-normal-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, '4.13-normal-dark-zh-Hant');
  await chooseTheme(page, 'light');
  await page.getByTestId('pur-rfq-save').click();
  await expect(page.getByTestId('pur-rfq-form-error')).toContainText('沒有需要保存');
  expect(updates).toBe(0);
  await page.getByTestId('pur-rfq-quantity-0').fill('2.000001');
  await page.getByTestId('pur-rfq-save').click();
  await expect(page.getByTestId('pur-rfq-reload')).toBeVisible();
  await expect(page.getByTestId('pur-rfq-quantity-0')).toHaveValue('2.000001');
  await expect(page.getByTestId('pur-rfq-form-success')).toHaveCount(0);
  expect(updates).toBe(1);
  await capture(page, '4.13-conflict-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, '4.13-conflict-dark-zh-Hant');
  savedVersion = { ...saved, status: 'Sent' };
  await chooseTheme(page, 'light');
  await page.reload();
  await expect(page.getByTestId('pur-rfq-not-editable')).toBeVisible();
  await expect(page.getByTestId('pur-rfq-save')).toBeDisabled();
  await capture(page, '4.13-sent-light-zh-Hant');
});

for (const issue of [
  { name: 'validation', status: 400, code: 'COMMON.REQUEST.VALIDATION_FAILED', message: '第 1 行' },
  { name: 'source', status: 422, code: 'PUR.RFQ.INVALID_SOURCE', message: '請購來源不可用' },
  { name: 'server', status: 500, code: '', message: 'rfq-form-trace' },
] as const) {
  test(`PUR-RFQ-079/082 explains ${issue.name} and keeps the complete draft`, async ({ page, pageErrors }) => {
    pageErrors.allow(new RegExp(`Failed to load resource: the server responded with a status of ${issue.status}`));
    if (issue.status === 500) pageErrors.allowApi5xx(/\/api\/v1\/pur\/rfqs$/, 500);
    let stored = 0;
    await page.route('**/api/v1/pur/rfqs**', route => {
      const path = new URL(route.request().url()).pathname;
      if (path.endsWith('/item-options')) return route.fulfill({ json: { items: [item], totalCount: 1 } });
      if (path.endsWith('/supplier-options')) return route.fulfill({ json: { items: [supplier], totalCount: 1 } });
      if (route.request().method() === 'POST') return route.fulfill({ status: issue.status, json:
        issue.status === 500 ? { traceId: 'rfq-form-trace' } : { errorCode: issue.code,
          errors: { 'lines[0].itemId': ['Unavailable reference'] } } });
      return route.fulfill({ json: { items: [], totalCount: stored } });
    });
    await page.goto('/pur/rfqs/new'); await fillDraft(page);
    await page.getByTestId('pur-rfq-save').click();
    await expect(page.getByTestId('pur-rfq-form-error')).toContainText(issue.message);
    await expect(page.getByTestId('pur-rfq-quantity-0')).toHaveValue('1.123456');
    await expect(page.getByTestId('pur-rfq-form-success')).toHaveCount(0);
    expect(stored).toBe(0);
    await capture(page, `4.12-${issue.name}-light-zh-Hant`);
    await chooseTheme(page, 'dark'); await capture(page, `4.12-${issue.name}-dark-zh-Hant`);
  });
}

test('PUR-RFQ-082 explains network failure and preserves a retryable draft', async ({ page, pageErrors }) => {
  pageErrors.allow(/net::ERR_/);
  let failed = false; let persisted = false;
  await page.route('**/api/v1/pur/rfqs**', route => {
    const path = new URL(route.request().url()).pathname;
    if (path.endsWith('/item-options')) return route.fulfill({ json: { items: [item], totalCount: 1 } });
    if (path.endsWith('/supplier-options')) return route.fulfill({ json: { items: [supplier], totalCount: 1 } });
    if (route.request().method() === 'POST') { if (!failed) { failed = true; return route.abort('internetdisconnected'); }
      persisted = true; return route.fulfill({ status: 201, json: saved }); }
    return route.fulfill({ json: { items: persisted ? [{ ...saved, lineCount: 1, invitationCount: 1 }] : [], totalCount: persisted ? 1 : 0 } });
  });
  await page.goto('/pur/rfqs/new'); await fillDraft(page);
  await page.getByTestId('pur-rfq-save').click();
  await expect(page.getByTestId('pur-rfq-form-error')).toContainText('網路');
  await expect(page.getByTestId('pur-rfq-quantity-0')).toHaveValue('1.123456');
  await capture(page, '4.12-network-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, '4.12-network-dark-zh-Hant');
  await page.getByTestId('pur-rfq-save').click();
  await expect(page.getByTestId('pur-rfq-row-17')).toContainText('RFQ-0017');
  expect(persisted).toBe(true);
});

test.describe('RFQ route permissions', () => {
  test.use({ authorization: { ...E2E_AUTHORIZATION, isFullAccess: false, permissions: [
    { code: 'PUR.RFQ.VIEW', dataScope: 'Company' as const },
  ] } });
  test('PUR-RFQ-080 refuses CREATE and EDIT without calling document APIs', async ({ page }) => {
    let documentReads = 0;
    await page.route('**/api/v1/pur/rfqs**', route => { documentReads++; return route.fulfill({ json: { items: [], totalCount: 0 } }); });
    await page.goto('/pur/rfqs/new');
    await expect(page).toHaveURL(/forbidden/);
    await page.goto('/pur/rfqs/17/edit');
    await expect(page).toHaveURL(/forbidden/);
    expect(documentReads).toBe(0);
  });
});

test('PUR-RFQ-080 clears an edited draft and saved references after company switch', async ({ page }) => {
  await page.route('**/api/v1/pur/rfqs/**', route => {
    const secondCompany = route.request().headers()['x-company-id'] === '2';
    if (route.request().url().endsWith('/saved-options')) return route.fulfill({ json: secondCompany ?
      { items: [{ ...item, id: 12, code: 'ITEM12' }], suppliers: [{ ...supplier, id: 8, code: 'SUP-8' }] } :
      { items: [item], suppliers: [supplier] } });
    return route.fulfill({ json: secondCompany ? { ...saved, businessDate: '2028-03-02', replyDeadline: '2028-03-03',
      lines: [{ ...saved.lines[0], itemId: 12 }], invitations: [{ ...saved.invitations[0], supplierId: 8 }] } : saved });
  });
  await page.goto('/pur/rfqs/17/edit');
  await expect(page.getByTestId('pur-rfq-item-0')).toContainText('ITEM11');
  await page.getByTestId('layout-company-select').click();
  await page.locator('nz-option-item', { hasText: 'HK01 Hong Kong' }).click();
  await expect(page.getByTestId('pur-rfq-date')).toHaveValue('2028-03-02');
  await expect(page.getByTestId('pur-rfq-item-0')).toContainText('ITEM12');
  await expect(page.getByTestId('pur-rfq-form')).not.toContainText('ITEM11');
  await expect(page.getByTestId('pur-rfq-form')).not.toContainText('SUP-7');
  await page.getByRole('heading', { name: '修訂詢價單' }).click();
  await capture(page, '4.13-company-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, '4.13-company-dark-zh-Hant');
});

test('PUR-RFQ-080 warns before leaving an unsaved CREATE draft', async ({ page }) => {
  await page.goto('/pur/rfqs/new');
  await page.getByTestId('pur-rfq-date').fill('2028-02-29');
  await page.getByRole('link', { name: /返回|Back/i }).click();
  await expect(page.getByRole('dialog')).toBeVisible();
  await page.waitForTimeout(350);
  await page.screenshot({ path: '../../tests/qa/PUR/screenshots/4.12-unsaved-light-zh-Hant.png', fullPage: true });
  await page.getByRole('button', { name: /繼續編輯|Keep editing/i }).click();
  await expect(page.getByTestId('pur-rfq-date')).toHaveValue('2028-02-29');
});

test('PUR-RFQ-081 shows an empty source choice and retains the draft', async ({ page }) => {
  let writes = 0;
  await page.route('**/api/v1/pur/rfqs**', route => {
    const path = new URL(route.request().url()).pathname;
    if (path.endsWith('/item-options')) return route.fulfill({ json: { items: [item], totalCount: 1 } });
    if (path.endsWith('/supplier-options')) return route.fulfill({ json: { items: [supplier], totalCount: 1 } });
    if (path.endsWith('/source-options')) return route.fulfill({ json: { items: [], totalCount: 0 } });
    if (route.request().method() === 'POST') writes++;
    return route.fulfill({ json: { items: [], totalCount: 0 } });
  });
  await page.goto('/pur/rfqs/new'); await fillDraft(page);
  await page.getByTestId('pur-rfq-source-0').click();
  await expect(page.getByTestId('erp-select-table')).toContainText('暫無資料');
  await expect(page.getByTestId('erp-select-confirm')).toBeDisabled();
  await page.waitForTimeout(350);
  await page.screenshot({ path: '../../tests/qa/PUR/screenshots/4.12-source-empty-light-zh-Hant.png', fullPage: true });
  await page.getByTestId('erp-select-cancel').click();
  await expect(page.getByTestId('pur-rfq-quantity-0')).toHaveValue('1.123456');
  expect(writes).toBe(0);
});
