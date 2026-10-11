import { E2E_AUTHORIZATION, expect, test } from '../support/fixtures';

test('倉庫搜尋、新增、修改、刪除提示與公司切換', async ({ page, pageErrors }) => {
  pageErrors.allow(/status of 409/);
  const rows: Record<string, Record<string, unknown>[]> = { '1': [
    { id: 1, code: 'MAIN', name: '總倉', address: '台北市', contactName: '王小明', phone: '02-1234',
      isActive: true, version: 1 }], '2': [] };
  const writes: { method: string; body: unknown; url: string }[] = [];
  await page.route('**/api/v1/md/warehouses**', async (route) => {
    const request = route.request(); const company = request.headers()['x-company-id'] ?? '1';
    const data = rows[company] ??= [];
    if (request.method() === 'GET') {
      const keyword = new URL(request.url()).searchParams.get('keyword')?.toLowerCase() ?? '';
      const items = data.filter((row) => [row['code'], row['name'], row['address']]
        .some((value) => String(value ?? '').toLowerCase().includes(keyword)));
      return route.fulfill({ json: { items, totalCount: items.length, page: 1, pageSize: 20 } });
    }
    writes.push({ method: request.method(), body: request.postDataJSON(), url: request.url() });
    if (request.method() === 'POST') {
      const row = { ...(request.postDataJSON() as Record<string, unknown>), id: 2, isActive: true, version: 1 };
      data.push(row);
      return route.fulfill({ status: 201, json: row });
    }
    if (request.method() === 'DELETE') return route.fulfill({ status: 409, contentType: 'application/problem+json',
      json: { status: 409, errorCode: 'MD.WAREHOUSE.HAS_LOCATIONS' } });
    const row = data.find((x) => x['id'] === 1)!;
    Object.assign(row, request.postDataJSON(), { version: 2 });
    return route.fulfill({ json: row });
  });
  await page.goto('/md/warehouses');
  await expect(page.getByTestId('md-warehouse-row-MAIN')).toContainText('台北市');
  await page.getByTestId('md-warehouse-search').fill('台北');
  await page.getByTestId('md-warehouse-search-button').click();
  await expect(page.getByTestId('md-warehouse-row-MAIN')).toBeVisible();
  await page.getByTestId('md-warehouse-search').fill('');
  await page.getByTestId('md-warehouse-search-button').click();

  await page.getByTestId('md-warehouse-create').click();
  await page.getByTestId('md-warehouse-code').fill('spare');
  await page.getByTestId('md-warehouse-name').fill('備品倉');
  await page.getByTestId('md-warehouse-phone').fill(' 03-555 ');
  await page.locator('.ant-modal-footer .ant-btn-primary').click();
  await expect(page.getByTestId('md-warehouse-row-SPARE')).toBeVisible();
  expect(writes[0]).toMatchObject({ method: 'POST', body: { code: 'SPARE', name: '備品倉', address: null, phone: '03-555' } });

  await page.getByTestId('md-warehouse-edit-MAIN').click();
  await expect(page.getByTestId('md-warehouse-code')).toBeDisabled();
  await page.getByTestId('md-warehouse-address').fill('新北市');
  await page.getByTestId('md-warehouse-active').uncheck();
  await page.locator('.ant-modal-footer .ant-btn-primary').click();
  await expect(page.getByTestId('md-warehouse-row-MAIN')).toContainText('停用');
  expect(writes[1]).toMatchObject({ method: 'PUT', body: { address: '新北市', isActive: false, version: 1 } });

  await page.getByTestId('md-warehouse-delete-MAIN').click();
  await page.getByTestId('erp-message-ok').click();
  await expect(page.getByTestId('md-warehouse-error')).toContainText('此倉庫仍有儲位');
  expect(writes[2]!.url).toContain('/warehouses/1?version=2');

  await page.getByTestId('layout-company-select').click();
  await page.locator('nz-option-item', { hasText: 'HK01 Hong Kong' }).click();
  await expect(page.getByTestId('md-warehouse-row-MAIN')).toHaveCount(0);
});

