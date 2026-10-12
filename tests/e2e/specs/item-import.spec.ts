import { E2E_AUTHORIZATION, expect, test } from '../support/fixtures';

test('物料匯入顯示實際列號與參照錯誤，重新預覽後提交並刷新清單', async ({ page }) => {
  const rows = [{ id: 1, code: 'OLD', name: 'Existing', specification: null, baseUomId: 1,
    brandId: null, categoryId: null, isActive: true, version: 1 }];
  let previews = 0;
  let commits = 0;
  await page.route('**/api/v1/md/items?**', (route) => route.fulfill({ json: {
    items: rows, page: 1, pageSize: 20, totalCount: rows.length,
  } }));
  await page.route('**/api/v1/md/uoms?**', (route) => route.fulfill({ json: { items: [], totalCount: 0 } }));
  await page.route('**/api/v1/md/item-brands?**', (route) => route.fulfill({ json: { items: [], totalCount: 0 } }));
  await page.route('**/api/v1/md/item-categories', (route) => route.fulfill({ json: [] }));
  await page.route('**/api/v1/md/items/import/template?**', (route) => route.fulfill({
    body: 'code,name,specification,baseUomCode,brandCode,categoryCode\n',
    headers: { 'content-type': 'text/csv', 'content-disposition': 'attachment; filename="item-import.csv"' },
  }));
  await page.route('**/api/v1/md/items/import/preview', (route) => route.fulfill({ json: ++previews === 1 ? {
    rows: [{ rowNumber: 3, code: 'NEW', name: 'New item', status: 'Error',
      errors: [{ field: 'baseUomCode', code: 'NOT_FOUND' }] }],
    createdCount: 0, skippedCount: 0, errorCount: 1,
  } : { rows: [{ rowNumber: 2, code: 'NEW', name: 'New item', status: 'Create', errors: [] },
    { rowNumber: 3, code: 'OLD', name: 'Existing', status: 'Skip', errors: [] }],
    createdCount: 1, skippedCount: 1, errorCount: 0 } }));
  await page.route('**/api/v1/md/items/import', async (route) => {
    commits++;
    rows.push({ id: 2, code: 'NEW', name: 'New item', specification: null, baseUomId: 1,
      brandId: null, categoryId: null, isActive: true, version: 1 });
    await route.fulfill({ json: { rows: [], createdCount: 1, skippedCount: 1, errorCount: 0 } });
  });
  await page.goto('/md/items');
  await page.getByTestId('md-item-import-open').click();
  const download = page.waitForEvent('download');
  await page.getByTestId('md-item-import-csv').click();
  expect((await download).suggestedFilename()).toBe('item-import.csv');
  await page.getByTestId('md-item-import-file').setInputFiles({ name: 'items.csv', mimeType: 'text/csv',
    buffer: Buffer.from('code,name,specification,baseUomCode,brandCode,categoryCode\nNEW,New item,,PCS,,\nOLD,Existing,,PCS,,') });
  await page.getByTestId('md-item-import-preview').click();
  await expect(page.getByTestId('md-item-import-row-3')).toContainText('baseUomCode');
  await expect(page.getByTestId('md-item-import-commit')).toBeDisabled();
  await page.getByTestId('md-item-import-preview').click();
  await expect(page.getByTestId('md-item-import-summary')).toContainText('1');
  await page.getByTestId('md-item-import-commit').click();
  expect(commits).toBe(1);
  await expect(page.getByTestId('md-item-import-commit')).toBeDisabled();
  await expect(page.getByTestId('md-item-row-NEW')).toBeVisible();
});

test.describe('物料匯入權限', () => {
  test.use({ authorization: { ...E2E_AUTHORIZATION, isFullAccess: false,
    permissions: [{ code: 'MD.ITEM.VIEW', dataScope: 'Company' }] } });
  test('沒有 IMPORT 權限時不顯示入口', async ({ page }) => {
    await page.route('**/api/v1/md/items?**', (route) => route.fulfill({ json: {
      items: [], page: 1, pageSize: 20, totalCount: 0,
    } }));
    await page.goto('/md/items');
    await expect(page.getByTestId('md-item-import-open')).toHaveCount(0);
  });
});
