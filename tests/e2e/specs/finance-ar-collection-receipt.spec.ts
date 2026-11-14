import { mkdir } from 'node:fs/promises';
import type { Page, Route } from '@playwright/test';
import { expectReadableText } from '../support/contrast';
import { chooseLanguage, chooseTheme, expect, test } from '../support/fixtures';

/** Saved receipt as the mock server holds it; `amount` / `unapplied` are exact decimal text written raw into JSON. */
type Receipt = { id: number; documentNo: string; customerId: number; businessDate: string; amount: string; bankAccountId: number;
  bankReference: string; remark: string | null; status: 'Draft' | 'Pending' | 'Rejected' | 'Posted'; version: number;
  unapplied: string | null; journalId: number | null; deleted?: boolean };
type Server = { receipts: Receipt[]; flowSteps: number; mapping: boolean; writes: { method: string; url: string; body: string }[] };

const API = '**/api/v1/ar/sales-collections**';
const PAGE = '/ar/sales-collections';
const CUSTOMERS = [{ id: 7, code: 'C001', name: '大成貿易' }, { id: 8, code: 'C002', name: 'Harbor Foods' }];
const FUNDS = [{ id: 3, code: 'BK01', name: '兆豐台幣', bankName: 'Mega', isActive: true }, { id: 4, code: 'CASH', name: '零用金', bankName: null, isActive: true }];

async function capture(page: Page, name: string): Promise<void> {
  const path = '../../tests/qa/FI/screenshots';
  await mkdir(path, { recursive: true });
  await page.screenshot({ path: `${path}/6.26-${name}.png`, fullPage: true, animations: 'disabled' });
}

/** Layout guard (UX review 6.26): the page never scrolls sideways and no money cell overflows into its neighbour. */
async function expectNoOverflow(page: Page): Promise<void> {
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true);
  const overflowing = await page.locator('erp-collection-receipt-page td.num').evaluateAll(cells =>
    cells.filter(cell => cell.scrollWidth > cell.clientWidth + 1).length);
  expect(overflowing).toBe(0);
}

const receipt = (over: Partial<Receipt> & { id: number }): Receipt => ({ documentNo: `RC2026110${over.id}`, customerId: 7, businessDate: '2026-11-1' + (over.id % 10),
  amount: '1200.5', bankAccountId: 3, bankReference: `TT-00${over.id}`, remark: null, status: 'Draft', version: 1, unapplied: null, journalId: null, ...over });

function json(r: Receipt): string {
  const customer = CUSTOMERS.find(c => c.id === r.customerId)!; const fund = FUNDS.find(f => f.id === r.bankAccountId)!;
  const text = JSON.stringify({ id: r.id, documentNo: r.documentNo, customerId: r.customerId, businessDate: r.businessDate, currency: 'TWD',
    amount: 'AMOUNT', bankAccountId: r.bankAccountId, bankReference: r.bankReference, remark: r.remark, status: r.status, version: r.version,
    unappliedAmount: r.unapplied === null ? null : 'UNAPPLIED', customerCode: customer.code, customerName: customer.name,
    bankAccountCode: fund.code, bankAccountName: fund.name, createdAt: '2026-11-14T09:00:00', createdBy: 1, updatedAt: null, updatedBy: null,
    journalId: r.journalId });
  return text.replace('"AMOUNT"', r.amount).replace('"UNAPPLIED"', r.unapplied ?? 'null');
}

/**
 * In-memory `/api/v1/ar/sales-collections` (ADR-129 contract): list filters status / keyword / dates, default newest date first;
 * create and update check the bank reference is unique (409); only Draft / Rejected may change (422); submit with no approval
 * steps posts at once (unapplied = amount) unless the collection map is missing (422 MAPPING_MISSING, nothing changes).
 */
