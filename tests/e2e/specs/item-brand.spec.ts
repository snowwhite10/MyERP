import { E2E_AUTHORIZATION, expect, test } from '../support/fixtures';

type Row = { id: number; code: string; name: string; isActive: boolean; version: number };

test('物料品牌依公司列出並可新增、修改、軟刪除及顯示重複代碼', async ({ page, pageErrors }) => {
  pageErrors.allow(/status of 409/);
  const rows: Record<string, Row[]> = {
    '1': [{ id: 1, code: 'ACME', name: 'Acme', isActive: true, version: 1 }],
    '2': [{ id: 2, code: 'HKBRAND', name: '港牌', isActive: true, version: 1 }],
  };
  const calls: { method: string; company: string; body: Record<string, unknown> | null }[] = [];
  await page.route('**/api/v1/md/item-brands**', async (route) => {
    const request = route.request();
    const company = request.headers()['x-company-id'] ?? '';
    const data = rows[company] ??= [];
    const body = request.postDataJSON() as Record<string, unknown> | null;
    calls.push({ method: request.method(), company, body });
    if (request.method() === 'GET') {
      await route.fulfill({ json: { items: data, page: 1, pageSize: 20, totalCount: data.length } });
    } else if (request.method() === 'POST' && data.some((row) => row.code === body?.['code'])) {
      await route.fulfill({ status: 409, contentType: 'application/problem+json',
        json: { status: 409, errorCode: 'MD.ITEM_BRAND.DUPLICATE_CODE' } });
    } else if (request.method() === 'POST') {
      const row = { id: 3, code: String(body?.['code']), name: String(body?.['name']), isActive: true, version: 1 };
      data.push(row);
      await route.fulfill({ status: 201, json: row });
    } else {
      const id = Number(new URL(request.url()).pathname.split('/').at(-1));
      const row = data.find((entry) => entry.id === id)!;
      if (request.method() === 'DELETE') {
        data.splice(data.indexOf(row), 1);
        await route.fulfill({ status: 204, body: '' });
      } else {
        Object.assign(row, body, { version: row.version + 1 });
        await route.fulfill({ json: row });
      }
    }
  });

  await page.goto('/');
  await page.getByTestId('layout-menu-md').click();
  await page.getByTestId('layout-menu-md-item-brands').click();
  await expect(page).toHaveURL(/\/md\/item-brands$/);
  await expect(page.getByTestId('md-category-title')).toHaveText('物料品牌');
  await expect(page.getByTestId('md-category-row-ACME')).toBeVisible();

  await page.getByTestId('md-category-create').click();
  await page.getByTestId('md-category-code').fill('acme');
  await page.getByTestId('md-category-name').fill('Acme 2');
  await page.locator('.ant-modal-footer .ant-btn-primary').click();
  await expect(page.getByTestId('md-category-save-error')).toContainText('此公司已使用該品牌代碼。');
  await page.getByTestId('md-category-code').fill('nova');
  await page.getByTestId('md-category-name').fill(' Nova ');
  await page.locator('.ant-modal-footer .ant-btn-primary').click();
  await expect(page.getByTestId('md-category-row-NOVA')).toBeVisible();
  expect(calls.filter((call) => call.method === 'POST').at(-1)?.body).toEqual({ code: 'NOVA', name: 'Nova' });

  await page.getByTestId('md-category-edit-NOVA').click();
  await expect(page.getByTestId('md-category-code')).toBeDisabled();
  await page.getByTestId('md-category-active').uncheck();
  await page.locator('.ant-modal-footer .ant-btn-primary').click();
  await expect(page.getByTestId('md-category-row-NOVA')).toContainText('停用');
  expect(calls.find((call) => call.method === 'PUT')?.body).toMatchObject({ isActive: false, version: 1 });

  await page.getByTestId('md-category-delete-NOVA').click();
  await page.getByTestId('erp-message-ok').click();
  await expect(page.getByTestId('md-category-row-NOVA')).toHaveCount(0);

  await page.getByTestId('layout-company-select').click();
  await page.locator('nz-option-item', { hasText: 'HK01 Hong Kong' }).click();
  await expect(page.getByTestId('md-category-row-HKBRAND')).toBeVisible();
  await expect(page.getByTestId('md-category-row-ACME')).toHaveCount(0);
  expect(calls.filter((call) => call.method === 'DELETE').map((call) => call.company)).toEqual(['1']);
});

test.describe('物料品牌只讀權限', () => {
  test.use({ authorization: { ...E2E_AUTHORIZATION, isFullAccess: false,
    permissions: [{ code: 'MD.ITEM_BRAND.VIEW', dataScope: 'Company' }] } });

  test('隱藏寫入操作並阻擋物料分類頁', async ({ page }) => {
    await page.route('**/api/v1/md/item-brands**', (route) =>
      route.fulfill({ json: { items: [{ id: 1, code: 'ACME', name: 'Acme', isActive: true, version: 1 }],
        page: 1, pageSize: 20, totalCount: 1 } }));
    await page.goto('/md/item-brands');
    await expect(page.getByTestId('md-category-row-ACME')).toBeVisible();
    await expect(page.getByTestId('md-category-create')).toHaveCount(0);
    await expect(page.getByTestId('md-category-edit-ACME')).toHaveCount(0);
    await expect(page.getByTestId('md-category-delete-ACME')).toHaveCount(0);
    await page.goto('/md/item-categories');
    await expect(page.getByTestId('error-page-403')).toBeVisible();
  });
});
