import { readFile } from 'node:fs/promises';
import { mockInventoryOptions, pickInventoryOption } from '../support/inventory-selection';
import { E2E_AUTHORIZATION, chooseLanguage, chooseTheme, expect, test } from '../support/fixtures';

const row = { id: 7, documentNo: 'ASM-0007', businessDate: '2026-10-16', status: 'Draft',
  finishedItemId: 1, finishedItemCode: 'KIT', finishedItemName: 'Kit', finishedQuantity: 2, componentCount: 1, version: 1 };
const detail = { ...row, remark: '=HYPERLINK("x")', postedAt: null, createdAt: '2026-10-16T01:00:00', approval: null,
  lines: [{ id: 1, lineNo: 1, role: 'Finished', itemId: 1, warehouseId: 2, locationId: null, quantity: 2,
    postedUnitCost: null, tracking: [] }, { id: 2, lineNo: 2, role: 'Component', itemId: 3, warehouseId: 2,
    locationId: null, quantity: 4, postedUnitCost: null, tracking: [] }] };

test('INV-ASM-010 filters saved assemblies and exports formula-safe CSV', async ({ page }) => {
  const queries: string[] = [];
  await page.route('**/api/v1/inv/assemblies**', route => {
    const url = new URL(route.request().url());
    if (url.pathname.endsWith('/saved-references')) return route.fulfill({ json: { items: [
      { id: 1, code: 'KIT', name: 'Kit' }, { id: 3, code: 'PART', name: 'Part' }],
      warehouses: [{ id: 2, code: 'WH', name: 'Main' }], locations: [] } });
    if (url.pathname.endsWith('/7')) return route.fulfill({ json: detail });
    queries.push(url.search);
    return route.fulfill({ json: { items: [row], totalCount: 1, page: 1, pageSize: 20 } });
  });
  await page.goto('/inv/assemblies');
  await expect(page.getByTestId('inv-assembly-row-7')).toBeVisible();
  await expect(page.getByTestId('inv-assembly-row-7')).toContainText('KIT · Kit');
  await page.getByTestId('inv-assembly-search').fill('ASM-0007');
  await page.getByTestId('inv-assembly-finished-id').fill('1');
  await page.getByTestId('inv-assembly-filter').click();
  await expect.poll(() => queries.some(x => x.includes('keyword=ASM-0007'))).toBe(true);
  await expect.poll(() => queries.some(x => x.includes('finishedItemId=1'))).toBe(true);
  const downloadPromise = page.waitForEvent('download');
  await page.getByTestId('inv-assembly-export').click();
  const download = await downloadPromise;
  const csv = await readFile((await download.path())!, 'utf8');
  expect(csv).toContain('"ASM-0007"');
  expect(csv).toContain('"\'=HYPERLINK(""x"")"');
  await page.getByTestId('inv-assembly-row-7').getByRole('button', { name: 'ASM-0007' }).click();
  await expect(page.getByTestId('inv-assembly-detail')).toContainText('KIT · Kit');
  await expect(page.getByTestId('inv-assembly-detail')).toContainText('PART · Part');
});

test('INV-ASM-010 exports every saved page and prints saved detail', async ({ page }) => {
  const listPages: number[] = [];
  const detailIds: number[] = [];
  await page.route('**/api/v1/inv/assemblies**', route => {
    const url = new URL(route.request().url());
    if (url.pathname.endsWith('/saved-references')) return route.fulfill({ json: {
      items: [{ id: 1, code: 'KIT', name: 'Kit' }, { id: 3, code: 'PART', name: 'Part' }],
      warehouses: [{ id: 2, code: 'WH', name: 'Main' }], locations: [] } });
    const id = Number(url.pathname.split('/').at(-1));
    if (Number.isSafeInteger(id)) { detailIds.push(id); return route.fulfill({ json: {
      ...detail, id, documentNo: `ASM-${String(id).padStart(4, '0')}`,
      lines: detail.lines.map(line => ({ ...line, id: line.id + id * 10 })) } }); }
    const pageNo = Number(url.searchParams.get('page') ?? 1);
    listPages.push(pageNo);
    const pageSize = Number(url.searchParams.get('pageSize') ?? 20);
    const first = (pageNo - 1) * pageSize + 1;
    const items = Array.from({ length: Math.max(0, Math.min(pageSize, 201 - first + 1)) }, (_, offset) => ({
      ...row, id: first + offset, documentNo: `ASM-${String(first + offset).padStart(4, '0')}` }));
    return route.fulfill({ json: { items, totalCount: 201, page: pageNo, pageSize } });
  });
  await page.goto('/inv/assemblies');
  await expect(page.getByTestId('inv-assembly-row-1')).toBeVisible();
  const downloadPromise = page.waitForEvent('download');
  await page.getByTestId('inv-assembly-export').click();
  const csv = await readFile((await (await downloadPromise).path())!, 'utf8');
  expect(listPages).toContain(2);
  expect(detailIds).toHaveLength(201);
  expect(csv).toContain('"ASM-0201"');
  expect(csv).toContain('"\'=HYPERLINK(""x"")"');
  expect(csv.match(/"ASM-\d{4}"/g)).toHaveLength(402);

  await page.evaluate(() => { (window as Window & { print: () => void }).print = () => {
    document.documentElement.dataset['printedAssembly'] = document.querySelector('[data-testid="inv-assembly-detail"]')?.textContent ?? '';
  }; });
  await page.getByTestId('inv-assembly-row-1').getByRole('button', { name: '列印' }).click();
  await expect.poll(() => page.locator('html').getAttribute('data-printed-assembly')).toContain('KIT · Kit');
  await expect.poll(() => page.locator('html').getAttribute('data-printed-assembly')).toContain('PART · Part');
});

