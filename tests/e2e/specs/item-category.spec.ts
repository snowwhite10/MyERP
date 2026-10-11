import { E2E_AUTHORIZATION, expect, test } from '../support/fixtures';

type Row = { id: number; code: string; name: string; parentId: number | null; isActive: boolean; version: number };

test('物料分類以樹狀顯示，可新增子分類、搬移、刪除並顯示樹規則錯誤', async ({ page, pageErrors }) => {
  pageErrors.allow(/status of 409/);
  pageErrors.allow(/status of 422/);
  const rows: Record<string, Row[]> = {
    '1': [{ id: 1, code: 'RAW', name: '原物料', parentId: null, isActive: true, version: 1 },
      { id: 2, code: 'METAL', name: '金屬', parentId: 1, isActive: true, version: 1 },
      { id: 3, code: 'PACK', name: '包材', parentId: null, isActive: true, version: 1 }],
    '2': [{ id: 9, code: 'HKGOODS', name: '港貨', parentId: null, isActive: true, version: 1 }],
  };
  const calls: { method: string; company: string; body: Record<string, unknown> | null }[] = [];
  await page.route('**/api/v1/md/item-categories**', async (route) => {
    const request = route.request();
    const company = request.headers()['x-company-id'] ?? '';
    const data = rows[company] ??= [];
    const body = request.postDataJSON() as Record<string, unknown> | null;
    calls.push({ method: request.method(), company, body });
    if (request.method() === 'GET') {
      await route.fulfill({ json: data });
    } else if (request.method() === 'POST') {
      const row = { id: 4, code: String(body?.['code']), name: String(body?.['name']),
        parentId: (body?.['parentId'] as number | null) ?? null, isActive: true, version: 1 };
      data.push(row);
      await route.fulfill({ status: 201, json: row });
    } else {
      const id = Number(new URL(request.url()).pathname.split('/').at(-1));
      const row = data.find((entry) => entry.id === id)!;
      if (request.method() === 'DELETE' && data.some((entry) => entry.parentId === id)) {
        await route.fulfill({ status: 409, contentType: 'application/problem+json',
          json: { status: 409, errorCode: 'MD.ITEM_CATEGORY.HAS_CHILDREN' } });
      } else if (request.method() === 'DELETE') {
        data.splice(data.indexOf(row), 1);
        await route.fulfill({ status: 204, body: '' });
      } else if (body?.['parentId'] === 3 && id === 1) {
        await route.fulfill({ status: 422, contentType: 'application/problem+json',
          json: { status: 422, errorCode: 'MD.ITEM_CATEGORY.TOO_DEEP' } });
      } else {
        Object.assign(row, body, { version: row.version + 1 });
        await route.fulfill({ json: row });
      }
    }
  });

  await page.goto('/');
  await page.getByTestId('layout-menu-md').click();
  await page.getByTestId('layout-menu-md-item-categories').click();
  await expect(page).toHaveURL(/\/md\/item-categories$/);
  await expect(page.getByTestId('md-item-category-title')).toHaveText('物料分類');
  const rowIds = page.locator('tr[data-testid^="md-item-category-row-"]');
  await expect(rowIds).toHaveCount(3);
  expect(await rowIds.evaluateAll((items) => items.map((item) => item.getAttribute('data-testid'))))
    .toEqual(['md-item-category-row-PACK', 'md-item-category-row-RAW', 'md-item-category-row-METAL']);
  await expect(page.getByTestId('md-item-category-row-METAL')).toHaveAttribute('aria-level', '2');

  await page.getByTestId('md-item-category-add-child-METAL').click();
  await expect(page.getByTestId('md-item-category-parent')).toHaveValue(/.+/);
  await page.getByTestId('md-item-category-code').fill('steel');
  await page.getByTestId('md-item-category-name').fill(' 鋼材 ');
  await page.locator('.ant-modal-footer .ant-btn-primary').click();
  await expect(page.getByTestId('md-item-category-row-STEEL')).toHaveAttribute('aria-level', '3');
  expect(calls.find((call) => call.method === 'POST')?.body).toEqual({ code: 'STEEL', name: '鋼材', parentId: 2 });

  await page.getByTestId('md-item-category-search').fill('鋼');
  await page.getByTestId('md-item-category-search-button').click();
  await expect(rowIds).toHaveCount(3);
  await expect(page.getByTestId('md-item-category-row-PACK')).toHaveCount(0);
  await page.getByTestId('md-item-category-search').fill('');
  await page.getByTestId('md-item-category-search-button').click();

  await page.getByTestId('md-item-category-edit-RAW').click();
  await expect(page.getByTestId('md-item-category-code')).toBeDisabled();
  await expect(page.getByTestId('md-item-category-parent').locator('option', { hasText: 'METAL' })).toHaveCount(0);
  await page.getByTestId('md-item-category-parent').selectOption({ label: 'PACK — 包材' });
  await page.locator('.ant-modal-footer .ant-btn-primary').click();
  await expect(page.getByTestId('md-item-category-save-error')).toContainText('物料分類最多 5 層。');
  await page.locator('.ant-modal-footer .ant-btn:not(.ant-btn-primary)').click();

  await page.getByTestId('md-item-category-edit-STEEL').click();
  await page.getByTestId('md-item-category-parent').selectOption({ label: 'PACK — 包材' });
  await page.locator('.ant-modal-footer .ant-btn-primary').click();
  await expect(page.getByTestId('md-item-category-row-STEEL')).toHaveAttribute('aria-level', '2');
  expect(calls.filter((call) => call.method === 'PUT').at(-1)?.body).toMatchObject({ parentId: 3, version: 1 });

  await page.getByTestId('md-item-category-delete-RAW').click();
  await page.getByTestId('erp-message-ok').click();
  await expect(page.getByText('此分類下仍有子分類，請先移走或刪除子分類。')).toBeVisible();
  await page.getByTestId('md-item-category-delete-METAL').click();
  await page.getByTestId('erp-message-ok').click();
  await expect(page.getByTestId('md-item-category-row-METAL')).toHaveCount(0);

  await page.getByTestId('layout-company-select').click();
  await page.locator('nz-option-item', { hasText: 'HK01 Hong Kong' }).click();
  await expect(page.getByTestId('md-item-category-row-HKGOODS')).toBeVisible();
  await expect(page.getByTestId('md-item-category-row-RAW')).toHaveCount(0);
  expect(calls.filter((call) => call.method === 'DELETE').map((call) => call.company)).toEqual(['1', '1']);
});

test.describe('物料分類只讀權限', () => {
  test.use({ authorization: { ...E2E_AUTHORIZATION, isFullAccess: false,
    permissions: [{ code: 'MD.ITEM_CATEGORY.VIEW', dataScope: 'Company' }] } });

  test('隱藏寫入操作並阻擋物料品牌頁', async ({ page }) => {
    await page.route('**/api/v1/md/item-categories**', (route) =>
      route.fulfill({ json: [{ id: 1, code: 'RAW', name: '原物料', parentId: null, isActive: true, version: 1 }] }));
    await page.goto('/md/item-categories');
    await expect(page.getByTestId('md-item-category-row-RAW')).toBeVisible();
    await expect(page.getByTestId('md-item-category-create')).toHaveCount(0);
    await expect(page.getByTestId('md-item-category-add-child-RAW')).toHaveCount(0);
    await expect(page.getByTestId('md-item-category-edit-RAW')).toHaveCount(0);
    await expect(page.getByTestId('md-item-category-delete-RAW')).toHaveCount(0);
    await page.goto('/md/item-brands');
    await expect(page.getByTestId('error-page-403')).toBeVisible();
  });
});
