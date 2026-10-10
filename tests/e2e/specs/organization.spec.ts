import { Page, Route } from '@playwright/test';
import { E2E_AUTHORIZATION, expect, openHome, test } from '../support/fixtures';

// 介面 8.58–8.60（REQ_F4118.20–22）：公司／分店／部門維護頁。API 以 page.route 模擬（ADR-013）。

interface Row {
  id: number;
  code: string;
  name: string;
  isActive: boolean;
  version: number;
  branchId?: number | null;
  parentDepartmentId?: number | null;
}

interface Call {
  method: string;
  path: string;
  company: string;
  body: unknown;
}

/** 模擬三個組織 API；資料按 X-Company-Id 分開存放，POST／PUT 會更新資料並記錄呼叫。 */
async function mockOrganizationApi(page: Page, data: Record<string, Record<string, Row[]>>): Promise<Call[]> {
  const calls: Call[] = [];
  const handler = (resource: string) => async (route: Route) => {
    const request = route.request();
    const url = new URL(request.url());
    const company = request.headers()['x-company-id'] ?? '';
    const rows = ((data[resource] ??= {})[company] ??= []);
    const body = request.postDataJSON() as Record<string, unknown> | null;
    calls.push({ method: request.method(), path: url.pathname, company, body });
    if (request.method() === 'GET') {
      const keyword = (url.searchParams.get('keyword') ?? '').toLowerCase();
      const items = rows.filter((r) => !keyword || `${r.code} ${r.name}`.toLowerCase().includes(keyword));
      await route.fulfill({ json: { items, page: 1, pageSize: 20, totalCount: items.length } });
    } else if (request.method() === 'POST') {
      const code = String(body?.['code']);
      if (rows.some((r) => r.code === code)) {
        await route.fulfill({ status: 409, json: { status: 409, errorCode: `SYS.${resource.toUpperCase()}.DUPLICATE_CODE` } });
        return;
      }
      const created = { id: 100 + rows.length, code, name: String(body?.['name']), isActive: true, version: 1 };
      rows.push(created);
      await route.fulfill({ status: 201, json: created });
    } else {
      const id = Number(url.pathname.split('/').at(-1));
      const row = rows.find((r) => r.id === id)!;
      if (body?.['version'] !== row.version) {
        await route.fulfill({ status: 409, json: { status: 409, errorCode: 'COMMON.CONCURRENCY.CONFLICT' } });
        return;
      }
      Object.assign(row, body, { version: row.version + 1 });
      await route.fulfill({ json: row });
    }
  };
  await page.route('**/api/v1/sys/companies**', handler('company'));
  await page.route('**/api/v1/sys/branches**', handler('branch'));
  await page.route('**/api/v1/sys/departments**', handler('department'));
  return calls;
}

async function saveDialog(page: Page): Promise<void> {
  await page.locator('.ant-modal-footer .ant-btn-primary').click();
}