test.describe('INV-ASM-009 read only', () => {
  test.use({ authorization: { ...E2E_AUTHORIZATION, isFullAccess: false,
    permissions: [{ code: 'INV.ASSEMBLY.VIEW', dataScope: 'Company' }] } });
  test('shows list but guards create and hides mutations', async ({ page }) => {
    let optionRequests = 0;
    await page.route('**/api/v1/inv/selection-options/**', route => { optionRequests++; return route.fulfill({ status: 403,
      json: { errorCode: 'COMMON.PERMISSION.DENIED' } }); });
    await page.route('**/api/v1/inv/assemblies**', route => route.fulfill({ json: { items: [row], totalCount: 1 } }));
    await page.goto('/inv/assemblies');
    await expect(page.getByTestId('inv-assembly-row-7')).toBeVisible();
    await expect(page.getByTestId('inv-assembly-new')).toHaveCount(0);
    await expect(page.getByTestId('inv-assembly-row-7').getByRole('button', { name: 'Submit' })).toHaveCount(0);
    await page.goto('/inv/assemblies/new');
    await expect(page.getByTestId('error-page-403')).toBeVisible();
    expect(optionRequests).toBe(0);
  });
});

test('INV-ASM-011 has translated list and dark theme', async ({ page }) => {
  await page.route('**/api/v1/inv/assemblies**', route => route.fulfill({ json: { items: [], totalCount: 0 } }));
  await page.goto('/inv/assemblies');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('組裝入庫');
  await chooseLanguage(page, 'en');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Product assembly');
  await chooseLanguage(page, 'zh-Hans');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('组装入库');
  await chooseTheme(page, 'dark');
  await expect(page.getByTestId('inv-assembly-table')).toBeVisible();
});

test('INV-ASM-011 creates and submits one finished item with a component', async ({ page }) => {
  await mockInventoryOptions(page, { items: [{ id: 1, code: 'KIT', name: 'Kit', trackingMode: 'None' },
    { id: 3, code: 'PART', name: 'Part', trackingMode: 'None' }],
    warehouses: [{ id: 2, code: 'WH', name: 'Main' }], locations: [] });
  let saved: Record<string, unknown> | null = null; let submitted: Record<string, unknown> | null = null;
  await page.route('**/api/v1/inv/assemblies**', route => {
    const path = new URL(route.request().url()).pathname;
    if (route.request().method() === 'POST' && path.endsWith('/submit')) {
      submitted = route.request().postDataJSON() as Record<string, unknown>;
      return route.fulfill({ json: { ...detail, status: 'Posted', version: 2 } });
    }
    if (route.request().method() === 'POST') { saved = route.request().postDataJSON() as Record<string, unknown>;
      return route.fulfill({ status: 201, json: detail }); }
    return route.fulfill({ json: { items: saved ? [row] : [], totalCount: saved ? 1 : 0 } });
  });
  await page.goto('/inv/assemblies');
  await page.getByTestId('inv-assembly-new').click();
  await page.getByTestId('inv-assembly-date').fill('2026-10-16');
  await pickInventoryOption(page, page.locator('.assembly-form'), '物料', 1);
  await pickInventoryOption(page, page.locator('.assembly-form'), '倉庫', 2);
  await page.locator('.assembly-form').getByRole('textbox', { name: '數量' }).first().fill('2');
  await page.getByTestId('inv-assembly-add').click();
  const component = page.getByTestId('inv-assembly-component-0');
  await pickInventoryOption(page, component, '物料', 3);
  await pickInventoryOption(page, component, '倉庫', 2);
  await component.getByRole('textbox', { name: '數量' }).fill('4');
  await page.getByTestId('inv-assembly-save').click();
  await expect.poll(() => saved).toMatchObject({ businessDate: '2026-10-16', finished: { lineNo: 1, itemId: 1, quantity: '2' },
    components: [{ lineNo: 2, itemId: 3, quantity: '4' }] });
  await expect(page.getByTestId('inv-assembly-row-7')).toBeVisible();
  await page.getByTestId('inv-assembly-row-7').getByRole('button', { name: '送審' }).click();
  await expect.poll(() => submitted).toEqual({ version: 1 });
});

