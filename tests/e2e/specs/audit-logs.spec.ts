import { Page } from '@playwright/test';
import { chooseLanguage, E2E_AUTHORIZATION, expect, openHome, test } from '../support/fixtures';

// 介面 8.66（REQ_F4118.27.6）：稽核查詢頁。API 以 page.route 模擬（ADR-013）。

interface AuditLog {
  id: number;
  occurredAt: string;
  category: string;
  action: string;
  tableName: string | null;
  recordId: string | null;
  userId: number | null;
  username: string | null;
  companyId: number | null;
  changes: Record<string, unknown> | null;
  remoteIp: string | null;
  traceId: string | null;
}

const LOGS: AuditLog[] = [
  {
    id: 3,
    occurredAt: '2026-10-11T09:10:00',
    category: 'DATA',
    action: 'UPDATE',
    tableName: 'sys_user',
    recordId: '7',
    userId: 1,
    username: 'admin',
    companyId: 1,
    changes: { is_active: { old: true, new: false }, password_hash: { old: '***', new: '***' } },
    remoteIp: '10.0.0.5',
    traceId: '00-trace-01',
  },
  {
    id: 2,
    occurredAt: '2026-10-11T08:00:00',
    category: 'SECURITY',
    action: 'LOGIN_FAILED',
    tableName: null,
    recordId: null,
    userId: 7,
    username: 'alice',
    companyId: null,
    changes: { reason: 'INVALID_CREDENTIALS' },
    remoteIp: '10.0.0.9',
    traceId: null,
  },
  {
    id: 1,
    occurredAt: '2026-10-01T12:00:00',
    category: 'DATA',
    action: 'CREATE',
    tableName: 'sys_role',
    recordId: '5',
    userId: 1,
    username: 'admin',
    companyId: 1,
    changes: { code: { new: 'CLERK' } },
    remoteIp: '10.0.0.5',
    traceId: null,
  },
];

/** 模擬稽核查詢 API：依 category、action、tableName、recordId、from／to 篩選，依 sort 排序；回傳每次請求的 query。 */
async function mockAuditApi(page: Page): Promise<URLSearchParams[]> {
  const calls: URLSearchParams[] = [];
  await page.route('**/api/v1/sys/audit-logs**', async (route) => {
    const params = new URL(route.request().url()).searchParams;
    calls.push(params);
    const day = (log: AuditLog) => log.occurredAt.slice(0, 10);
    const items = LOGS.filter(
      (log) =>
        (!params.get('category') || log.category === params.get('category')) &&
        (!params.get('action') || log.action === params.get('action')) &&
        (!params.get('tableName') || log.tableName === params.get('tableName')) &&
        (!params.get('recordId') || log.recordId === params.get('recordId')) &&
        (!params.get('from') || day(log) >= params.get('from')!) &&
        (!params.get('to') || day(log) <= params.get('to')!),
    ).sort((a, b) => (params.get('sort') === 'occurredAt' ? a.id - b.id : b.id - a.id));
    await route.fulfill({ json: { items, page: 1, pageSize: 20, totalCount: items.length } });
  });
  return calls;
}

