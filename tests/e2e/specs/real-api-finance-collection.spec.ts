import { mkdir } from 'node:fs/promises';
import { randomUUID } from 'node:crypto';
import { expect, request, test, type APIRequestContext, type APIResponse, type Page } from '@playwright/test';
import { realApiLogin, type RealApiTokens } from '../support/real-api';
import { companyId, sql } from '../support/real-api-sql';

// WU-FI-005c3b1 (FI-COLL-REAL-001–004, ADR-136): the sales collection screens (6.26 / 6.29–6.30) against the running
// API, SYS approval and SQL Server. Every screen value is compared with the row SQL Server saved (read back with sqlcmd).
// Arrange (admin, through the MD API, never SQL): a fresh approved customer and a TWD bank fund account on ledger 1102,
// so the totals of this run are not mixed with older data. The AR.COLLECTION approval flow is put back as it was.
// Settling against a posted receivable needs an AR source (SD-007a or an FI opening-balance feature): WU-FI-005c3b2.
// Run: & ./tools/scripts/Invoke-RealApiE2E.ps1 -Specs specs/real-api-finance-collection.spec
const apiUrl = process.env['E2E_API_URL'];
const root = '/api/v1';
const DOCUMENT_TYPE = 'AR.COLLECTION';

type SavedReceipt = { id: number; documentNo: string; customerId: number; bankAccountId: number; amount: string;
  status: string; currency: string; version: number; bankReference: string };
type SavedPosting = { amount: string; unappliedAmount: string; journalId: number; baseCurrency: string };
type Flow = { id: number; name: string; documentType: string; allowSelfApproval: boolean; isActive: boolean;
  steps: unknown[]; version: number };

/** The saved document row with the given bank reference in the company (amounts as exact `decimal(19,4)` text). */
function savedReceipt(company: number, reference: string): SavedReceipt | undefined {
  return sql<SavedReceipt>(`SELECT id, document_no AS documentNo, customer_id AS customerId, bank_account_id AS bankAccountId,
    CONVERT(varchar(40), amount) AS amount, status, currency, version, bank_reference AS bankReference
    FROM ar_collection_receipt_doc WHERE company_id = ${company} AND bank_reference = '${reference}' AND is_deleted = 0`)[0];
}

/** The posted unapplied-cash row FI wrote for the document (none until posted). */
function savedPosting(company: number, documentId: number): SavedPosting | undefined {
  return sql<SavedPosting>(`SELECT CONVERT(varchar(40), amount) AS amount, CONVERT(varchar(40), unapplied_amount) AS unappliedAmount,
    gl_journal_id AS journalId, base_currency AS baseCurrency FROM ar_collection_receipt
    WHERE company_id = ${company} AND source_id = ${documentId}`)[0];
}

/** Journal lines as `code:debit:credit`, in sequence order. */
function journalLines(company: number, journalId: number): string[] {
  return sql<{ line: string }>(`SELECT account_code + ':' + CONVERT(varchar(40), debit) + ':' + CONVERT(varchar(40), credit) AS line
    FROM gl_journal_line WHERE company_id = ${company} AND journal_id = ${journalId} ORDER BY sequence`).map(row => row.line);
}