test('INV-ASM-011 cancelling a choice keeps the saved warehouse and changing it clears the location', async ({ page }) => {
  await mockInventoryOptions(page, { items: [{ id: 1, code: 'KIT', name: 'Kit', trackingMode: 'None' },
    { id: 3, code: 'PART', name: 'Part', trackingMode: 'None' }],
    warehouses: [{ id: 2, code: 'WH', name: 'Main' }, { id: 5, code: 'WH2', name: 'Second' }],
    locations: [{ id: 4, code: 'LOC', name: 'Shelf' }] });
  let saved: Record<string, unknown> | null = null;
  await page.route('**/api/v1/inv/assemblies**', route => {
    const path = new URL(route.request().url()).pathname;
    if (path.endsWith('/saved-references')) return route.fulfill({ json: { items: [
      { id: 1, code: 'KIT', name: 'Kit', trackingMode: 'None' }, { id: 3, code: 'PART', name: 'Part', trackingMode: 'None' }],
      warehouses: [{ id: 2, code: 'WH', name: 'Main' }], locations: [{ id: 4, code: 'LOC', name: 'Shelf' }] } });
    if (route.request().method() === 'PUT') { saved = route.request().postDataJSON() as Record<string, unknown>;
      return route.fulfill({ json: { ...detail, version: 2 } }); }
    if (path.endsWith('/7')) return route.fulfill({ json: { ...detail,
      lines: detail.lines.map(line => line.role === 'Finished' ? { ...line, locationId: 4 } : line) } });
    return route.fulfill({ json: { items: [row], totalCount: 1 } });
  });
  await page.goto('/inv/assemblies/7/edit');
  const form = page.locator('.assembly-form');
  await expect(form.getByRole('button', { name: '倉庫' }).first()).toContainText('WH · Main');
  await expect(form.getByRole('button', { name: '儲位' }).first()).toContainText('LOC · Shelf');
  await form.getByRole('button', { name: '倉庫' }).first().click();
  await page.getByTestId('erp-select-row-5').click();
  await page.getByTestId('erp-select-cancel').click();
  await expect(form.getByRole('button', { name: '倉庫' }).first()).toContainText('WH · Main');
  await expect(form.getByRole('button', { name: '儲位' }).first()).toContainText('LOC · Shelf');
  await form.getByRole('button', { name: '倉庫' }).first().click();
  await expect(page.getByTestId('erp-select-dialog')).toBeVisible();
  await page.getByTestId('erp-select-row-5').click();
  await page.getByTestId('erp-select-confirm').click();
  await expect(form.getByRole('button', { name: '倉庫' }).first()).toContainText('WH2 · Second');
  await expect(form.getByRole('button', { name: '儲位' }).first()).toContainText('—');
  await page.getByTestId('inv-assembly-save').click();
  await expect.poll(() => saved).toMatchObject({ finished: { warehouseId: 5, locationId: null } });
  await expect(page.getByTestId('inv-assembly-row-7')).toBeVisible();
});

test('INV-ASM-012 keeps the draft when cost is unknown on submit', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 422/);
  let submits = 0;
  await page.route('**/api/v1/inv/assemblies**', route => {
    if (new URL(route.request().url()).pathname.endsWith('/submit')) { submits++;
      return route.fulfill({ status: 422, json: { errorCode: 'INV.ASSEMBLY.COST_UNKNOWN' } }); }
    return route.fulfill({ json: { items: [row], totalCount: 1 } });
  });
  await page.goto('/inv/assemblies');
  await page.getByTestId('inv-assembly-row-7').getByRole('button', { name: '送審' }).click();
  await expect(page.getByTestId('inv-assembly-error')).toContainText('耗料成本尚未可靠定價');
  await expect(page.getByTestId('inv-assembly-row-7')).toContainText('草稿');
  expect(submits).toBe(1);
});

