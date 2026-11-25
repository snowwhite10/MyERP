import { mkdir } from 'node:fs/promises';
import { randomUUID } from 'node:crypto';
import { expect, request, test, type APIRequestContext, type APIResponse, type Page } from '@playwright/test';
import { realApiLogin, type RealApiTokens } from '../support/real-api';
import { companyId, sql } from '../support/real-api-sql';

// WU-SD-008b3c2a (SD-RETURN-REAL-001–003, ADR-199): Sales Return Create 2.17 and View 2.16 against the running API and
// SQL Server. sales picks a fresh Approved order on 2.17, returns 2 not-invoiced + 1 credited on its Posted invoice to
// another warehouse, saves a Draft (sal_return / sal_return_line / sal_return_invoice_allocation compared with what the
// form showed), then submits it on 2.16: with no active SAL.RETURN flow it posts in the same request, so INV holds one
// SALES_RETURN inbound movement per line in the chosen warehouse, FI holds one Return sales source with a balanced
// Posted journal and one credit allocation against the invoice receivable, whose open amount drops by the credited
// gross. Resending the posted request key replays 200 without a second return; a changed payload is 409.
// Arrange (admin and sales, through the MD / FI / INV / SD APIs, never SQL writes): a fresh unit, item, two warehouses,
// a 5% output tax code, an approved customer with a TWD credit limit, opening stock, an Approved TWD order, a Posted
// delivery and a Posted invoice. Active SAL.ORDER / SAL.DELIVERY / SAL.RETURN flows are switched off while the spec
// runs and put back as they were in afterAll.
// Run: & ./tools/scripts/Invoke-RealApiE2E.ps1 -Specs specs/real-api-sales-return.spec
const apiUrl = process.env['E2E_API_URL'];
const root = '/api/v1';
const FLOWS = ['SAL.ORDER', 'SAL.DELIVERY', 'SAL.RETURN'] as const;
const ORDERED = 10;
const PRICE = 100;
const INVOICED = 4;
const UNBILLED = 2;
const CREDITED = 1;
const RETURNED = UNBILLED + CREDITED;
// 3 × 100 = 300 net, 5% tax 15, gross 315; the credited part 1 × 105 lowers the 420 invoice receivable to 315.
const NET = RETURNED * PRICE;
const TAX = NET * 0.05;
const GROSS = NET + TAX;
const INVOICE_GROSS = INVOICED * PRICE * 1.05;
const CREDIT_GROSS = CREDITED * PRICE * 1.05;

type User = 'sales' | 'admin';
type Flow = { id: number; name: string; documentType: string; allowSelfApproval: boolean; isActive: boolean;
  steps: unknown[]; version: number };
type SavedReturn = { id: number; documentNo: string; requestKey: string; customerId: number; orderId: number;
  businessDate: string; currencyCode: string; status: string; hasPostedAt: number; fiCreditNoteId: number | null;
  approvalInstanceId: number | null; originalNet: string; originalTax: string; originalGross: string; baseGross: string;
  createdBy: number; version: number };
type SavedLine = { id: number; lineNo: number; deliveryId: number; deliveryLineId: number; orderLineId: number;
  itemId: number; warehouseId: number; locationId: number | null; quantity: string; unbilledQuantity: string;
  invMovementId: number | null; originalOutboundMovementId: number; baseGross: string };
type SavedAllocation = { invoiceId: number; invoiceLineId: number; quantity: string; originalGross: string; baseGross: string };

const savedReturns = (company: number, order: number) => sql<SavedReturn>(`SELECT id, document_no AS documentNo,
  request_key AS requestKey, customer_id AS customerId, order_id AS orderId,
  CONVERT(char(10), business_date, 23) AS businessDate, currency_code AS currencyCode, status,
  CASE WHEN posted_at IS NULL THEN 0 ELSE 1 END AS hasPostedAt, fi_credit_note_id AS fiCreditNoteId,
  approval_instance_id AS approvalInstanceId, CONVERT(varchar(40), original_net) AS originalNet,
  CONVERT(varchar(40), original_tax) AS originalTax, CONVERT(varchar(40), original_gross) AS originalGross,
  CONVERT(varchar(40), base_gross) AS baseGross, created_by AS createdBy, version
  FROM sal_return WHERE company_id = ${company} AND order_id = ${order} ORDER BY id`);