test.describe('組織維護（全權限）', () => {
  test('由選單進入分店頁，新增分店後列表更新；重複代碼顯示錯誤', async ({ page, pageErrors }) => {
    pageErrors.allow(/status of 409/);
    const calls = await mockOrganizationApi(page, { branch: { '1': [{ id: 1, code: 'TPE', name: '台北總店', isActive: true, version: 1 }] } });
    await openHome(page);

    await page.getByTestId('layout-menu-sys').click();
    await page.getByTestId('layout-menu-sys-branches').click();
    await expect(page).toHaveURL(/\/sys\/branches$/);
    await expect(page.getByTestId('sys-organization-row-TPE')).toBeVisible();
    expect(calls.find((c) => c.method === 'GET')?.company).toBe('1');

    await page.getByTestId('sys-organization-create').click();
    await page.getByTestId('sys-code').fill('tpe');
    await page.getByTestId('sys-name').fill('重複');
    await saveDialog(page);
    await expect(page.getByTestId('sys-organization-save-error')).toContainText('分店代碼');

    await page.getByTestId('sys-code').fill('kh01');
    await page.getByTestId('sys-name').fill(' 高雄分店 ');
    await saveDialog(page);
    await expect(page.getByTestId('sys-organization-row-KH01')).toBeVisible();
    expect(calls.filter((c) => c.method === 'POST').at(-1)?.body).toEqual({ code: 'KH01', name: '高雄分店' });
  });

  test('切換公司後分店列表只顯示新公司的資料', async ({ page }) => {
    const calls = await mockOrganizationApi(page, {
      branch: {
        '1': [{ id: 1, code: 'TPE', name: '台北總店', isActive: true, version: 1 }],
        '2': [{ id: 2, code: 'KLN', name: '九龍分店', isActive: true, version: 1 }],
      },
    });
    await page.goto('/sys/branches');
    await expect(page.getByTestId('sys-organization-row-TPE')).toBeVisible();

    await page.getByTestId('layout-company-select').click();
    await page.locator('nz-option-item', { hasText: 'HK01 Hong Kong' }).click();

    await expect(page.getByTestId('sys-organization-row-KLN')).toBeVisible();
    await expect(page.getByTestId('sys-organization-row-TPE')).toBeHidden();
    expect(calls.filter((c) => c.method === 'GET').at(-1)?.company).toBe('2');
  });

  test('編輯部門：停用並保存；版本衝突時顯示提示', async ({ page, pageErrors }) => {
    pageErrors.allow(/status of 409/);
    const calls = await mockOrganizationApi(page, {
      branch: { '1': [{ id: 9, code: 'TPE', name: '台北總店', isActive: true, version: 1 }] },
      department: { '1': [{ id: 5, code: 'FIN', name: '財務部', branchId: 9, parentDepartmentId: null, isActive: true, version: 3 }] },
    });
    await page.goto('/sys/departments');
    const row = page.getByTestId('sys-organization-row-FIN');
    await expect(row).toContainText('TPE — 台北總店');

    await page.getByTestId('sys-organization-edit-FIN').click();
    await expect(page.getByTestId('sys-code')).toBeDisabled();
    await page.getByTestId('sys-active').uncheck();
    await saveDialog(page);
    await expect(row).toContainText('停用');
    expect(calls.find((c) => c.method === 'PUT')?.body).toMatchObject({ isActive: false, version: 3, branchId: 9 });

    // 模擬保存前已被他人修改 → 409
    await page.route('**/api/v1/sys/departments/5', (route) =>
      route.fulfill({ status: 409, json: { status: 409, errorCode: 'COMMON.CONCURRENCY.CONFLICT' } }));
    await page.getByTestId('sys-organization-edit-FIN').click();
    await saveDialog(page);
    await expect(page.getByTestId('sys-organization-save-error')).toContainText('已被他人修改');
  });
});

test.describe('組織維護（只可檢視公司）', () => {
  test.use({
    authorization: { ...E2E_AUTHORIZATION, isFullAccess: false, permissions: [{ code: 'SYS.COMPANY.VIEW', dataScope: 'Company' }] },
  });

  test('選單只顯示公司；沒有新增／編輯按鈕；直接進入分店頁顯示 403', async ({ page }) => {
    await mockOrganizationApi(page, { company: { '1': [{ id: 1, code: 'TW01', name: 'Taipei', isActive: true, version: 1 }] } });
    await openHome(page);

    await page.getByTestId('layout-menu-sys').click();
    await expect(page.getByTestId('layout-menu-sys-companies')).toBeVisible();
    await expect(page.getByTestId('layout-menu-sys-branches')).toHaveCount(0);
    await page.getByTestId('layout-menu-sys-companies').click();
    await expect(page.getByTestId('sys-organization-row-TW01')).toBeVisible();
    await expect(page.getByTestId('sys-organization-create')).toHaveCount(0);
    await expect(page.getByTestId('sys-organization-edit-TW01')).toHaveCount(0);

    await page.goto('/sys/branches');
    await expect(page.getByTestId('error-page-403')).toBeVisible();
  });
});
