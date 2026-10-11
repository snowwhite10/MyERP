import { E2E_AUTHORIZATION, expect, test } from '../support/fixtures';

type Row = { id: number; code: string; name: string; isActive: boolean; version: number };

test('供應商分類依公司列出並可新增、修改、軟刪除及顯示引用錯誤', async ({ page, pageErrors }) => {
  pageErrors.allow(/status of 409/);
  const rows: Record<string, Row[]> = {
    '1': [{ id: 1, code: 'RAW', name: '原物料', isActive: true, version: 1 }],
    '2': [{ id: 2, code: 'LOGISTICS', name: '物流', isActive: true, version: 1 }],
  };
  const calls: { method: string; company: string; body: Record<string, unknown> | null }[] = [];
  await page.route('**/api/v1/md/supplier-categories**', async (route) => {
    const request = route.request();
    const company = request.headers()['x-company-id'] ?? '';
    const data = rows[company] ??= [];
    const body = request.postDataJSON() as Record<string, unknown> | null;
    calls.push({ method: request.method(), company, body });
    if (request.method() === 'GET') {
      await route.fulfill({ json: { items: data, page: 1, pageSize: 20, totalCount: data.length } });
    } else if (request.method() === 'POST') {
      const row = { id: 3, code: String(body?.['code']), name: String(body?.['name']), isActive: true, version: 1 };
      data.push(row);
      await route.fulfill({ status: 201, json: row });
    } else {
      const id = Number(new URL(request.url()).pathname.split('/').at(-1));
      const row = data.find((entry) => entry.id === id)!;
      if (request.method() === 'DELETE' && row.code === 'RAW') {
        await route.fulfill({ status: 409, contentType: 'application/problem+json',
          json: { status: 409, errorCode: 'MD.SUPPLIER_CATEGORY.IN_USE' } });
      } else if (request.method() === 'DELETE') {
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
  await page.getByTestId('layout-menu-md-supplier-categories').click();
  await expect(page).toHaveURL(/\/md\/supplier-categories$/);
  await expect(page.getByTestId('md-category-title')).toHaveText('供應商分類');
  await expect(page.getByTestId('md-category-row-RAW')).toBeVisible();

  await page.getByTestId('md-category-create').click();
  await page.getByTestId('md-category-code').fill('svc-01');
  await page.getByTestId('md-category-name').fill(' 服務 ');
  await page.locator('.ant-modal-footer .ant-btn-primary').click();
  await expect(page.getByTestId('md-category-row-SVC-01')).toBeVisible();
  expect(calls.find((call) => call.method === 'POST')?.body).toEqual({ code: 'SVC-01', name: '服務' });

  await page.getByTestId('md-category-edit-SVC-01').click();
  await expect(page.getByTestId('md-category-code')).toBeDisabled();
  await page.getByTestId('md-category-active').uncheck();
  await page.locator('.ant-modal-footer .ant-btn-primary').click();
  await expect(page.getByTestId('md-category-row-SVC-01')).toContainText('停用');
  expect(calls.find((call) => call.method === 'PUT')?.body).toMatchObject({ isActive: false, version: 1 });

  await page.getByTestId('md-category-delete-SVC-01').click();
  await page.getByTestId('erp-message-ok').click();
  await expect(page.getByTestId('md-category-row-SVC-01')).toHaveCount(0);

  await page.getByTestId('md-category-delete-RAW').click();
  await page.getByTestId('erp-message-ok').click();
  await expect(page.getByText('已有供應商使用此分類，請改為停用。')).toBeVisible();
  await expect(page.getByTestId('md-category-row-RAW')).toBeVisible();

  await page.getByTestId('layout-company-select').click();
  await page.locator('nz-option-item', { hasText: 'HK01 Hong Kong' }).click();
  await expect(page.getByTestId('md-category-row-LOGISTICS')).toBeVisible();
  await expect(page.getByTestId('md-category-row-RAW')).toHaveCount(0);
  expect(calls.filter((call) => call.method === 'DELETE').map((call) => call.company)).toEqual(['1', '1']);
});

test.describe('供應商分類只讀權限', () => {
  test.use({ authorization: { ...E2E_AUTHORIZATION, isFullAccess: false,
    permissions: [{ code: 'MD.SUPPLIER_CATEGORY.VIEW', dataScope: 'Company' }] } });

  test('隱藏寫入操作並阻擋客戶分類頁', async ({ page }) => {
    await page.route('**/api/v1/md/supplier-categories**', (route) =>
      route.fulfill({ json: { items: [{ id: 1, code: 'RAW', name: '原物料', isActive: true, version: 1 }],
        page: 1, pageSize: 20, totalCount: 1 } }));
    await page.goto('/md/supplier-categories');
    await expect(page.getByTestId('md-category-row-RAW')).toBeVisible();
    await expect(page.getByTestId('md-category-create')).toHaveCount(0);
    await expect(page.getByTestId('md-category-edit-RAW')).toHaveCount(0);
    await expect(page.getByTestId('md-category-delete-RAW')).toHaveCount(0);
    await page.goto('/md/customer-categories');
    await expect(page.getByTestId('error-page-403')).toBeVisible();
  });
});
