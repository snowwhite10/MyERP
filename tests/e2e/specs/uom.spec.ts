import { E2E_AUTHORIZATION, expect, test } from '../support/fixtures';

type Unit = { id: number; code: string; name: string; decimalPlaces: number; isActive: boolean; version: number };
type Conversion = { id: number; fromUomId: number; fromUomCode: string; toUomId: number; toUomCode: string;
  factor: number; version: number };

test('計量單位依公司列出，可維護單位與換算並試算', async ({ page, pageErrors }) => {
  pageErrors.allow(/status of 409/);
  pageErrors.allow(/status of 422/);
  const units: Record<string, Unit[]> = {
    '1': [{ id: 1, code: 'KG', name: '公斤', decimalPlaces: 3, isActive: true, version: 1 },
      { id: 2, code: 'G', name: '公克', decimalPlaces: 0, isActive: true, version: 1 }],
    '2': [{ id: 5, code: 'LB', name: '磅', decimalPlaces: 2, isActive: true, version: 1 }],
  };
  const conversions: Record<string, Conversion[]> = { '1': [], '2': [] };
  let releaseDelete!: () => void;
  const pendingDelete = new Promise<void>((resolve) => { releaseDelete = resolve; });
  const calls: { method: string; url: string; company: string; body: Record<string, unknown> | null }[] = [];
  await page.route('**/api/v1/md/uom**', async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    const company = request.headers()['x-company-id'] ?? '';
    const body = request.postDataJSON() as Record<string, unknown> | null;
    calls.push({ method: request.method(), url: url.pathname, company, body });
    const unitRows = units[company] ??= [];
    const conversionRows = conversions[company] ??= [];
    const id = Number(url.pathname.split('/').at(-1));
    if (url.pathname.endsWith('/uom-conversions/convert')) {
      const to = Number(url.searchParams.get('toUomId'));
      if (to === 3) {
        await route.fulfill({ status: 422, contentType: 'application/problem+json',
          json: { status: 422, errorCode: 'MD.UOM_CONVERSION.NO_PATH' } });
      } else {
        await route.fulfill({ json: { fromUomId: 1, toUomId: to, quantity: 1.25, result: 1250 } });
      }
    } else if (url.pathname.includes('/uom-conversions')) {
      if (request.method() === 'GET') {
        await route.fulfill({ json: conversionRows });
      } else if (request.method() === 'POST' && body?.['fromUomId'] === 2) {
        await route.fulfill({ status: 409, contentType: 'application/problem+json',
          json: { status: 409, errorCode: 'MD.UOM_CONVERSION.PATH_EXISTS' } });
      } else if (request.method() === 'POST') {
        const code = (uomId: unknown) => unitRows.find((unit) => unit.id === uomId)!.code;
        const row = { id: 9, fromUomId: Number(body?.['fromUomId']), fromUomCode: code(body?.['fromUomId']),
          toUomId: Number(body?.['toUomId']), toUomCode: code(body?.['toUomId']), factor: Number(body?.['factor']), version: 1 };
        conversionRows.push(row);
        await route.fulfill({ status: 201, json: row });
      } else {
        const row = conversionRows.find((entry) => entry.id === id)!;
        Object.assign(row, body, { version: row.version + 1 });
        await route.fulfill({ json: row });
      }
    } else if (request.method() === 'GET') {
      await route.fulfill({ json: { items: unitRows, page: 1, pageSize: 20, totalCount: unitRows.length } });
    } else if (request.method() === 'POST') {
      const row = { id: 3, code: String(body?.['code']), name: String(body?.['name']),
        decimalPlaces: Number(body?.['decimalPlaces']), isActive: true, version: 1 };
      unitRows.push(row);
      await route.fulfill({ status: 201, json: row });
    } else if (request.method() === 'DELETE') {
      await pendingDelete;
      await route.fulfill({ status: 409, contentType: 'application/problem+json', json: { status: 409, errorCode: 'MD.UOM.IN_USE' } });
    } else {
      const row = unitRows.find((entry) => entry.id === id)!;
      Object.assign(row, body, { version: row.version + 1 });
      await route.fulfill({ json: row });
    }
  });

  await page.goto('/');
  await page.getByTestId('layout-menu-md').click();
  await page.getByTestId('layout-menu-md-uoms').click();
  await expect(page).toHaveURL(/\/md\/uoms$/);
  await expect(page.getByTestId('md-uom-title')).toHaveText('計量單位');
  await expect(page.getByTestId('md-uom-row-KG')).toContainText('3');

  await page.getByTestId('md-uom-create').click();
  await page.getByTestId('md-uom-code').fill('pcs');
  await page.getByTestId('md-uom-name').fill(' 個 ');
  await page.getByTestId('md-uom-decimal-places').selectOption('0');
  await page.locator('.ant-modal-footer .ant-btn-primary').click();
  await expect(page.getByTestId('md-uom-row-PCS')).toBeVisible();
  expect(calls.find((call) => call.method === 'POST')?.body).toEqual({ code: 'PCS', name: '個', decimalPlaces: 0 });

  await page.getByTestId('md-uom-edit-KG').click();
  await expect(page.getByTestId('md-uom-code')).toBeDisabled();
  await page.getByTestId('md-uom-decimal-places').selectOption('2');
  await page.locator('.ant-modal-footer .ant-btn-primary').click();
  await expect(page.getByTestId('md-uom-form')).toBeHidden();
  expect(calls.find((call) => call.method === 'PUT')?.body).toMatchObject({ decimalPlaces: 2, version: 1 });

  await page.getByTestId('md-uom-conversion-create').click();
  await page.getByTestId('md-uom-conversion-from').selectOption({ label: 'KG — 公斤' });
  await page.getByTestId('md-uom-conversion-to').selectOption({ label: 'KG — 公斤' });
  await page.getByTestId('md-uom-conversion-factor').fill('1000');
  await page.locator('.ant-modal-footer .ant-btn-primary').last().click();
  await expect(page.getByText('來源與目標單位不可相同。')).toBeVisible();
  await page.getByTestId('md-uom-conversion-to').selectOption({ label: 'G — 公克' });
  await page.locator('.ant-modal-footer .ant-btn-primary').last().click();
  await expect(page.getByTestId('md-uom-conversion-row-KG-G')).toContainText('1 KG = 1000 G');

  await page.getByTestId('md-uom-conversion-create').click();
  await page.getByTestId('md-uom-conversion-from').selectOption({ label: 'G — 公克' });
  await page.getByTestId('md-uom-conversion-to').selectOption({ label: 'KG — 公斤' });
  await page.getByTestId('md-uom-conversion-factor').fill('0.001');
  await page.locator('.ant-modal-footer .ant-btn-primary').last().click();
  await expect(page.getByTestId('md-uom-conversion-save-error')).toContainText('這兩個單位已可換算');
  await page.locator('.ant-modal-footer .ant-btn').filter({ hasText: '取消' }).last().click();

  await page.getByTestId('md-uom-delete-G').click();
  await page.getByTestId('erp-message-ok').click();
  await expect(page.getByTestId('md-uom-delete-G')).toBeDisabled();
  await expect(page.getByTestId('md-uom-delete-G')).toContainText('處理中');
  expect(units['1'].some((unit) => unit.code === 'G')).toBe(true);
  releaseDelete();
  await expect(page.getByTestId('md-uom-error')).toContainText('已有換算或物料使用此單位');

  await page.getByTestId('md-uom-calc-quantity').fill('1.25');
  await page.getByTestId('md-uom-calc-from').selectOption({ label: 'KG' });
  await page.getByTestId('md-uom-calc-to').selectOption({ label: 'G' });
  await page.getByTestId('md-uom-calc-convert').click();
  await expect(page.getByTestId('md-uom-calc-result')).toHaveText('1250');
  expect(calls.find((call) => call.url.endsWith('/convert'))).toBeTruthy();
  await page.getByTestId('md-uom-calc-to').selectOption({ label: 'PCS' });
  await page.getByTestId('md-uom-calc-convert').click();
  await expect(page.getByTestId('md-uom-calc-error')).toHaveText('這兩個單位之間沒有換算。');

  await page.getByTestId('layout-company-select').click();
  await page.locator('nz-option-item', { hasText: 'HK01 Hong Kong' }).click();
  await expect(page.getByTestId('md-uom-row-LB')).toBeVisible();
  await expect(page.getByTestId('md-uom-row-KG')).toHaveCount(0);
  await expect(page.getByTestId('md-uom-conversion-row-KG-G')).toHaveCount(0);
  await expect(page.getByTestId('md-uom-calc-result')).toHaveCount(0);
});

