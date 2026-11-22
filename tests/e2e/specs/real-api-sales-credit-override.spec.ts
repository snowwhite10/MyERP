import { mkdir } from 'node:fs/promises';
import { expect, request, test, type APIRequestContext, type APIResponse, type Page } from '@playwright/test';
import { realApiLogin, type RealApiTokens } from '../support/real-api';
import { companyId, sql, utf16 } from '../support/real-api-sql';

// WU-SD-006c3b (SD-CREDIT-REAL-001–003, ADR-170–175): the whole credit override path against the running API and
// SQL Server. sales saves an order over the customer's FI credit limit (Create 2.5 saved-credit panel), submits it on
// View 2.4 with an override reason, and manager final-approves it on the Approval Inbox 8.70 with a second reason.
// Every figure on screen is compared with what SQL Server saved: sal_order, sal_credit_override (Submit and
// FinalApproval rows) and sys_approval_instance (read back with sqlcmd).
// Arrange (admin, through the MD / FI / SYS APIs, never SQL): a fresh unit, item, 5% output tax code, approved customer
// and a TWD credit limit of 1,500, so posted AR and other approved orders are 0 and the figures are exact. The
// SAL.ORDER flow is set to one step (user manager) and put back as it was in afterAll.
// manager holds SAL.CREDIT.OVERRIDE after --seed (ADR-175); sales holds SAL.ORDER.* and SAL.CREDIT.OVERRIDE.
// Run: & ./tools/scripts/Invoke-RealApiE2E.ps1 -Specs specs/real-api-sales-credit-override.spec
const apiUrl = process.env['E2E_API_URL'];
const root = '/api/v1';
const ORDER_FLOW = 'SAL.ORDER';
const LIMIT = 1500;
const PRICE = '800';
const QUANTITY = '2';
// 2 × 800 = 1,600 net, 5% tax 80, gross 1,680 → 180 over a 1,500 limit (posted AR and other orders are 0).
const GROSS = 1680;
const EXCESS = 180;

type User = 'sales' | 'manager' | 'admin';
type SavedOrder = { id: number; documentNo: string; status: string; version: number; baseGross: string;
  baseCurrency: string; approvalInstanceId: number | null; hasApprovedAt: number };
type SavedOverride = { stage: string; orderVersion: number; approvalInstanceId: number; creditLimitVersion: number;
  baseCurrency: string; creditLimit: string; postedOpenAr: string; otherCommitments: string; candidate: string;
  excess: string; reason: string; createdBy: number; evaluationDate: string };
type Flow = { id: number; name: string; documentType: string; allowSelfApproval: boolean; isActive: boolean;
  steps: unknown[]; version: number };

const savedOrders = (company: number, customer: number) => sql<SavedOrder>(`SELECT id, document_no AS documentNo, status,
  version, CONVERT(varchar(40), base_gross) AS baseGross, base_currency_code AS baseCurrency,
  approval_instance_id AS approvalInstanceId, CASE WHEN approved_at IS NULL THEN 0 ELSE 1 END AS hasApprovedAt
  FROM sal_order WHERE company_id = ${company} AND customer_id = ${customer} AND is_deleted = 0 ORDER BY id`);
const savedOverrides = (company: number, order: number) => sql<SavedOverride>(`SELECT stage, order_version AS orderVersion,
  approval_instance_id AS approvalInstanceId, credit_limit_version AS creditLimitVersion, base_currency_code AS baseCurrency,
  CONVERT(varchar(40), credit_limit) AS creditLimit, CONVERT(varchar(40), posted_open_ar) AS postedOpenAr,
  CONVERT(varchar(40), other_commitments) AS otherCommitments, CONVERT(varchar(40), candidate) AS candidate,
  CONVERT(varchar(40), excess) AS excess, CONVERT(varchar(max), CONVERT(varbinary(max), reason), 2) AS reason,
  created_by AS createdBy, CONVERT(varchar(10), evaluation_date, 23) AS evaluationDate
  FROM sal_credit_override WHERE company_id = ${company} AND order_id = ${order} ORDER BY id`)
  .map(row => ({ ...row, reason: utf16(row.reason) }));
const approvalInstance = (id: number) => sql<{ status: string; documentNo: string; documentId: number }>(`SELECT status,
  document_no AS documentNo, document_id AS documentId FROM sys_approval_instance WHERE id = ${id}`)[0];

