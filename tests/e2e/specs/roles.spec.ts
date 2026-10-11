import { Page } from '@playwright/test';
import { chooseLanguage, E2E_AUTHORIZATION, expect, openHome, test } from '../support/fixtures';

// 介面 8.64（REQ_F4118.25.1–4）：角色維護頁。API 以 page.route 模擬（ADR-013）。

interface Role {
  id: number;
  code: string;
  name: string;
  description: string | null;
  dataScope: string;
  isFullAccess: boolean;
  isActive: boolean;
  version: number;
  permissions: string[];
}

interface Call {
  method: string;
  path: string;
  query: string;
  body: Record<string, unknown> | null;
}

const CATALOG = [
  { code: 'SYS.USER.VIEW', module: 'SYS' },
  { code: 'SYS.USER.EDIT', module: 'SYS' },
  { code: 'SYS.ROLE.VIEW', module: 'SYS' },
  { code: 'MD.ITEM.VIEW', module: 'MD' },
];

const role = (id: number, code: string, overrides: Partial<Role> = {}): Role => ({
  id,
  code,
  name: code,
  description: null,
  dataScope: 'Self',
  isFullAccess: false,
  isActive: true,
  version: 1,
  permissions: [],
  ...overrides,
});

/** 模擬角色與權限目錄 API；`inUse` 中的代碼刪除時回 422，重複代碼回 409，版本不符回 409。 */
async function mockRoleApi(page: Page, rows: Role[], inUse: string[] = []): Promise<Call[]> {
  const calls: Call[] = [];
  await page.route('**/api/v1/sys/permissions', (route) => route.fulfill({ json: CATALOG }));
  await page.route('**/api/v1/sys/roles**', async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    const body = request.postDataJSON() as Record<string, unknown> | null;
    calls.push({ method: request.method(), path: url.pathname, query: url.search, body });
    const id = Number(url.pathname.split('/')[5]);
    const row = rows.find((r) => r.id === id);

    if (request.method() === 'GET') {
      const keyword = (url.searchParams.get('keyword') ?? '').toLowerCase();
      const items = rows
        .filter((r) => !keyword || r.code.toLowerCase().includes(keyword) || r.name.toLowerCase().includes(keyword))
        .sort((a, b) => a.code.localeCompare(b.code));
      await route.fulfill({ json: { items, page: 1, pageSize: 20, totalCount: items.length } });
    } else if (request.method() === 'POST') {
      const code = String(body?.['code']);
      if (rows.some((r) => r.code === code)) {
        await route.fulfill({ status: 409, json: { status: 409, errorCode: 'SYS.ROLE.DUPLICATE_CODE' } });
        return;
      }
      const created = role(100 + rows.length, code, body as Partial<Role>);
      rows.push(created);
      await route.fulfill({ status: 201, json: created });
    } else if (!row) {
      await route.fulfill({ status: 404, json: { status: 404, errorCode: 'SYS.ROLE.NOT_FOUND' } });
    } else if (request.method() === 'DELETE') {
      if (inUse.includes(row.code)) {
        await route.fulfill({ status: 422, json: { status: 422, errorCode: 'SYS.ROLE.IN_USE' } });
        return;
      }
      rows.splice(rows.indexOf(row), 1);
      await route.fulfill({ status: 204 });
    } else {
      if (body?.['version'] !== row.version) {
        await route.fulfill({ status: 409, json: { status: 409, errorCode: 'COMMON.CONCURRENCY.CONFLICT' } });
        return;
      }
      Object.assign(row, body, { version: row.version + 1 });
      await route.fulfill({ json: row });
    }
  });
  return calls;
}

async function saveDialog(page: Page): Promise<void> {
  await page.locator('.ant-modal-footer .ant-btn-primary').click();
}

/** 在共用確認框（erp-message-dialog）按確定；確認框關閉後才繼續。 */
async function confirmDelete(page: Page): Promise<void> {
  await expect(page.getByTestId('erp-message-dialog')).toBeVisible();
  await page.getByTestId('erp-message-ok').click();
  await expect(page.getByTestId('erp-message-dialog')).toHaveCount(0);
}

