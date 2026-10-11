import { E2E_AUTHORIZATION, expect, test } from '../support/fixtures';

test('客戶分類依公司列出並可新增、修改及軟刪除', async ({ page }) => {
  const rows: Record<string, { id: number; code: string; name: string; isActive: boolean; version: number }[]> = {
    '1': [{ id: 1, code: 'RETAIL', name: '零售', isActive: true, version: 1 }],
    '2': [{ id: 2, code: 'EXPORT', name: '外銷', isActive: true, version: 1 }],
  };
  const calls: { method: string; company: string; body: Record<string, unknown> | null }[] = [];
  await page.route('**/api/v1/md/customer-categories**', async (route) => {
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
      if (request.method() === 'DELETE') {
        data.splice(data.indexOf(row), 1);
        await route.fulfill({ status: 204, body: '' });
      } else {
        Object.assign(row, body, { version: row.version + 1 });
        await route.fulfill({ json: row });
      }
    }
  });

  await page.goto('/md/customer-categories');
  await expect(page.getByTestId('md-category-row-RETAIL')).toBeVisible();
  await page.getByTestId('md-category-create').click();
  await page.getByTestId('md-category-code').fill('direct');
  await page.getByTestId('md-category-name').fill(' 直銷 ');
  await page.locator('.ant-modal-footer .ant-btn-primary').click();
  await expect(page.getByTestId('md-category-row-DIRECT')).toBeVisible();
  expect(calls.find((call) => call.method === 'POST')?.body).toEqual({ code: 'DIRECT', name: '直銷' });

  await page.getByTestId('md-category-edit-DIRECT').click();
  await expect(page.getByTestId('md-category-code')).toBeDisabled();
  await page.getByTestId('md-category-active').uncheck();
  await page.locator('.ant-modal-footer .ant-btn-primary').click();
  await expect(page.getByTestId('md-category-row-DIRECT')).toContainText('停用');
  expect(calls.find((call) => call.method === 'PUT')?.body).toMatchObject({ isActive: false, version: 1 });

  await page.getByTestId('md-category-delete-DIRECT').click();
  await page.getByTestId('erp-message-ok').click();
  await expect(page.getByTestId('md-category-row-DIRECT')).toHaveCount(0);
  expect(calls.find((call) => call.method === 'DELETE')?.company).toBe('1');

  await page.getByTestId('layout-company-select').click();
  await page.locator('nz-option-item', { hasText: 'HK01 Hong Kong' }).click();
  await expect(page.getByTestId('md-category-row-EXPORT')).toBeVisible();
  await expect(page.getByTestId('md-category-row-RETAIL')).toHaveCount(0);
});

test.describe('客戶分類只讀權限', () => {
  test.use({ authorization: { ...E2E_AUTHORIZATION, isFullAccess: false,
    permissions: [{ code: 'MD.CUSTOMER_CATEGORY.VIEW', dataScope: 'Company' }] } });

  test('隱藏寫入操作並阻擋無權限頁面', async ({ page }) => {
    await page.route('**/api/v1/md/customer-categories**', (route) =>
      route.fulfill({ json: { items: [], page: 1, pageSize: 20, totalCount: 0 } }));
    await page.goto('/md/customer-categories');
    await expect(page.getByTestId('md-category-create')).toHaveCount(0);
    await page.goto('/md');
    await expect(page.getByTestId('error-page-403')).toBeVisible();
  });
});
