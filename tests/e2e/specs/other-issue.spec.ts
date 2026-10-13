import { E2E_AUTHORIZATION, chooseLanguage, chooseTheme, expect, test } from '../support/fixtures';

const row = { id: 7, documentNo: 'II-0007', businessDate: '2026-10-13', status: 'Draft',
  remark: 'Opening', lineCount: 1, totalQuantity: 1, totalAmount: 2, version: 1 };

test('INV-ISS-004 creates a draft from MD selections and submits the saved row', async ({ page }) => {
  await page.route('**/api/v1/md/items?**', route => route.fulfill({ json: { items: [
    { id: 3, code: 'ITEM', name: 'Bolt', isActive: true }], totalCount: 1 } }));
  await page.route('**/api/v1/md/warehouses?**', route => route.fulfill({ json: { items: [
    { id: 4, code: 'WH', name: 'Main', isActive: true }], totalCount: 1 } }));
  await page.route('**/api/v1/md/warehouses/4/locations?**', route => route.fulfill({ json: { items: [
    { id: 5, code: 'A1', name: 'Shelf A1', isActive: true }], totalCount: 1 } }));
  let saved: Record<string, unknown> | null = null;
  let submitted: Record<string, unknown> | null = null;
  await page.route('**/api/v1/inv/other-issues**', route => {
    const request = route.request(); const path = new URL(request.url()).pathname;
    if (request.method() === 'POST' && path.endsWith('/submit')) {
      submitted = request.postDataJSON() as Record<string, unknown>; return route.fulfill({ json: { ...row, status: 'PendingApproval' } });
    }
    if (request.method() === 'POST') { saved = request.postDataJSON() as Record<string, unknown>;
      return route.fulfill({ status: 201, json: { ...row, lines: [] } }); }
    if (path.endsWith('/7')) return route.fulfill({ json: { ...row, lines: [] } });
    return route.fulfill({ json: { items: [row], totalCount: 1, page: 1, pageSize: 20 } });
  });
  await page.goto('/inv/other-issues');
  await expect(page.getByTestId('inv-issue-row-7')).toBeVisible();
  await page.getByTestId('inv-issue-new').click();
  await page.getByTestId('inv-issue-date').fill('2026-10-13');
  const line = page.getByTestId('inv-issue-line-0');
  await line.getByRole('combobox', { name: '物料' }).selectOption({ label: 'ITEM · Bolt' });
  await line.getByRole('combobox', { name: '倉庫' }).selectOption({ label: 'WH · Main' });
  await line.getByRole('combobox', { name: '儲位' }).selectOption({ label: 'A1 · Shelf A1' });
  await line.getByRole('textbox', { name: '數量' }).fill('1.000001');
  await line.getByRole('textbox', { name: '估算單價' }).fill('2.500001');
  await page.getByTestId('inv-issue-save').click();
  await expect.poll(() => saved).toMatchObject({ businessDate: '2026-10-13',
    lines: [{ itemId: 3, warehouseId: 4, locationId: 5, quantity: '1.000001', unitCost: '2.500001' }] });
  await expect(page.getByTestId('inv-issue-row-7')).toBeVisible();
  await page.getByTestId('inv-issue-row-7').getByRole('button', { name: '送審' }).click();
  await expect.poll(() => submitted).toEqual({ version: 1 });
});

test.describe('INV-ISS-004 read only access', () => {
  test.use({ authorization: { ...E2E_AUTHORIZATION, isFullAccess: false,
    permissions: [{ code: 'INV.OTHER_ISSUE.VIEW', dataScope: 'Company' }] } });
  test('hides draft actions and guards create route', async ({ page }) => {
    await page.route('**/api/v1/inv/other-issues**', route => route.fulfill({ json: { items: [row], totalCount: 1 } }));
    await page.goto('/inv/other-issues');
    await expect(page.getByTestId('inv-issue-row-7')).toBeVisible();
    await expect(page.getByTestId('inv-issue-new')).toHaveCount(0);
    await expect(page.getByTestId('inv-issue-row-7').getByRole('button', { name: '送審' })).toHaveCount(0);
    await expect(page.getByTestId('inv-issue-row-7').getByRole('button', { name: '刪除' })).toHaveCount(0);
    await expect(page.getByTestId('inv-issue-row-7').getByRole('link', { name: '編輯出庫單' })).toHaveCount(0);
    await expect(page.getByRole('button', { name: '匯出供 Excel 使用' })).toBeVisible();
    await page.goto('/inv/other-issues/new');
    await expect(page.getByTestId('error-page-403')).toBeVisible();
  });
});