test.describe('角色維護（全權限）', () => {
  test('由選單進入角色頁，新增角色：代碼格式、依模組勾選權限、重複代碼與成功', async ({ page, pageErrors }) => {
    pageErrors.allow(/status of 409/);
    const calls = await mockRoleApi(page, [role(1, 'ADMIN', { name: '系統管理員', dataScope: 'Company', isFullAccess: true })]);
    await openHome(page);

    await page.getByTestId('layout-menu-sys').click();
    await page.getByTestId('layout-menu-sys-roles').click();
    await expect(page).toHaveURL(/\/sys\/roles$/);
    await expect(page.getByTestId('sys-role-row-ADMIN')).toContainText('全公司');
    await expect(page.getByTestId('sys-role-full-ADMIN')).toBeVisible();

    await page.getByTestId('sys-role-create').click();
    await page.getByTestId('sys-role-code').fill('bad code');
    await page.getByTestId('sys-role-name').fill('出納');
    await saveDialog(page);
    await expect(page.getByTestId('sys-role-form')).toContainText('請輸入 1–50 個英文字母');
    expect(calls.some((c) => c.method === 'POST')).toBe(false);

    // 模組全選後取消其中一項 → 模組勾選框呈半選
    await page.getByTestId('sys-role-module-toggle-SYS').click();
    await page.getByTestId('sys-role-permission-SYS.USER.EDIT').click();
    await expect(page.getByTestId('sys-role-module-toggle-SYS').locator('.ant-checkbox-indeterminate')).toBeVisible();
    await page.getByTestId('sys-role-permission-MD.ITEM.VIEW').click();
    await page.getByTestId('sys-role-scope').click();
    await page.locator('nz-option-item', { hasText: '本分店' }).click();

    await page.getByTestId('sys-role-code').fill('admin');
    await saveDialog(page);
    await expect(page.getByTestId('sys-role-save-error')).toContainText('角色代碼已被使用');

    await page.getByTestId('sys-role-code').fill(' cashier ');
    await saveDialog(page);
    await expect(page.getByTestId('sys-role-row-CASHIER')).toContainText('3 項權限');
    await expect(page.getByTestId('sys-role-notice')).toContainText('已新增角色 CASHIER');
    expect(calls.filter((c) => c.method === 'POST').at(-1)?.body).toEqual({
      code: 'CASHIER',
      name: '出納',
      description: null,
      dataScope: 'Branch',
      isFullAccess: false,
      permissions: ['MD.ITEM.VIEW', 'SYS.ROLE.VIEW', 'SYS.USER.VIEW'],
    });
  });

  test('編輯角色帶版本、改為全權並停用；刪除使用中角色顯示錯誤，未使用角色可刪除', async ({ page, pageErrors }) => {
    pageErrors.allow(/status of 422/);
    const calls = await mockRoleApi(
      page,
      [role(1, 'CLERK', { name: '文員', version: 4, permissions: ['SYS.USER.VIEW'] }), role(2, 'OLD', { version: 2 })],
      ['CLERK'],
    );
    await page.goto('/sys/roles');

    await page.getByTestId('sys-role-edit-CLERK').click();
    await expect(page.locator('.ant-modal-title')).toContainText('編輯角色 CLERK');
    await expect(page.getByTestId('sys-role-code')).toHaveCount(0);
    await expect(page.getByTestId('sys-role-permission-SYS.USER.VIEW').locator('.ant-checkbox-checked')).toBeVisible();
    await page.getByTestId('sys-role-full-access').click();
    await expect(page.getByTestId('sys-role-full-access-hint')).toBeVisible();
    await page.getByTestId('sys-role-active').click();
    await saveDialog(page);
    await expect(page.getByTestId('sys-role-notice')).toContainText('已更新角色 CLERK');
    await expect(page.getByTestId('sys-role-full-CLERK')).toBeVisible();
    await expect(page.getByTestId('sys-role-row-CLERK')).toContainText('停用');
    expect(calls.find((c) => c.method === 'PUT')?.body).toMatchObject({ isFullAccess: true, isActive: false, version: 4, permissions: ['SYS.USER.VIEW'] });

    // 取消確認框不送出刪除
    await page.getByTestId('sys-role-delete-CLERK').click();
    await expect(page.getByTestId('erp-message-title')).toContainText('確定刪除角色 CLERK？');
    await expect(page.getByTestId('erp-message-content')).toContainText('此操作無法復原');
    await page.getByTestId('erp-message-cancel').click();
    await expect(page.getByTestId('erp-message-dialog')).toHaveCount(0);
    expect(calls.some((c) => c.method === 'DELETE')).toBe(false);

    await page.getByTestId('sys-role-delete-CLERK').click();
    await confirmDelete(page);
    await expect(page.getByTestId('sys-role-action-error')).toContainText('此角色仍指派給用戶');
    await expect(page.getByTestId('sys-role-row-CLERK')).toBeVisible();

    await page.getByTestId('sys-role-delete-OLD').click();
    await confirmDelete(page);
    await expect(page.getByTestId('sys-role-notice')).toContainText('已刪除角色 OLD');
    await expect(page.getByTestId('sys-role-row-OLD')).toHaveCount(0);
    expect(calls.filter((c) => c.method === 'DELETE').at(-1)?.query).toBe('?version=2');
  });

  test('英文介面', async ({ page }) => {
    await mockRoleApi(page, [role(1, 'CLERK', { dataScope: 'Department' })]);
    await page.goto('/sys/roles');
    await chooseLanguage(page, 'en');
    await expect(page.getByTestId('sys-role-title')).toHaveText('Roles');
    await expect(page.getByTestId('sys-role-row-CLERK')).toContainText('Department');
  });
});

test.describe('角色維護（只可檢視）', () => {
  test.use({
    authorization: { ...E2E_AUTHORIZATION, isFullAccess: false, permissions: [{ code: 'SYS.ROLE.VIEW', dataScope: 'Company' }] },
  });

  test('沒有新增、編輯與刪除按鈕；沒有用戶選單', async ({ page }) => {
    await mockRoleApi(page, [role(1, 'CLERK')]);
    await openHome(page);
    await page.getByTestId('layout-menu-sys').click();
    await expect(page.getByTestId('layout-menu-sys-users')).toHaveCount(0);
    await page.getByTestId('layout-menu-sys-roles').click();
    await expect(page.getByTestId('sys-role-row-CLERK')).toBeVisible();
    await expect(page.getByTestId('sys-role-create')).toHaveCount(0);
    await expect(page.getByTestId('sys-role-edit-CLERK')).toHaveCount(0);
    await expect(page.getByTestId('sys-role-delete-CLERK')).toHaveCount(0);
  });
});

test.describe('角色維護（無角色權限）', () => {
  test.use({
    authorization: { ...E2E_AUTHORIZATION, isFullAccess: false, permissions: [{ code: 'SYS.USER.VIEW', dataScope: 'Company' }] },
  });

  test('選單沒有角色項目，直接進入顯示 403', async ({ page }) => {
    await openHome(page);
    await page.getByTestId('layout-menu-sys').click();
    await expect(page.getByTestId('layout-menu-sys-roles')).toHaveCount(0);
    await page.goto('/sys/roles');
    await expect(page.getByTestId('error-page-403')).toBeVisible();
  });
});
