import { mkdir, readFile } from 'node:fs/promises';
import type { Page } from '@playwright/test';
import { mockInventoryOptions, pickInventoryOption } from '../support/inventory-selection';
import { E2E_AUTHORIZATION, chooseLanguage, chooseTheme, expect, test } from '../support/fixtures';

const row = { id: 7, documentNo: 'DIS-0007', businessDate: '2026-10-18', status: 'Draft',
  finishedItemId: 1, finishedItemCode: 'KIT', finishedItemName: 'Kit', finishedQuantity: 2, componentCount: 1, version: 1 };
const detail = { ...row, remark: '=HYPERLINK("x")', postedAt: null, createdAt: '2026-10-18T01:00:00', approval: null,
  lines: [{ id: 1, lineNo: 1, role: 'Finished', itemId: 1, warehouseId: 2, locationId: null,
    quantity: 2, allocationWeight: null, postedUnitCost: null, tracking: [] },
  { id: 2, lineNo: 2, role: 'Component', itemId: 3, warehouseId: 2, locationId: null,
    quantity: 4, allocationWeight: 1.25, postedUnitCost: null, tracking: [] }] };

async function capture(page: Page, name: string): Promise<void> {
  const directory = '../../tests/qa/INV/screenshots';
  await mkdir(directory, { recursive: true });
  await expect(page.getByTestId('erp-select-dialog')).toHaveCount(0);
  await expect(page.locator('.ant-spin-spinning')).toHaveCount(0);
  await expect(page.locator('.ant-spin-blur')).toHaveCount(0);
  await page.waitForFunction(() => [...document.querySelectorAll('.ant-spin-container')]
    .every(element => getComputedStyle(element).opacity === '1'));
  await page.screenshot({ path: `${directory}/${name}.png`, fullPage: true });
}

test('INV-DIS-010/014 shows saved detail and exports formula-safe CSV', async ({ page }) => {
  const queries: string[] = [];
  await page.route('**/api/v1/inv/disassemblies**', route => {
    const url = new URL(route.request().url());
    if (url.pathname.endsWith('/saved-references')) return route.fulfill({ json: { items: [
      { id: 1, code: 'KIT', name: 'Kit' }, { id: 3, code: 'PART', name: 'Part' }],
      warehouses: [{ id: 2, code: 'WH', name: 'Main' }], locations: [] } });
    if (url.pathname.endsWith('/7')) return route.fulfill({ json: detail });
    queries.push(url.search);
    return route.fulfill({ json: { items: [row], totalCount: 1, page: 1, pageSize: 20 } });
  });
  await page.goto('/inv/disassemblies');
  await expect(page.getByTestId('inv-disassembly-row-7')).toContainText('KIT · Kit');
  await capture(page, '5.12-normal-light-zh-Hant');
  await chooseTheme(page, 'dark');
  await capture(page, '5.12-normal-dark-zh-Hant');
  await chooseTheme(page, 'light');
  await page.getByTestId('inv-disassembly-search').fill('DIS-0007');
  await page.getByTestId('inv-disassembly-filter').click();
  await expect.poll(() => queries.some(q => q.includes('keyword=DIS-0007'))).toBe(true);
  const downloading = page.waitForEvent('download');
  await page.getByTestId('inv-disassembly-export').click();
  const download = await downloading;
  expect(download.suggestedFilename()).toBe('inventory-disassemblies.csv');
  const csv = await readFile((await download.path())!, 'utf8');
  expect(csv).toContain('"單號","業務日期","狀態"');
  expect(csv).toContain('"DIS-0007"');
  expect(csv).toContain('"1.25"');
  expect(csv).toContain('"\'=HYPERLINK(""x"")"');
  await page.getByTestId('inv-disassembly-row-7').getByRole('button', { name: 'DIS-0007' }).click();
  await expect(page.getByTestId('inv-disassembly-detail')).toContainText('PART · Part');
  await expect(page.getByTestId('inv-disassembly-detail')).toContainText('1.25');
  await capture(page, '5.12-detail-light-zh-Hant');
});