async function mockServer(page: Page, server: Server) {
  await page.route('**/api/v1/md/customers**', route => route.fulfill({ json: { items: CUSTOMERS, totalCount: CUSTOMERS.length, page: 1, pageSize: 20 } }));
  await page.route('**/api/v1/md/fund-accounts**', route => route.fulfill({ json: { items: FUNDS, totalCount: FUNDS.length, page: 1, pageSize: 20 } }));
  await page.route(API, async (route: Route) => {
    const request = route.request(); const url = new URL(request.url()); const method = request.method();
    const match = /sales-collections(?:\/(\d+))?(?:\/(submit|withdraw))?$/.exec(url.pathname)!;
    const id = match[1] ? Number(match[1]) : null; const action = match[2];
    const live = server.receipts.filter(r => !r.deleted);
    const fail = (status: number, errorCode: string) => route.fulfill({ status, json: { status, errorCode } });
    const text = (status: number, body: string) => route.fulfill({ status, contentType: 'application/json', body });
    if (method !== 'GET') server.writes.push({ method, url: url.pathname, body: request.postData() ?? '' });
    if (method === 'GET' && id === null) {
      const p = url.searchParams; const keyword = (p.get('keyword') ?? '').toLowerCase();
      const rows = live.filter(r => (!p.get('status') || r.status === p.get('status'))
        && (!keyword || r.documentNo.toLowerCase().includes(keyword) || r.bankReference.toLowerCase().includes(keyword))
        && (!p.get('from') || r.businessDate >= p.get('from')!) && (!p.get('to') || r.businessDate <= p.get('to')!))
        .sort((a, b) => b.businessDate.localeCompare(a.businessDate) || b.id - a.id);
      return text(200, `{"items":[${rows.map(json).join(',')}],"totalCount":${rows.length},"page":1,"pageSize":20}`);
    }
    const target = live.find(r => r.id === id);
    if (id !== null && !target) return fail(404, 'FI.COLLECTION.RECEIPT_NOT_FOUND');
    if (method === 'GET') return text(200, json(target!));
    if (method === 'POST' && !action || method === 'PUT') {
      const raw = request.postData()!; const body = JSON.parse(raw);
      const amount = /"amount":([0-9.]+)/.exec(raw)![1];
      if (target && body.version !== target.version) return fail(409, 'COMMON.CONCURRENCY.CONFLICT');
      if (target && !['Draft', 'Rejected'].includes(target.status)) return fail(422, 'FI.COLLECTION.RECEIPT_INVALID_STATUS');
      if (live.some(r => r.id !== id && r.bankReference.toLowerCase() === body.bankReference.toLowerCase()))
        return fail(409, 'FI.COLLECTION.BANK_REFERENCE_DUPLICATE');
      const fields = { customerId: body.customerId, businessDate: body.businessDate, amount, bankAccountId: body.bankAccountId,
        bankReference: body.bankReference, remark: body.remark };
      if (target) { Object.assign(target, fields, { status: 'Draft', version: target.version + 1 }); return text(200, json(target)); }
      const next = Math.max(0, ...server.receipts.map(r => r.id)) + 1;
      const saved = receipt({ id: next, documentNo: `RC2026111${next}`, ...fields });
      server.receipts.push(saved); return text(201, json(saved));
    }
    if (method === 'DELETE') {
      if (Number(url.searchParams.get('version')) !== target!.version) return fail(409, 'COMMON.CONCURRENCY.CONFLICT');
      target!.deleted = true; return route.fulfill({ status: 204 });
    }
    const version = JSON.parse(request.postData()!).version;
    if (version !== target!.version) return fail(409, 'COMMON.CONCURRENCY.CONFLICT');
    if (action === 'withdraw') {
      target!.status = 'Draft'; target!.version++;
      return text(200, JSON.stringify({ id: target!.id, documentNo: target!.documentNo, status: 'Draft', version: target!.version,
        approval: { instanceId: 9, status: 'Cancelled', currentStepNo: null, stepCount: 1 }, posting: null }));
    }
    if (target!.status !== 'Draft') return fail(422, 'FI.COLLECTION.RECEIPT_INVALID_STATUS');
    if (server.flowSteps > 0) {
      target!.status = 'Pending'; target!.version++;
      return text(200, JSON.stringify({ id: target!.id, documentNo: target!.documentNo, status: 'Pending', version: target!.version,
        approval: { instanceId: 9, status: 'Pending', currentStepNo: 1, stepCount: server.flowSteps }, posting: null }));
    }
    if (!server.mapping) return fail(422, 'FI.COLLECTION_POSTING.MAPPING_MISSING');
    target!.status = 'Posted'; target!.version += 2; target!.unapplied = target!.amount; target!.journalId = 501;
    return text(200, `{"id":${target!.id},"documentNo":"${target!.documentNo}","status":"Posted","version":${target!.version},`
      + `"approval":{"instanceId":9,"status":"Approved","currentStepNo":null,"stepCount":0},"posting":{"amount":${target!.amount},`
      + `"unappliedAmount":${target!.amount},"journalId":501,"baseCurrency":"TWD"}}`);
  });
}