const savedLines = (ret: number) => sql<SavedLine>(`SELECT id, line_no AS [lineNo], delivery_id AS deliveryId,
  delivery_line_id AS deliveryLineId, order_line_id AS orderLineId, item_id AS itemId, warehouse_id AS warehouseId,
  location_id AS locationId, CONVERT(varchar(40), quantity) AS quantity,
  CONVERT(varchar(40), unbilled_quantity) AS unbilledQuantity, inv_movement_id AS invMovementId,
  original_outbound_movement_id AS originalOutboundMovementId, CONVERT(varchar(40), base_gross) AS baseGross
  FROM sal_return_line WHERE return_id = ${ret} ORDER BY line_no`);
const savedAllocations = (line: number) => sql<SavedAllocation>(`SELECT invoice_id AS invoiceId,
  invoice_line_id AS invoiceLineId, CONVERT(varchar(40), quantity) AS quantity,
  CONVERT(varchar(40), original_gross) AS originalGross, CONVERT(varchar(40), base_gross) AS baseGross
  FROM sal_return_invoice_allocation WHERE return_line_id = ${line} ORDER BY invoice_line_id`);
const movement = (company: number, id: number) => sql<{ sourceType: string; sourceId: number; sourceLineNo: number;
  direction: string; itemId: number; warehouseId: number; locationId: number | null; quantity: string;
  businessDate: string }>(`SELECT source_type AS sourceType, source_id AS sourceId, source_line_no AS sourceLineNo,
  direction, item_id AS itemId, warehouse_id AS warehouseId, location_id AS locationId,
  CONVERT(varchar(40), quantity) AS quantity, CONVERT(char(10), business_date, 23) AS businessDate
  FROM inv_stock_movement WHERE company_id = ${company} AND id = ${id}`)[0];
/** On-hand quantity of the item per warehouse, summed from the movement ledger. */
const onHand = (company: number, item: number, warehouse: number) => Number(sql<{ qty: string }>(`SELECT
  CONVERT(varchar(40), COALESCE(SUM(CASE direction WHEN 'In' THEN quantity ELSE -quantity END), 0)) AS qty
  FROM inv_stock_movement WHERE company_id = ${company} AND item_id = ${item} AND warehouse_id = ${warehouse}`)[0]!.qty);
const creditSource = (company: number, id: number) => sql<{ customerId: number; sourceType: string; sourceId: number;
  journalId: number; baseGross: string }>(`SELECT customer_id AS customerId, source_type AS sourceType,
  source_id AS sourceId, gl_journal_id AS journalId, CONVERT(varchar(40), base_gross_amount) AS baseGross
  FROM ar_sales_source WHERE company_id = ${company} AND id = ${id}`)[0];
const creditAllocations = (company: number, source: number) => sql<{ receivableId: number; invoiceLineId: number;
  quantity: string; baseGross: string }>(`SELECT receivable_id AS receivableId, invoice_line_id AS invoiceLineId,
  CONVERT(varchar(40), quantity) AS quantity, CONVERT(varchar(40), base_gross_amount) AS baseGross
  FROM ar_credit_allocation WHERE company_id = ${company} AND credit_source_id = ${source}`);
const journal = (company: number, id: number) => sql<{ status: string; businessDate: string; totalDebit: string;
  totalCredit: string }>(`SELECT status, CONVERT(char(10), business_date, 23) AS businessDate,
  CONVERT(varchar(40), total_debit) AS totalDebit, CONVERT(varchar(40), total_credit) AS totalCredit
  FROM gl_journal WHERE company_id = ${company} AND id = ${id}`)[0];
const openReceivable = (company: number, id: number) => sql<{ open: string }>(`SELECT
  CONVERT(varchar(40), open_base_amount) AS [open] FROM ar_posted_receivable WHERE company_id = ${company} AND id = ${id}`)[0]!.open;
/** Rows FI and INV wrote for this customer's returns: a replay must not add any. */
const effects = (company: number, customer: number, item: number) => sql<{ sources: number; movements: number }>(`SELECT
  (SELECT COUNT(*) FROM ar_sales_source WHERE company_id = ${company} AND customer_id = ${customer} AND source_type = 'Return') AS sources,
  (SELECT COUNT(*) FROM inv_stock_movement WHERE company_id = ${company} AND item_id = ${item} AND source_type = 'SALES_RETURN') AS movements`)[0];

const pad = (n: number) => String(n).padStart(2, '0');
const isoDate = (d: Date) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
const today = () => isoDate(new Date());
const inDays = (days: number) => { const d = new Date(); d.setDate(d.getDate() + days); return isoDate(d); };
const four = (value: number) => value.toFixed(4);
const six = (value: number) => value.toFixed(6);

