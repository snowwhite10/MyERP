import { E2E_AUTHORIZATION, expect, test } from '../support/fixtures';

test('物料搜尋、新增、修改與公司切換；參照只可選啟用項', async ({ page }) => {
  const rows: Record<string, Record<string, unknown>[]> = { '1': [
    { id: 1, code: 'BOLT', name: '螺栓', specification: 'M8', baseUomId: 1,
      brandId: 2, categoryId: 3, isActive: true, version: 1 }], '2': [] };
  const writes: Record<string, unknown>[] = [];
  await page.route('**/api/v1/md/items**', async (route) => {
    const request = route.request(); const company = request.headers()['x-company-id'] ?? '1';
    const data = rows[company] ??= [];
    if (request.method() === 'GET') {
      const keyword = new URL(request.url()).searchParams.get('keyword')?.toLowerCase() ?? '';
      const items = data.filter((item) => [item['code'], item['name'], item['specification']]
        .some((value) => String(value ?? '').toLowerCase().includes(keyword)));
      await route.fulfill({ json: { items, totalCount: items.length, page: 1, pageSize: 20 } });
    } else {
      const body = request.postDataJSON() as Record<string, unknown>;
      writes.push(body);
      if (request.method() === 'POST') {
        const item = { ...body, id: 2, isActive: true, version: 1 }; data.push(item);
        await route.fulfill({ status: 201, json: item });
      } else {
        const item = data.find((x) => x['id'] === 1)!;
        Object.assign(item, body, { version: 2 }); await route.fulfill({ json: item });
      }
    }
  });
  await page.route('**/api/v1/md/uoms**', (route) => route.fulfill({ json: { items: [
    { id: 1, code: 'EA', name: '個', isActive: true }, { id: 9, code: 'OLD', name: '舊單位', isActive: false }], totalCount: 2 } }));
  await page.route('**/api/v1/md/item-brands**', (route) => route.fulfill({ json: { items: [
    { id: 2, code: 'ACME', name: '品牌', isActive: true }], totalCount: 1 } }));
  await page.route('**/api/v1/md/item-categories**', (route) => route.fulfill({ json: [
    { id: 3, code: 'PARTS', name: '零件', parentId: null, isActive: true, version: 1 }] }));
  await page.goto('/md/items');
  await expect(page.getByTestId('md-item-row-BOLT')).toContainText('M8');
  await page.getByTestId('md-item-search').fill('M8');
  await page.getByTestId('md-item-search-button').click();
  await expect(page.getByTestId('md-item-row-BOLT')).toBeVisible();
  await page.getByTestId('md-item-create').click();
  await expect(page.getByTestId('md-item-uom').locator('option', { hasText: 'OLD' })).toBeDisabled();
  await page.getByTestId('md-item-code').fill('nut');
  await page.getByTestId('md-item-name').fill('螺帽');
  await page.getByTestId('md-item-uom').selectOption({ label: 'EA — 個' });
  await page.locator('.ant-modal-footer .ant-btn-primary').click();
  expect(writes[0]).toMatchObject({ code: 'NUT', baseUomId: 1 });
  await page.getByTestId('md-item-search').fill(''); await page.getByTestId('md-item-search-button').click();
  await page.getByTestId('md-item-edit-BOLT').click();
  await expect(page.getByTestId('md-item-code')).toBeDisabled();
  await page.getByTestId('md-item-name').fill('新螺栓');
  await page.locator('.ant-modal-footer .ant-btn-primary').click();
  expect(writes[1]).toMatchObject({ name: '新螺栓', version: 1 });
  await page.getByTestId('layout-company-select').click();
  await page.locator('nz-option-item', { hasText: 'HK01 Hong Kong' }).click();
  await expect(page.getByTestId('md-item-row-BOLT')).toHaveCount(0);
});

test.describe('物料只讀權限', () => {
  test.use({ authorization: { ...E2E_AUTHORIZATION, isFullAccess: false,
    permissions: [{ code: 'MD.ITEM.VIEW', dataScope: 'Company' }] } });
  test('隱藏寫入按鈕，參照資源無權時不請求，無 VIEW 時拒絕進入', async ({ page }) => {
    const refs: string[] = [];
    await page.route('**/api/v1/md/item**', (route) => {
      if (!new URL(route.request().url()).pathname.endsWith('/items')) refs.push(route.request().url());
      return route.fulfill({ json: { items: [], totalCount: 0 } });
    });
    await page.goto('/md/items');
    await expect(page.getByTestId('md-item-title')).toBeVisible();
    await expect(page.getByTestId('md-item-create')).toHaveCount(0);
    expect(refs).toEqual([]);
    await page.goto('/md/item-categories');
    await expect(page.getByTestId('error-page-403')).toBeVisible();
  });
});