test('儲位：列出、新增、重複代碼提示、修改、刪除；停用倉庫不可新增', async ({ page, pageErrors }) => {
  pageErrors.allow(/status of 409/);
  const locations: Record<string, unknown>[] = [{ id: 7, warehouseId: 1, code: 'A-01', name: '貨架 A1', isActive: true, version: 1 }];
  const writes: { method: string; body: unknown; url: string }[] = [];
  await page.route('**/api/v1/md/warehouses?**', (route) => route.fulfill({ json: { items: [
    { id: 1, code: 'MAIN', name: '總倉', address: null, contactName: null, phone: null, isActive: true, version: 1 },
    { id: 2, code: 'OLD', name: '舊倉', address: null, contactName: null, phone: null, isActive: false, version: 1 }], totalCount: 2 } }));
  await page.route('**/api/v1/md/warehouses/*/locations**', async (route) => {
    const request = route.request();
    if (request.method() === 'GET') {
      const items = request.url().includes('/warehouses/1/') ? locations : [];
      return route.fulfill({ json: { items, totalCount: items.length, page: 1, pageSize: 10 } });
    }
    writes.push({ method: request.method(), body: request.postDataJSON(), url: request.url() });
    if (request.method() === 'POST') {
      const body = request.postDataJSON() as { code: string; name: string };
      if (body.code === 'A-01') return route.fulfill({ status: 409, contentType: 'application/problem+json',
        json: { status: 409, errorCode: 'MD.STORAGE_LOCATION.DUPLICATE_CODE' } });
      locations.push({ id: 8, warehouseId: 1, ...body, isActive: true, version: 1 });
      return route.fulfill({ status: 201, json: locations.at(-1) });
    }
    if (request.method() === 'PUT') {
      Object.assign(locations[0]!, request.postDataJSON(), { version: 2 });
      return route.fulfill({ json: locations[0] });
    }
    locations.splice(0, 1);
    return route.fulfill({ status: 204 });
  });
  await page.goto('/md/warehouses');
  await page.getByTestId('md-warehouse-locations-MAIN').click();
  await expect(page.getByTestId('md-location-row-A-01')).toContainText('貨架 A1');
  await page.getByTestId('md-location-code').fill('a-01');
  await page.getByTestId('md-location-name').fill('重複');
  await page.getByTestId('md-location-save').click();
  await expect(page.getByTestId('md-location-error')).toContainText('此倉庫已使用該儲位代碼');
  await page.getByTestId('md-location-code').fill('b-02');
  await page.getByTestId('md-location-name').fill('貨架 B2');
  await page.getByTestId('md-location-save').click();
  await expect(page.getByTestId('md-location-row-B-02')).toBeVisible();
  expect(writes.at(-1)).toMatchObject({ method: 'POST', body: { code: 'B-02', name: '貨架 B2' } });

  await page.getByTestId('md-location-edit-A-01').click();
  await expect(page.getByTestId('md-location-code')).toBeDisabled();
  await page.getByTestId('md-location-name').fill('冷藏貨架');
  await page.getByTestId('md-location-save').click();
  await expect(page.getByTestId('md-location-row-A-01')).toContainText('冷藏貨架');
  expect(writes.at(-1)).toMatchObject({ method: 'PUT', body: { name: '冷藏貨架', isActive: true, version: 1 } });

  await page.getByTestId('md-location-delete-A-01').click();
  await page.getByTestId('erp-message-ok').click();
  await expect(page.getByTestId('md-location-row-A-01')).toHaveCount(0);
  expect(writes.at(-1)!.url).toContain('/warehouses/1/locations/7?version=2');

  await page.keyboard.press('Escape');
  await expect(page.getByTestId('md-location-dialog')).toHaveCount(0);
  await page.getByTestId('md-warehouse-locations-OLD').click();
  await expect(page.getByTestId('md-location-inactive')).toBeVisible();
  await expect(page.getByTestId('md-location-form')).toHaveCount(0);
});

test.describe('倉庫只讀權限', () => {
  test.use({ authorization: { ...E2E_AUTHORIZATION, isFullAccess: false,
    permissions: [{ code: 'MD.WAREHOUSE.VIEW', dataScope: 'Company' }] } });
  test('隱藏寫入按鈕，儲位只可查看', async ({ page }) => {
    await page.route('**/api/v1/md/warehouses?**', (route) => route.fulfill({ json: { items: [
      { id: 1, code: 'MAIN', name: '總倉', address: null, contactName: null, phone: null, isActive: true, version: 1 }], totalCount: 1 } }));
    await page.route('**/api/v1/md/warehouses/1/locations**', (route) => route.fulfill({ json: { items: [
      { id: 7, warehouseId: 1, code: 'A-01', name: '貨架 A1', isActive: true, version: 1 }], totalCount: 1 } }));
    await page.goto('/md/warehouses');
    await expect(page.getByTestId('md-warehouse-title')).toBeVisible();
    await expect(page.getByTestId('md-warehouse-create')).toHaveCount(0);
    await expect(page.getByTestId('md-warehouse-edit-MAIN')).toHaveCount(0);
    await expect(page.getByTestId('md-warehouse-delete-MAIN')).toHaveCount(0);
    await page.getByTestId('md-warehouse-locations-MAIN').click();
    await expect(page.getByTestId('md-location-row-A-01')).toBeVisible();
    await expect(page.getByTestId('md-location-form')).toHaveCount(0);
    await expect(page.getByTestId('md-location-edit-A-01')).toHaveCount(0);
  });
});
