import { mkdir } from 'node:fs/promises';
import { expect, request, test, type APIRequestContext, type APIResponse, type Page } from '@playwright/test';
import { realApiLogin, type RealApiTokens } from '../support/real-api';
import { companyId, sql } from '../support/real-api-sql';

// WU-SD-007b3b (SD-INV-REAL-001–003, ADR-183): Sales Invoice Issue 2.15 and View 2.14 against the running API and
// SQL Server. sales picks a fresh Approved order on 2.15, issues part of its Posted delivery line, and the detail on
// 2.14 is compared with what SQL Server saved: sal_invoice / sal_invoice_line, the FI receivable (ar_posted_receivable),
// the FI sales source (ar_sales_source) and the Posted GL journal (gl_journal / gl_journal_line, accounts from the
// TW01 sales invoice account map). Resending the same request key through the API returns 200 and posts nothing again;
// a changed payload under that key is 409.
// Arrange (admin and sales, through the MD / FI / INV / SD APIs, never SQL writes): a fresh unit, item, warehouse, 5%
// output tax code, approved customer, a TWD credit limit, opening stock, an Approved TWD order and a Posted delivery.
// The SAL.ORDER and SAL.DELIVERY approval flows are switched off for the arrange step when active (so submit posts in
// the same request, ADR-144) and put back as they were in afterAll.
// Run: & ./tools/scripts/Invoke-RealApiE2E.ps1 -Specs specs/real-api-sales-invoice.spec
const apiUrl = process.env['E2E_API_URL'];
const root = '/api/v1';
const FLOWS = ['SAL.ORDER', 'SAL.DELIVERY'] as const;
const ORDERED = 10;
const PRICE = 100;
const ISSUED = 4;
// 4 × 100 = 400 net, 5% tax 20, gross 420 (TWD order in a TWD company: rate 1, original = base).
const NET = ISSUED * PRICE;
const TAX = NET * 0.05;
const GROSS = NET + TAX;

type User = 'sales' | 'admin';
type Flow = { id: number; name: string; documentType: string; allowSelfApproval: boolean; isActive: boolean;
  steps: unknown[]; version: number };
type SavedInvoice = { id: number; documentNo: string; requestKey: string; customerId: number; orderId: number;
  businessDate: string; currencyCode: string; baseCurrencyCode: string; exchangeRate: string; status: string;
  hasPostedAt: number; fiSalesSourceId: number; fiReceivableId: number; fiJournalId: number; originalNet: string;
  originalTax: string; originalGross: string; baseNet: string; baseTax: string; baseGross: string; createdBy: number };
type SavedInvoiceLine = { id: number; lineNo: number; deliveryId: number; deliveryLineId: number; orderLineId: number;
  itemId: number; taxRatePercent: string; quantity: string; originalNet: string; originalTax: string;
  originalGross: string; baseGross: string };
type Map = { receivable: string; revenue: string; outputTax: string };

const savedInvoices = (company: number, order: number) => sql<SavedInvoice>(`SELECT id, document_no AS documentNo,
  request_key AS requestKey, customer_id AS customerId, order_id AS orderId,
  CONVERT(char(10), business_date, 23) AS businessDate, currency_code AS currencyCode,
  base_currency_code AS baseCurrencyCode, CONVERT(varchar(40), exchange_rate) AS exchangeRate, status,
  CASE WHEN posted_at IS NULL THEN 0 ELSE 1 END AS hasPostedAt, fi_sales_source_id AS fiSalesSourceId,
  fi_receivable_id AS fiReceivableId, fi_journal_id AS fiJournalId, CONVERT(varchar(40), original_net) AS originalNet,
  CONVERT(varchar(40), original_tax) AS originalTax, CONVERT(varchar(40), original_gross) AS originalGross,
  CONVERT(varchar(40), base_net) AS baseNet, CONVERT(varchar(40), base_tax) AS baseTax,
  CONVERT(varchar(40), base_gross) AS baseGross, created_by AS createdBy
  FROM sal_invoice WHERE company_id = ${company} AND order_id = ${order} ORDER BY id`);
