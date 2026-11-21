import { mkdir } from 'node:fs/promises';
import { expect, request, test, type APIRequestContext, type APIResponse, type Page } from '@playwright/test';
import { realApiLogin, type RealApiTokens } from '../support/real-api';
import { companyId, sql } from '../support/real-api-sql';

// WU-SD-005b3a (SD-PRICE-REAL-001–004, ADR-166): price rule → quotation → conversion → manual order against the running
// API and SQL Server. Every screen value is compared with the row SQL Server saved (read back with sqlcmd).
// Arrange (admin, through the MD API, never SQL): a fresh unit, item, output tax code with a 5% rate and an approved
// customer, so the rule key (item + customer + TWD) cannot overlap older runs. The SAL.QUOTATION approval flow is
// switched off for the zero-step submit and put back as it was.
// The rule screen runs as `sales` (SAL.PRICING.* after --seed). The quotation and order forms run as `admin`: their
// customer / item / tax pickers still read the MD lists, which the seeded SALES role cannot view (WU-SD-FORM-OPTIONS).
// Run: & ./tools/scripts/Invoke-RealApiE2E.ps1 -Specs specs/real-api-sales-pricing.spec
const apiUrl = process.env['E2E_API_URL'];
const root = '/api/v1';
const QUOTE_FLOW = 'SAL.QUOTATION';
const RULE_PRICE = '1234.5';
const OVERRIDE_PRICE = '1200';

type User = 'sales' | 'admin';
type SavedRule = { id: number; itemId: number; customerId: number; currencyCode: string; validFrom: string; validTo: string;
  unitPrice: string; discountCeiling: string; isActive: boolean; version: number };
type SavedLine = { lineNo: number; itemId: number; unitPrice: string; discountRate: string; pricingMode: string;
  pricingRuleId: number | null; pricingRuleVersion: number | null; ruleUnitPrice: string | null;
  ruleDiscountCeiling: string | null; overrideReason: string | null; overrideBy: number | null; hasOverrideAt: number;
  originalNet: string };
type SavedDocument = { id: number; documentNo: string; status: string; version: number };
type Flow = { id: number; name: string; documentType: string; allowSelfApproval: boolean; isActive: boolean;
  steps: unknown[]; version: number };

const LINE_COLUMNS = `line_no AS [lineNo], item_id AS itemId, CONVERT(varchar(40), unit_price) AS unitPrice,
  CONVERT(varchar(40), discount_rate) AS discountRate, pricing_mode AS pricingMode, pricing_rule_id AS pricingRuleId,
  pricing_rule_version AS pricingRuleVersion, CONVERT(varchar(40), rule_unit_price) AS ruleUnitPrice,
  CONVERT(varchar(40), rule_discount_ceiling) AS ruleDiscountCeiling, CONVERT(varchar(max), CONVERT(varbinary(max), override_reason), 2) AS overrideReason,
  override_by AS overrideBy, CASE WHEN override_at IS NULL THEN 0 ELSE 1 END AS hasOverrideAt,
  CONVERT(varchar(40), original_net) AS originalNet`;

const savedRules = (company: number, customer: number) => sql<SavedRule>(`SELECT id, item_id AS itemId,
  customer_id AS customerId, currency_code AS currencyCode, CONVERT(varchar(10), valid_from, 23) AS validFrom,
  CONVERT(varchar(10), valid_to, 23) AS validTo, CONVERT(varchar(40), unit_price) AS unitPrice,
  CONVERT(varchar(40), discount_ceiling) AS discountCeiling, is_active AS isActive, version
  FROM sal_price_rule WHERE company_id = ${company} AND customer_id = ${customer} AND is_deleted = 0`);
const savedQuotations = (company: number, customer: number) => sql<SavedDocument>(`SELECT id, document_no AS documentNo,
  status, version FROM sal_quotation WHERE company_id = ${company} AND customer_id = ${customer} AND is_deleted = 0 ORDER BY id`);
const savedOrders = (company: number, customer: number) => sql<SavedDocument & { sourceQuotationId: number | null }>(
  `SELECT id, document_no AS documentNo, status, version, source_quotation_id AS sourceQuotationId FROM sal_order
  WHERE company_id = ${company} AND customer_id = ${customer} AND is_deleted = 0 ORDER BY id`);
