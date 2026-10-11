import { Page } from '@playwright/test';
import { expect, openHome, test } from '../support/fixtures';

interface Rule { id: number; scope: string; targetId: number | null; ipRange: string;
  validFrom: string | null; validTo: string | null; description: string | null; isActive: boolean; version: number; }
const systemRule: Rule = { id: 1, scope: 'System', targetId: null, ipRange: '10.0.0.0/8',
  validFrom: null, validTo: null, description: null, isActive: true, version: 1 };

async function mockApi(page: Page, rows: Rule[]): Promise<string[]> {
  const calls: string[] = [];
  await page.route('**/api/v1/sys/ip-whitelist**', async route => {
    const req = route.request(); const url = new URL(req.url());
    calls.push(req.method());
    if (req.method() === 'GET') {
      const keyword = url.searchParams.get('keyword') ?? '';
      const items = rows.filter(r => r.ipRange.includes(keyword) || r.description?.includes(keyword));
      await route.fulfill({ json: { items, totalCount: items.length, page: 1, pageSize: 20 } });
    } else if (req.method() === 'POST') {
      const row = { ...req.postDataJSON(), id: rows.length + 1, version: 1, isActive: true } as Rule;
      rows.push(row); await route.fulfill({ status: 201, json: row });
    } else {
      const row = rows.find(r => r.id === Number(url.pathname.split('/').at(-1)))!;
      Object.assign(row, req.postDataJSON(), { version: row.version + 1 });
      await route.fulfill({ json: row });
    }
  });
  return calls;
}

test('IP 白名單：新增、取消停用與確認停用', async ({ page }) => {
  const rows = [{ ...systemRule }]; const calls = await mockApi(page, rows);
  await openHome(page);
  await page.getByTestId('layout-menu-sys').click();
  await page.getByTestId('layout-menu-sys-ip-whitelist').click();
  await expect(page.getByTestId('ip-row-1')).toContainText('10.0.0.0/8');
  await page.getByTestId('ip-create').click();
  await page.getByTestId('ip-range').fill('192.168.1.1');
  await page.locator('.ant-modal-footer .ant-btn-primary').click();
  await expect(page.getByTestId('ip-row-2')).toContainText('192.168.1.1');
  await page.getByTestId('ip-disable-2').click();
  await page.getByTestId('erp-message-cancel').click();
  expect(calls.filter(c => c === 'PUT')).toHaveLength(0);
  await page.getByTestId('ip-disable-2').click();
  await page.getByTestId('erp-message-ok').click();
  await expect.poll(() => calls.filter(c => c === 'PUT').length).toBe(1);
  await expect(page.getByTestId('ip-disable-2')).toHaveCount(0);
});
