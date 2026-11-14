import { mkdir } from 'node:fs/promises';
import type { Page, Route } from '@playwright/test';
import { expectReadableText } from '../support/contrast';
import { chooseLanguage, chooseTheme, expect, test } from '../support/fixtures';

/**
 * Saved settlement as the mock server holds it. Amounts are exact decimal text written raw into JSON; `open` is each
 * receivable's open amount, `unapplied` the receipt's unapplied cash (both reduced only when a settlement posts).
 */
type Line = { receivableId: number; amount: string };
type Settlement = { id: number; documentNo: string; customerId: number; businessDate: string; receiptId: number; remark: string | null;
  status: 'Draft' | 'Pending' | 'Rejected' | 'Posted'; version: number; lines: Line[]; deleted?: boolean };
type Server = { settlements: Settlement[]; open: Record<number, string>; unapplied: Record<number, string>; flowSteps: number;
  writes: { method: string; url: string }[] };

const API = '**/api/v1/ar/collection-settlements**';
const PAGE = '/ar/collection-settlements';
const CUSTOMERS: Record<number, { code: string; name: string }> = { 7: { code: 'C001', name: '大成貿易' }, 8: { code: 'C002', name: 'Harbor Foods' } };
const RECEIPT_NO: Record<number, string> = { 31: 'RC20261101', 33: 'RC20261103' };

async function capture(page: Page, name: string): Promise<void> {
  const path = '../../tests/qa/FI/screenshots';
  await mkdir(path, { recursive: true });
  await page.screenshot({ path: `${path}/6.29-${name}.png`, fullPage: true, animations: 'disabled' });
}

/** Layout guard: the page never scrolls sideways and no money cell overflows into its neighbour. */
async function expectNoOverflow(page: Page): Promise<void> {
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true);
  const overflowing = await page.locator('erp-collection-settlement-page td.num').evaluateAll(cells =>
    cells.filter(cell => cell.scrollWidth > cell.clientWidth + 1).length);
  expect(overflowing).toBe(0);
}

/** Exact decimal arithmetic on 4-decimal text (BigInt in ten-thousandths). */
const units = (text: string) => { const [i, f = ''] = text.split('.'); return BigInt(i) * 10000n + BigInt((f + '0000').slice(0, 4)); };
const text4 = (value: bigint) => { const s = value.toString().padStart(5, '0'); return `${s.slice(0, -4)}.${s.slice(-4)}`.replace(/\.?0+$/, '') || '0'; };
const total = (s: Settlement) => text4(s.lines.reduce((sum, l) => sum + units(l.amount), 0n));

function summary(s: Settlement): string {
  const c = CUSTOMERS[s.customerId];
  return JSON.stringify({ id: s.id, documentNo: s.documentNo, customerId: s.customerId, businessDate: s.businessDate, currency: 'TWD',
    receiptId: s.receiptId, receiptDocumentNo: RECEIPT_NO[s.receiptId] ?? null, totalAmount: 'TOTAL', lineCount: s.lines.length,
    status: s.status, version: s.version, customerCode: c.code, customerName: c.name }).replace('"TOTAL"', total(s));
}
function detail(s: Settlement, server: Server): string {
  const lines = s.lines.map((l, i) => `{"lineNo":${i + 1},"receivableId":${l.receivableId},"amount":${l.amount},"receivableOpenAmount":${server.open[l.receivableId]}}`);
  return summary(s).replace(/,"lineCount":\d+/, '').replace(/}$/, `,"receiptUnappliedAmount":${server.unapplied[s.receiptId] ?? 'null'},`
    + `"remark":${JSON.stringify(s.remark)},"createdAt":"2026-11-14T09:00:00","createdBy":1,"updatedAt":null,"updatedBy":null,"lines":[${lines.join(',')}]}`);
}

/**
 * In-memory `/api/v1/ar/collection-settlements` (ADR-130 contract): list filters status / keyword / dates, newest date first;
 * only Draft / Rejected may be deleted (422); submit re-checks every line against the open amounts and the receipt's unapplied
 * cash (422 with `lineErrors` / RECEIPT_INSUFFICIENT, nothing changes); with no approval steps it posts at once and reduces both.
 */