test.describe('稽核查詢（全權限）', () => {
  test('由選單進入，展開欄位差異並以類別、操作與日期篩選', async ({ page }) => {
    const calls = await mockAuditApi(page);
    await openHome(page);
    await page.getByTestId('layout-menu-sys').click();
    await page.getByTestId('layout-menu-sys-audit-logs').click();
    await expect(page).toHaveURL(/\/sys\/audit-logs$/);
    await expect(page.getByTestId('sys-audit-title')).toHaveText('稽核日誌');

    await expect(page.getByTestId('sys-audit-row-3')).toContainText('2026-10-11 09:10:00');
    await expect(page.getByTestId('sys-audit-row-3')).toContainText('資料變更');
    await expect(page.getByTestId('sys-audit-row-3')).toContainText('修改');
    await expect(page.getByTestId('sys-audit-row-3')).toContainText('sys_user #7');
    await expect(page.getByTestId('sys-audit-row-2')).toContainText('登入失敗');
    expect(calls.at(-1)?.get('sort')).toBe('-occurredAt');

    // 展開：old → new，*** 照原樣
    await page.getByTestId('sys-audit-expand-3').locator('button').click();
    await expect(page.getByTestId('sys-audit-change-3-is_active')).toContainText('true');
    await expect(page.getByTestId('sys-audit-change-3-is_active')).toContainText('false');
    await expect(page.getByTestId('sys-audit-change-3-password_hash')).toContainText('***');
    await page.getByTestId('sys-audit-expand-2').locator('button').click();
    await expect(page.getByTestId('sys-audit-change-2-reason')).toContainText('INVALID_CREDENTIALS');

    await page.getByTestId('sys-audit-category').click();
    await page.locator('nz-option-item', { hasText: '安全事件' }).click();
    await page.getByTestId('sys-audit-search').click();
    await expect(page.getByTestId('sys-audit-row-2')).toBeVisible();
    await expect(page.getByTestId('sys-audit-row-3')).toHaveCount(0);
    expect(calls.at(-1)?.get('category')).toBe('SECURITY');

    await page.getByTestId('sys-audit-reset').click();
    await page.getByTestId('sys-audit-action').click();
    await page.locator('nz-option-item', { hasText: '新增' }).click();
    await page.getByTestId('sys-audit-search').click();
    await expect(page.getByTestId('sys-audit-row-1')).toBeVisible();
    await expect(page.getByTestId('sys-audit-row-3')).toHaveCount(0);
    expect(calls.at(-1)?.get('action')).toBe('CREATE');
    expect(calls.at(-1)?.has('category')).toBe(false);

    // 結束日早於開始日：不送出查詢
    await page.getByTestId('sys-audit-reset').click();
    await expect(page.getByTestId('sys-audit-row-3')).toBeVisible();
    const before = calls.length;
    await page.getByTestId('sys-audit-from').fill('2026-10-11');
    await page.getByTestId('sys-audit-to').fill('2026-10-01');
    await page.getByTestId('sys-audit-search').click();
    await expect(page.getByTestId('sys-audit-filter-error')).toContainText('結束日期不可早於開始日期');
    expect(calls.length).toBe(before);

    await page.getByTestId('sys-audit-from').fill('2026-10-01');
    await page.getByTestId('sys-audit-to').fill('2026-10-05');
    await page.getByTestId('sys-audit-search').click();
    await expect(page.getByTestId('sys-audit-row-1')).toBeVisible();
    await expect(page.getByTestId('sys-audit-row-3')).toHaveCount(0);
    expect(calls.at(-1)?.get('from')).toBe('2026-10-01');
    expect(calls.at(-1)?.get('to')).toBe('2026-10-05');
  });

  test('以 query string 開啟單據修改歷史，並可由列切換到另一筆記錄的歷史', async ({ page }) => {
    const calls = await mockAuditApi(page);
    await page.goto('/sys/audit-logs?tableName=sys_role&recordId=5');
    await expect(page.getByTestId('sys-audit-history')).toContainText('sys_role #5 的修改歷史');
    await expect(page.getByTestId('sys-audit-row-1')).toBeVisible();
    await expect(page.getByTestId('sys-audit-row-3')).toHaveCount(0);
    expect(calls.at(-1)?.get('tableName')).toBe('sys_role');
    expect(calls.at(-1)?.get('recordId')).toBe('5');

    // 重設清除網址參數與歷史提示，回到全部記錄
    await page.getByTestId('sys-audit-reset').click();
    await expect(page).toHaveURL(/\/sys\/audit-logs$/);
    await expect(page.getByTestId('sys-audit-history')).toHaveCount(0);
    await expect(page.getByTestId('sys-audit-row-3')).toBeVisible();

    await page.getByTestId('sys-audit-history-3').click();
    await expect(page).toHaveURL(/tableName=sys_user&recordId=7/);
    await expect(page.getByTestId('sys-audit-history')).toContainText('sys_user #7');
    await expect(page.getByTestId('sys-audit-row-3')).toBeVisible();
    await expect(page.getByTestId('sys-audit-row-1')).toHaveCount(0);
  });

  test('英文與簡體介面', async ({ page }) => {
    await mockAuditApi(page);
    await page.goto('/sys/audit-logs');
    await chooseLanguage(page, 'en');
    await expect(page.getByTestId('sys-audit-title')).toHaveText('Audit log');
    await expect(page.getByTestId('sys-audit-row-2')).toContainText('Sign-in failed');
    await chooseLanguage(page, 'zh-Hans');
    await expect(page.getByTestId('sys-audit-title')).toHaveText('审计日志');
    await expect(page.getByTestId('sys-audit-row-2')).toContainText('安全事件');
  });
});

test.describe('稽核查詢（無稽核權限）', () => {
  test.use({
    authorization: { ...E2E_AUTHORIZATION, isFullAccess: false, permissions: [{ code: 'SYS.USER.VIEW', dataScope: 'Company' }] },
  });

  test('選單沒有稽核日誌，直接進入顯示 403', async ({ page }) => {
    await openHome(page);
    await page.getByTestId('layout-menu-sys').click();
    await expect(page.getByTestId('layout-menu-sys-audit-logs')).toHaveCount(0);
    await page.goto('/sys/audit-logs');
    await expect(page.getByTestId('error-page-403')).toBeVisible();
  });
});