test('INV-DIS-011/013 saves weighted outputs, then submits the persisted version', async ({ page }) => {
  await mockInventoryOptions(page, { items: [{ id: 1, code: 'KIT', name: 'Kit', trackingMode: 'None' },
    { id: 3, code: 'PART', name: 'Part', trackingMode: 'None' }],
    warehouses: [{ id: 2, code: 'WH', name: 'Main' }], locations: [] });
  let saved: Record<string, unknown> | null = null;
  let submitted: Record<string, unknown> | null = null;
  let posted = false;
  await page.route('**/api/v1/inv/disassemblies**', route => {
    const path = new URL(route.request().url()).pathname;
    if (route.request().method() === 'POST' && path.endsWith('/submit')) {
      submitted = route.request().postDataJSON() as Record<string, unknown>;
      posted = true;
      return route.fulfill({ json: { ...detail, status: 'Posted', version: 2 } });
    }
    if (route.request().method() === 'POST') {
      saved = route.request().postDataJSON() as Record<string, unknown>;
      return route.fulfill({ status: 201, json: detail });
    }
    return route.fulfill({ json: { items: saved ? [{ ...row, status: posted ? 'Posted' : 'Draft', version: posted ? 2 : 1 }] : [], totalCount: saved ? 1 : 0 } });
  });
  await page.goto('/inv/disassemblies');
  await page.getByTestId('inv-disassembly-new').click();
  await page.getByTestId('inv-disassembly-date').fill('2026-10-18');
  await pickInventoryOption(page, page.locator('.disassembly-form'), '物料', 1);
  await pickInventoryOption(page, page.locator('.disassembly-form'), '倉庫', 2);
  await page.locator('.disassembly-form').getByRole('textbox', { name: '數量' }).first().fill('2');
  await page.getByTestId('inv-disassembly-add').click();
  const component = page.getByTestId('inv-disassembly-component-0');
  await pickInventoryOption(page, component, '物料', 3);
  await pickInventoryOption(page, component, '倉庫', 2);
  await component.getByRole('textbox', { name: '數量' }).fill('4');
  await component.getByRole('textbox', { name: '分攤權重' }).fill('1.25');
  await capture(page, '5.13-normal-light-zh-Hant');
  await chooseTheme(page, 'dark');
  await capture(page, '5.13-normal-dark-zh-Hant');
  await chooseTheme(page, 'light');
  await page.getByTestId('inv-disassembly-save').click();
  await expect.poll(() => saved).toMatchObject({ businessDate: '2026-10-18',
    finished: { lineNo: 1, itemId: 1, quantity: '2', allocationWeight: null },
    components: [{ lineNo: 2, itemId: 3, quantity: '4', allocationWeight: '1.25' }] });
  await expect(page.getByTestId('inv-disassembly-row-7')).toBeVisible();
  await page.getByTestId('inv-disassembly-row-7').getByRole('button', { name: '送審' }).click();
  await expect.poll(() => submitted).toEqual({ version: 1 });
  await expect(page.getByTestId('inv-disassembly-row-7')).toContainText('已過帳');
  await capture(page, '5.12-posted-light-zh-Hant');
  await chooseTheme(page, 'dark');
  await capture(page, '5.12-posted-dark-zh-Hant');
});

test('INV-DIS-012/015 exposes server reason and approval history in three languages', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 422/);
  await page.route('**/api/v1/inv/disassemblies**', route => {
    const path = new URL(route.request().url()).pathname;
    if (path.endsWith('/submit')) return route.fulfill({ status: 422,
      json: { errorCode: 'INV.DISASSEMBLY.COST_UNKNOWN' } });
    if (path.endsWith('/saved-references')) return route.fulfill({ json: { items: [], warehouses: [], locations: [] } });
    if (path.endsWith('/7')) return route.fulfill({ json: { ...detail, approvalHistory: [{ instanceId: 42,
      status: 'Rejected', submittedAt: '2026-10-18T02:00:00', steps: [{ stepNo: 1, name: 'Manager',
        status: 'Rejected', actedBy: 8, actedAt: '2026-10-18T03:00:00', comment: '序號不符' }] }] } });
    return route.fulfill({ json: { items: [row], totalCount: 1 } });
  });
  await page.goto('/inv/disassemblies');
  await page.getByTestId('inv-disassembly-row-7').getByRole('button', { name: '送審' }).click();
  await expect(page.getByTestId('inv-disassembly-error')).toContainText('成品成本尚未可靠定價');
  await capture(page, '5.12-error-light-zh-Hant');
  await chooseTheme(page, 'dark');
  await capture(page, '5.12-error-dark-zh-Hant');
  await expect(page.getByTestId('inv-disassembly-row-7')).toContainText('草稿');
  await page.getByTestId('inv-disassembly-row-7').getByRole('button', { name: 'DIS-0007' }).click();
  await expect(page.getByTestId('inv-disassembly-approval-history')).toContainText('序號不符');
  await capture(page, '5.12-detail-dark-zh-Hant');
  await chooseLanguage(page, 'en');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Product disassembly');
  await capture(page, '5.12-detail-dark-en');
  await chooseLanguage(page, 'zh-Hans');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('拆卸入库');
  await capture(page, '5.12-detail-dark-zh-Hans');
});