const savedLines = (company: number, invoice: number) => sql<SavedInvoiceLine>(`SELECT id, line_no AS [lineNo],
  delivery_id AS deliveryId, delivery_line_id AS deliveryLineId, order_line_id AS orderLineId, item_id AS itemId,
  CONVERT(varchar(40), tax_rate_percent) AS taxRatePercent, CONVERT(varchar(40), quantity) AS quantity,
  CONVERT(varchar(40), original_net) AS originalNet, CONVERT(varchar(40), original_tax) AS originalTax,
  CONVERT(varchar(40), original_gross) AS originalGross, CONVERT(varchar(40), base_gross) AS baseGross
  FROM sal_invoice_line WHERE company_id = ${company} AND invoice_id = ${invoice} ORDER BY line_no`);
const receivable = (company: number, id: number) => sql<{ customerId: number; sourceType: string; sourceId: number;
  baseCurrency: string; original: string; open: string; postingDate: string }>(`SELECT customer_id AS customerId,
  source_type AS sourceType, source_id AS sourceId, base_currency AS baseCurrency,
  CONVERT(varchar(40), original_base_amount) AS original, CONVERT(varchar(40), open_base_amount) AS [open],
  CONVERT(char(10), posting_date, 23) AS postingDate FROM ar_posted_receivable WHERE company_id = ${company} AND id = ${id}`)[0];
const salesSource = (company: number, id: number) => sql<{ sourceType: string; sourceId: number; journalId: number;
  net: string; tax: string; gross: string; baseGross: string }>(`SELECT source_type AS sourceType, source_id AS sourceId,
  gl_journal_id AS journalId, CONVERT(varchar(40), net_amount) AS net, CONVERT(varchar(40), tax_amount) AS tax,
  CONVERT(varchar(40), gross_amount) AS gross, CONVERT(varchar(40), base_gross_amount) AS baseGross
  FROM ar_sales_source WHERE company_id = ${company} AND id = ${id}`)[0];
const journal = (company: number, id: number) => sql<{ status: string; businessDate: string; totalDebit: string;
  totalCredit: string }>(`SELECT status, CONVERT(char(10), business_date, 23) AS businessDate,
  CONVERT(varchar(40), total_debit) AS totalDebit, CONVERT(varchar(40), total_credit) AS totalCredit
  FROM gl_journal WHERE company_id = ${company} AND id = ${id}`)[0];
const journalLines = (company: number, id: number) => sql<{ accountCode: string; debit: string; credit: string }>(`SELECT
  account_code AS accountCode, CONVERT(varchar(40), debit) AS debit, CONVERT(varchar(40), credit) AS credit
  FROM gl_journal_line WHERE company_id = ${company} AND journal_id = ${id} ORDER BY debit DESC, account_code`);
/** Rows FI wrote for SD invoices of this customer: a replay must not add any. */
const fiCounts = (company: number, customer: number) => sql<{ sources: number; receivables: number }>(`SELECT
  (SELECT COUNT(*) FROM ar_sales_source WHERE company_id = ${company} AND customer_id = ${customer}) AS sources,
  (SELECT COUNT(*) FROM ar_posted_receivable WHERE company_id = ${company} AND customer_id = ${customer}) AS receivables`)[0];
const accountMap = (company: number) => sql<Map>(`SELECT receivable_account_code AS receivable,
  revenue_account_code AS revenue, output_tax_account_code AS outputTax
  FROM gl_sales_invoice_account_map WHERE company_id = ${company}`)[0];

const pad = (n: number) => String(n).padStart(2, '0');
const isoDate = (d: Date) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
const today = () => isoDate(new Date());
const inDays = (days: number) => { const d = new Date(); d.setDate(d.getDate() + days); return isoDate(d); };
const four = (value: number) => value.toFixed(4);