async function mockServer(page: Page, server: Server) {
  await page.route(API, async (route: Route) => {
    const request = route.request(); const url = new URL(request.url()); const method = request.method();
    const match = /collection-settlements(?:\/(\d+))?(?:\/(submit|withdraw))?$/.exec(url.pathname)!;
    const id = match[1] ? Number(match[1]) : null; const action = match[2];
    const live = server.settlements.filter(s => !s.deleted);
    const fail = (status: number, errorCode: string, extra: object = {}) => route.fulfill({ status, json: { status, errorCode, ...extra } });
    const text = (status: number, body: string) => route.fulfill({ status, contentType: 'application/json', body });
    if (method !== 'GET') server.writes.push({ method, url: url.pathname });
    if (method === 'GET' && id === null) {
      const p = url.searchParams; const keyword = (p.get('keyword') ?? '').toLowerCase();
      const rows = live.filter(s => (!p.get('status') || s.status === p.get('status')) && (!keyword || s.documentNo.toLowerCase().includes(keyword))
        && (!p.get('from') || s.businessDate >= p.get('from')!) && (!p.get('to') || s.businessDate <= p.get('to')!))
        .sort((a, b) => b.businessDate.localeCompare(a.businessDate) || b.id - a.id);
      return text(200, `{"items":[${rows.map(summary).join(',')}],"totalCount":${rows.length},"page":1,"pageSize":20}`);
    }
    const target = live.find(s => s.id === id);
    if (!target) return fail(404, 'FI.COLLECTION.SETTLEMENT_NOT_FOUND');
    if (method === 'GET') return text(200, detail(target, server));
    if (method === 'DELETE') {
      if (Number(url.searchParams.get('version')) !== target.version) return fail(409, 'COMMON.CONCURRENCY.CONFLICT');
      if (!['Draft', 'Rejected'].includes(target.status)) return fail(422, 'FI.COLLECTION.SETTLEMENT_INVALID_STATUS');
      target.deleted = true; return route.fulfill({ status: 204 });
    }
    const version = JSON.parse(request.postData()!).version;
    if (version !== target.version) return fail(409, 'COMMON.CONCURRENCY.CONFLICT');
    const result = (posting: string) => text(200, `{"id":${target.id},"documentNo":"${target.documentNo}","status":"${target.status}",`
      + `"version":${target.version},"approval":{"instanceId":9,"status":"${target.status === 'Draft' ? 'Cancelled' : target.status === 'Pending' ? 'Pending' : 'Approved'}",`
      + `"currentStepNo":null,"stepCount":${server.flowSteps}},"posting":${posting}}`);
    if (action === 'withdraw') { target.status = 'Draft'; target.version++; return result('null'); }
    if (target.status !== 'Draft') return fail(422, 'FI.COLLECTION.SETTLEMENT_INVALID_STATUS');
    const lineErrors = target.lines.map((l, i) => ({ line: i + 1, receivableId: l.receivableId,
      reason: server.open[l.receivableId] === undefined ? 'FI.COLLECTION_WRITEOFF.LINE.RECEIVABLE_NOT_FOUND'
        : units(l.amount) > units(server.open[l.receivableId]) ? 'FI.COLLECTION_WRITEOFF.LINE.EXCEEDS_OPEN' : '' })).filter(e => e.reason);
    if (lineErrors.length) return fail(422, 'FI.COLLECTION_WRITEOFF.ALLOCATION_INVALID', { lineErrors });
    if (units(total(target)) > units(server.unapplied[target.receiptId])) return fail(422, 'FI.COLLECTION_WRITEOFF.RECEIPT_INSUFFICIENT');
    if (server.flowSteps > 0) { target.status = 'Pending'; target.version++; return result('null'); }
    for (const l of target.lines) server.open[l.receivableId] = text4(units(server.open[l.receivableId]) - units(l.amount));
    server.unapplied[target.receiptId] = text4(units(server.unapplied[target.receiptId]) - units(total(target)));
    target.status = 'Posted'; target.version += 2;
    const allocations = target.lines.map(l => `{"receivableId":${l.receivableId},"amount":${l.amount},"receivableOpenAmount":${server.open[l.receivableId]}}`);
    return result(`{"baseCurrency":"TWD","totalAmount":${total(target)},"receiptUnappliedAmount":${server.unapplied[target.receiptId]},`
      + `"journalId":601,"allocations":[${allocations.join(',')}]}`);
  });
}

const settlement = (over: Partial<Settlement> & { id: number }): Settlement => ({ documentNo: `ST2026110${over.id}`, customerId: 7,
  businessDate: '2026-11-1' + (over.id % 10), receiptId: 31, remark: null, status: 'Draft', version: 1, lines: [], ...over });
