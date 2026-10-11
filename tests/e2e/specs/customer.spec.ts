import { E2E_AUTHORIZATION, expect, test } from '../support/fixtures';

test('客戶可按公司查看、新增、停用及搜尋', async ({ page }) => {
  const rows: Record<string, { id: number; code: string; name: string; categoryId: number | null; isActive: boolean; version: number }[]> = {
    '1': [], '2': [{ id: 20, code: 'HK01', name: 'Hong Kong customer', categoryId: null, isActive: true, version: 1 }],
  };
  await page.route('**/api/v1/md/customer-categories**', (route) => route.fulfill({ json: {
    items: [{ id: 3, code: 'RETAIL', name: 'Retail', isActive: true, version: 1 }],
    page: 1, pageSize: 100, totalCount: 1,
  } }));
  await page.route('**/api/v1/md/customers**', async (route) => {
    const request = route.request();
    const company = request.headers()['x-company-id'] ?? '';
    const data = rows[company] ??= [];
    if (request.method() === 'GET') {
      const keyword = new URL(request.url()).searchParams.get('keyword')?.toLowerCase() ?? '';
      const items = data.filter((row) => row.code.toLowerCase().includes(keyword) || row.name.toLowerCase().includes(keyword));
      await route.fulfill({ json: { items, page: 1, pageSize: 20, totalCount: items.length } });
    } else if (request.method() === 'POST') {
      const body = request.postDataJSON();
      const row = { id: 4, code: body.code, name: body.name, categoryId: body.categoryId, isActive: true, version: 1 };
      data.push(row);
      await route.fulfill({ status: 201, json: row });
    } else {
      const row = data.find((entry) => entry.id === Number(new URL(request.url()).pathname.split('/').at(-1)))!;
      Object.assign(row, request.postDataJSON(), { version: row.version + 1 });
      await route.fulfill({ json: row });
    }
  });

  await page.goto('/md/customers');
  await page.getByTestId('md-customer-create').click();
  await page.getByTestId('md-customer-code').fill('acme');
  await page.getByTestId('md-customer-name').fill(' Acme ');
  await page.getByTestId('md-customer-category').selectOption({ label: 'Retail' });
  await page.locator('.ant-modal-footer .ant-btn-primary').click();
  await expect(page.getByTestId('md-customer-row-ACME')).toContainText('Retail');
  await page.getByTestId('md-customer-edit-ACME').click();
  await page.getByTestId('md-customer-active').uncheck();
  await page.locator('.ant-modal-footer .ant-btn-primary').click();
  await expect(page.getByTestId('md-customer-row-ACME')).toContainText('停用');
  await page.getByTestId('md-customer-search').fill('missing');
  await page.getByTestId('md-customer-search-button').click();
  await expect(page.getByTestId('md-customer-row-ACME')).toHaveCount(0);
  await page.getByTestId('md-customer-search').fill('');
  await page.getByTestId('md-customer-search-button').click();
  await page.getByTestId('layout-company-select').click();
  await page.locator('nz-option-item', { hasText: 'HK01 Hong Kong' }).click();
  await expect(page.getByTestId('md-customer-row-HK01')).toBeVisible();
  await expect(page.getByTestId('md-customer-row-ACME')).toHaveCount(0);
});

test.describe('客戶只讀權限', () => {
  test.use({ authorization: { ...E2E_AUTHORIZATION, isFullAccess: false,
    permissions: [{ code: 'MD.CUSTOMER.VIEW', dataScope: 'Company' }] } });
  test('隱藏寫入操作', async ({ page }) => {
    await page.route('**/api/v1/md/customers**', (route) => route.fulfill({ json: {
      items: [{ id: 1, code: 'VIEW', name: 'View only', categoryId: null, isActive: true, version: 1 }],
      page: 1, pageSize: 20, totalCount: 1,
    } }));
    await page.goto('/md/customers');
    await expect(page.getByTestId('md-customer-row-VIEW')).toBeVisible();
    await expect(page.getByTestId('md-customer-create')).toHaveCount(0);
    await expect(page.getByTestId('md-customer-edit-VIEW')).toHaveCount(0);
  });
});
