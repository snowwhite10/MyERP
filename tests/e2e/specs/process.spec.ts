import { E2E_AUTHORIZATION, expect, test } from '../support/fixtures';

const pageResult = (items: unknown[]) => ({ items, page: 1, pageSize: 20, totalCount: items.length });
const categories = [
  { id: 1, code: 'ASM', name: '組裝', sortOrder: 1, processCount: 2, isActive: true, version: 3 },
  { id: 2, code: 'OLD', name: '舊類別', sortOrder: 2, processCount: 0, isActive: false, version: 1 },
];
const uoms = [
  { id: 8, code: 'PCS', name: '個', decimalPlaces: 0, isActive: true, version: 1 },
  { id: 9, code: 'BOX', name: '箱', decimalPlaces: 0, isActive: false, version: 1 },
];

test('工序類別按排序號列出工序數，建立帶排序號，刪除有工序的類別顯示三語錯誤', async ({ page, pageErrors }) => {
  pageErrors.allow(/status of 409/);
  const calls: { method: string; body: Record<string, unknown> | null; query: string }[] = [];
  await page.route('**/api/v1/md/process-categories**', route => {
    const request = route.request();
    calls.push({ method: request.method(), body: request.postDataJSON() as Record<string, unknown> | null,
      query: new URL(request.url()).search });
    if (request.method() === 'POST') return route.fulfill({ status: 201, json: { id: 3, processCount: 0, isActive: true, version: 1,
      ...(request.postDataJSON() as object) } });
    if (request.method() === 'DELETE') return route.fulfill({ status: 409, contentType: 'application/problem+json',
      json: { status: 409, errorCode: 'MD.PROCESS_CATEGORY.HAS_PROCESSES' } });
    return route.fulfill({ json: pageResult(categories) });
  });
  await page.goto('/md/process-categories');
  await expect(page.getByTestId('md-process-title')).toHaveText('工序類別');
  expect(calls[0].query).toContain('sort=sortOrder');
  await expect(page.getByTestId('md-process-row-ASM')).toContainText('2');
  await page.getByTestId('md-process-create').click();
  await page.getByTestId('md-process-code').fill('pnt');
  await page.getByTestId('md-process-name').fill('噴漆');
  await page.getByTestId('md-process-sort').fill('10000');
  await page.locator('.ant-modal-footer .ant-btn-primary').click();
  await expect(page.getByText('請輸入 0–9999 的整數。')).toBeVisible();
  await page.getByTestId('md-process-sort').fill('5');
  await page.locator('.ant-modal-footer .ant-btn-primary').click();
  await expect.poll(() => calls.filter(x => x.method === 'POST').length).toBe(1);
  expect(calls.find(x => x.method === 'POST')?.body).toEqual({ code: 'PNT', name: '噴漆', sortOrder: 5 });
  await page.getByTestId('md-process-row-ASM').getByRole('button', { name: '刪除' }).click();
  await page.getByRole('dialog').getByRole('button', { name: '刪除' }).click();
  await expect(page.getByText('類別下仍有工序')).toBeVisible();
  expect(calls.find(x => x.method === 'DELETE')?.query).toContain('version=3');
});