const seeded = (): Server => ({ flowSteps: 0, writes: [],
  open: { 10: '800', 11: '250.1234', 12: '999999999999999.9999', 13: '0' },
  unapplied: { 31: '1200.5', 33: '999999999999999.9999' },
  settlements: [
    settlement({ id: 1, businessDate: '2026-11-11', lines: [{ receivableId: 10, amount: '700' }, { receivableId: 11, amount: '250.1234' }] }),
    settlement({ id: 2, businessDate: '2026-11-12', status: 'Pending', version: 2, lines: [{ receivableId: 10, amount: '100' }] }),
    settlement({ id: 3, businessDate: '2026-11-13', status: 'Posted', version: 3, customerId: 8, receiptId: 33,
      lines: [{ receivableId: 12, amount: '999999999999999.9999' }] }),
    settlement({ id: 4, businessDate: '2026-11-10', status: 'Rejected', version: 3, remark: '金額有誤', lines: [{ receivableId: 13, amount: '5' }] }),
  ] });

test('FI-SETL-UI-101 lists settlements with exact totals, lines and state actions in three languages and both themes', async ({ page }) => {
  await mockServer(page, seeded());
  await page.goto('/');
  await page.getByTestId('layout-menu-fi').click();
  await page.getByTestId('layout-menu-fi-collection-settlements').click();
  await expect(page).toHaveURL(/\/ar\/collection-settlements$/);
  await expect(page.getByTestId('fi-settle-total-3')).toHaveText('999,999,999,999,999.9999 TWD');
  await expect(page.getByTestId('fi-settle-total-1')).toHaveText('950.1234 TWD');
  await expect(page.getByTestId('fi-settle-receipt-1')).toHaveText('RC20261101');
  await expect(page.getByTestId('fi-settle-status-2')).toHaveText('審批中');
  await expect(page.getByTestId('fi-settle-withdraw-2')).toBeVisible();
  await expect(page.getByTestId('fi-settle-submit-3')).toHaveCount(0);
  await expect(page.locator('tbody tr[data-testid^="fi-settle-row-"]').first()).toHaveAttribute('data-testid', 'fi-settle-row-3');
  await expectNoOverflow(page);
  await capture(page, 'normal-light-zh-Hant');
  await chooseTheme(page, 'dark'); await expectReadableText(page.locator('.hint')); await capture(page, 'normal-dark-zh-Hant');
  await chooseLanguage(page, 'en');
  await expect(page.getByTestId('fi-settle-status-2')).toHaveText('Pending approval');
  await expect(page.locator('erp-collection-settlement-page h1')).toHaveText('Collection settlements');
  await capture(page, 'normal-dark-en');
  await chooseTheme(page, 'light'); await capture(page, 'normal-light-en');
  await chooseLanguage(page, 'zh-Hans');
  await expect(page.getByTestId('fi-settle-status-3')).toHaveText('已过账');
  await capture(page, 'normal-light-zh-Hans');
  await chooseTheme(page, 'dark'); await capture(page, 'normal-dark-zh-Hans');
  await page.getByTestId('fi-settle-view-3').click();
  await expect(page.getByTestId('fi-settle-state-hint')).toContainText('已核销应收');
  await expect(page.getByTestId('fi-settle-line-amount-1')).toHaveText('999,999,999,999,999.9999');
  await expect(page.getByTestId('fi-settle-detail-unapplied')).toHaveText('999,999,999,999,999.9999 TWD');
  await expectReadableText(page.locator('.lines caption'));
  await capture(page, 'detail-dark-zh-Hans');
});