async function capture(page: Page, name: string): Promise<void> {
  const path = '../../tests/qa/SD/screenshots';
  await mkdir(path, { recursive: true });
  await page.screenshot({ path: `${path}/real-invoice-${name}.png`, fullPage: true, animations: 'disabled' });
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

test.describe('真實 API 銷售發票：2.15 開票 → 2.14 明細與 SQL／應收／總帳一致；同鍵重送不重複過帳', () => {
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
  let order: { id: number; documentNo: string; lineId: number };
  let delivery: { id: number; documentNo: string; lineId: number };
  let invoice: SavedInvoice | undefined;
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
      JOIN sys_role r ON r.id = rp.role_id WHERE r.code = 'SALES' AND rp.permission_code LIKE 'SAL.INVOICE.%'`).map(x => x.code);
    expect(grants.sort(), 'SALES holds SAL.INVOICE.ISSUE and VIEW: run --seed').toEqual(['SAL.INVOICE.ISSUE', 'SAL.INVOICE.VIEW']);
    expect(accountMap(tw01), 'TW01 sales invoice account map: run --seed (ADR-112)').toBeDefined();

    const admin = apis.admin!;
    const sales = apis.sales!;
    // Approval flows off for the arrange step only; afterAll puts each back exactly as it was.
    const flows = await json(admin.get(`${root}/sys/approval-flows?pageSize=100`));
    for (const type of FLOWS) {
      const summary = flows.items.find((flow: Flow) => flow.documentType === type && flow.isActive);
      if (!summary) continue;
      const flow: Flow = await json(admin.get(`${root}/sys/approval-flows/${summary.id}`));
      await json(admin.put(`${root}/sys/approval-flows/${flow.id}`, { data: { name: flow.name,
        allowSelfApproval: flow.allowSelfApproval, isActive: false, steps: flow.steps, version: flow.version } }));
      suspended.push(flow);
    }

    const uom = await json(admin.post(`${root}/md/uoms`, { data: { code: `E2EIU${run}`, name: `E2E 發票個 ${run}`, decimalPlaces: 0 } }), 201);
    item = await json(admin.post(`${root}/md/items`, { data: { code: `E2EIP${run}`, name: `E2E 發票物料 ${run}`,
      specification: null, baseUomId: uom.id, brandId: null, categoryId: null } }), 201);
    const warehouse = await json(admin.post(`${root}/md/warehouses`, { data: { code: `E2EIW${run}`, name: `E2E 發票倉 ${run}`,
      address: null, contactName: null, phone: null } }), 201);
    const tax = await json(admin.post(`${root}/md/tax-codes`, { data: { code: `E2EIT${run}`, name: `E2E 發票銷項稅 ${run}`, taxType: 'Output' } }), 201);
    const rate = await json(admin.post(`${root}/md/tax-codes/${tax.id}/rates`, { data: { effectiveFrom: '2020-01-01', effectiveTo: null, ratePercent: 5 } }), 201);
    const created = await json(admin.post(`${root}/md/customers`, { data: { code: `E2EIC${run}`, name: `E2E 發票客戶 ${run}`,
      categoryId: null, defaultPaymentTermId: null, defaultSettlementMethodId: null } }), 201);
    await json(admin.post(`${root}/md/customers/${created.id}/submit`, { data: { version: created.version } }));
    const approved = sql<{ reviewStatus: string }>(`SELECT review_status AS reviewStatus FROM md_customer WHERE id = ${created.id}`)[0];
    expect(approved?.reviewStatus, 'MD.CUSTOMER must have no active approval flow for this spec').toBe('Approved');
    customer = created;
    await json(admin.post(`${root}/ar/credit-limits`, { data: { customerId: customer.id,
      effectiveFrom: inDays(-30), effectiveTo: inDays(365), amount: 1_000_000 } }), 201);
    await json(admin.post(`${root}/inv/opening-stocks`, { data: { requestKey: `e2e-inv-${run}`, openingDate: inDays(-1),
      remark: `E2E 發票期初 ${run}`, lines: [{ lineNo: 1, itemId: item.id, warehouseId: warehouse.id, locationId: null,
        quantity: ORDERED, unitCost: 50 }] } }), 201);

    // sales: Approved order → Posted delivery (both submits complete in the same request without an active flow).
    const draft = await json(sales.post(`${root}/sal/orders`, { data: { customerId: customer.id, businessDate: today(),
      currencyCode: 'TWD', remark: null, lines: [{ lineNo: 1, itemId: item.id, quantity: ORDERED, unitPrice: PRICE,
        discountRate: 0, taxRateId: rate.id, overrideReason: `E2E 無價格規則手動定價 ${run}` }] } }), 201);
    const submitted = await json(sales.post(`${root}/sal/orders/${draft.id}/submit`, { data: { version: draft.version } }));
    expect(submitted.order.status, 'the order is Approved on submit (no active SAL.ORDER flow)').toBe('Approved');
    order = { id: draft.id, documentNo: draft.documentNo, lineId: draft.lines[0].id };
    const outbound = await json(sales.post(`${root}/sal/deliveries`, { data: { orderId: order.id, businessDate: today(),
      lines: [{ lineNo: 1, orderLineId: order.lineId, warehouseId: warehouse.id, locationId: null, quantity: ORDERED }] } }), 201);
    const posted = await json(sales.post(`${root}/sal/deliveries/${outbound.id}/submit`, { data: { version: outbound.version } }));
    expect(posted.status, 'the delivery posts on submit (no active SAL.DELIVERY flow)').toBe('Posted');
    delivery = { id: outbound.id, documentNo: outbound.documentNo, lineId: outbound.lines[0].id };
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

  async function chooseOrder(page: Page): Promise<void> {
    await page.getByTestId('sd-invoice-issue-order-pick').click();
    await expect(page.getByTestId('erp-select-dialog')).toBeVisible();
    await page.getByTestId('erp-select-keyword').fill(order.documentNo);
    await page.getByTestId('erp-select-search').click();
    await page.getByTestId(`erp-select-row-${order.id}`).click();
    await page.getByTestId('erp-select-confirm').click();
    await expect(page.getByTestId('sd-invoice-issue-order')).toHaveText(`${order.documentNo} · ${customer.code} · ${customer.name}`);
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

  test('SD-INV-REAL-001 sales issues part of a Posted delivery on 2.15; 2.14 shows what SQL, AR and GL saved', async ({ page }) => {
    await login(page, 'sales');
    await page.goto('/sal/invoices');
    await page.getByTestId('sd-invoice-new').click();
    await expect(page).toHaveURL(/\/sal\/invoices\/new$/);
    await chooseOrder(page);
    const line = delivery.lineId;
    await expect(page.getByTestId(`sd-invoice-issue-line-${line}`)).toContainText(delivery.documentNo);
    await expect(page.getByTestId(`sd-invoice-issue-remaining-${line}`)).toHaveText(String(ORDERED));
    await expect(page.getByTestId('sd-invoice-issue-rate')).toHaveText('1');
    await expect(page.getByTestId('sd-invoice-issue-count')).toHaveText('已選 1 / 1 行');
    // The server sends 10.000000; the input is prefilled the way the balance column shows it.
    await expect(page.getByTestId(`sd-invoice-issue-quantity-${line}`)).toHaveValue(String(ORDERED));
    await page.getByTestId('sd-invoice-issue-date').fill(today());
    await page.getByTestId(`sd-invoice-issue-quantity-${line}`).fill(String(ISSUED));
    await capture(page, '2.15-ready-light-zh-Hant');
    expect(savedInvoices(tw01, order.id), 'nothing issued before submit').toEqual([]);
    await page.getByTestId('sd-invoice-issue-submit').click();
    await expect(page).toHaveURL(/\/sal\/invoices$/);

    const invoices = savedInvoices(tw01, order.id);
    expect(invoices, 'exactly one invoice for the fresh order').toHaveLength(1);
    invoice = invoices[0]!;
    expect(invoice).toMatchObject({ customerId: customer.id, orderId: order.id, businessDate: today(), currencyCode: 'TWD',
      baseCurrencyCode: 'TWD', status: 'Posted', hasPostedAt: 1, originalNet: four(NET), originalTax: four(TAX),
      originalGross: four(GROSS), baseNet: four(NET), baseTax: four(TAX), baseGross: four(GROSS), createdBy: salesUserId });
    expect(Number(invoice.exchangeRate)).toBe(1);
    const lines = savedLines(tw01, invoice.id);
    expect(lines).toEqual([expect.objectContaining({ lineNo: 1, deliveryId: delivery.id, deliveryLineId: line,
      orderLineId: order.lineId, itemId: item.id, quantity: ISSUED.toFixed(6), originalNet: four(NET),
      originalTax: four(TAX), originalGross: four(GROSS), baseGross: four(GROSS) })]);
    expect(Number(lines[0]!.taxRatePercent)).toBe(5);

    // FI: one open receivable and one sales source for (SD, Invoice, id), and a balanced Posted journal on the map.
    expect(receivable(tw01, invoice.fiReceivableId)).toEqual({ customerId: customer.id, sourceType: 'Invoice',
      sourceId: invoice.id, baseCurrency: 'TWD', original: four(GROSS), open: four(GROSS), postingDate: today() });
    expect(salesSource(tw01, invoice.fiSalesSourceId)).toMatchObject({ sourceType: 'Invoice', sourceId: invoice.id,
      journalId: invoice.fiJournalId, net: four(NET), tax: four(TAX), gross: four(GROSS), baseGross: four(GROSS) });
    expect(journal(tw01, invoice.fiJournalId)).toEqual({ status: 'Posted', businessDate: today(),
      totalDebit: four(GROSS), totalCredit: four(GROSS) });
    const map = accountMap(tw01)!;
    expect(journalLines(tw01, invoice.fiJournalId)).toEqual([
      { accountCode: map.receivable, debit: four(GROSS), credit: four(0) },
      ...[{ accountCode: map.revenue, debit: four(0), credit: four(NET) },
        { accountCode: map.outputTax, debit: four(0), credit: four(TAX) }].sort((a, b) => a.accountCode.localeCompare(b.accountCode))]);

    // 2.14 opens the saved detail: every figure and reference equals the SQL row.
    await expect(page.getByTestId('sd-invoice-detail-title')).toHaveText(invoice.documentNo);
    await expect(page.getByTestId('sd-invoice-detail-title')).toBeFocused();
    await expect(page.getByTestId('sd-invoice-detail-status')).toHaveText('已過帳');
    await expect(page.getByTestId('sd-invoice-detail-customer')).toHaveText(`${customer.code} · ${customer.name}`);
    await expect(page.getByTestId('sd-invoice-detail-rate')).toHaveText('1');
    await expect(page.getByTestId('sd-invoice-fi-journal')).toHaveText(String(invoice.fiJournalId));
    await expect(page.getByTestId('sd-invoice-fi-receivable')).toHaveText(String(invoice.fiReceivableId));
    await expect(page.getByTestId('sd-invoice-fi-source')).toHaveText(String(invoice.fiSalesSourceId));
    const totals = page.getByTestId('sd-invoice-totals');
    await expect(totals).toContainText(`TWD ${GROSS}.00`);
    await expect(totals).toContainText(`${NET}.00`);
    await expect(totals).toContainText(`${TAX}.00`);
    const row = page.getByTestId(`sd-invoice-line-${lines[0]!.id}`);
    await expect(row).toContainText(delivery.documentNo);
    await expect(row).toContainText(`${item.code} · ${item.name}`);
    await expect(row.locator('td').nth(4)).toHaveText(String(ISSUED));
    await expect(row.locator('td').nth(5)).toHaveText('5%');
    await expect(row.locator('td').nth(6)).toHaveText(`${NET}.00`);
    await expect(row.locator('td').nth(7)).toHaveText(`${TAX}.00`);
    await expect(row.locator('td').nth(8)).toHaveText(`${GROSS}.00`);
    await expect(row.locator('td').nth(9)).toHaveText(`${GROSS}.00`);
    await capture(page, '2.14-detail-light-zh-Hant');
    await dark(page);
    await capture(page, '2.14-detail-dark-zh-Hant');
  });

  test('SD-INV-REAL-002 the same request key replays 200 without posting again; a changed payload is 409', async () => {
    expect(invoice, 'SD-INV-REAL-001 must pass first').toBeDefined();
    const before = fiCounts(tw01, customer.id);
    expect(before).toEqual({ sources: 1, receivables: 1 });
    const body = { requestKey: invoice!.requestKey, businessDate: invoice!.businessDate,
      lines: [{ lineNo: 1, deliveryLineId: delivery.lineId, quantity: ISSUED }] };
    const replay = await json(apis.sales!.post(`${root}/sal/invoices`, { data: body }));
    expect(replay).toMatchObject({ id: invoice!.id, documentNo: invoice!.documentNo, fiJournalId: invoice!.fiJournalId,
      fiReceivableId: invoice!.fiReceivableId, fiSalesSourceId: invoice!.fiSalesSourceId });
    const conflict = await apis.sales!.post(`${root}/sal/invoices`, { data: { ...body,
      lines: [{ lineNo: 1, deliveryLineId: delivery.lineId, quantity: ISSUED - 1 }] } });
    expect(conflict.status()).toBe(409);
    expect((await conflict.json()).errorCode).toBe('SAL.INVOICE.REQUEST_CONFLICT');
    expect(savedInvoices(tw01, order.id), 'still one invoice').toHaveLength(1);
    expect(fiCounts(tw01, customer.id), 'replay and conflict post nothing to FI').toEqual(before);
    expect(receivable(tw01, invoice!.fiReceivableId)?.open).toBe(four(GROSS));
  });

  test('SD-INV-REAL-003 2.15 shows the remaining balance after the invoice and 2.14 lists it for the customer', async ({ page }) => {
    expect(invoice, 'SD-INV-REAL-001 must pass first').toBeDefined();
    await login(page, 'sales');
    await page.goto('/sal/invoices/new');
    await chooseOrder(page);
    const line = delivery.lineId;
    await expect(page.getByTestId(`sd-invoice-issue-line-${line}`)).toContainText(String(ISSUED));
    await expect(page.getByTestId(`sd-invoice-issue-remaining-${line}`)).toHaveText(String(ORDERED - ISSUED));
    await expect(page.getByTestId(`sd-invoice-issue-quantity-${line}`)).toHaveValue(String(ORDERED - ISSUED));
    await capture(page, '2.15-remaining-light-zh-Hant');

    await page.goto('/sal/invoices');
    await page.getByTestId('sd-invoice-keyword').fill(invoice!.documentNo);
    await page.getByTestId('sd-invoice-search').click();
    const listed = page.getByTestId(`sd-invoice-row-${invoice!.id}`);
    await expect(listed).toContainText(invoice!.documentNo);
    await expect(listed).toContainText(order.documentNo);
    await expect(listed).toContainText(`${GROSS}.00`);
    await expect(page.locator('[data-testid^="sd-invoice-row-"]')).toHaveCount(1);
    await dark(page);
    await capture(page, '2.14-list-dark-zh-Hant');
  });
});