const pad = (n: number) => String(n).padStart(2, '0');
const isoDate = (d: Date) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
const today = () => isoDate(new Date());
const inDays = (days: number) => { const d = new Date(); d.setDate(d.getDate() + days); return isoDate(d); };
/** The shared credit figures format (erp-sales-credit-figures): grouped, 2–4 decimals. */
const money = (value: number) => value.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 4 });

async function capture(page: Page, name: string): Promise<void> {
  const path = '../../tests/qa/SD/screenshots';
  await mkdir(path, { recursive: true });
  await page.screenshot({ path: `${path}/real-credit-${name}.png`, fullPage: true, animations: 'disabled' });
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

test.describe('真實 API 信用覆核：超額訂單 → 送審覆核 → 最終核准覆核（SQL 讀回雙斷言）', () => {
  test.describe.configure({ mode: 'serial' });
  test.skip(!apiUrl, '需要 E2E_API_URL：請用 tools/scripts/Invoke-RealApiE2E.ps1 執行');
  test.setTimeout(180_000);

  const run = `${Date.now() % 1_000_000_000}${Math.floor(Math.random() * 100)}`;
  const submitReason = `E2E 長期客戶，業務主管同意 ${run}`;
  const approvalReason = `E2E 財務長核准超額 ${run}`;
  const tokens: Partial<Record<User, RealApiTokens>> = {};
  const apis: Partial<Record<User, APIRequestContext>> = {};
  const userIds: Partial<Record<User, number>> = {};
  let tw01: number;
  let customer: { id: number; code: string; name: string };
  let item: { id: number; code: string; name: string };
  let tax: { id: number; code: string; rateId: number };
  let limit: { id: number; version: number };
  let order: SavedOrder | undefined;
  let originalFlow: Flow | undefined;
  let flowId: number | undefined;
  let consoleErrors: string[];
  let serverErrors: string[];

  test.beforeAll(async () => {
    test.setTimeout(300_000); // realApiLogin may wait out the login rate-limit window
    tw01 = companyId('TW01');
    for (const [user, password] of [['sales', 'Demo@2026'], ['manager', 'Demo@2026'], ['admin', 'az5911']] as const) {
      tokens[user] = await realApiLogin(apiUrl!, user, password);
      apis[user] = await request.newContext({ baseURL: apiUrl, extraHTTPHeaders: {
        Authorization: `Bearer ${tokens[user]!.accessToken}`, 'X-Company-Id': String(tw01) } });
      userIds[user] = sql<{ id: number }>(`SELECT id FROM sys_user WHERE username = '${user}'`)[0]!.id;
    }
    const grants = sql<{ role: string; code: string }>(`SELECT r.code AS role, rp.permission_code AS code
      FROM sys_role_permission rp JOIN sys_role r ON r.id = rp.role_id
      WHERE r.code IN ('SALES', 'MANAGER') AND rp.permission_code = 'SAL.CREDIT.OVERRIDE'`).map(x => x.role);
    expect(grants.sort(), 'SAL.CREDIT.OVERRIDE for SALES and MANAGER (ADR-175): run --seed').toEqual(['MANAGER', 'SALES']);

    const admin = apis.admin!;
    const uom = await json(admin.post(`${root}/md/uoms`, { data: { code: `E2ECU${run}`, name: `E2E 信用個 ${run}`, decimalPlaces: 0 } }), 201);
    item = await json(admin.post(`${root}/md/items`, { data: { code: `E2ECP${run}`, name: `E2E 信用物料 ${run}`,
      specification: null, baseUomId: uom.id, brandId: null, categoryId: null } }), 201);
    tax = await json(admin.post(`${root}/md/tax-codes`, { data: { code: `E2ECT${run}`, name: `E2E 信用銷項稅 ${run}`, taxType: 'Output' } }), 201);
    tax.rateId = (await json(admin.post(`${root}/md/tax-codes/${tax.id}/rates`, { data: { effectiveFrom: '2020-01-01', effectiveTo: null, ratePercent: 5 } }), 201)).id;
    const created = await json(admin.post(`${root}/md/customers`, { data: { code: `E2ECC${run}`, name: `E2E 信用客戶 ${run}`,
      categoryId: null, defaultPaymentTermId: null, defaultSettlementMethodId: null } }), 201);
    await json(admin.post(`${root}/md/customers/${created.id}/submit`, { data: { version: created.version } }));
    const approved = sql<{ reviewStatus: string }>(`SELECT review_status AS reviewStatus FROM md_customer WHERE id = ${created.id}`)[0];
    expect(approved?.reviewStatus, 'MD.CUSTOMER must have no active approval flow for this spec').toBe('Approved');
    customer = created;
    limit = await json(admin.post(`${root}/ar/credit-limits`, { data: { customerId: customer.id,
      effectiveFrom: inDays(-30), effectiveTo: inDays(365), amount: LIMIT } }), 201);

    // SAL.ORDER: one step approved by the user manager; afterAll restores the original flow (or switches ours off).
    const flows = await json(admin.get(`${root}/sys/approval-flows?pageSize=100`));
    const existing = flows.items.find((flow: Flow) => flow.documentType === ORDER_FLOW);
    if (existing) originalFlow = await json(admin.get(`${root}/sys/approval-flows/${existing.id}`));
    const flowInput = { name: `E2E ${ORDER_FLOW}`, allowSelfApproval: false,
      steps: [{ name: 'E2E 主管', approverType: 'User', roleId: null, userId: userIds.manager, minAmount: null }] };
    if (originalFlow) {
      await json(admin.put(`${root}/sys/approval-flows/${originalFlow.id}`, { data: { ...flowInput, isActive: true, version: originalFlow.version } }));
      flowId = originalFlow.id;
    } else {
      flowId = (await json(admin.post(`${root}/sys/approval-flows`, { data: { ...flowInput, documentType: ORDER_FLOW } }), 201)).id;
    }
  });

  test.afterAll(async () => {
    const admin = apis.admin;
    try {
      if (admin && flowId) {
        const current: Flow = await json(admin.get(`${root}/sys/approval-flows/${flowId}`));
        const restore = originalFlow
          ? { name: originalFlow.name, allowSelfApproval: originalFlow.allowSelfApproval, isActive: originalFlow.isActive,
              steps: originalFlow.steps, version: current.version }
          : { name: current.name, allowSelfApproval: current.allowSelfApproval, isActive: false, steps: current.steps,
              version: current.version };
        await json(admin.put(`${root}/sys/approval-flows/${flowId}`, { data: restore }));
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

  async function pick(page: Page, opener: string, keyword: string, rowId: number): Promise<void> {
    await page.getByTestId(opener).click();
    await expect(page.getByTestId('erp-select-dialog')).toBeVisible();
    await page.getByTestId('erp-select-keyword').fill(keyword);
    await page.getByTestId('erp-select-search').click();
    await page.getByTestId(`erp-select-row-${rowId}`).click();
    await page.getByTestId('erp-select-confirm').click();
  }

  async function openOrder(page: Page, saved: SavedOrder): Promise<void> {
    await page.goto('/sal/orders');
    await page.getByTestId('sd-order-keyword').fill(saved.documentNo);
    await page.getByTestId('sd-order-search').click();
    await page.getByTestId(`sd-order-open-${saved.id}`).click();
  }

  test.beforeEach(({ page }) => {
    consoleErrors = []; serverErrors = [];
    page.on('console', message => {
      // The refused first approve (422 SAL.CREDIT.LIMIT_EXCEEDED) is the expected path on 8.70.
      if (message.type() === 'error' && !/status of 422/.test(message.text())) consoleErrors.push(message.text());
    });
    page.on('pageerror', error => consoleErrors.push(error.message));
    page.on('response', response => { if (response.status() >= 500) serverErrors.push(`${response.status()} ${response.url()}`); });
  });

  test.afterEach(async ({ page }) => {
    expect(await page.locator('body').innerText()).not.toMatch(/\b(sd|sys|common|layout|errors)\.[a-zA-Z][A-Za-z_]+\.[A-Za-z][A-Za-z_.]+\b/);
    expect(serverErrors).toEqual([]);
    expect(consoleErrors).toEqual([]);
  });

  test('SD-CREDIT-REAL-001 sales saves an order over the FI limit; the saved-credit panel shows the figures SQL saved', async ({ page }) => {
    await login(page, 'sales');
    await page.goto('/sal/orders/new');
    await pick(page, 'sd-order-create-customer-pick', customer.code, customer.id);
    await page.getByTestId('sd-order-create-date').fill(today());
    await pick(page, 'sd-order-create-item-pick-0', item.code, item.id);
    await pick(page, 'sd-order-create-tax-pick-0', tax.code, tax.rateId);
    await page.getByTestId('sd-order-create-quantity-0').fill(QUANTITY);
    await page.getByTestId('sd-order-create-price-0').fill(PRICE);
    // No price rule for the fresh item: the line price needs a pricing override reason (BR-SD pricing, ADR-166).
    await page.getByTestId('sd-order-reason-0').fill(`E2E 無價格規則手動定價 ${run}`);
    await page.getByTestId('sd-order-create-save').click();
    await expect(page.getByTestId('sd-order-create-receipt')).toBeVisible();

    const orders = savedOrders(tw01, customer.id);
    expect(orders, 'exactly one order for the fresh customer').toHaveLength(1);
    order = orders[0];
    expect(order).toMatchObject({ status: 'Draft', baseCurrency: 'TWD', baseGross: `${GROSS}.0000`, approvalInstanceId: null });
    expect(savedOverrides(tw01, order!.id), 'saving never writes an override').toEqual([]);
    await expect(page.getByTestId('sd-order-create-receipt')).toContainText(order!.documentNo);

    const panel = page.getByTestId('sd-order-saved-credit');
    await expect(panel.getByTestId('sd-order-credit-status')).toHaveText('超出額度');
    await expect(panel.getByTestId('sd-order-credit-limit')).toHaveText(money(LIMIT));
    await expect(panel.getByTestId('sd-order-credit-candidate')).toHaveText(money(GROSS));
    await expect(panel.getByTestId('sd-order-credit-headroom')).toHaveText(money(LIMIT - GROSS));
    await expect(panel.getByTestId('sd-order-saved-credit-excess')).toContainText(`${money(EXCESS)} TWD`);
    await expect(panel.getByTestId('sd-order-saved-credit-override-hint')).toContainText('信用覆核理由');
    await capture(page, '2.5-over-limit-light-zh-Hant');
    await dark(page);
    await capture(page, '2.5-over-limit-dark-zh-Hant');
  });

  test('SD-CREDIT-REAL-002 sales submits on 2.4 with a reason; SQL keeps a Submit override row equal to the screen', async ({ page }) => {
    expect(order, 'SD-CREDIT-REAL-001 must pass first').toBeDefined();
    await login(page, 'sales');
    await openOrder(page, order!);
    await page.getByTestId('sd-order-submit').click();
    await expect(page.getByTestId('sd-order-credit-status')).toHaveText('超出額度');
    await expect(page.getByTestId('sd-order-credit-excess')).toContainText(`${money(EXCESS)} TWD`);
    await page.getByTestId('sd-order-submit-confirm').click();
    await expect(page.getByTestId('sd-order-override-error')).toHaveText('請輸入覆核信用額度的理由。');
    expect(savedOrders(tw01, customer.id)[0], 'nothing submitted without a reason').toMatchObject({ status: 'Draft', version: order!.version });
    await page.getByTestId('sd-order-override-reason').fill(`  ${submitReason}  `);
    await capture(page, '2.4-submit-reason-light-zh-Hant');
    await page.getByTestId('sd-order-submit-confirm').click();
    await expect(page.getByTestId('sd-order-submit-notice')).toBeVisible();

    const pending = savedOrders(tw01, customer.id)[0]!;
    expect(pending).toMatchObject({ id: order!.id, status: 'Pending', hasApprovedAt: 0 });
    expect(pending.approvalInstanceId).toBeGreaterThan(0);
    expect(pending.version).toBeGreaterThan(order!.version);
    const overrides = savedOverrides(tw01, order!.id);
    expect(overrides).toEqual([expect.objectContaining({ stage: 'Submit', approvalInstanceId: pending.approvalInstanceId,
      baseCurrency: 'TWD', creditLimit: `${LIMIT}.0000`, postedOpenAr: '0.0000', otherCommitments: '0.0000',
      candidate: `${GROSS}.0000`, excess: `${EXCESS}.0000`, reason: submitReason, createdBy: userIds.sales,
      evaluationDate: today() })]);
    expect(approvalInstance(pending.approvalInstanceId!)).toMatchObject({ status: 'Pending', documentNo: order!.documentNo, documentId: order!.id });
    // Screen equals SQL: the notice names the saved version, the saved reason (trimmed) and excess; status is Pending.
    await expect(page.getByTestId('sd-order-submit-notice')).toContainText(`${order!.documentNo} 已送審，等待審批（版本 ${pending.version}）`);
    await expect(page.getByTestId('sd-order-submit-override')).toContainText(`超額 ${money(EXCESS)} TWD。理由：${submitReason}`);
    await expect(page.getByTestId('sd-order-detail-status')).toHaveText('審批中');
    await expect(page.getByTestId(`sd-order-row-status-${order!.id}`)).toHaveText('審批中');
    await capture(page, '2.4-submitted-light-zh-Hant');
    await dark(page);
    await capture(page, '2.4-submitted-dark-zh-Hant');
    order = pending;
  });

  test('SD-CREDIT-REAL-003 manager final-approves on 8.70 with a reason; SQL adds a FinalApproval row and approves the order', async ({ page }) => {
    expect(order?.status, 'SD-CREDIT-REAL-002 must pass first').toBe('Pending');
    const no = order!.documentNo;
    await login(page, 'manager');
    await page.goto('/sys/approvals');
    await page.getByTestId(`sys-approval-approve-${no}`).click();
    await page.getByTestId('sys-approval-comment').fill('同意出貨');
    await page.locator('.ant-modal-footer .ant-btn-primary').click();
    // The first approve is refused by SD (422): nothing is saved and the reason field appears.
    await expect(page.getByTestId('sys-approval-credit-excess')).toContainText(`超出客戶可用信用額度 ${EXCESS} TWD，您的核准尚未保存`);
    await expect(page.getByTestId('sys-approval-override-reason')).toBeFocused();
    expect(savedOrders(tw01, customer.id)[0], 'refused approve leaves the order pending').toMatchObject({ status: 'Pending', version: order!.version });
    expect(approvalInstance(order!.approvalInstanceId!)?.status).toBe('Pending');
    expect(savedOverrides(tw01, order!.id)).toHaveLength(1);
    await capture(page, '8.70-reason-light-zh-Hant');
    await dark(page);
    await capture(page, '8.70-reason-dark-zh-Hant');
    await page.emulateMedia({ colorScheme: 'light' });

    await page.getByTestId('sys-approval-override-reason').fill(`  ${approvalReason} `);
    await page.locator('.ant-modal-footer .ant-btn-primary').click();
    await expect(page.getByTestId('sys-approval-notice')).toContainText(`已核准 ${no}，並以您的理由覆核超出信用額度 ${EXCESS} TWD`);
    await expect(page.getByTestId(`sys-approval-task-${no}`)).toHaveCount(0);

    const final = savedOrders(tw01, customer.id)[0]!;
    expect(final).toMatchObject({ id: order!.id, status: 'Approved', hasApprovedAt: 1, approvalInstanceId: order!.approvalInstanceId });
    expect(approvalInstance(order!.approvalInstanceId!)?.status).toBe('Approved');
    const overrides = savedOverrides(tw01, order!.id);
    expect(overrides).toHaveLength(2);
    expect(overrides[1]).toEqual(expect.objectContaining({ stage: 'FinalApproval', orderVersion: order!.version,
      approvalInstanceId: order!.approvalInstanceId, baseCurrency: 'TWD', creditLimit: `${LIMIT}.0000`,
      postedOpenAr: '0.0000', otherCommitments: '0.0000', candidate: `${GROSS}.0000`, excess: `${EXCESS}.0000`,
      reason: approvalReason, createdBy: userIds.manager, evaluationDate: today() }));
    expect(overrides[0]).toMatchObject({ stage: 'Submit', reason: submitReason, createdBy: userIds.sales });
    expect(overrides[1]!.creditLimitVersion).toBe(overrides[0]!.creditLimitVersion);
    await capture(page, '8.70-approved-light-zh-Hant');
    await dark(page);
    await capture(page, '8.70-approved-dark-zh-Hant');
  });
});