const exact = (amount: number) => `${amount}.0000`;
const grouped = (amount: number) => amount.toLocaleString('en-US');
const today = () => { const d = new Date(); return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`; };

async function capture(page: Page, name: string): Promise<void> {
  const path = '../../tests/qa/FI/screenshots';
  await mkdir(path, { recursive: true });
  await page.screenshot({ path: `${path}/6.26-real-${name}.png`, fullPage: true, animations: 'disabled' });
}

async function json(response: Promise<APIResponse>, status = 200): Promise<any> {
  const value = await response;
  expect(value.status(), await value.text()).toBe(status);
  return value.json();
}

test.describe('真實 API 銷售收款（SQL 讀回雙斷言）', () => {
  test.describe.configure({ mode: 'serial' });
  test.skip(!apiUrl, '需要 E2E_API_URL：請用 tools/scripts/Invoke-RealApiE2E.ps1 執行');
  test.setTimeout(180_000);

  const run = `${Date.now() % 1_000_000_000}${Math.floor(Math.random() * 100)}`;
  const tokens: Partial<Record<'finance' | 'manager' | 'admin' | 'sales', RealApiTokens>> = {};
  const apis: Partial<Record<'finance' | 'manager' | 'admin' | 'sales', APIRequestContext>> = {};
  let tw01: number;
  let customer: { id: number; code: string; name: string };
  let fund: { id: number; code: string; name: string };
  const originalFlows = new Map<string, Flow | undefined>();
  const touchedFlows = new Set<string>();
  let postedReceipt: SavedReceipt | undefined;
  let consoleErrors: string[];
  let serverErrors: string[];

  test.beforeAll(async () => {
    test.setTimeout(300_000); // realApiLogin may wait out the login rate-limit window
    tw01 = companyId('TW01');
    for (const [user, password] of [['finance', 'Demo@2026'], ['manager', 'Demo@2026'], ['admin', 'az5911'],
      ['sales', 'Demo@2026']] as const) {
      tokens[user] = await realApiLogin(apiUrl!, user, password);
      apis[user] = await request.newContext({ baseURL: apiUrl, extraHTTPHeaders: {
        Authorization: `Bearer ${tokens[user]!.accessToken}`, 'X-Company-Id': String(tw01) } });
    }
    const admin = apis.admin!;
    const [base] = sql<{ id: number; code: string }>(`SELECT id, code FROM md_currency
      WHERE company_id = ${tw01} AND is_base = 1 AND is_deleted = 0`);
    expect(base?.code, 'TW01 base currency: run --seed (ADR-105)').toBe('TWD');
    const [ledger] = sql<{ id: number }>(`SELECT id FROM md_account WHERE company_id = ${tw01} AND code = '1102' AND is_deleted = 0`);
    expect(ledger, 'TW01 account 1102: run --seed (ADR-105)').toBeDefined();
    fund = await json(admin.post(`${root}/md/fund-accounts`, { data: { code: `E2EB${run}`, name: `E2E 台幣銀行 ${run}`,
      currencyId: base!.id, fundType: 'Bank', bankName: 'E2E Bank', bankAccountNo: run, ledgerAccountId: ledger!.id } }), 201);
    const created = await json(admin.post(`${root}/md/customers`, { data: { code: `E2EAR${run}`, name: `E2E 收款客戶 ${run}`,
      categoryId: null, defaultPaymentTermId: null, defaultSettlementMethodId: null } }), 201);
    await json(admin.post(`${root}/md/customers/${created.id}/submit`, { data: { version: created.version } }));
    const approved = sql<{ reviewStatus: string }>(`SELECT review_status AS reviewStatus FROM md_customer WHERE id = ${created.id}`)[0];
    expect(approved?.reviewStatus, 'MD.CUSTOMER must have no active approval flow for this spec').toBe('Approved');
    customer = created;
    const flows = await json(admin.get(`${root}/sys/approval-flows?pageSize=100`));
    for (const type of [DOCUMENT_TYPE, 'AR.SETTLEMENT', 'SAL.ORDER', 'SAL.DELIVERY']) {
      const summary: Flow | undefined = flows.items.find((flow: Flow) => flow.documentType === type);
      originalFlows.set(type, summary ? await json(admin.get(`${root}/sys/approval-flows/${summary.id}`)) : undefined);
    }
  });

  test.afterAll(async () => {
    const admin = apis.admin;
    try {
      if (admin) {
        for (const type of touchedFlows) {
        const flows = await json(admin.get(`${root}/sys/approval-flows?pageSize=100`));
        const now: Flow | undefined = flows.items.find((flow: Flow) => flow.documentType === type);
        if (now) {
          const current: Flow = await json(admin.get(`${root}/sys/approval-flows/${now.id}`));
          const originalFlow = originalFlows.get(type);
          const restore = originalFlow
            ? { name: originalFlow.name, allowSelfApproval: originalFlow.allowSelfApproval, isActive: originalFlow.isActive,
                steps: originalFlow.steps, version: current.version }
            : { name: current.name, allowSelfApproval: current.allowSelfApproval, isActive: false, steps: current.steps,
                version: current.version };
          await json(admin.put(`${root}/sys/approval-flows/${now.id}`, { data: restore }));
        }
        }
      }
    } finally {
      await Promise.all(Object.values(apis).map(api => api?.dispose()));
    }
  });

  /** Sets the AR.COLLECTION flow: no steps (inactive) or one step approved by `manager`, self-approval off. */
  async function setFlow(steps: 'none' | 'manager', documentType = DOCUMENT_TYPE): Promise<void> {
    const admin = apis.admin!;
    touchedFlows.add(documentType);
    const flows = await json(admin.get(`${root}/sys/approval-flows?pageSize=100`));
    const existing: Flow | undefined = flows.items.find((flow: Flow) => flow.documentType === documentType);
    if (steps === 'none') {
      if (!existing?.isActive) return;
      const current: Flow = await json(admin.get(`${root}/sys/approval-flows/${existing.id}`));
      await json(admin.put(`${root}/sys/approval-flows/${existing.id}`, { data: { name: current.name,
        allowSelfApproval: current.allowSelfApproval, isActive: false, steps: current.steps, version: current.version } }));
      return;
    }
    const users = await json(admin.get(`${root}/sys/users?pageSize=100`));
    const managerId = users.items.find((user: { username: string }) => user.username === 'manager')?.id;
    expect(managerId).toBeTruthy();
    const input = { name: `E2E ${documentType}`, allowSelfApproval: false,
      steps: [{ name: 'E2E 主管核准', approverType: 'User', roleId: null, userId: managerId, minAmount: null }] };
    if (existing) {
      const current: Flow = await json(admin.get(`${root}/sys/approval-flows/${existing.id}`));
      await json(admin.put(`${root}/sys/approval-flows/${existing.id}`, { data: { ...input, isActive: true, version: current.version } }));
    } else {
      await json(admin.post(`${root}/sys/approval-flows`, { data: { ...input, documentType } }), 201);
    }
  }

  async function login(page: Page): Promise<void> {
    await page.addInitScript(([key, value]) => localStorage.setItem(key, value),
      ['myerp.auth.session', JSON.stringify({ ...tokens.finance, username: 'finance' })]);
    await page.goto('/');
    await expect(page.getByTestId('home-title')).toBeVisible();
    await expect(page.getByTestId('layout-company-select')).toContainText('TW01');
  }

  /** Picks one row in the shared select dialog after searching `keyword`. */
  async function pick(page: Page, opener: string, keyword: string, rowId: number): Promise<void> {
    await page.getByTestId(opener).click();
    await expect(page.getByTestId('erp-select-dialog')).toBeVisible();
    await page.getByTestId('erp-select-keyword').fill(keyword);
    await page.getByTestId('erp-select-search').click();
    await page.getByTestId(`erp-select-row-${rowId}`).click();
    await page.getByTestId('erp-select-confirm').click();
  }

  /** Filters the receipt list to one bank reference so the row is on the first page whatever else is saved. */
  async function showOnly(page: Page, reference: string): Promise<void> {
    await page.getByTestId('fi-receipt-keyword').fill(reference);
    await page.getByTestId('fi-receipt-search').click();
  }

  test.beforeEach(({ page }) => {
    consoleErrors = []; serverErrors = [];
    page.on('console', message => { if (message.type() === 'error') consoleErrors.push(message.text()); });
    page.on('pageerror', error => consoleErrors.push(error.message));
    page.on('response', response => { if (response.status() >= 500) serverErrors.push(`${response.status()} ${response.url()}`); });
  });

  test.afterEach(async ({ page }) => {
    expect(await page.locator('body').innerText()).not.toMatch(/\b(fi|common|layout|errors)\.[a-zA-Z][A-Za-z_]+\.[A-Za-z][A-Za-z_.]+\b/);
    expect(serverErrors).toEqual([]);
    expect(consoleErrors).toEqual([]);
  });

  test('FI-COLL-REAL-001 finance creates a collection on screen; the draft saved in SQL matches every field', async ({ page }) => {
    await setFlow('none');
    const amount = 10_000 + Math.floor(Math.random() * 9_000);
    const reference = `E2E-${run}-A`;
    await login(page);
    await page.getByTestId('layout-menu-fi').click();
    await page.getByTestId('layout-menu-fi-sales-collections').click();
    await expect(page).toHaveURL(/\/ar\/sales-collections$/);
    await page.getByTestId('fi-receipt-new').click();
    await pick(page, 'fi-receipt-customer-pick', customer.code, customer.id);
    await expect(page.getByTestId('fi-receipt-customer')).toHaveText(`${customer.code} · ${customer.name}`);
    await pick(page, 'fi-receipt-fund-pick', fund.code, fund.id);
    await page.getByTestId('fi-receipt-date').fill(today());
    await page.getByTestId('fi-receipt-amount').fill(grouped(amount));
    await page.getByTestId('fi-receipt-reference').fill(`  ${reference}  `);
    await page.getByTestId('fi-receipt-save').click();
    await expect(page).toHaveURL(/\/ar\/sales-collections$/);

    const saved = savedReceipt(tw01, reference);
    expect(saved, 'one draft saved with the trimmed reference').toBeDefined();
    expect(saved).toMatchObject({ customerId: customer.id, bankAccountId: fund.id, amount: exact(amount), status: 'Draft',
      currency: 'TWD', bankReference: reference });
    // AR.COLLECTION number rule seeded by ADR-135: RC + year-month + 4 digits.
    expect(saved!.documentNo).toMatch(new RegExp(`^RC${today().slice(0, 4)}${today().slice(5, 7)}\\d{4}$`));
    expect(savedPosting(tw01, saved!.id), 'a draft has no AR or GL effect').toBeUndefined();
    const notice = page.getByTestId('fi-receipt-notice');
    await expect(notice).toHaveAttribute('data-type', 'success');
    await expect(notice).toContainText(`已儲存收款單 ${saved!.documentNo}（草稿）`);
    await showOnly(page, reference);
    await expect(page.getByTestId(`fi-receipt-status-${saved!.id}`)).toHaveText('草稿');
    await expect(page.getByTestId(`fi-receipt-amount-${saved!.id}`)).toHaveText(`${grouped(amount)} TWD`);
    await expect(page.getByTestId(`fi-receipt-unapplied-${saved!.id}`)).toHaveText('—');
    await capture(page, 'draft-light-zh-Hant');
    postedReceipt = saved;
  });

  test('FI-COLL-REAL-002 with no approval steps submit posts at once: unapplied = amount on screen and in AR / GL', async ({ page }) => {
    expect(postedReceipt, 'FI-COLL-REAL-001 must pass first').toBeDefined();
    const draft = postedReceipt!;
    const amount = Number(draft.amount);
    await login(page);
    await page.goto('/ar/sales-collections');
    await showOnly(page, draft.bankReference);
    await page.getByTestId(`fi-receipt-submit-${draft.id}`).click();
    const notice = page.getByTestId('fi-receipt-notice');
    await expect(notice).toContainText(`已核准並過帳：未核銷款 ${grouped(amount)} TWD`);
    await expect(notice).toContainText('尚未核銷任何發票');
    await expect(page.getByTestId(`fi-receipt-status-${draft.id}`)).toHaveText('已過帳');
    await expect(page.getByTestId(`fi-receipt-unapplied-${draft.id}`)).toHaveText(grouped(amount));

    expect(savedReceipt(tw01, draft.bankReference)).toMatchObject({ status: 'Posted' });
    const posting = savedPosting(tw01, draft.id);
    expect(posting).toMatchObject({ amount: exact(amount), unappliedAmount: exact(amount), baseCurrency: 'TWD' });
    // Dr the fund account's ledger 1102; Cr the seeded unapplied-cash account of the collection map (ADR-123).
    const [map] = sql<{ code: string }>(`SELECT unapplied_cash_account_code AS code FROM gl_sales_collection_account_map WHERE company_id = ${tw01}`);
    expect(journalLines(tw01, posting!.journalId)).toEqual([`1102:${exact(amount)}:0.0000`, `${map!.code}:0.0000:${exact(amount)}`]);
    await page.getByTestId(`fi-receipt-view-${draft.id}`).click();
    await expect(page.getByTestId('fi-receipt-detail-journal')).toHaveText(`#${posting!.journalId}`);
    await capture(page, 'posted-light-zh-Hant');
  });

  test('FI-COLL-REAL-003 with one SYS step submit only waits; the manager approval posts it', async ({ page }) => {
    await setFlow('manager');
    const amount = 20_000 + Math.floor(Math.random() * 9_000);
    const reference = `E2E-${run}-B`;
    const draft = await json(apis.finance!.post(`${root}/ar/sales-collections`, { data: { customerId: customer.id,
      businessDate: today(), amount, bankAccountId: fund.id, bankReference: reference, remark: null } }), 201);
    await login(page);
    await page.goto('/ar/sales-collections');
    await showOnly(page, reference);
    await page.getByTestId(`fi-receipt-submit-${draft.id}`).click();
    await expect(page.getByTestId('fi-receipt-notice')).toContainText('已送審，等待審批；尚未過帳');
    await expect(page.getByTestId(`fi-receipt-status-${draft.id}`)).toHaveText('審批中');
    await expect(page.getByTestId(`fi-receipt-unapplied-${draft.id}`)).toHaveText('—');
    expect(savedReceipt(tw01, reference)).toMatchObject({ status: 'Pending', amount: exact(amount) });
    expect(savedPosting(tw01, draft.id), 'pending approval has no AR or GL effect').toBeUndefined();
    await capture(page, 'pending-light-zh-Hant');

    const [instance] = sql<{ id: number }>(`SELECT TOP 1 id FROM sys_approval_instance WHERE company_id = ${tw01}
      AND document_type = '${DOCUMENT_TYPE}' AND document_id = ${draft.id} ORDER BY id DESC`);
    expect(instance).toBeDefined();
    const pending = await json(apis.manager!.get(`${root}/sys/approvals/${instance!.id}`));
    const approved = await json(apis.manager!.post(`${root}/sys/approvals/${instance!.id}/approve`, {
      data: { comment: 'E2E 核准收款', version: pending.version } }));
    expect(approved.status).toBe('Approved');

    await page.getByTestId('fi-receipt-search').click();
    await expect(page.getByTestId(`fi-receipt-status-${draft.id}`)).toHaveText('已過帳');
    await expect(page.getByTestId(`fi-receipt-unapplied-${draft.id}`)).toHaveText(grouped(amount));
    expect(savedReceipt(tw01, reference)).toMatchObject({ status: 'Posted' });
    expect(savedPosting(tw01, draft.id)).toMatchObject({ amount: exact(amount), unappliedAmount: exact(amount) });
  });

  test('FI-COLL-REAL-004 settlement form: the posted receipt is offered with its customer; open receivables match SQL', async ({ page }) => {
    expect(postedReceipt, 'FI-COLL-REAL-001 must pass first').toBeDefined();
    const receipt = postedReceipt!;
    await login(page);
    await page.goto('/ar/collection-settlements/new');
    await pick(page, 'fi-settle-receipt-pick', receipt.documentNo, receipt.id);
    await expect(page.getByTestId('fi-settle-form-receipt')).toContainText(receipt.documentNo);
    await expect(page.getByTestId('fi-settle-form-customer')).toContainText(customer.code);
    await expect(page.getByTestId('fi-settle-form-unapplied')).toContainText(grouped(Number(receipt.amount)));
    // A fresh customer has no posted receivable: the picker says so, as SQL does (settling one is WU-FI-005c3b2).
    const [open] = sql<{ n: number }>(`SELECT COUNT(*) AS n FROM ar_posted_receivable WHERE company_id = ${tw01}
      AND customer_id = ${customer.id} AND open_base_amount > 0`);
    expect(open!.n).toBe(0);
    await page.getByTestId('fi-settle-add-lines').click();
    await expect(page.getByTestId('erp-select-dialog')).toBeVisible();
    await expect(page.getByTestId('erp-select-table')).toContainText('暫無資料');
    await expect(page.getByTestId('erp-select-confirm')).toBeDisabled();
    await capture(page, 'settlement-no-open-light-zh-Hant');
    await page.getByTestId('erp-select-cancel').click();
    await expect(page.getByTestId('fi-settle-form-no-lines')).toBeVisible();
  });

  test('FI-COLL-REAL-005 SD invoice becomes a real AR source; final SYS approval reduces both balances', async ({ page }) => {
    expect(postedReceipt, 'FI-COLL-REAL-002 must post the receipt first').toBeDefined();
    const receipt = postedReceipt!;
    const admin = apis.admin!;
    const sales = apis.sales!;
    for (const type of ['SAL.ORDER', 'SAL.DELIVERY']) await setFlow('none', type);
    await setFlow('manager', 'AR.SETTLEMENT');

    // Arrange through published module APIs. FI must never be given an ad-hoc receivable row.
    const uom = await json(admin.post(`${root}/md/uoms`, { data: { code: `E2ESU${run}`, name: `E2E 核銷單位 ${run}`,
      decimalPlaces: 0 } }), 201);
    const item = await json(admin.post(`${root}/md/items`, { data: { code: `E2ESI${run}`, name: `E2E 核銷品項 ${run}`,
      specification: null, baseUomId: uom.id, brandId: null, categoryId: null } }), 201);
    const warehouse = await json(admin.post(`${root}/md/warehouses`, { data: { code: `E2ESW${run}`,
      name: `E2E 核銷倉 ${run}`, address: null, contactName: null, phone: null } }), 201);
    const tax = await json(admin.post(`${root}/md/tax-codes`, { data: { code: `E2EST${run}`, name: `E2E 核銷銷項稅 ${run}`,
      taxType: 'Output' } }), 201);
    const rate = await json(admin.post(`${root}/md/tax-codes/${tax.id}/rates`, { data: {
      effectiveFrom: '2020-01-01', effectiveTo: null, ratePercent: 5 } }), 201);
    await json(admin.post(`${root}/ar/credit-limits`, { data: { customerId: customer.id,
      effectiveFrom: '2020-01-01', effectiveTo: '2099-12-31', amount: 1_000_000 } }), 201);
    await json(admin.post(`${root}/inv/opening-stocks`, { data: { requestKey: randomUUID(), openingDate: today(),
      remark: `E2E 核銷期初 ${run}`, lines: [{ lineNo: 1, itemId: item.id, warehouseId: warehouse.id,
        locationId: null, quantity: 5, unitCost: 50 }] } }), 201);
    const order = await json(sales.post(`${root}/sal/orders`, { data: { customerId: customer.id,
      businessDate: today(), currencyCode: 'TWD', remark: null, lines: [{ lineNo: 1, itemId: item.id,
        quantity: 5, unitPrice: 100, discountRate: 0, taxRateId: rate.id,
        overrideReason: `E2E 核銷手動定價 ${run}` }] } }), 201);
    const orderDecision = await json(sales.post(`${root}/sal/orders/${order.id}/submit`, { data: { version: order.version } }));
    expect(orderDecision.order.status).toBe('Approved');
    const delivery = await json(sales.post(`${root}/sal/deliveries`, { data: { orderId: order.id,
      businessDate: today(), lines: [{ lineNo: 1, orderLineId: order.lines[0].id, warehouseId: warehouse.id,
        locationId: null, quantity: 5 }] } }), 201);
    const delivered = await json(sales.post(`${root}/sal/deliveries/${delivery.id}/submit`, {
      data: { version: delivery.version } }));
    expect(delivered.status).toBe('Posted');
    const invoice = await json(sales.post(`${root}/sal/invoices`, { data: { requestKey: randomUUID(),
      businessDate: today(), lines: [{ lineNo: 1, deliveryLineId: delivery.lines[0].id, quantity: 4 }] } }), 201);
    expect(invoice.status).toBe('Posted');
    const receivableId: number = invoice.fiReceivableId;
    const open = () => sql<{ amount: string }>(`SELECT CONVERT(varchar(40), open_base_amount) AS amount
      FROM ar_posted_receivable WHERE company_id = ${tw01} AND id = ${receivableId}`)[0]?.amount;
    expect(open()).toBe('420.0000');
    expect(savedPosting(tw01, receipt.id)?.unappliedAmount).toBe(receipt.amount);

    await login(page);
    await page.goto('/ar/collection-settlements/new');
    await pick(page, 'fi-settle-receipt-pick', receipt.documentNo, receipt.id);
    await page.getByTestId('fi-settle-add-lines').click();
    await expect(page.getByTestId('erp-select-table')).toContainText(`#${receivableId}`);
    await page.getByTestId(`erp-select-row-${receivableId}`).click();
    await page.getByTestId('erp-select-confirm').click();
    await expect(page.getByTestId(`fi-settle-form-amount-${receivableId}`)).toHaveValue('420');
    await expect(page.getByTestId('fi-settle-form-total')).toHaveText('420');
    await page.getByTestId('fi-settle-save').click();
    await expect(page).toHaveURL(/\/ar\/collection-settlements$/);
    const [draft] = sql<{ id: number; documentNo: string; status: string; amount: string }>(`SELECT TOP 1 id,
      document_no AS documentNo, status, CONVERT(varchar(40), total_amount) AS amount
      FROM ar_collection_settlement_doc WHERE company_id = ${tw01} AND receipt_id = ${receipt.id}
      AND is_deleted = 0 ORDER BY id DESC`);
    expect(draft).toMatchObject({ status: 'Draft', amount: '420.0000' });
    expect(open(), 'saving a draft does not reduce AR').toBe('420.0000');
    await expect(page.getByTestId('fi-settle-notice')).toContainText(draft!.documentNo);
    await page.getByTestId(`fi-settle-submit-${draft!.id}`).click();
    await expect(page.getByTestId('fi-settle-notice')).toContainText('已送審，等待審批；尚未減少任何應收');
    expect(open(), 'pending approval does not reduce AR').toBe('420.0000');
    expect(savedPosting(tw01, receipt.id)?.unappliedAmount).toBe(receipt.amount);

    const [instance] = sql<{ id: number }>(`SELECT TOP 1 id FROM sys_approval_instance WHERE company_id = ${tw01}
      AND document_type = 'AR.SETTLEMENT' AND document_id = ${draft!.id} ORDER BY id DESC`);
    expect(instance).toBeDefined();
    const pending = await json(apis.manager!.get(`${root}/sys/approvals/${instance!.id}`));
    const approved = await json(apis.manager!.post(`${root}/sys/approvals/${instance!.id}/approve`, {
      data: { comment: 'E2E 核准核銷', version: pending.version } }));
    expect(approved.status).toBe('Approved');
    await page.getByTestId('fi-settle-search').click();
    await expect(page.getByTestId(`fi-settle-status-${draft!.id}`)).toHaveText('已過帳');
    await page.getByTestId(`fi-settle-view-${draft!.id}`).click();
    const [allocation] = sql<{ id: number; amount: string }>(`SELECT id,
      CONVERT(varchar(40), amount) AS amount FROM ar_collection_allocation
      WHERE company_id = ${tw01} AND receivable_id = ${receivableId} AND settlement_id =
      (SELECT id FROM ar_collection_settlement WHERE company_id = ${tw01} AND source_id = ${draft!.id})`);
    expect(allocation?.amount).toBe('420.0000');
    await expect(page.getByTestId('fi-settle-line-open-1')).toHaveText('0');
    const remaining = (Number(receipt.amount) - 420).toFixed(4);
    await expect(page.getByTestId('fi-settle-detail-unapplied')).toContainText(grouped(Number(remaining)));
    expect(open()).toBe('0.0000');
    expect(savedPosting(tw01, receipt.id)?.unappliedAmount).toBe(remaining);
    await capture(page, 'settlement-posted-light-zh-Hant');
  });
});