const quotationLines = (company: number, id: number) => sql<SavedLine>(`SELECT ${LINE_COLUMNS} FROM sal_quotation_line
  WHERE company_id = ${company} AND quotation_id = ${id} ORDER BY line_no`).map(decodeReason);
const orderLines = (company: number, id: number) => sql<SavedLine>(`SELECT ${LINE_COLUMNS} FROM sal_order_line
  WHERE company_id = ${company} AND order_id = ${id} ORDER BY line_no`).map(decodeReason);

function decodeReason(line: SavedLine): SavedLine {
  return { ...line, overrideReason: line.overrideReason == null ? null : Buffer.from(line.overrideReason, 'hex').toString('utf16le') };
}

const pad = (n: number) => String(n).padStart(2, '0');
const isoDate = (d: Date) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
const today = () => isoDate(new Date());
const inDays = (days: number) => { const d = new Date(); d.setDate(d.getDate() + days); return isoDate(d); };

async function capture(page: Page, name: string): Promise<void> {
  const path = '../../tests/qa/SD/screenshots';
  await mkdir(path, { recursive: true });
  await page.screenshot({ path: `${path}/real-pricing-${name}.png`, fullPage: true, animations: 'disabled' });
}

async function json(response: Promise<APIResponse>, status = 200): Promise<any> {
  const value = await response;
  expect(value.status(), await value.text()).toBe(status);
  return value.status() === 204 ? null : value.json();
}

