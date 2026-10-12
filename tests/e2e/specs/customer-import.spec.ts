import { E2E_AUTHORIZATION, expect, test } from '../support/fixtures';

test('客戶匯入可下載範本、預覽列號與錯誤、修正後整批提交', async ({ page }) => {
  const rows = [{ id: 1, code: 'OLD', name: 'Existing', categoryId: null, isActive: true, version: 1 }];
  let previewCount = 0;
  let commitCount = 0;
  await page.route('**/api/v1/md/customer-categories**', (route) => route.fulfill({ json: {
    items: [], page: 1, pageSize: 100, totalCount: 0,
  } }));
  await page.route('**/api/v1/md/customers?**', (route) => route.fulfill({ json: {
    items: rows, page: 1, pageSize: 20, totalCount: rows.length,
  } }));
  await page.route('**/api/v1/md/customers/import/template?**', (route) => route.fulfill({
    body: 'code,name\n', headers: { 'content-type': 'text/csv', 'content-disposition': 'attachment; filename="customer-import.csv"' },
  }));
  await page.route('**/api/v1/md/customers/import/preview', (route) => {
    previewCount++;
    return route.fulfill({ json: previewCount === 1 ? {
      rows: [{ rowNumber: 3, code: 'NEW', name: '', status: 'Error', errors: [{ field: 'name', code: 'REQUIRED' }] }],
      createdCount: 0, skippedCount: 0, errorCount: 1,
    } : { rows: [{ rowNumber: 2, code: 'NEW', name: 'New customer', status: 'Create', errors: [] },
      { rowNumber: 3, code: 'OLD', name: 'Existing', status: 'Skip', errors: [] }],
      createdCount: 1, skippedCount: 1, errorCount: 0 } });
  });
  await page.route('**/api/v1/md/customers/import', async (route) => {
    commitCount++;
    rows.push({ id: 2, code: 'NEW', name: 'New customer', categoryId: null, isActive: true, version: 1 });
    await route.fulfill({ json: { rows: [], createdCount: 1, skippedCount: 1, errorCount: 0 } });
  });
  await page.goto('/md/customers');
  await page.getByTestId('md-customer-import-open').click();
  const download = page.waitForEvent('download');
  await page.getByTestId('md-customer-import-csv').click();
  expect((await download).suggestedFilename()).toBe('customer-import.csv');
  await page.getByTestId('md-customer-import-file').setInputFiles({ name: 'customers.csv', mimeType: 'text/csv',
    buffer: Buffer.from('code,name\nNEW,New customer\nOLD,Existing') });
  await page.getByTestId('md-customer-import-preview').click();
  await expect(page.getByTestId('md-customer-import-row-3')).toContainText('name');
  await expect(page.getByTestId('md-customer-import-commit')).toBeDisabled();
  await page.getByTestId('md-customer-import-preview').click();
  await expect(page.getByTestId('md-customer-import-summary')).toContainText('1');
  await page.getByTestId('md-customer-import-commit').click();
  expect(commitCount).toBe(1);
  await expect(page.getByTestId('md-customer-import-commit')).toBeDisabled();
  await expect(page.getByTestId('md-customer-row-NEW')).toBeVisible();
});

test.describe('客戶匯入權限', () => {
  test.use({ authorization: { ...E2E_AUTHORIZATION, isFullAccess: false,
    permissions: [{ code: 'MD.CUSTOMER.VIEW', dataScope: 'Company' }] } });
  test('沒有 IMPORT 權限時不顯示入口', async ({ page }) => {
    await page.route('**/api/v1/md/customers?**', (route) => route.fulfill({ json: {
      items: [], page: 1, pageSize: 20, totalCount: 0,
    } }));
    await page.goto('/md/customers');
    await expect(page.getByTestId('md-customer-import-open')).toHaveCount(0);
  });
});
