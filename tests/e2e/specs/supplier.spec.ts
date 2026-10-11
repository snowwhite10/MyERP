import { E2E_AUTHORIZATION, expect, test } from '../support/fixtures';

test('供應商可按公司查看、新增、停用及搜尋，重複代碼顯示錯誤', async ({ page, pageErrors }) => {
  pageErrors.allow(/status of 409/);
  const rows: Record<string, { id: number; code: string; name: string; categoryId: number | null; isActive: boolean; version: number }[]> = {
    '1': [], '2': [{ id: 20, code: 'HKSUP', name: 'Hong Kong supplier', categoryId: null, isActive: true, version: 1 }],
  };
  await page.route('**/api/v1/md/supplier-categories**', (route) => route.fulfill({ json: {
    items: [{ id: 5, code: 'RAW', name: 'Raw material', isActive: true, version: 1 }],
    page: 1, pageSize: 100, totalCount: 1,
  } }));
  await page.route('**/api/v1/md/suppliers**', async (route) => {
    const request = route.request();
    const company = request.headers()['x-company-id'] ?? '';
    const data = rows[company] ??= [];
    if (request.method() === 'GET') {
      const keyword = new URL(request.url()).searchParams.get('keyword')?.toLowerCase() ?? '';
      const items = data.filter((row) => row.code.toLowerCase().includes(keyword) || row.name.toLowerCase().includes(keyword));
      await route.fulfill({ json: { items, page: 1, pageSize: 20, totalCount: items.length } });
    } else if (request.method() === 'POST') {
      const body = request.postDataJSON();
      if (data.some((row) => row.code === body.code)) {
        await route.fulfill({ status: 409, json: { errorCode: 'MD.SUPPLIER.DUPLICATE_CODE' } });
        return;
      }
      const row = { id: 9, code: body.code, name: body.name, categoryId: body.categoryId, isActive: true, version: 1 };
      data.push(row);
      await route.fulfill({ status: 201, json: row });
    } else {
      const row = data.find((entry) => entry.id === Number(new URL(request.url()).pathname.split('/').at(-1)))!;
      Object.assign(row, request.postDataJSON(), { version: row.version + 1 });
      await route.fulfill({ json: row });
    }
  });

  await page.goto('/');
  await page.getByTestId('layout-menu-md').click();
  await page.getByTestId('layout-menu-md-suppliers').click();
  await expect(page.getByTestId('md-supplier-title')).toHaveText('供應商');
  await page.getByTestId('md-supplier-create').click();
  await page.getByTestId('md-supplier-code').fill('steel');
  await page.getByTestId('md-supplier-name').fill(' Steel Co ');
  await page.getByTestId('md-supplier-category').selectOption({ label: 'Raw material' });
  await page.locator('.ant-modal-footer .ant-btn-primary').click();
  await expect(page.getByTestId('md-supplier-row-STEEL')).toContainText('Raw material');
  await page.getByTestId('md-supplier-create').click();
  await page.getByTestId('md-supplier-code').fill('STEEL');
  await page.getByTestId('md-supplier-name').fill('Again');
  await page.locator('.ant-modal-footer .ant-btn-primary').click();
  await expect(page.getByTestId('md-supplier-save-error')).toContainText('此公司已使用該供應商代碼');
  await page.locator('.ant-modal-footer .ant-btn:not(.ant-btn-primary)').click();
  await page.getByTestId('md-supplier-edit-STEEL').click();
  await page.getByTestId('md-supplier-active').uncheck();
  await page.locator('.ant-modal-footer .ant-btn-primary').click();
  await expect(page.getByTestId('md-supplier-row-STEEL')).toContainText('停用');
  await page.getByTestId('md-supplier-search').fill('missing');
  await page.getByTestId('md-supplier-search-button').click();
  await expect(page.getByTestId('md-supplier-row-STEEL')).toHaveCount(0);
  await page.getByTestId('md-supplier-search').fill('');
  await page.getByTestId('md-supplier-search-button').click();
  await page.getByTestId('layout-company-select').click();
  await page.locator('nz-option-item', { hasText: 'HK01 Hong Kong' }).click();
  await expect(page.getByTestId('md-supplier-row-HKSUP')).toBeVisible();
  await expect(page.getByTestId('md-supplier-row-STEEL')).toHaveCount(0);
});

test.describe('供應商只讀權限', () => {
  test.use({ authorization: { ...E2E_AUTHORIZATION, isFullAccess: false,
    permissions: [{ code: 'MD.SUPPLIER.VIEW', dataScope: 'Company' }] } });
  test('隱藏寫入操作且不載入分類', async ({ page }) => {
    let categoryRequests = 0;
    await page.route('**/api/v1/md/supplier-categories**', (route) => { categoryRequests++; return route.abort(); });
    await page.route('**/api/v1/md/suppliers**', (route) => route.fulfill({ json: {
      items: [{ id: 1, code: 'VIEW', name: 'View only', categoryId: null, isActive: true, version: 1 }],
      page: 1, pageSize: 20, totalCount: 1,
    } }));
    await page.goto('/md/suppliers');
    await expect(page.getByTestId('md-supplier-row-VIEW')).toBeVisible();
    await expect(page.getByTestId('md-supplier-create')).toHaveCount(0);
    await expect(page.getByTestId('md-supplier-edit-VIEW')).toHaveCount(0);
    expect(categoryRequests).toBe(0);
  });
});