test.describe('計量單位只讀權限', () => {
  test.use({ authorization: { ...E2E_AUTHORIZATION, isFullAccess: false,
    permissions: [{ code: 'MD.UOM.VIEW', dataScope: 'Company' }] } });

  test('隱藏寫入操作但仍可試算', async ({ page }) => {
    await page.route('**/api/v1/md/uoms**', (route) =>
      route.fulfill({ json: { items: [{ id: 1, code: 'KG', name: '公斤', decimalPlaces: 3, isActive: true, version: 1 }],
        page: 1, pageSize: 20, totalCount: 1 } }));
    await page.route('**/api/v1/md/uom-conversions', (route) => route.fulfill({ json: [
      { id: 7, fromUomId: 1, fromUomCode: 'KG', toUomId: 2, toUomCode: 'G', factor: 1000, version: 1 }] }));
    await page.goto('/md/uoms');
    await expect(page.getByTestId('md-uom-row-KG')).toBeVisible();
    await expect(page.getByTestId('md-uom-conversion-row-KG-G')).toBeVisible();
    await expect(page.getByTestId('md-uom-create')).toHaveCount(0);
    await expect(page.getByTestId('md-uom-edit-KG')).toHaveCount(0);
    await expect(page.getByTestId('md-uom-delete-KG')).toHaveCount(0);
    await expect(page.getByTestId('md-uom-conversion-create')).toHaveCount(0);
    await expect(page.getByTestId('md-uom-conversion-edit-7')).toHaveCount(0);
    await expect(page.getByTestId('md-uom-calc-convert')).toBeVisible();
    await page.goto('/md/suppliers');
    await expect(page.getByTestId('error-page-403')).toBeVisible();
  });
});