const seeded = (): Server => ({ flowSteps: 0, mapping: true, writes: [], receipts: [
  receipt({ id: 1, businessDate: '2026-11-11' }),
  receipt({ id: 2, businessDate: '2026-11-12', status: 'Pending', version: 2, bankReference: 'TT-PENDING' }),
  receipt({ id: 3, businessDate: '2026-11-13', status: 'Posted', version: 3, amount: '999999999999999.9999', unapplied: '350.25', journalId: 88, customerId: 8, bankAccountId: 4 }),
  receipt({ id: 4, businessDate: '2026-11-10', status: 'Rejected', version: 3, remark: '金額有誤' }),
] });

test('FI-COLL-UI-101 lists collections with exact amounts and state actions in three languages and both themes', async ({ page }) => {
  await mockServer(page, seeded());
  await page.goto('/');
  await page.getByTestId('layout-menu-fi').click();
  await page.getByTestId('layout-menu-fi-sales-collections').click();
  await expect(page).toHaveURL(/\/ar\/sales-collections$/);
  await expect(page.getByTestId('fi-receipt-amount-3')).toHaveText('999,999,999,999,999.9999 TWD');
  await expect(page.getByTestId('fi-receipt-unapplied-3')).toHaveText('350.25');
  await expect(page.getByTestId('fi-receipt-unapplied-1')).toHaveText('—');
  await expect(page.getByTestId('fi-receipt-status-2')).toHaveText('審批中');
  await expect(page.getByTestId('fi-receipt-withdraw-2')).toBeVisible();
  await expect(page.getByTestId('fi-receipt-edit-3')).toHaveCount(0);
  await expect(page.locator('tbody tr[data-testid^="fi-receipt-row-"]').first()).toHaveAttribute('data-testid', 'fi-receipt-row-3');
  await expectNoOverflow(page);
  await capture(page, 'normal-light-zh-Hant');
  await chooseTheme(page, 'dark'); await expectReadableText(page.locator('.hint')); await capture(page, 'normal-dark-zh-Hant');
  await chooseLanguage(page, 'en');
  await expect(page.getByTestId('fi-receipt-status-2')).toHaveText('Pending approval');
  await expect(page.locator('erp-collection-receipt-page h1')).toHaveText('Sales collections');
  await capture(page, 'normal-dark-en');
  await chooseTheme(page, 'light'); await capture(page, 'normal-light-en');
  await chooseLanguage(page, 'zh-Hans');
  await expect(page.getByTestId('fi-receipt-status-3')).toHaveText('已过账');
  await capture(page, 'normal-light-zh-Hans');
  await chooseTheme(page, 'dark'); await capture(page, 'normal-dark-zh-Hans');
  await page.getByTestId('fi-receipt-view-3').click();
  await expect(page.getByTestId('fi-receipt-state-hint')).toContainText('已过账为未核销款');
  await expect(page.getByTestId('fi-receipt-detail-journal')).toHaveText('#88');
  await capture(page, 'detail-dark-zh-Hans');
});

test('FI-COLL-UI-102/103 create validates on screen, saves the exact amount, then submit posts unapplied cash only', async ({ page }) => {
  const server = seeded();
  await mockServer(page, server);
  await page.goto(PAGE);
  await page.getByTestId('fi-receipt-new').click();
  await expect(page).toHaveURL(/\/ar\/sales-collections\/new$/);
  await page.getByTestId('fi-receipt-amount').fill('0');
  await page.getByTestId('fi-receipt-save').click();
  await expect(page.getByTestId('fi-receipt-issue-customerId')).toHaveText('此欄位必填。');
  await expect(page.getByTestId('fi-receipt-issue-amount')).toHaveText('金額必須大於 0。');
  await expect(page.getByTestId('fi-receipt-issue-bankReference')).toHaveText('此欄位必填。');
  await expect(page.getByTestId('fi-receipt-customer-pick')).toBeFocused();
  expect(server.writes).toHaveLength(0);
  await capture(page, 'validation-light-zh-Hant');
  await chooseTheme(page, 'dark'); await expectReadableText(page.locator('.field-error')); await capture(page, 'validation-dark-zh-Hant');
  await chooseTheme(page, 'light');
  await page.getByTestId('fi-receipt-customer-pick').click();
  await page.getByTestId('erp-select-row-8').click(); await page.getByTestId('erp-select-confirm').click();
  await page.getByTestId('fi-receipt-fund-pick').click();
  await page.getByTestId('erp-select-row-3').click(); await page.getByTestId('erp-select-confirm').click();
  await expect(page.getByTestId('fi-receipt-customer')).toHaveText('C002 · Harbor Foods');
  await page.getByTestId('fi-receipt-date').fill('2026-11-14');
  await page.getByTestId('fi-receipt-amount').fill('123,456,789,012,345.6789');
  await page.getByTestId('fi-receipt-reference').fill('  tt-new-01  ');
  await page.getByTestId('fi-receipt-save').click();
  await expect(page).toHaveURL(/\/ar\/sales-collections$/);
  await expect(page.getByTestId('fi-receipt-notice')).toHaveAttribute('data-type', 'success');
  await expect(page.getByTestId('fi-receipt-notice')).toContainText('已儲存收款單 RC20261115（草稿）');
  // Screen and saved data agree: one new Draft with every digit, trimmed reference.
  const created = server.receipts.find(r => r.id === 5)!;
  expect(created).toMatchObject({ status: 'Draft', amount: '123456789012345.6789', bankReference: 'tt-new-01', customerId: 8, bankAccountId: 3 });
  await expect(page.getByTestId('fi-receipt-amount-5')).toHaveText('123,456,789,012,345.6789 TWD');
  await capture(page, 'created-light-zh-Hant');
  await page.getByTestId('fi-receipt-submit-5').click();
  const notice = page.getByTestId('fi-receipt-notice');
  await expect(notice).toContainText('已核准並過帳：未核銷款 123,456,789,012,345.6789 TWD');
  await expect(notice).toContainText('尚未核銷任何發票');
  await expect(notice).not.toContainText('已收款');
  await expect(page.getByTestId('fi-receipt-status-5')).toHaveText('已過帳');
  await expect(page.getByTestId('fi-receipt-unapplied-5')).toHaveText('123,456,789,012,345.6789');
  expect(server.receipts.find(r => r.id === 5)).toMatchObject({ status: 'Posted', unapplied: '123456789012345.6789' });
  await capture(page, 'posted-light-zh-Hant');
  await chooseLanguage(page, 'en'); await chooseTheme(page, 'dark');
  await expect(notice).toContainText('No invoice has been settled yet');
  await expectNoOverflow(page);
  await capture(page, 'posted-dark-en');
});