test('INV-DIS-012 keeps failed submission reason visible after refreshing persisted draft', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 409/);
  let submissions = 0;
  await page.route('**/api/v1/inv/disassemblies**', route => {
    const path = new URL(route.request().url()).pathname;
    if (path.endsWith('/submit')) {
      submissions++;
      return route.fulfill({ status: 409, json: { errorCode: 'COMMON.CONCURRENCY.CONFLICT' } });
    }
    return route.fulfill({ json: { items: [row], totalCount: 1, page: 1, pageSize: 20 } });
  });
  await page.goto('/inv/disassemblies');
  await page.getByTestId('inv-disassembly-row-7').getByRole('button', { name: '送審' }).click();
  await expect(page.getByTestId('inv-disassembly-error')).toBeVisible();
  await expect(page.getByTestId('inv-disassembly-row-7')).toContainText('草稿');
  expect(submissions).toBe(1);
});

test('INV-DIS-012 labels rejected component weight and preserves the draft', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 400/);
  await page.route('**/api/v1/inv/disassemblies**', route => {
    if (new URL(route.request().url()).pathname.endsWith('/submit')) return route.fulfill({ status: 400,
      json: { errorCode: 'COMMON.REQUEST.VALIDATION_FAILED', errors: {
        'components[0].allocationWeight': ['Component allocation weight must be positive and have at most 6 decimals.'],
      } } });
    return route.fulfill({ json: { items: [row], totalCount: 1 } });
  });
  await page.goto('/inv/disassemblies');
  await page.getByTestId('inv-disassembly-row-7').getByRole('button', { name: '送審' }).click();
  await expect(page.getByTestId('inv-disassembly-error')).toContainText('產出組件第 1 行 · 分攤權重：分攤權重須為正數');
  await expect(page.getByTestId('inv-disassembly-row-7')).toContainText('草稿');
  await capture(page, '5.12-validation-light-zh-Hant');
  await chooseTheme(page, 'dark');
  await capture(page, '5.12-validation-dark-zh-Hant');
});

test('INV-DIS-012 shows 500 trace ID and keeps the persisted draft', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 500/);
  pageErrors.allowApi5xx(/\/api\/v1\/inv\/disassemblies\/7\/submit$/, 500);
  await page.route('**/api/v1/inv/disassemblies**', route => {
    if (new URL(route.request().url()).pathname.endsWith('/submit')) return route.fulfill({ status: 500,
      json: { errorCode: 'COMMON.SERVER.ERROR', traceId: '00-qa-disassembly-01' } });
    return route.fulfill({ json: { items: [row], totalCount: 1 } });
  });
  await page.goto('/inv/disassemblies');
  await page.getByTestId('inv-disassembly-row-7').getByRole('button', { name: '送審' }).click();
  await expect(page.getByTestId('inv-disassembly-error')).toContainText('00-qa-disassembly-01');
  await expect(page.getByTestId('inv-disassembly-row-7')).toContainText('草稿');
  await capture(page, '5.12-server-error-light-zh-Hant');
  await chooseTheme(page, 'dark');
  await capture(page, '5.12-server-error-dark-zh-Hant');
});

test('INV-DIS-012 reports disconnected submit and keeps the draft', async ({ page, pageErrors }) => {
  pageErrors.allow(/net::ERR_/);
  await page.route('**/api/v1/inv/disassemblies**', route => {
    if (new URL(route.request().url()).pathname.endsWith('/submit')) return route.abort('internetdisconnected');
    return route.fulfill({ json: { items: [row], totalCount: 1 } });
  });
  await page.goto('/inv/disassemblies');
  await page.getByTestId('inv-disassembly-row-7').getByRole('button', { name: '送審' }).click();
  await expect(page.getByTestId('inv-disassembly-error')).toContainText('網路');
  await expect(page.getByTestId('inv-disassembly-row-7')).toContainText('草稿');
  await capture(page, '5.12-network-error-light-zh-Hant');
  await chooseTheme(page, 'dark');
  await capture(page, '5.12-network-error-dark-zh-Hant');
});

