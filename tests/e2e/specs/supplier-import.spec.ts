import { E2E_AUTHORIZATION, expect, test } from '../support/fixtures';

test('供應商匯入可下載範本、預覽列號與錯誤、修正後整批提交', async ({ page }) => {
  const rows = [{ id: 1, code: 'OLD', name: 'Existing', categoryId: null, isActive: true, version: 1 }];
  let previewCount = 0;
  let commitCount = 0;
  await page.route('**/api/v1/md/supplier-categories**', (route) => route.fulfill({ json: {
    items: [], page: 1, pageSize: 100, totalCount: 0,
  } }));
  await page.route('**/api/v1/md/suppliers?**', (route) => route.fulfill({ json: {
    items: rows, page: 1, pageSize: 20, totalCount: rows.length,
  } }));
  await page.route('**/api/v1/md/suppliers/import/template?**', (route) => route.fulfill({
    body: 'code,name\n', headers: { 'content-type': 'text/csv', 'content-disposition': 'attachment; filename="supplier-import.csv"' },
  }));
  await page.route('**/api/v1/md/suppliers/import/preview', (route) => {
    previewCount++;
    return route.fulfill({ json: previewCount === 1 ? {
      rows: [{ rowNumber: 2, code: 'NEW', name: 'New supplier', status: 'Error', errors: [{ field: 'contactName', code: 'REQUIRED' }] }],
      createdCount: 0, skippedCount: 0, errorCount: 1,
    } : { rows: [{ rowNumber: 2, code: 'NEW', name: 'New supplier', status: 'Create', errors: [] },
      { rowNumber: 3, code: 'OLD', name: 'Existing', status: 'Skip', errors: [] }],
      createdCount: 1, skippedCount: 1, errorCount: 0 } });
  });
  await page.route('**/api/v1/md/suppliers/import', async (route) => {
    commitCount++;
    rows.push({ id: 2, code: 'NEW', name: 'New supplier', categoryId: null, isActive: true, version: 1 });
    await route.fulfill({ json: { rows: [], createdCount: 1, skippedCount: 1, errorCount: 0 } });
  });
  await page.goto('/md/suppliers');
  await page.getByTestId('md-supplier-import-open').click();
  const download = page.waitForEvent('download');
  await page.getByTestId('md-supplier-import-csv').click();
  expect((await download).suggestedFilename()).toBe('supplier-import.csv');
  await page.getByTestId('md-supplier-import-file').setInputFiles({ name: 'suppliers.csv', mimeType: 'text/csv',
    buffer: Buffer.from('code,name,contactMobile\nNEW,New supplier,0912\nOLD,Existing,') });
  await page.getByTestId('md-supplier-import-preview').click();
  await expect(page.getByTestId('md-supplier-import-row-2')).toContainText('聯絡人: 必填');
  await expect(page.getByTestId('md-supplier-import-commit')).toBeDisabled();
  await page.getByTestId('md-supplier-import-preview').click();
  await expect(page.getByTestId('md-supplier-import-summary')).toContainText('1');
  await page.getByTestId('md-supplier-import-commit').click();
  expect(commitCount).toBe(1);
  await expect(page.getByTestId('md-supplier-import-commit')).toBeDisabled();
  await expect(page.getByTestId('md-supplier-row-NEW')).toBeVisible();
});

test.describe('供應商匯入權限', () => {
  test.use({ authorization: { ...E2E_AUTHORIZATION, isFullAccess: false,
    permissions: [{ code: 'MD.SUPPLIER.VIEW', dataScope: 'Company' }] } });
  test('沒有 IMPORT 權限時不顯示入口', async ({ page }) => {
    await page.route('**/api/v1/md/suppliers?**', (route) => route.fulfill({ json: {
      items: [], page: 1, pageSize: 20, totalCount: 0,
    } }));
    await page.goto('/md/suppliers');
    await expect(page.getByTestId('md-supplier-import-open')).toHaveCount(0);
  });
});