test('FI-COLL-UI-104/105 duplicate reference and posting refusal change nothing; pending flow, withdraw and delete', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of (409|422)/);
  const server = seeded();
  await mockServer(page, server);
  await page.goto(`${PAGE}/4/edit`);
  await expect(page.getByTestId('fi-receipt-form-rejected')).toBeVisible();
  await expect(page.getByTestId('fi-receipt-amount')).toHaveValue('1200.5');
  await page.getByTestId('fi-receipt-reference').fill('tt-pending');
  await page.getByTestId('fi-receipt-save').click();
  await expect(page.getByTestId('fi-receipt-issue-bankReference')).toContainText('已被其他收款單使用');
  await expect(page.getByTestId('fi-receipt-form-error')).toContainText('沒有儲存任何內容');
  expect(server.receipts.find(r => r.id === 4)).toMatchObject({ status: 'Rejected', version: 3, bankReference: 'TT-004' });
  await capture(page, 'duplicate-light-zh-Hant');
  await page.getByTestId('fi-receipt-reference').fill('TT-004B');
  await page.getByTestId('fi-receipt-save').click();
  await expect(page.getByTestId('fi-receipt-notice')).toContainText('RC20261104');
  expect(server.receipts.find(r => r.id === 4)).toMatchObject({ status: 'Draft', version: 4, bankReference: 'TT-004B' });
  // No collection map: the immediate approval is refused and nothing is posted.
  server.mapping = false;
  await page.getByTestId('fi-receipt-submit-1').click();
  await expect(page.getByTestId('fi-receipt-error')).toContainText('尚未設定收款科目');
  await expect(page.getByTestId('fi-receipt-notice')).toHaveCount(0);
  await expect(page.getByTestId('fi-receipt-status-1')).toHaveText('草稿');
  expect(server.receipts.find(r => r.id === 1)!.status).toBe('Draft');
  await capture(page, 'posting-refused-light-zh-Hant');
  server.flowSteps = 1;
  await page.getByTestId('fi-receipt-submit-1').click();
  await expect(page.getByTestId('fi-receipt-notice')).toContainText('已送審，等待審批；尚未過帳');
  await expect(page.getByTestId('fi-receipt-status-1')).toHaveText('審批中');
  await page.goto(`${PAGE}/1/edit`);
  await expect(page.getByTestId('fi-receipt-form-locked')).toContainText('請先在列表撤回');
  await expect(page.getByTestId('fi-receipt-save')).toBeDisabled();
  await capture(page, 'locked-light-zh-Hant');
  await page.getByTestId('fi-receipt-back').click();
  await page.getByTestId('fi-receipt-withdraw-1').click();
  await expect(page.getByTestId('fi-receipt-notice')).toContainText('已撤回，回到草稿');
  await page.getByTestId('fi-receipt-delete-1').click();
  await page.getByRole('button', { name: '刪除' }).last().click();
  await expect(page.getByTestId('fi-receipt-notice')).toContainText('已刪除收款單 RC20261101');
  await expect(page.getByTestId('fi-receipt-row-1')).toHaveCount(0);
  expect(server.receipts.find(r => r.id === 1)!.deleted).toBe(true);
});