test('FI-SETL-UI-102/103 a refused submit names each bad line and changes nothing; a valid one posts and the balances agree', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 422/);
  const server = seeded();
  server.open[11] = '250.1233';
  await mockServer(page, server);
  await page.goto(PAGE);
  await page.getByTestId('fi-settle-submit-1').click();
  await expect(page.getByTestId('fi-settle-error')).toContainText('部分明細無法核銷，沒有核銷任何應收');
  await expect(page.getByTestId('fi-settle-line-error-2')).toHaveText('第 2 行（應收 #11）：金額超過此應收目前的未清金額（可能已被折讓或核銷）。');
  await expect(page.getByTestId('fi-settle-line-error-1')).toHaveCount(0);
  await expect(page.getByTestId('fi-settle-notice')).toHaveCount(0);
  await expect(page.getByTestId('fi-settle-status-1')).toHaveText('草稿');
  // Screen and data agree: nothing was settled.
  expect(server.settlements[0]).toMatchObject({ status: 'Draft', version: 1 });
  expect(server.open).toMatchObject({ 10: '800', 11: '250.1233' });
  expect(server.unapplied[31]).toBe('1200.5');
  await capture(page, 'line-errors-light-zh-Hant');
  await chooseTheme(page, 'dark'); await expectReadableText(page.locator('.line-errors')); await capture(page, 'line-errors-dark-zh-Hant');
  await chooseTheme(page, 'light');
  // The receivable is open again (a credit was reversed): the same settlement now posts.
  server.open[11] = '250.1234';
  await page.getByTestId('fi-settle-submit-1').click();
  const notice = page.getByTestId('fi-settle-notice');
  await expect(notice).toHaveAttribute('data-type', 'success');
  await expect(notice).toContainText('已核銷 2 筆應收共 950.1234 TWD');
  await expect(notice).toContainText('收款單 RC20261101 尚餘未核銷款 250.3766 TWD');
  await expect(page.getByTestId('fi-settle-line-errors')).toHaveCount(0);
  await expect(page.getByTestId('fi-settle-status-1')).toHaveText('已過帳');
  expect(server.settlements[0]).toMatchObject({ status: 'Posted', version: 3 });
  expect(server.open).toMatchObject({ 10: '100', 11: '0' });
  expect(server.unapplied[31]).toBe('250.3766');
  await page.getByTestId('fi-settle-view-1').click();
  await expect(page.getByTestId('fi-settle-detail-unapplied')).toHaveText('250.3766 TWD');
  await expect(page.getByTestId('fi-settle-line-open-1')).toHaveText('100');
  await expect(page.getByTestId('fi-settle-line-open-2')).toHaveText('0');
  await capture(page, 'posted-light-zh-Hant');
  await chooseLanguage(page, 'en'); await chooseTheme(page, 'dark');
  await expect(notice).toContainText('Collection RC20261101 has 250.3766 TWD unapplied left.');
  await expectNoOverflow(page);
  // UX regression guards (6.29 review): document numbers and amounts never break mid-number; the lines caption sits above the table.
  await expect(page.getByTestId('fi-settle-receipt-1')).toHaveCSS('white-space', 'nowrap');
  await expect(page.getByTestId('fi-settle-total-3').locator('.amt')).toHaveCSS('white-space', 'nowrap');
  const caption = await page.locator('.lines caption').boundingBox();
  const firstLine = await page.getByTestId('fi-settle-line-1').boundingBox();
  expect(caption!.y).toBeLessThan(firstLine!.y);
  await capture(page, 'posted-dark-en');
});

test('FI-SETL-UI-104 pending flow, withdraw, delete with confirmation and a stale delete', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 409/);
  const server = seeded();
  server.flowSteps = 1;
  await mockServer(page, server);
  await page.goto(PAGE);
  await page.getByTestId('fi-settle-submit-1').click();
  await expect(page.getByTestId('fi-settle-notice')).toContainText('已送審，等待審批；尚未減少任何應收');
  await expect(page.getByTestId('fi-settle-status-1')).toHaveText('審批中');
  expect(server.open[10]).toBe('800');
  await page.getByTestId('fi-settle-withdraw-1').click();
  await expect(page.getByTestId('fi-settle-notice')).toContainText('已撤回，回到草稿');
  await expect(page.getByTestId('fi-settle-status-1')).toHaveText('草稿');
  // Someone else edited the rejected settlement meanwhile: the delete is refused and nothing is removed.
  server.settlements[3].version = 4;
  await page.getByTestId('fi-settle-delete-4').click();
  await page.getByRole('button', { name: '刪除' }).last().click();
  await expect(page.getByTestId('fi-settle-error')).toContainText('資料已被他人修改');
  await expect(page.getByTestId('fi-settle-row-4')).toBeVisible();
  expect(server.settlements[3].deleted).toBeUndefined();
  await capture(page, 'conflict-light-zh-Hant');
  await page.getByTestId('fi-settle-delete-4').click();
  await page.getByRole('button', { name: '刪除' }).last().click();
  await expect(page.getByTestId('fi-settle-notice')).toContainText('已刪除核銷單 ST20261104');
  await expect(page.getByTestId('fi-settle-row-4')).toHaveCount(0);
  await expect(page.getByTestId('fi-settle-error')).toHaveCount(0);
  expect(server.settlements[3].deleted).toBe(true);
});