test('INV-DIS-010 distinguishes empty list from no results', async ({ page }) => {
  await page.route('**/api/v1/inv/disassemblies**', route => route.fulfill({ json: { items: [], totalCount: 0 } }));
  await page.goto('/inv/disassemblies');
  await expect(page.getByTestId('inv-disassembly-table')).toContainText('沒有拆卸單');
  const emptyColor = await page.locator('.ant-table-placeholder .ant-table-cell').evaluate(node => getComputedStyle(node).color);
  const readableColor = await page.evaluate(() => getComputedStyle(document.documentElement).getPropertyValue('--erp-color-text-secondary').trim());
  expect(emptyColor).toBe('rgb(89, 89, 89)');
  expect(readableColor).toBe('#595959');
  await capture(page, '5.12-empty-light-zh-Hant');
  await chooseTheme(page, 'dark');
  await capture(page, '5.12-empty-dark-zh-Hant');
  await page.getByTestId('inv-disassembly-search').fill('missing');
  await page.getByTestId('inv-disassembly-filter').click();
  await expect(page.getByTestId('inv-disassembly-table')).toContainText('沒有符合篩選的拆卸單');
  await capture(page, '5.12-no-results-dark-zh-Hant');
  await chooseTheme(page, 'light');
  await capture(page, '5.12-no-results-light-zh-Hant');
});

test('INV-DIS-011 edits persisted references and allocation', async ({ page }) => {
  await page.route('**/api/v1/inv/disassemblies**', route => {
    const path = new URL(route.request().url()).pathname;
    if (path.endsWith('/saved-references')) return route.fulfill({ json: { items: [
      { id: 1, code: 'KIT', name: 'Kit', trackingMode: 'None' },
      { id: 3, code: 'PART', name: 'Part', trackingMode: 'None', isActive: false }],
      warehouses: [{ id: 2, code: 'WH', name: 'Main' }], locations: [] } });
    if (path.endsWith('/7')) return route.fulfill({ json: detail });
    return route.fulfill({ json: { items: [row], totalCount: 1 } });
  });
  await page.goto('/inv/disassemblies/7/edit');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('編輯拆卸單');
  await expect(page.getByTestId('inv-disassembly-component-0').getByRole('textbox', { name: '分攤權重' })).toHaveValue('1.25');
  await expect(page.getByTestId('inv-disassembly-component-0').getByRole('button', { name: '物料' })).toContainText('已停用');
  await capture(page, '5.14-normal-light-zh-Hant');
  await chooseTheme(page, 'dark');
  await capture(page, '5.14-normal-dark-zh-Hant');
});

test('INV-DIS-011 cancelling selection keeps source and changing warehouse clears location', async ({ page }) => {
  await mockInventoryOptions(page, { items: [{ id: 1, code: 'KIT', name: 'Kit', trackingMode: 'None' }],
    warehouses: [{ id: 2, code: 'WH1', name: 'Main' }, { id: 4, code: 'WH2', name: 'Secondary' }],
    locations: [{ id: 5, code: 'LOC', name: 'Rack' }] });
  await page.goto('/inv/disassemblies/new');
  const form = page.locator('.disassembly-form');
  await form.getByRole('button', { name: '物料' }).click();
  await page.getByTestId('erp-select-cancel').click();
  await expect(form.getByRole('button', { name: '物料' })).toHaveText('—');
  await pickInventoryOption(page, form, '物料', 1);
  await pickInventoryOption(page, form, '倉庫', 2);
  await pickInventoryOption(page, form, '儲位', 5);
  await expect(form.getByRole('button', { name: '儲位' })).toContainText('LOC');
  await pickInventoryOption(page, form, '倉庫', 4);
  await expect(form.getByRole('button', { name: '儲位' })).toHaveText('—');
  await expect(form.getByRole('button', { name: '倉庫' })).toContainText('WH2');
});