test('FI-COLL-UI-106 empty, search-empty, server error with trace ID and network loss', async ({ page, pageErrors }) => {
  pageErrors.allowApi5xx(/\/api\/v1\/ar\/sales-collections/, 500);
  pageErrors.allow(/Failed to load resource: the server responded with a status of 500/);
  pageErrors.allow(/Failed to load resource: net::ERR_FAILED/);
  const server: Server = { receipts: [], flowSteps: 0, mapping: true, writes: [] };
  let mode: 'ok' | 'fail' | 'offline' = 'ok';
  await mockServer(page, server);
  // Registered last, so it runs first; 'ok' falls back to the mock server.
  await page.route(API, route => mode === 'fail' ? route.fulfill({ status: 500, json: { traceId: 'trace-receipt-500' } })
    : mode === 'offline' ? route.abort('failed') : route.fallback());
  await page.goto(PAGE);
  await expect(page.getByTestId('fi-receipt-table')).toContainText('尚未有收款單');
  await capture(page, 'empty-light-zh-Hant');
  server.receipts.push(receipt({ id: 1 }));
  await page.getByTestId('fi-receipt-keyword').fill('nothing');
  await page.getByTestId('fi-receipt-search').click();
  await expect(page.getByTestId('fi-receipt-table')).toContainText('沒有符合篩選條件的收款單');
  await capture(page, 'search-empty-light-zh-Hant');
  await page.getByTestId('fi-receipt-from').fill('2026-11-30'); await page.getByTestId('fi-receipt-to').fill('2026-11-01');
  await page.getByTestId('fi-receipt-search').click();
  await expect(page.getByTestId('fi-receipt-range-error')).toBeVisible();
  mode = 'fail'; await page.getByTestId('fi-receipt-reset').click();
  await expect(page.getByTestId('fi-receipt-error')).toContainText('trace-receipt-500');
  await capture(page, 'server-error-light-zh-Hant');
  mode = 'offline'; await page.getByTestId('fi-receipt-search').click();
  await expect(page.getByTestId('fi-receipt-error')).toContainText('無法連線到伺服器');
  await chooseTheme(page, 'dark'); await capture(page, 'network-error-dark-zh-Hant');
  mode = 'ok'; await page.getByTestId('fi-receipt-search').click();
  await expect(page.getByTestId('fi-receipt-error')).toHaveCount(0);
  await expect(page.getByTestId('fi-receipt-row-1')).toBeVisible();
});

test.describe('FI-COLL-UI-107 permissions', () => {
  test.describe('view only', () => {
    test.use({ authorization: { companies: [{ id: 1, code: 'TW01', name: 'Taipei', isDefault: true }],
      companyId: 1, isFullAccess: false, permissions: [{ code: 'AR.COLLECTION.VIEW', dataScope: 'Company' }] } });
    test('reads the list without create or row actions; the create route is forbidden', async ({ page }) => {
      const server = seeded();
      await mockServer(page, server);
      await page.goto(PAGE);
      await expect(page.getByTestId('fi-receipt-row-1')).toBeVisible();
      await expect(page.getByTestId('fi-receipt-new')).toHaveCount(0);
      await expect(page.locator('td.actions button, td.actions a')).toHaveCount(0);
      await capture(page, 'view-only-light-zh-Hant');
      await page.goto(`${PAGE}/new`);
      await expect(page).toHaveURL(/\/forbidden/);
      expect(server.writes).toHaveLength(0);
    });
  });
  test.describe('no permission', () => {
    test.use({ authorization: { companies: [{ id: 1, code: 'TW01', name: 'Taipei', isDefault: true }],
      companyId: 1, isFullAccess: false, permissions: [{ code: 'GL.PERIOD.VIEW', dataScope: 'Company' }] } });
    test('hides the menu and refuses the route without reading', async ({ page }) => {
      let reads = 0;
      await page.route(API, route => { reads++; return route.abort(); });
      await page.goto(PAGE);
      await expect(page).toHaveURL(/\/forbidden/);
      await page.getByTestId('layout-menu-fi').click();
      await expect(page.getByTestId('layout-menu-fi-sales-collections')).toHaveCount(0);
      expect(reads).toBe(0);
    });
  });
});