test('FI-SETL-UI-105 empty, search-empty, server error with trace ID and network loss', async ({ page, pageErrors }) => {
  pageErrors.allowApi5xx(/\/api\/v1\/ar\/collection-settlements/, 500);
  pageErrors.allow(/Failed to load resource: the server responded with a status of 500/);
  pageErrors.allow(/Failed to load resource: net::ERR_FAILED/);
  const server: Server = { settlements: [], open: {}, unapplied: {}, flowSteps: 0, writes: [] };
  let mode: 'ok' | 'fail' | 'offline' = 'ok';
  await mockServer(page, server);
  await page.route(API, route => mode === 'fail' ? route.fulfill({ status: 500, json: { traceId: 'trace-settle-500' } })
    : mode === 'offline' ? route.abort('failed') : route.fallback());
  await page.goto(PAGE);
  await expect(page.getByTestId('fi-settle-table')).toContainText('尚未有核銷單');
  await capture(page, 'empty-light-zh-Hant');
  server.settlements.push(settlement({ id: 1 }));
  await page.getByTestId('fi-settle-keyword').fill('nothing');
  await page.getByTestId('fi-settle-search').click();
  await expect(page.getByTestId('fi-settle-table')).toContainText('沒有符合篩選條件的核銷單');
  await capture(page, 'search-empty-light-zh-Hant');
  await page.getByTestId('fi-settle-from').fill('2026-11-30'); await page.getByTestId('fi-settle-to').fill('2026-11-01');
  await page.getByTestId('fi-settle-search').click();
  await expect(page.getByTestId('fi-settle-range-error')).toBeVisible();
  mode = 'fail'; await page.getByTestId('fi-settle-reset').click();
  await expect(page.getByTestId('fi-settle-error')).toContainText('trace-settle-500');
  // UX regression guard (6.29 review): a failed read never claims there are no settlements.
  await expect(page.getByTestId('fi-settle-table')).toContainText('無法載入列表');
  await expect(page.getByTestId('fi-settle-table')).not.toContainText('尚未有核銷單');
  await capture(page, 'server-error-light-zh-Hant');
  mode = 'offline'; await page.getByTestId('fi-settle-search').click();
  await expect(page.getByTestId('fi-settle-error')).toContainText('無法連線到伺服器');
  await chooseTheme(page, 'dark'); await capture(page, 'network-error-dark-zh-Hant');
  mode = 'ok'; await page.getByTestId('fi-settle-search').click();
  await expect(page.getByTestId('fi-settle-error')).toHaveCount(0);
  await expect(page.getByTestId('fi-settle-row-1')).toBeVisible();
});

test.describe('FI-SETL-UI-106 permissions', () => {
  test.describe('view only', () => {
    test.use({ authorization: { companies: [{ id: 1, code: 'TW01', name: 'Taipei', isDefault: true }],
      companyId: 1, isFullAccess: false, permissions: [{ code: 'AR.SETTLEMENT.VIEW', dataScope: 'Company' },
        { code: 'AR.COLLECTION.SUBMIT', dataScope: 'Company' }, { code: 'AR.COLLECTION.DELETE', dataScope: 'Company' }] } });
    test('reads the list without row actions; receipt permissions grant nothing', async ({ page }) => {
      const server = seeded();
      await mockServer(page, server);
      await page.goto(PAGE);
      await expect(page.getByTestId('fi-settle-row-1')).toBeVisible();
      await expect(page.locator('td.actions button, td.actions a')).toHaveCount(0);
      await capture(page, 'view-only-light-zh-Hant');
      expect(server.writes).toHaveLength(0);
    });
  });
  test.describe('no permission', () => {
    test.use({ authorization: { companies: [{ id: 1, code: 'TW01', name: 'Taipei', isDefault: true }],
      companyId: 1, isFullAccess: false, permissions: [{ code: 'AR.COLLECTION.VIEW', dataScope: 'Company' }] } });
    test('hides the menu and refuses the route without reading', async ({ page }) => {
      let reads = 0;
      await page.route(API, route => { reads++; return route.abort(); });
      await page.goto(PAGE);
      await expect(page).toHaveURL(/\/forbidden/);
      await page.getByTestId('layout-menu-fi').click();
      await expect(page.getByTestId('layout-menu-fi-sales-collections')).toBeVisible();
      await expect(page.getByTestId('layout-menu-fi-collection-settlements')).toHaveCount(0);
      expect(reads).toBe(0);
    });
  });
});