test.describe('真實 API 定價：規則 → 報價 → 轉單 → 人工訂單（SQL 讀回雙斷言）', () => {
  test.describe.configure({ mode: 'serial' });
  test.skip(!apiUrl, '需要 E2E_API_URL：請用 tools/scripts/Invoke-RealApiE2E.ps1 執行');
  test.setTimeout(180_000);

  const run = `${Date.now() % 1_000_000_000}${Math.floor(Math.random() * 100)}`;
  const tokens: Partial<Record<User, RealApiTokens>> = {};
  const apis: Partial<Record<User, APIRequestContext>> = {};
  let tw01: number;
  let adminId: number;
  let customer: { id: number; code: string; name: string };
  let item: { id: number; code: string; name: string };
  let tax: { id: number; code: string };
  let rule: SavedRule | undefined;
  let quotation: SavedDocument | undefined;
  let originalFlow: Flow | undefined;
  let flowTouched = false;
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
    const granted = sql<{ code: string }>(`SELECT DISTINCT rp.permission_code AS code FROM sys_role_permission rp
      JOIN sys_role r ON r.id = rp.role_id WHERE r.code = 'SALES' AND rp.permission_code LIKE 'SAL.PRICING.%'`).map(x => x.code);
    expect(granted.sort(), 'SALES role pricing grants: run --seed').toEqual(
      ['SAL.PRICING.CREATE', 'SAL.PRICING.DEACTIVATE', 'SAL.PRICING.EDIT', 'SAL.PRICING.OVERRIDE', 'SAL.PRICING.VIEW']);
    const admin = apis.admin!;
    adminId = sql<{ id: number }>(`SELECT id FROM sys_user WHERE username = 'admin'`)[0]!.id;
    const uom = await json(admin.post(`${root}/md/uoms`, { data: { code: `E2EU${run}`, name: `E2E 個 ${run}`, decimalPlaces: 0 } }), 201);
    item = await json(admin.post(`${root}/md/items`, { data: { code: `E2EP${run}`, name: `E2E 定價物料 ${run}`,
      specification: null, baseUomId: uom.id, brandId: null, categoryId: null } }), 201);
    tax = await json(admin.post(`${root}/md/tax-codes`, { data: { code: `E2ET${run}`, name: `E2E 銷項稅 ${run}`, taxType: 'Output' } }), 201);
    await json(admin.post(`${root}/md/tax-codes/${tax.id}/rates`, { data: { effectiveFrom: '2020-01-01', effectiveTo: null, ratePercent: 5 } }), 201);
    const created = await json(admin.post(`${root}/md/customers`, { data: { code: `E2EPC${run}`, name: `E2E 定價客戶 ${run}`,
      categoryId: null, defaultPaymentTermId: null, defaultSettlementMethodId: null } }), 201);
    await json(admin.post(`${root}/md/customers/${created.id}/submit`, { data: { version: created.version } }));
    const approved = sql<{ reviewStatus: string }>(`SELECT review_status AS reviewStatus FROM md_customer WHERE id = ${created.id}`)[0];
    expect(approved?.reviewStatus, 'MD.CUSTOMER must have no active approval flow for this spec').toBe('Approved');
    customer = created;
    const flows = await json(admin.get(`${root}/sys/approval-flows?pageSize=100`));
    originalFlow = flows.items.find((flow: Flow) => flow.documentType === QUOTE_FLOW);
    if (originalFlow) originalFlow = await json(admin.get(`${root}/sys/approval-flows/${originalFlow.id}`));
  });

  test.afterAll(async () => {
    const admin = apis.admin;
    try {
      if (admin && flowTouched && originalFlow?.isActive) {
        const current: Flow = await json(admin.get(`${root}/sys/approval-flows/${originalFlow.id}`));
        await json(admin.put(`${root}/sys/approval-flows/${originalFlow.id}`, { data: { name: originalFlow.name,
          allowSelfApproval: originalFlow.allowSelfApproval, isActive: true, steps: originalFlow.steps, version: current.version } }));
      }
    } finally {
      await Promise.all(Object.values(apis).map(api => api?.dispose()));
    }
  });

  /** Switches the SAL.QUOTATION flow off so submit approves at once (zero steps); afterAll restores it. */
  async function quotationFlowOff(): Promise<void> {
    if (!originalFlow?.isActive) return;
    const admin = apis.admin!;
    flowTouched = true;
    const current: Flow = await json(admin.get(`${root}/sys/approval-flows/${originalFlow.id}`));
    await json(admin.put(`${root}/sys/approval-flows/${originalFlow.id}`, { data: { name: current.name,
      allowSelfApproval: current.allowSelfApproval, isActive: false, steps: current.steps, version: current.version } }));
  }

  async function login(page: Page, user: User): Promise<void> {
    await page.addInitScript(([key, value]) => localStorage.setItem(key, value),
      ['myerp.auth.session', JSON.stringify({ ...tokens[user], username: user })]);
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

  /** Tax code then its rate valid on the document date (the only rate of the fresh code). */
  async function pickTax(page: Page, opener: string): Promise<void> {
    await pick(page, opener, tax.code, tax.id);
    await expect(page.getByTestId('erp-select-dialog')).toBeVisible();
    await page.getByText('5%', { exact: true }).last().click();
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

  test('SD-PRICE-REAL-001 sales creates a customer rule on screen; the active rule saved in SQL matches every field', async ({ page }) => {
    const from = today();
    const to = inDays(60);
    await login(page, 'sales');
    await page.goto('/sal/price-rules');
    await page.getByTestId('sd-price-rule-create').click();
    await expect(page).toHaveURL(/\/sal\/price-rules\/new$/);
    await pick(page, 'sd-price-rule-form-item-pick', item.code, item.id);
    await expect(page.getByTestId('sd-price-rule-form-item')).toHaveText(`${item.code} · ${item.name}`);
    await page.getByTestId('sd-price-rule-form-scope').selectOption('customer');
    await pick(page, 'sd-price-rule-form-customer-pick', customer.code, customer.id);
    await expect(page.getByTestId('sd-price-rule-form-customer')).toHaveText(`${customer.code} · ${customer.name}`);
    await page.getByTestId('sd-price-rule-form-currency').selectOption('TWD');
    await page.getByTestId('sd-price-rule-form-from').fill(from);
    await page.getByTestId('sd-price-rule-form-to').fill(to);
    await page.getByTestId('sd-price-rule-form-price').fill(RULE_PRICE);
    await page.getByTestId('sd-price-rule-form-ceiling').fill('10');
    await page.getByTestId('sd-price-rule-form-save').click();
    await expect(page.getByTestId('sd-price-rule-form-success')).toBeVisible();

    const rules = savedRules(tw01, customer.id);
    expect(rules, 'exactly one rule for the fresh customer').toHaveLength(1);
    expect(rules[0]).toMatchObject({ itemId: item.id, currencyCode: 'TWD', validFrom: from, validTo: to,
      unitPrice: '1234.500000', discountCeiling: '10.0000', isActive: true, version: 1 });
    rule = rules[0];
    await expect(page.getByTestId('sd-price-rule-form-success')).toContainText(`價格規則 ${rule!.id} 已建立並生效`);
    await capture(page, '2.12-created-light-zh-Hant');
    await page.getByTestId('sd-price-rule-form-to-list').click();
    await expect(page.getByTestId(`sd-price-rule-row-${rule!.id}`)).toContainText('1,234.5');
    await expect(page.getByTestId(`sd-price-rule-row-${rule!.id}`)).toContainText(customer.code);
  });

  test('SD-PRICE-REAL-002 the quotation form applies the saved rule; the line saved in SQL is rule-priced with its ID and version', async ({ page }) => {
    expect(rule, 'SD-PRICE-REAL-001 must pass first').toBeDefined();
    await login(page, 'admin');
    await page.goto('/sal/quotations/new');
    await pick(page, 'sd-quote-create-customer-pick', customer.code, customer.id);
    await page.getByTestId('sd-quote-create-date').fill(today());
    await page.getByTestId('sd-quote-create-expiry').fill(inDays(30));
    await pick(page, 'sd-quote-create-item-pick-0', item.code, item.id);
    await pickTax(page, 'sd-quote-create-tax-pick-0');
    await page.getByTestId('sd-quote-create-quantity-0').fill('2');
    await expect(page.getByTestId('sd-quote-pricing-0')).toContainText(`價格規則 #${rule!.id}（版本 1）：單價 1234.5，折扣上限 10%`);
    await expect(page.getByTestId('sd-quote-create-price-0')).toHaveValue(RULE_PRICE);
    await expect(page.getByTestId('sd-quote-reason-0')).toHaveCount(0);
    await capture(page, '2.2-rule-light-zh-Hant');
    await page.getByTestId('sd-quote-create-save').click();
    await expect(page.getByTestId('sd-quote-create-success')).toBeVisible();

    const quotes = savedQuotations(tw01, customer.id);
    expect(quotes).toHaveLength(1);
    quotation = quotes[0];
    expect(quotation).toMatchObject({ status: 'Draft' });
    await expect(page.getByTestId('sd-quote-create-success')).toContainText(quotation!.documentNo);
    expect(quotationLines(tw01, quotation!.id)).toEqual([expect.objectContaining({ lineNo: 1, itemId: item.id,
      unitPrice: '1234.500000', discountRate: '0.0000', pricingMode: 'Rule', pricingRuleId: rule!.id, pricingRuleVersion: 1,
      ruleUnitPrice: '1234.500000', ruleDiscountCeiling: '10.0000', overrideReason: null, overrideBy: null, hasOverrideAt: 0,
      originalNet: '2469.0000' })]);
    await page.goto('/sal/quotations');
    await page.getByTestId('sd-quote-keyword').fill(quotation!.documentNo);
    await page.getByTestId('sd-quote-search').click();
    await page.getByRole('button', { name: quotation!.documentNo }).click();
    await expect(page.getByTestId('sd-quote-detail-pricing-1')).toHaveText(`規則 #${rule!.id}（版本 1）`);
  });

  test('SD-PRICE-REAL-003 an approved quotation converted on screen keeps the rule provenance in the order saved in SQL', async ({ page }) => {
    expect(quotation, 'SD-PRICE-REAL-002 must pass first').toBeDefined();
    await quotationFlowOff();
    const submitted = await json(apis.admin!.post(`${root}/sal/quotations/${quotation!.id}/submit`, { data: { version: quotation!.version } }));
    expect(submitted.status, 'zero-step SAL.QUOTATION flow approves at once').toBe('Approved');
    expect(savedQuotations(tw01, customer.id)[0]).toMatchObject({ status: 'Approved' });
    await login(page, 'admin');
    await page.goto('/sal/orders/new');
    await page.getByRole('button', { name: '選擇報價' }).click();
    await expect(page.getByTestId('erp-select-dialog')).toBeVisible();
    await page.getByTestId('erp-select-keyword').fill(quotation!.documentNo);
    await page.getByTestId('erp-select-search').click();
    await page.getByTestId(`erp-select-row-${quotation!.id}`).click();
    await page.getByTestId('erp-select-confirm').click();
    await expect(page.getByTestId('sd-order-create-quotation')).toContainText(quotation!.documentNo);
    await page.getByTestId('sd-order-create-convert').click();
    await expect(page.getByTestId('sd-order-create-receipt')).toBeVisible();

    const orders = savedOrders(tw01, customer.id);
    expect(orders).toHaveLength(1);
    expect(orders[0]).toMatchObject({ status: 'Draft', sourceQuotationId: quotation!.id });
    await expect(page.getByTestId('sd-order-create-receipt')).toContainText(orders[0]!.documentNo);
    expect(orderLines(tw01, orders[0]!.id)).toEqual([expect.objectContaining({ lineNo: 1, itemId: item.id,
      unitPrice: '1234.500000', pricingMode: 'Rule', pricingRuleId: rule!.id, pricingRuleVersion: 1,
      ruleUnitPrice: '1234.500000', ruleDiscountCeiling: '10.0000', overrideReason: null, originalNet: '2469.0000' })]);
    await page.goto('/sal/orders');
    await page.getByTestId('sd-order-keyword').fill(orders[0]!.documentNo);
    await page.getByTestId('sd-order-search').click();
    await page.getByRole('button', { name: orders[0]!.documentNo }).click();
    await expect(page.getByTestId('sd-order-detail-pricing-1')).toHaveText(`規則 #${rule!.id}（版本 1）`);
    await capture(page, '2.4-converted-source-light-zh-Hant');
  });

  test('SD-PRICE-REAL-004 a manual order below the rule price needs a reason; SQL keeps the override with the rule snapshot', async ({ page }) => {
    expect(rule, 'SD-PRICE-REAL-001 must pass first').toBeDefined();
    const reason = `E2E 大量採購 ${run}`;
    await login(page, 'admin');
    await page.goto('/sal/orders/new');
    await pick(page, 'sd-order-create-customer-pick', customer.code, customer.id);
    await page.getByTestId('sd-order-create-date').fill(today());
    await pick(page, 'sd-order-create-item-pick-0', item.code, item.id);
    await pickTax(page, 'sd-order-create-tax-pick-0');
    await expect(page.getByTestId('sd-order-create-price-0')).toHaveValue(RULE_PRICE);
    await page.getByTestId('sd-order-create-price-0').fill(OVERRIDE_PRICE);
    await expect(page.getByTestId('sd-order-override-needed-0')).toContainText('單價與規則不同或折扣超過上限');
    await page.getByTestId('sd-order-create-save').click();
    await expect(page.getByTestId('sd-order-pricing-0')).toContainText('請填寫覆核理由。');
    expect(savedOrders(tw01, customer.id), 'no order saved without a reason').toHaveLength(1);
    await page.getByTestId('sd-order-reason-0').fill(`  ${reason}  `);
    await capture(page, '2.5-override-light-zh-Hant');
    await page.getByTestId('sd-order-create-save').click();
    await expect(page.getByTestId('sd-order-create-receipt')).toBeVisible();

    const orders = savedOrders(tw01, customer.id);
    expect(orders).toHaveLength(2);
    const manual = orders[1]!;
    expect(manual).toMatchObject({ status: 'Draft', sourceQuotationId: null });
    await expect(page.getByTestId('sd-order-create-receipt')).toContainText(manual.documentNo);
    expect(orderLines(tw01, manual.id)).toEqual([expect.objectContaining({ lineNo: 1, itemId: item.id,
      unitPrice: '1200.000000', pricingMode: 'Override', pricingRuleId: rule!.id, pricingRuleVersion: 1,
      ruleUnitPrice: '1234.500000', ruleDiscountCeiling: '10.0000', overrideReason: reason, overrideBy: adminId,
      hasOverrideAt: 1, originalNet: '1200.0000' })]);
    await page.goto('/sal/orders');
    await page.getByTestId('sd-order-keyword').fill(manual.documentNo);
    await page.getByTestId('sd-order-search').click();
    await page.getByRole('button', { name: manual.documentNo }).click();
    await expect(page.getByTestId('sd-order-detail-pricing-1')).toHaveText(`覆核（規則 #${rule!.id} 版本 1）：${reason}`);
    await capture(page, '2.4-override-source-light-zh-Hant');
  });
});