test('INV-DIS-011 warns before leaving an unsaved draft', async ({ page }) => {
  let creates = 0;
  await page.route('**/api/v1/inv/disassemblies**', route => {
    if (route.request().method() === 'POST') creates++;
    return route.fulfill({ json: { items: [], totalCount: 0 } });
  });
  await page.goto('/inv/disassemblies/new');
  await page.getByTestId('inv-disassembly-date').fill('2026-10-18');
  await page.getByRole('link', { name: '返回拆卸列表' }).click();
  await expect(page.getByRole('dialog')).toContainText('未儲存的修改將會遺失');
  await page.getByRole('dialog').getByRole('button', { name: '繼續編輯' }).click();
  await expect(page).toHaveURL(/\/inv\/disassemblies\/new$/);
  await expect(page.getByTestId('inv-disassembly-date')).toHaveValue('2026-10-18');
  await page.getByRole('link', { name: '返回拆卸列表' }).click();
  await page.getByRole('dialog').getByRole('button', { name: '離開', exact: true }).click();
  await expect(page).toHaveURL(/\/inv\/disassemblies$/);
  expect(creates).toBe(0);
});

test('INV-DIS-011 shows create validation without creating data', async ({ page }) => {
  let creates = 0;
  await page.route('**/api/v1/inv/disassemblies**', route => {
    if (route.request().method() === 'POST') creates++;
    return route.fulfill({ json: { items: [], totalCount: 0 } });
  });
  await page.goto('/inv/disassemblies/new');
  await page.getByTestId('inv-disassembly-save').click();
  await expect(page.getByTestId('inv-disassembly-error')).toContainText('請填日期、成品');
  await capture(page, '5.13-validation-light-zh-Hant');
  await chooseTheme(page, 'dark');
  await capture(page, '5.13-validation-dark-zh-Hant');
  expect(creates).toBe(0);
});

test('INV-DIS-012 keeps edit values after a 409 conflict', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 409/);
  await page.route('**/api/v1/inv/disassemblies**', route => {
    const path = new URL(route.request().url()).pathname;
    if (route.request().method() === 'PUT') return route.fulfill({ status: 409,
      json: { errorCode: 'COMMON.CONCURRENCY.CONFLICT' } });
    if (path.endsWith('/saved-references')) return route.fulfill({ json: { items: [
      { id: 1, code: 'KIT', name: 'Kit', trackingMode: 'None' },
      { id: 3, code: 'PART', name: 'Part', trackingMode: 'None' }],
      warehouses: [{ id: 2, code: 'WH', name: 'Main' }], locations: [] } });
    return route.fulfill({ json: detail });
  });
  await page.goto('/inv/disassemblies/7/edit');
  await page.getByTestId('inv-disassembly-remark').fill('keep this correction');
  await page.getByTestId('inv-disassembly-save').click();
  await expect(page.getByTestId('inv-disassembly-error')).toBeVisible();
  await expect(page.getByTestId('inv-disassembly-remark')).toHaveValue('keep this correction');
  await capture(page, '5.14-conflict-light-zh-Hant');
  await chooseTheme(page, 'dark');
  await capture(page, '5.14-conflict-dark-zh-Hant');
});

test('INV-DIS-010/014 downloads every filtered page with safe formula prefixes', async ({ page }) => {
  let detailCalls = 0;
  await page.route('**/api/v1/inv/disassemblies**', route => {
    const url = new URL(route.request().url());
    const match = /\/disassemblies\/(\d+)$/.exec(url.pathname);
    if (match) {
      detailCalls++;
      const id = Number(match[1]);
      const remarks = ['=SUM(1)', '+SUM(1)', '-SUM(1)', '@SUM(1)', '繁中,"quoted"\nline'];
      return route.fulfill({ json: { ...detail, id, documentNo: `DIS-${String(id).padStart(4, '0')}`,
        remark: remarks[id % remarks.length] } });
    }
    const pageNo = Number(url.searchParams.get('page') ?? 1);
    const start = (pageNo - 1) * 200 + 1;
    const items = Array.from({ length: Math.max(0, Math.min(200, 202 - start)) }, (_, i) => ({ ...row,
      id: start + i, documentNo: `DIS-${String(start + i).padStart(4, '0')}` }));
    return route.fulfill({ json: { items, totalCount: 201, page: pageNo, pageSize: 200 } });
  });
  await page.goto('/inv/disassemblies');
  const downloading = page.waitForEvent('download');
  await page.getByTestId('inv-disassembly-export').click();
  const csv = await readFile((await (await downloading).path())!, 'utf8');
  await expect.poll(() => detailCalls).toBe(201);
  expect(csv).toContain('"DIS-0201"');
  for (const prefix of ['=', '+', '-', '@']) expect(csv).toContain(`"'${prefix}SUM(1)"`);
  expect(csv).toContain('繁中,""quoted""\nline');
  expect(csv.split('"DIS-').length - 1).toBe(402);
});