async function capture(page: Page, name: string): Promise<void> {
  const path = '../../tests/qa/SD/screenshots';
  await mkdir(path, { recursive: true });
  await page.screenshot({ path: `${path}/real-return-${name}.png`, fullPage: true, animations: 'disabled' });
}
async function captureFi(page: Page, name: string): Promise<void> {
  const path = '../../tests/qa/FI/screenshots';
  await mkdir(path, { recursive: true });
  await page.screenshot({ path: `${path}/6.31-${name}.png`, fullPage: true, animations: 'disabled' });
}

/** Follow-system dark mode; waits until the dark stylesheet has loaded. */
async function dark(page: Page): Promise<void> {
  await page.emulateMedia({ colorScheme: 'dark' });
  await page.waitForFunction(() => {
    const link = document.getElementById('erp-theme-dark') as HTMLLinkElement | null;
    return document.documentElement.classList.contains('erp-dark') && !!link?.sheet;
  });
}

async function json(response: Promise<APIResponse>, status = 200): Promise<any> {
  const value = await response;
  expect(value.status(), await value.text()).toBe(status);
  return value.status() === 204 ? null : value.json();
}

test.describe('真實 API 銷售退貨：2.17 建立草稿 → 2.16 送審過帳，畫面與 SQL／INV 入庫／FI 信用票據一致；同鍵重送不重複', () => {
  test.describe.configure({ mode: 'serial' });
  test.skip(!apiUrl, '需要 E2E_API_URL：請用 tools/scripts/Invoke-RealApiE2E.ps1 執行');
  test.setTimeout(180_000);

  const run = `${Date.now() % 1_000_000_000}${Math.floor(Math.random() * 100)}`;
  const tokens: Partial<Record<User, RealApiTokens>> = {};
  const apis: Partial<Record<User, APIRequestContext>> = {};
  const suspended: Flow[] = [];
  let salesUserId: number;
  let tw01: number;
  let customer: { id: number; code: string; name: string };
  let item: { id: number; code: string; name: string };
  let shipFrom: { id: number; code: string; name: string };
  let returnTo: { id: number; code: string; name: string };
  let order: { id: number; documentNo: string; lineId: number };
  let delivery: { id: number; documentNo: string; lineId: number };
  let invoice: { id: number; documentNo: string; lineId: number; receivableId: number };
  let saved: SavedReturn | undefined;
  let postedBody: string | undefined;
  let consoleErrors: string[];
  let serverErrors: string[];

  test.beforeAll(async () => {
    test.setTimeout(300_000); // realApiLogin may wait out the login rate-limit window
    tw01 = companyId('TW01');
    for (const [user, password] of [['sales', 'Demo@2026'], ['admin', 'az5911']] as const) {
      tokens[user] = await realApiLogin(apiUrl!, user, password);
      apis[user] = await request.newContext({ baseURL: apiUrl, extraHTTPHeaders: {
        Authorization: `Bearer ${tokens[user]!.accessToken}`, 'X-Company-Id': String(tw01) } });
    }
    salesUserId = sql<{ id: number }>(`SELECT id FROM sys_user WHERE username = 'sales'`)[0]!.id;
    const grants = sql<{ code: string }>(`SELECT rp.permission_code AS code FROM sys_role_permission rp
      JOIN sys_role r ON r.id = rp.role_id WHERE r.code = 'SALES' AND rp.permission_code LIKE 'SAL.RETURN.%'`).map(x => x.code);
    expect(grants, 'SALES holds SAL.RETURN.CREATE / VIEW / SUBMIT: run --seed')
      .toEqual(expect.arrayContaining(['SAL.RETURN.CREATE', 'SAL.RETURN.VIEW', 'SAL.RETURN.SUBMIT']));

    const admin = apis.admin!;
    const sales = apis.sales!;
    // Approval flows off while the spec runs; afterAll puts each back exactly as it was.
    const flows = await json(admin.get(`${root}/sys/approval-flows?pageSize=100`));
    for (const type of FLOWS) {
      const summary = flows.items.find((flow: Flow) => flow.documentType === type && flow.isActive);
      if (!summary) continue;
      const flow: Flow = await json(admin.get(`${root}/sys/approval-flows/${summary.id}`));
      await json(admin.put(`${root}/sys/approval-flows/${flow.id}`, { data: { name: flow.name,
        allowSelfApproval: flow.allowSelfApproval, isActive: false, steps: flow.steps, version: flow.version } }));
      suspended.push(flow);
    }

    const uom = await json(admin.post(`${root}/md/uoms`, { data: { code: `E2ERU${run}`, name: `E2E 銷退個 ${run}`, decimalPlaces: 0 } }), 201);
    item = await json(admin.post(`${root}/md/items`, { data: { code: `E2ERP${run}`, name: `E2E 銷退物料 ${run}`,
      specification: null, baseUomId: uom.id, brandId: null, categoryId: null } }), 201);
    shipFrom = await json(admin.post(`${root}/md/warehouses`, { data: { code: `E2ERW${run}`, name: `E2E 銷退出貨倉 ${run}`,
      address: null, contactName: null, phone: null } }), 201);
    returnTo = await json(admin.post(`${root}/md/warehouses`, { data: { code: `E2ERR${run}`, name: `E2E 退貨倉 ${run}`,
      address: null, contactName: null, phone: null } }), 201);
    const tax = await json(admin.post(`${root}/md/tax-codes`, { data: { code: `E2ERT${run}`, name: `E2E 銷退銷項稅 ${run}`, taxType: 'Output' } }), 201);
    const rate = await json(admin.post(`${root}/md/tax-codes/${tax.id}/rates`, { data: { effectiveFrom: '2020-01-01', effectiveTo: null, ratePercent: 5 } }), 201);
    const created = await json(admin.post(`${root}/md/customers`, { data: { code: `E2ERC${run}`, name: `E2E 銷退客戶 ${run}`,
      categoryId: null, defaultPaymentTermId: null, defaultSettlementMethodId: null } }), 201);
    await json(admin.post(`${root}/md/customers/${created.id}/submit`, { data: { version: created.version } }));
    const approved = sql<{ reviewStatus: string }>(`SELECT review_status AS reviewStatus FROM md_customer WHERE id = ${created.id}`)[0];
    expect(approved?.reviewStatus, 'MD.CUSTOMER must have no active approval flow for this spec').toBe('Approved');
    customer = created;
    await json(admin.post(`${root}/ar/credit-limits`, { data: { customerId: customer.id,
      effectiveFrom: inDays(-30), effectiveTo: inDays(365), amount: 1_000_000 } }), 201);
    await json(admin.post(`${root}/inv/opening-stocks`, { data: { requestKey: `e2e-inv-${run}`, openingDate: inDays(-1),
      remark: `E2E 銷退期初 ${run}`, lines: [{ lineNo: 1, itemId: item.id, warehouseId: shipFrom.id, locationId: null,
        quantity: ORDERED, unitCost: 50 }] } }), 201);

    // sales: Approved order → Posted delivery → Posted invoice of part of it.
    const draft = await json(sales.post(`${root}/sal/orders`, { data: { customerId: customer.id, businessDate: today(),
      currencyCode: 'TWD', remark: null, lines: [{ lineNo: 1, itemId: item.id, quantity: ORDERED, unitPrice: PRICE,
        discountRate: 0, taxRateId: rate.id, overrideReason: `E2E 無價格規則手動定價 ${run}` }] } }), 201);
    const submitted = await json(sales.post(`${root}/sal/orders/${draft.id}/submit`, { data: { version: draft.version } }));
    expect(submitted.order.status, 'the order is Approved on submit (no active SAL.ORDER flow)').toBe('Approved');
    order = { id: draft.id, documentNo: draft.documentNo, lineId: draft.lines[0].id };
    const outbound = await json(sales.post(`${root}/sal/deliveries`, { data: { orderId: order.id, businessDate: today(),
      lines: [{ lineNo: 1, orderLineId: order.lineId, warehouseId: shipFrom.id, locationId: null, quantity: ORDERED }] } }), 201);
    const posted = await json(sales.post(`${root}/sal/deliveries/${outbound.id}/submit`, { data: { version: outbound.version } }));
    expect(posted.status, 'the delivery posts on submit (no active SAL.DELIVERY flow)').toBe('Posted');
    delivery = { id: outbound.id, documentNo: outbound.documentNo, lineId: outbound.lines[0].id };
    const issued = await json(sales.post(`${root}/sal/invoices`, { data: { requestKey: randomUUID(), businessDate: today(),
      lines: [{ lineNo: 1, deliveryLineId: delivery.lineId, quantity: INVOICED }] } }), 201);
    expect(issued.status).toBe('Posted');
    invoice = { id: issued.id, documentNo: issued.documentNo, lineId: issued.lines[0].id, receivableId: issued.fiReceivableId };
  });

  test.afterAll(async () => {
    const admin = apis.admin;
    try {
      for (const flow of suspended) {
        const current: Flow = await json(admin!.get(`${root}/sys/approval-flows/${flow.id}`));
        await json(admin!.put(`${root}/sys/approval-flows/${flow.id}`, { data: { name: flow.name,
          allowSelfApproval: flow.allowSelfApproval, isActive: flow.isActive, steps: flow.steps, version: current.version } }));
      }
    } finally {
      await Promise.all(Object.values(apis).map(api => api?.dispose()));
    }
  });

  async function login(page: Page, user: User): Promise<void> {
    await page.addInitScript(([key, value]) => localStorage.setItem(key, value),
      ['myerp.auth.session', JSON.stringify({ ...tokens[user], username: user })]);
    await page.goto('/');
    await expect(page.getByTestId('home-title')).toBeVisible();
    await expect(page.getByTestId('layout-company-select')).toContainText('TW01');
  }

  /** Searches the shared select dialog by keyword and confirms the row with that ID. */
  async function pick(page: Page, opener: string, keyword: string, id: number): Promise<void> {
    await page.getByTestId(opener).click();
    await expect(page.getByTestId('erp-select-dialog')).toBeVisible();
    await page.getByTestId('erp-select-keyword').fill(keyword);
    await page.getByTestId('erp-select-search').click();
    await page.getByTestId(`erp-select-row-${id}`).click();
    await page.getByTestId('erp-select-confirm').click();
  }

  test.beforeEach(({ page }) => {
    consoleErrors = []; serverErrors = [];
    page.on('console', message => { if (message.type() === 'error') consoleErrors.push(message.text()); });
    page.on('pageerror', error => consoleErrors.push(error.message));
    page.on('response', response => { if (response.status() >= 500) serverErrors.push(`${response.status()} ${response.url()}`); });
  });

  test.afterEach(async ({ page }) => {
    expect(await page.locator('body').innerText()).not.toMatch(/\b(sd|common|layout|errors)\.[a-zA-Z][A-Za-z_]+\.[A-Za-z][A-Za-z_.]+\b/);
    expect(serverErrors).toEqual([]);
    expect(consoleErrors).toEqual([]);
  });

  test('SD-RETURN-REAL-001 sales saves a Draft on 2.17 with a new destination; 2.16 shows what SQL saved and nothing posted', async ({ page }) => {
    await login(page, 'sales');
    await page.goto('/sal/returns');
    await page.getByTestId('sd-return-new').click();
    await expect(page).toHaveURL(/\/sal\/returns\/new$/);
    await pick(page, 'sd-return-create-order-pick', order.documentNo, order.id);
    await expect(page.getByTestId('sd-return-create-order')).toContainText(`${order.documentNo} · ${customer.code} · ${customer.name}`);
    await expect(page.getByTestId('sd-return-create-rate')).toHaveText('1');
    const line = delivery.lineId;
    await expect(page.getByTestId(`sd-return-create-line-${line}`)).toContainText(delivery.documentNo);
    await expect(page.getByTestId(`sd-return-create-returnable-${line}`)).toHaveText(String(ORDERED));
    await expect(page.getByTestId(`sd-return-create-line-${line}`)).toContainText(`未開票（最多 ${ORDERED - INVOICED}）`);
    await expect(page.getByTestId(`sd-return-create-line-${line}`)).toContainText(`沖減 ${invoice.documentNo}（${today()}，最多 ${INVOICED}）`);
    await page.getByTestId('sd-return-create-date').fill(today());
    await page.getByTestId(`sd-return-create-select-${line}`).check();
    await page.getByTestId(`sd-return-create-unbilled-${line}`).fill(String(UNBILLED));
    await page.getByTestId(`sd-return-create-billed-${line}-${invoice.lineId}`).fill(String(CREDITED));
    await expect(page.getByTestId(`sd-return-create-total-${line}`)).toHaveText(String(RETURNED));
    await expect(page.getByTestId(`sd-return-create-destination-text-${line}`)).toHaveText(`${shipFrom.code} · ${shipFrom.name}`);
    await pick(page, `sd-return-create-warehouse-pick-${line}`, returnTo.code, returnTo.id);
    await expect(page.getByTestId(`sd-return-create-destination-text-${line}`)).toHaveText(`${returnTo.code} · ${returnTo.name}`);
    await capture(page, '2.17-ready-light-zh-Hant');
    expect(savedReturns(tw01, order.id), 'nothing saved before submit').toEqual([]);

    page.on('request', sent => {
      if (sent.method() === 'POST' && new URL(sent.url()).pathname === `${root}/sal/returns`) postedBody = sent.postData() ?? undefined;
    });
    await page.getByTestId('sd-return-create-submit').click();
    await expect.poll(() => savedReturns(tw01, order.id).length).toBe(1);
    saved = savedReturns(tw01, order.id)[0]!;
    await expect(page.locator('.ant-message')).toContainText(`銷售退貨 ${saved.documentNo} 已儲存為草稿。`);
    expect(saved).toMatchObject({ customerId: customer.id, orderId: order.id, businessDate: today(), currencyCode: 'TWD',
      status: 'Draft', hasPostedAt: 0, fiCreditNoteId: null, approvalInstanceId: null, createdBy: salesUserId, version: 1 });
    expect([saved.originalNet, saved.originalTax, saved.originalGross, saved.baseGross].map(Number)).toEqual([NET, TAX, GROSS, GROSS]);
    expect(JSON.parse(postedBody!).requestKey).toBe(saved.requestKey);
    const lines = savedLines(saved.id);
    expect(lines).toEqual([expect.objectContaining({ lineNo: 1, deliveryId: delivery.id, deliveryLineId: line,
      orderLineId: order.lineId, itemId: item.id, warehouseId: returnTo.id, locationId: null, quantity: six(RETURNED),
      unbilledQuantity: six(UNBILLED), invMovementId: null })]);
    expect(Number(lines[0]!.baseGross)).toBe(GROSS);
    const allocations = savedAllocations(lines[0]!.id);
    expect(allocations).toEqual([expect.objectContaining({ invoiceId: invoice.id, invoiceLineId: invoice.lineId, quantity: six(CREDITED) })]);
    expect(Number(allocations[0]!.baseGross)).toBe(CREDIT_GROSS);
    expect(effects(tw01, customer.id, item.id), 'a Draft touches neither INV nor FI').toEqual({ sources: 0, movements: 0 });
    expect(Number(openReceivable(tw01, invoice.receivableId))).toBe(INVOICE_GROSS);

    // The list opens the saved Draft: every figure equals the SQL row.
    await expect(page.getByTestId('sd-return-detail-title')).toContainText(saved.documentNo);
    await expect(page.getByTestId(`sd-return-status-${saved.id}`)).toContainText('草稿');
    await expect(page.getByTestId('sd-return-state')).toContainText('尚未影響庫存與應收');
    await expect(page.getByTestId('sd-return-detail-order')).toContainText(order.documentNo);
    await expect(page.getByTestId('sd-return-detail-customer')).toContainText(customer.name);
    const row = `sd-return-line-${lines[0]!.id}`;
    await expect(page.getByTestId(row)).toContainText(`${item.code} · ${item.name}`);
    await expect(page.getByTestId(`sd-return-destination-${lines[0]!.id}`)).toContainText(`${returnTo.code} · ${returnTo.name}`);
    await expect(page.getByTestId(`sd-return-billed-${lines[0]!.id}`)).toContainText(invoice.documentNo);
    await expect(page.getByTestId(`sd-return-inv-${lines[0]!.id}`)).toHaveText('尚未入庫');
    await expect(page.getByTestId('sd-return-totals')).toContainText(`${GROSS}.00`);
    await capture(page, '2.16-draft-light-zh-Hant');
  });

  test('SD-RETURN-REAL-002 submit on 2.16 posts with no approval step: INV receipt, FI credit and AR match the screen', async ({ page }) => {
    expect(saved, 'SD-RETURN-REAL-001 must pass first').toBeDefined();
    const shipBefore = onHand(tw01, item.id, shipFrom.id);
    const returnBefore = onHand(tw01, item.id, returnTo.id);
    await login(page, 'sales');
    await page.goto(`/sal/returns?open=${saved!.id}`);
    await expect(page.getByTestId('sd-return-detail-title')).toContainText(saved!.documentNo);
    await page.getByTestId('sd-return-submit').click();
    await expect(page.getByTestId('erp-message-title')).toContainText(saved!.documentNo);
    await page.getByTestId('erp-message-ok').click();
    await expect(page.getByTestId('sd-return-action-notice')).toContainText('無需審批，已過帳（版本 3）');

    const posted = savedReturns(tw01, order.id);
    expect(posted, 'still one return').toHaveLength(1);
    const ret = posted[0]!;
    expect(ret).toMatchObject({ id: saved!.id, status: 'Posted', hasPostedAt: 1, version: 3 });
    expect(ret.fiCreditNoteId).not.toBeNull();
    const line = savedLines(ret.id)[0]!;
    expect(line.invMovementId).not.toBeNull();

    // INV: one SALES_RETURN inbound of the returned quantity into the chosen warehouse; the shipping warehouse is untouched.
    expect(movement(tw01, line.invMovementId!)).toEqual({ sourceType: 'SALES_RETURN', sourceId: ret.id, sourceLineNo: 1,
      direction: 'In', itemId: item.id, warehouseId: returnTo.id, locationId: null, quantity: six(RETURNED), businessDate: today() });
    expect(onHand(tw01, item.id, returnTo.id)).toBe(returnBefore + RETURNED);
    expect(onHand(tw01, item.id, shipFrom.id)).toBe(shipBefore);

    // FI: one Return sales source with a balanced Posted journal; the credited part is allocated to the invoice receivable.
    const source = creditSource(tw01, ret.fiCreditNoteId!)!;
    expect(source).toMatchObject({ customerId: customer.id, sourceType: 'Return', sourceId: ret.id });
    const entry = journal(tw01, source.journalId)!;
    expect(entry.status).toBe('Posted');
    expect(entry.businessDate).toBe(today());
    expect(entry.totalDebit).toBe(entry.totalCredit);
    expect(creditAllocations(tw01, ret.fiCreditNoteId!)).toEqual([{ receivableId: invoice.receivableId, invoiceLineId: invoice.lineId,
      quantity: six(CREDITED), baseGross: four(CREDIT_GROSS) }]);
    expect(Number(openReceivable(tw01, invoice.receivableId))).toBe(INVOICE_GROSS - CREDIT_GROSS);
    expect(effects(tw01, customer.id, item.id)).toEqual({ sources: 1, movements: 1 });

    // 2.16 now shows the Posted state with the IDs SQL saved.
    await expect(page.getByTestId(`sd-return-status-${ret.id}`)).toContainText('已過帳');
    await expect(page.getByTestId('sd-return-state')).toContainText('貨品已入庫');
    await expect(page.getByTestId('sd-return-fi-credit')).toHaveText(String(ret.fiCreditNoteId));
    await expect(page.getByTestId(`sd-return-inv-${line.id}`)).toHaveText(String(line.invMovementId));
    await expect(page.getByTestId('sd-return-submit')).toHaveCount(0);
    await expect(page.getByTestId('sd-return-edit')).toHaveCount(0);
    await capture(page, '2.16-posted-light-zh-Hant');
    await dark(page);
    await capture(page, '2.16-posted-dark-zh-Hant');
  });

  test('SD-RETURN-REAL-003 the posted request key replays 200 with no second return; a changed payload is 409; 2.17 shows what is left', async ({ page }) => {
    expect(saved, 'SD-RETURN-REAL-001 must pass first').toBeDefined();
    expect(postedBody, 'the 2.17 POST body was captured').toBeDefined();
    const before = effects(tw01, customer.id, item.id);
    const body = JSON.parse(postedBody!);
    const replay = await json(apis.sales!.post(`${root}/sal/returns`, { data: body }));
    expect(replay).toMatchObject({ id: saved!.id, documentNo: saved!.documentNo, status: 'Posted' });
    const conflict = await apis.sales!.post(`${root}/sal/returns`, { data: { ...body,
      lines: body.lines.map((line: { unbilledQuantity: number }) => ({ ...line, unbilledQuantity: line.unbilledQuantity - 1 })) } });
    expect(conflict.status()).toBe(409);
    expect((await conflict.json()).errorCode).toBe('SAL.RETURN.REQUEST_CONFLICT');
    expect(savedReturns(tw01, order.id), 'still one return').toHaveLength(1);
    expect(effects(tw01, customer.id, item.id), 'replay and conflict post nothing to INV or FI').toEqual(before);

    // 2.17 offers only what is left: 10 shipped − 3 returned; not invoiced 6 − 2; credit on the invoice 4 − 1.
    await login(page, 'sales');
    await page.goto('/sal/returns/new');
    await pick(page, 'sd-return-create-order-pick', order.documentNo, order.id);
    const line = delivery.lineId;
    await expect(page.getByTestId(`sd-return-create-returnable-${line}`)).toHaveText(String(ORDERED - RETURNED));
    await expect(page.getByTestId(`sd-return-create-line-${line}`)).toContainText(`未開票（最多 ${ORDERED - INVOICED - UNBILLED}）`);
    await expect(page.getByTestId(`sd-return-create-line-${line}`)).toContainText(`最多 ${INVOICED - CREDITED}）`);
    await capture(page, '2.17-remaining-light-zh-Hant');

    await page.goto('/sal/returns');
    await page.getByTestId('sd-return-keyword').fill(saved!.documentNo);
    await page.getByTestId('sd-return-search').click();
    const listed = page.getByTestId(`sd-return-row-${saved!.id}`);
    await expect(listed).toContainText(saved!.documentNo);
    await expect(listed).toContainText(order.documentNo);
    await expect(listed).toContainText(`${GROSS}.00`);
    await expect(page.locator('[data-testid^="sd-return-row-"]')).toHaveCount(1);
    await dark(page);
    await capture(page, '2.16-list-dark-zh-Hant');
  });

  test('FI-SRC-REAL-001 interface 6.31 agrees with committed SD, FI, AR and GL SQL rows', async ({ page }) => {
    const ret = savedReturns(tw01, order.id)[0]!;
    expect(ret.fiCreditNoteId, 'the prior return posted a FI credit source').toBeTruthy();
    const fiCredit = creditSource(tw01, ret.fiCreditNoteId!)!;
    const fiInvoice = sql<{ id: number; journalId: number; sourceId: number; baseGross: string }>(`SELECT id,
      gl_journal_id AS journalId, source_id AS sourceId, CONVERT(varchar(40), base_gross_amount) AS baseGross
      FROM ar_sales_source WHERE company_id = ${tw01} AND source_type = 'Invoice' AND source_id = ${invoice.id}`)[0]!;
    const creditJournal = journal(tw01, fiCredit.journalId)!;
    expect(creditJournal.status).toBe('Posted');
    expect(fiCredit.sourceId).toBe(ret.id);
    expect(fiCredit.baseGross).toBe(four(CREDIT_GROSS));
    expect(openReceivable(tw01, invoice.receivableId)).toBe(four(INVOICE_GROSS - CREDIT_GROSS));
    const allocations = creditAllocations(tw01, ret.fiCreditNoteId!);
    expect(allocations).toEqual([{ receivableId: invoice.receivableId, invoiceLineId: invoice.lineId,
      quantity: six(CREDITED), baseGross: four(CREDIT_GROSS) }]);

    const api = apis.admin!;
    const byKey = await json(api.get(`${root}/ar/sales-sources/by-key/Return/${ret.id}`));
    expect(byKey.source).toMatchObject({ id: ret.fiCreditNoteId, sourceId: ret.id, sourceType: 'Return',
      glJournalId: fiCredit.journalId });
    expect(byKey.allocations).toHaveLength(1);
    await login(page, 'admin'); await page.goto('/ar/sales-sources');
    await page.getByTestId('fi-source-customer').fill(String(customer.id));
    await page.getByTestId('fi-source-search').click();
    await expect(page.getByTestId(`fi-source-row-${fiInvoice.id}`)).toContainText(`#${fiInvoice.journalId}`);
    await expect(page.getByTestId(`fi-source-row-${ret.fiCreditNoteId}`)).toContainText(`#${fiCredit.journalId}`);
    await page.getByTestId(`fi-source-view-${ret.fiCreditNoteId}`).click();
    const detail = page.getByTestId('fi-source-detail');
    await expect(detail).toContainText(`#${ret.id}`);
    await expect(detail).toContainText(`#${fiCredit.journalId}`);
    await expect(page.getByTestId('fi-source-allocations')).toContainText(`#${invoice.lineId}`);
    await expect(page.getByTestId('fi-source-allocations')).toContainText(String(CREDIT_GROSS));
    await captureFi(page, 'real-return-light-zh-Hant');
    await dark(page); await captureFi(page, 'real-return-dark-zh-Hant');
    await page.getByTestId('fi-source-lookup-id').fill(String(invoice.id));
    await page.getByTestId('fi-source-lookup-button').click();
    await expect(page.getByTestId('fi-source-detail')).toContainText(`#${fiInvoice.journalId}`);
    await expect(page.getByTestId('fi-source-open')).toContainText(String(INVOICE_GROSS - CREDIT_GROSS));
    await captureFi(page, 'real-invoice-dark-zh-Hant');
  });
});