test('INV-ISS-004 edits a rejected issue and exports the filtered register', async ({ page }) => {
  await page.addInitScript(() => { window.print = () => sessionStorage.setItem('issue-printed', 'yes'); });
  await page.route('**/api/v1/md/items?**', route => route.fulfill({ json: { items: [
    { id: 3, code: 'ITEM', name: 'Bolt', isActive: true }], totalCount: 1 } }));
  await page.route('**/api/v1/md/warehouses?**', route => route.fulfill({ json: { items: [
    { id: 4, code: 'WH', name: 'Main', isActive: true }], totalCount: 1 } }));
  await page.route('**/api/v1/md/warehouses/4/locations?**', route => route.fulfill({ json: { items: [], totalCount: 0 } }));
  let updated: Record<string, unknown> | null = null;
  const rejected = { ...row, status: 'Rejected', version: 4 };
  await page.route('**/api/v1/inv/other-issues**', route => {
    const request = route.request(); const path = new URL(request.url()).pathname;
    if (request.method() === 'PUT') { updated = request.postDataJSON() as Record<string, unknown>;
      return route.fulfill({ json: { ...rejected, status: 'Draft', version: 5, lines: [] } }); }
    if (path.endsWith('/7')) return route.fulfill({ json: { ...rejected,
      lines: [{ id: 9, lineNo: 1, itemId: 3, warehouseId: 4, locationId: null, quantity: 1, unitCost: 2, amount: 2 }] } });
    return route.fulfill({ json: { items: [rejected], totalCount: 1, page: 1, pageSize: 20 } });
  });
  await page.goto('/inv/other-issues');
  await expect(page.getByTestId('inv-issue-row-7')).toContainText('已駁回');
  const downloadPromise = page.waitForEvent('download');
  await page.getByRole('button', { name: '匯出供 Excel 使用' }).click();
  const download = await downloadPromise;
  expect(download.suggestedFilename()).toBe('other-issues.csv');
  await page.getByTestId('inv-issue-row-7').getByRole('button', { name: '列印' }).click();
  await expect(page.getByTestId('inv-issue-detail')).toContainText('ITEM · Bolt');
  await expect.poll(() => page.evaluate(() => sessionStorage.getItem('issue-printed'))).toBe('yes');
  await page.getByTestId('inv-issue-row-7').getByRole('link', { name: '編輯出庫單' }).click();
  const quantity = page.getByTestId('inv-issue-line-0').getByRole('textbox', { name: '數量' });
  await expect(quantity).toHaveValue('1');
  await quantity.fill('3');
  await expect(quantity).toHaveValue('3');
  await page.getByTestId('inv-issue-save').click();
  await expect.poll(() => updated).toMatchObject({ version: 4, lines: [{ quantity: '3', itemId: 3, warehouseId: 4 }] });
});

test('INV-ISS-004 keeps labels readable in three languages and dark mode', async ({ page }) => {
  await page.route('**/api/v1/inv/other-issues**', route => route.fulfill({ json: { items: [], totalCount: 0 } }));
  await page.goto('/inv/other-issues');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('其他出庫');
  await chooseLanguage(page, 'en');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Other issues');
  await chooseLanguage(page, 'zh-Hans');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('其他出库');
  await chooseTheme(page, 'dark');
  await expect(page.locator('link#erp-theme-dark')).toHaveAttribute('href', 'theme-dark.css');
  await expect(page.getByTestId('inv-issue-table')).toBeVisible();
});

test('INV-ISS-004 keeps the draft after insufficient stock on submit', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 422/);
  await page.route('**/api/v1/inv/other-issues**', route => {
    const request = route.request();
    if (request.method() === 'POST' && new URL(request.url()).pathname.endsWith('/submit')) {
      return route.fulfill({ status: 422, json: { errorCode: 'INV.STOCK.INSUFFICIENT' } });
    }
    return route.fulfill({ json: { items: [row], totalCount: 1 } });
  });
  await page.goto('/inv/other-issues');
  await page.getByTestId('inv-issue-row-7').getByRole('button', { name: '送審' }).click();
  await expect(page.getByText('庫存不足。')).toBeVisible();
  await expect(page.getByTestId('inv-issue-row-7')).toContainText('草稿');
  await expect(page.getByTestId('inv-issue-row-7').getByRole('button', { name: '送審' })).toBeEnabled();
});