test('INV-ASM-012 shows a server trace ID without claiming success', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 500/);
  pageErrors.allowApi5xx(/\/api\/v1\/inv\/assemblies\/7\/submit$/, 500);
  await page.route('**/api/v1/inv/assemblies**', route => {
    if (new URL(route.request().url()).pathname.endsWith('/submit')) return route.fulfill({ status: 500,
      json: { errorCode: 'COMMON.SERVER.ERROR', traceId: '00-qa-assembly-01' } });
    return route.fulfill({ json: { items: [row], totalCount: 1 } });
  });
  await page.goto('/inv/assemblies');
  await page.getByTestId('inv-assembly-row-7').getByRole('button', { name: '送審' }).click();
  await expect(page.getByTestId('inv-assembly-error')).toContainText('00-qa-assembly-01');
  await expect(page.getByTestId('inv-assembly-row-7')).toContainText('草稿');
});

test('INV-ASM-012 shows the rejected field and reason from a 400 response', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 400/);
  await page.route('**/api/v1/inv/assemblies**', route => {
    if (new URL(route.request().url()).pathname.endsWith('/submit')) return route.fulfill({ status: 400,
      json: { errorCode: 'COMMON.REQUEST.VALIDATION_FAILED', errors: {
        'components[0].quantity': ['Quantity must be positive.'],
      } } });
    return route.fulfill({ json: { items: [row], totalCount: 1 } });
  });
  await page.goto('/inv/assemblies');
  await page.getByTestId('inv-assembly-row-7').getByRole('button', { name: '送審' }).click();
  await expect(page.getByTestId('inv-assembly-error')).toContainText('components[0].quantity: Quantity must be positive.');
  await expect(page.getByTestId('inv-assembly-row-7')).toContainText('草稿');
});

test('INV-ASM-012 shows a 422 line reason and leaves the draft unchanged', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 422/);
  let submits = 0;
  await page.route('**/api/v1/inv/assemblies**', route => {
    if (new URL(route.request().url()).pathname.endsWith('/submit')) { submits++;
      return route.fulfill({ status: 422, json: { errorCode: 'INV.ASSEMBLY.TRACKING_INVALID', errors: {
        'components[1].tracking[0].serialNo': ['序號不在來源倉庫。'],
      } } }); }
    return route.fulfill({ json: { items: [row], totalCount: 1 } });
  });
  await page.goto('/inv/assemblies');
  await page.getByTestId('inv-assembly-row-7').getByRole('button', { name: '送審' }).click();
  await expect(page.getByTestId('inv-assembly-error')).toContainText('components[1].tracking[0].serialNo: 序號不在來源倉庫。');
  await expect(page.getByTestId('inv-assembly-row-7')).toContainText('草稿');
  expect(submits).toBe(1);
});

test('INV-ASM-012 explains a disconnected submit and preserves the draft row', async ({ page, pageErrors }) => {
  pageErrors.allow(/net::ERR_/);
  let submits = 0;
  await page.route('**/api/v1/inv/assemblies**', route => {
    if (new URL(route.request().url()).pathname.endsWith('/submit')) { submits++;
      return route.abort('internetdisconnected'); }
    return route.fulfill({ json: { items: [row], totalCount: 1 } });
  });
  await page.goto('/inv/assemblies');
  await page.getByTestId('inv-assembly-row-7').getByRole('button', { name: '送審' }).click();
  await expect(page.getByTestId('inv-assembly-error')).toContainText('網路');
  await expect(page.getByTestId('inv-assembly-row-7')).toContainText('草稿');
  expect(submits).toBe(1);
});

test('INV-ASM-012 reports a timed out submit without changing the draft', async ({ page, pageErrors }) => {
  pageErrors.allow(/net::ERR_TIMED_OUT/);
  let submits = 0;
  await page.route('**/api/v1/inv/assemblies**', route => {
    if (new URL(route.request().url()).pathname.endsWith('/submit')) { submits++;
      return route.abort('timedout'); }
    return route.fulfill({ json: { items: [row], totalCount: 1 } });
  });
  await page.goto('/inv/assemblies');
  await page.getByTestId('inv-assembly-row-7').getByRole('button', { name: '送審' }).click();
  await expect(page.getByTestId('inv-assembly-error')).toContainText('網路');
  await expect(page.getByTestId('inv-assembly-row-7')).toContainText('草稿');
  expect(submits).toBe(1);
});