test('工序依類別篩選，選項只列啟用類別與單位，編輯送出工價與版本', async ({ page }) => {
  const process = { id: 5, code: 'CUT', name: '裁切', categoryId: 2, uomId: 9, standardRate: 1.5, sortOrder: 3, isActive: true, version: 4 };
  const calls: { method: string; body: Record<string, unknown> | null; query: string }[] = [];
  await page.route('**/api/v1/md/process-categories**', route => route.fulfill({ json: pageResult(categories) }));
  await page.route('**/api/v1/md/uoms**', route => route.fulfill({ json: pageResult(uoms) }));
  await page.route('**/api/v1/md/processes**', route => {
    const request = route.request();
    calls.push({ method: request.method(), body: request.postDataJSON() as Record<string, unknown> | null,
      query: new URL(request.url()).search });
    if (request.method() === 'POST') return route.fulfill({ status: 201, json: { id: 6, isActive: true, version: 1,
      ...(request.postDataJSON() as object) } });
    if (request.method() === 'PUT') return route.fulfill({ json: { ...process, ...(request.postDataJSON() as object) } });
    return route.fulfill({ json: pageResult([process]) });
  });
  await page.goto('/md/processes');
  await expect(page.getByTestId('md-process-row-CUT')).toContainText('OLD — 舊類別');
  await expect(page.getByTestId('md-process-row-CUT')).toContainText('BOX');
  await page.getByTestId('md-process-filter').selectOption({ label: 'ASM — 組裝' });
  await expect.poll(() => calls.some(x => x.method === 'GET' && x.query.includes('categoryId=1'))).toBe(true);

  await page.getByTestId('md-process-create').click();
  await expect(page.getByTestId('md-process-category').locator('option')).toHaveText(['不指定類別', 'ASM — 組裝']);
  await expect(page.getByTestId('md-process-uom').locator('option')).toHaveText(['不指定單位', 'PCS — 個']);
  await page.getByTestId('md-process-code').fill('weld');
  await page.getByTestId('md-process-name').fill('焊接');
  await page.getByTestId('md-process-uom').selectOption({ label: 'PCS — 個' });
  await page.getByTestId('md-process-rate').fill('-1');
  await page.locator('.ant-modal-footer .ant-btn-primary').click();
  await expect(page.getByText('工價須 ≥ 0')).toBeVisible();
  await page.getByTestId('md-process-rate').fill('12.345678');
  await page.locator('.ant-modal-footer .ant-btn-primary').click();
  await expect.poll(() => calls.filter(x => x.method === 'POST').length).toBe(1);
  expect(calls.find(x => x.method === 'POST')?.body).toEqual({ code: 'WELD', name: '焊接', categoryId: 1, uomId: 8,
    standardRate: 12.345678, sortOrder: 0 });

  await page.getByTestId('md-process-row-CUT').getByRole('button', { name: '編輯' }).click();
  await expect(page.getByTestId('md-process-code')).toBeDisabled();
  await expect(page.getByTestId('md-process-category').locator('option')).toHaveText(['不指定類別', 'ASM — 組裝', 'OLD — 舊類別']);
  await page.getByTestId('md-process-rate').fill('2');
  await page.getByTestId('md-process-active').uncheck();
  await page.locator('.ant-modal-footer .ant-btn-primary').click();
  await expect.poll(() => calls.filter(x => x.method === 'PUT').length).toBe(1);
  expect(calls.find(x => x.method === 'PUT')?.body).toEqual({ name: '裁切', categoryId: 2, uomId: 9, standardRate: 2,
    sortOrder: 3, isActive: false, version: 4 });
});

test('工序已被使用時刪除顯示三語提示', async ({ page, pageErrors }) => {
  pageErrors.allow(/status of 409/);
  await page.route('**/api/v1/md/process-categories**', route => route.fulfill({ json: pageResult(categories) }));
  await page.route('**/api/v1/md/uoms**', route => route.fulfill({ json: pageResult(uoms) }));
  await page.route('**/api/v1/md/processes**', route => route.request().method() === 'DELETE'
    ? route.fulfill({ status: 409, contentType: 'application/problem+json', json: { status: 409, errorCode: 'MD.PROCESS.IN_USE' } })
    : route.fulfill({ json: pageResult([{ id: 5, code: 'CUT', name: '裁切', categoryId: null, uomId: null,
      standardRate: 0, sortOrder: 0, isActive: true, version: 1 }]) }));
  await page.goto('/md/processes');
  await page.getByTestId('md-process-row-CUT').getByRole('button', { name: '刪除' }).click();
  await page.getByRole('dialog').getByRole('button', { name: '刪除' }).click();
  await expect(page.getByText('工序已被生產資料使用，只可停用。')).toBeVisible();
});

test.describe('只讀權限', () => {
  test.use({ authorization: { ...E2E_AUTHORIZATION, isFullAccess: false,
    permissions: [{ code: 'MD.PROCESS.VIEW', dataScope: 'Company' }] } });
  test('不查無權限的類別與單位 API，隱藏維護動作，類別頁 403', async ({ page }) => {
    let referenceCalls = 0;
    await page.route('**/api/v1/md/process-categories**', route => { referenceCalls++; return route.fulfill({ json: pageResult([]) }); });
    await page.route('**/api/v1/md/uoms**', route => { referenceCalls++; return route.fulfill({ json: pageResult([]) }); });
    await page.route('**/api/v1/md/processes**', route => route.fulfill({ json: pageResult([{ id: 5, code: 'CUT', name: '裁切',
      categoryId: null, uomId: null, standardRate: 0, sortOrder: 0, isActive: true, version: 1 }]) }));
    await page.goto('/md/processes');
    await expect(page.getByTestId('md-process-row-CUT')).toBeVisible();
    await expect(page.getByTestId('md-process-create')).toHaveCount(0);
    await expect(page.getByTestId('md-process-row-CUT').getByRole('button')).toHaveCount(0);
    expect(referenceCalls).toBe(0);
    await page.goto('/md/process-categories');
    await expect(page.getByTestId('error-page-403')).toBeVisible();
  });
});