test('INV-DIS-010 prints the persisted detail', async ({ page }) => {
  await page.addInitScript(() => {
    (window as Window & { __printed?: boolean }).__printed = false;
    window.print = () => { (window as Window & { __printed?: boolean }).__printed = true; };
  });
  await page.route('**/api/v1/inv/disassemblies**', route => {
    const path = new URL(route.request().url()).pathname;
    if (path.endsWith('/saved-references')) return route.fulfill({ json: { items: [], warehouses: [], locations: [] } });
    if (path.endsWith('/7')) return route.fulfill({ json: detail });
    return route.fulfill({ json: { items: [row], totalCount: 1 } });
  });
  await page.goto('/inv/disassemblies');
  await page.getByTestId('inv-disassembly-row-7').getByRole('button', { name: '列印' }).click();
  await expect(page.getByTestId('inv-disassembly-detail')).toContainText('DIS-0007');
  await expect.poll(() => page.evaluate(() => (window as Window & { __printed?: boolean }).__printed)).toBe(true);
});

test('INV-DIS-014 rejects a failed detail export without downloading a partial CSV', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 500/);
  pageErrors.allowApi5xx(/\/api\/v1\/inv\/disassemblies\/2$/, 500);
  let downloads = 0;
  page.on('download', () => downloads++);
  await page.route('**/api/v1/inv/disassemblies**', route => {
    const path = new URL(route.request().url()).pathname;
    if (path.endsWith('/1')) return route.fulfill({ json: { ...detail, id: 1 } });
    if (path.endsWith('/2')) return route.fulfill({ status: 500,
      json: { errorCode: 'COMMON.SERVER.ERROR', traceId: '00-qa-disassembly-export' } });
    return route.fulfill({ json: { items: [{ ...row, id: 1 }, { ...row, id: 2 }], totalCount: 2 } });
  });
  await page.goto('/inv/disassemblies');
  await page.getByTestId('inv-disassembly-export').click();
  await expect(page.getByTestId('inv-disassembly-error')).toContainText('00-qa-disassembly-export');
  await expect(page.getByTestId('inv-disassembly-export')).toBeEnabled();
  expect(downloads).toBe(0);
});

test('INV-DIS-012 reports timed out submit and keeps the draft', async ({ page, pageErrors }) => {
  pageErrors.allow(/net::ERR_TIMED_OUT/);
  await page.route('**/api/v1/inv/disassemblies**', route => {
    if (new URL(route.request().url()).pathname.endsWith('/submit')) return route.abort('timedout');
    return route.fulfill({ json: { items: [row], totalCount: 1 } });
  });
  await page.goto('/inv/disassemblies');
  await page.getByTestId('inv-disassembly-row-7').getByRole('button', { name: '送審' }).click();
  await expect(page.getByTestId('inv-disassembly-error')).toContainText('網路');
  await expect(page.getByTestId('inv-disassembly-row-7')).toContainText('草稿');
});

test.describe('INV-DIS-015 view only', () => {
  test.use({ authorization: { ...E2E_AUTHORIZATION, isFullAccess: false,
    permissions: [{ code: 'INV.DISASSEMBLY.VIEW', dataScope: 'Company' }] } });
  test('hides mutation and blocks direct create route', async ({ page }) => {
    await page.route('**/api/v1/inv/disassemblies**', route => route.fulfill({ json: { items: [row], totalCount: 1 } }));
    await page.goto('/inv/disassemblies');
    await expect(page.getByTestId('inv-disassembly-row-7')).toBeVisible();
    await expect(page.getByTestId('inv-disassembly-new')).toHaveCount(0);
    await expect(page.getByTestId('inv-disassembly-row-7').getByRole('button', { name: '送審' })).toHaveCount(0);
    await page.goto('/inv/disassemblies/new');
    await expect(page.getByTestId('error-page-403')).toBeVisible();
    await capture(page, '5.13-permission-light-zh-Hant');
    await chooseTheme(page, 'dark');
    await capture(page, '5.13-permission-dark-zh-Hant');
  });
});
