import { mkdir } from 'node:fs/promises';
import { expect, request, test, type APIRequestContext, type APIResponse, type Page } from '@playwright/test';
import { realApiLogin, type RealApiTokens } from '../support/real-api';
import { companyId, sql, utf16 } from '../support/real-api-sql';

// WU-FI-008c3 (FI-OP-REAL-001–006): the other payment screen (6.10–6.12) against the running API, SYS approval and SQL
// Server. Every screen value is compared with the row SQL Server saved (read back with sqlcmd), and posting is checked in
// the GL (Dr the category's expense account, Cr the fund account's ledger, same amount) while AP stays untouched.
// Arrange (admin through the MD API, finance through the GL map API, never SQL): a fresh TWD bank fund account on ledger
// 1102, a mapped expense category (→ 6102) and an unmapped one. The AP.OTHER_PAYMENT approval flow is put back as it was.
// Run: & ./tools/scripts/Invoke-RealApiE2E.ps1 -Specs specs/real-api-finance-other-payment.spec.ts
const apiUrl = process.env['E2E_API_URL'];
const root = '/api/v1';
const DOCUMENT_TYPE = 'AP.OTHER_PAYMENT';
const EXPENSE_ACCOUNT = '6102';
const BANK_LEDGER = '1102';

type User = 'finance' | 'manager' | 'admin' | 'viewer';
type SavedPayment = { id: number; documentNo: string; payeeHex: string; expenseCategoryId: number; bankAccountId: number;
  amount: string; status: string; currency: string; version: number; bankReference: string; businessDate: string };
type SavedSource = { amount: string; journalId: number; expenseAccountCode: string; mappingVersion: number;
  bankReference: string; baseCurrency: string };
type ApBalance = { payables: number; openBase: string; allocations: number; paymentSources: number };
type Flow = { id: number; name: string; documentType: string; allowSelfApproval: boolean; isActive: boolean;
  steps: unknown[]; version: number };
type Ref = { id: number; code: string; name: string };

/** The live document row with the given withdrawal reference in the company (amount as exact `decimal(19,4)` text). */
function savedPayment(company: number, reference: string): SavedPayment | undefined {
  return sql<SavedPayment>(`SELECT id, document_no AS documentNo,
    CONVERT(varchar(max), CONVERT(varbinary(max), payee_description), 2) AS payeeHex,
    expense_category_id AS expenseCategoryId, bank_account_id AS bankAccountId, CONVERT(varchar(40), amount) AS amount,
    status, currency, version, bank_reference AS bankReference, CONVERT(char(10), business_date, 23) AS businessDate
    FROM ap_other_payment_doc WHERE company_id = ${company} AND bank_reference = '${reference}' AND is_deleted = 0`)[0];
}

/** The immutable posted source FI wrote for the document (none until posted), with its expense account code. */
function savedSource(company: number, documentId: number): SavedSource | undefined {
  return sql<SavedSource>(`SELECT CONVERT(varchar(40), s.amount) AS amount, s.gl_journal_id AS journalId,
    a.code AS expenseAccountCode, s.mapping_version AS mappingVersion, s.bank_reference AS bankReference,
    s.base_currency AS baseCurrency
    FROM ap_other_payment_source s JOIN md_account a ON a.id = s.expense_account_id
    WHERE s.company_id = ${company} AND s.source_id = ${documentId}`)[0];
}

/** Journal lines as `code:debit:credit`, in sequence order. */
function journalLines(company: number, journalId: number): string[] {
  return sql<{ line: string }>(`SELECT account_code + ':' + CONVERT(varchar(40), debit) + ':' + CONVERT(varchar(40), credit) AS line
    FROM gl_journal_line WHERE company_id = ${company} AND journal_id = ${journalId} ORDER BY sequence`).map(row => row.line);
}

/** Everything AP owns: an other payment must not add or reduce payables, allocations or supplier payments. */
function apBalance(company: number): ApBalance {
  return sql<ApBalance>(`SELECT
    (SELECT COUNT(*) FROM ap_posted_payable WHERE company_id = ${company}) AS payables,
    (SELECT CONVERT(varchar(40), ISNULL(SUM(open_base_amount), 0)) FROM ap_posted_payable WHERE company_id = ${company}) AS openBase,
    (SELECT COUNT(*) FROM ap_payment_allocation WHERE company_id = ${company}) AS allocations,
    (SELECT COUNT(*) FROM ap_payment_source WHERE company_id = ${company}) AS paymentSources`)[0]!;
}

const exact = (amount: number) => `${amount}.0000`;
const grouped = (amount: number) => amount.toLocaleString('en-US');
const today = () => { const d = new Date(); return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`; };

async function capture(page: Page, name: string): Promise<void> {
  const path = '../../tests/qa/FI/screenshots';
  await mkdir(path, { recursive: true });
  await page.screenshot({ path: `${path}/6.10-real-${name}.png`, fullPage: true, animations: 'disabled' });
}

async function json(response: Promise<APIResponse>, status = 200): Promise<any> {
  const value = await response;
  expect(value.status(), await value.text()).toBe(status);
  return value.status() === 204 ? undefined : value.json();
}

test.describe('真實 API 其他付款（SQL 讀回雙斷言）', () => {
  test.describe.configure({ mode: 'serial' });
  test.skip(!apiUrl, '需要 E2E_API_URL：請用 tools/scripts/Invoke-RealApiE2E.ps1 執行');
  test.setTimeout(180_000);

  const run = `${Date.now() % 1_000_000_000}${Math.floor(Math.random() * 100)}`;
  const tokens: Partial<Record<User, RealApiTokens>> = {};
  const apis: Partial<Record<User, APIRequestContext>> = {};
  let tw01: number;
  let fund: Ref;
  let mapped: Ref;
  let unmapped: Ref;
  let mappingVersion: number;
  let originalFlow: Flow | undefined;
  let flowTouched = false;
  let draftReference: string | undefined;
  let consoleErrors: string[];
  let serverErrors: string[];
  let allowedConsole: RegExp[];

  test.beforeAll(async () => {
    test.setTimeout(300_000); // realApiLogin may wait out the login rate-limit window
    tw01 = companyId('TW01');
    for (const [user, password] of [['finance', 'Demo@2026'], ['manager', 'Demo@2026'], ['admin', 'az5911'],
      ['viewer', 'Demo@2026']] as const) {
      tokens[user] = await realApiLogin(apiUrl!, user, password);
      apis[user] = await request.newContext({ baseURL: apiUrl, extraHTTPHeaders: {
        Authorization: `Bearer ${tokens[user]!.accessToken}`, 'X-Company-Id': String(tw01) } });
    }
    const admin = apis.admin!;
    const [base] = sql<{ id: number; code: string }>(`SELECT id, code FROM md_currency
      WHERE company_id = ${tw01} AND is_base = 1 AND is_deleted = 0`);
    expect(base?.code, 'TW01 base currency: run --seed (ADR-105)').toBe('TWD');
    const [ledger] = sql<{ id: number }>(`SELECT id FROM md_account WHERE company_id = ${tw01} AND code = '${BANK_LEDGER}' AND is_deleted = 0`);
    expect(ledger, `TW01 account ${BANK_LEDGER}: run --seed (ADR-105)`).toBeDefined();
    fund = await json(admin.post(`${root}/md/fund-accounts`, { data: { code: `E2EOPB${run}`, name: `E2E 付款銀行 ${run}`,
      currencyId: base!.id, fundType: 'Bank', bankName: 'E2E Bank', bankAccountNo: run, ledgerAccountId: ledger!.id } }), 201);
    mapped = await json(admin.post(`${root}/md/income-expense-categories`, { data: { code: `E2EOPM${run}`,
      name: `E2E 租金類別 ${run}`, direction: 'Expense', ledgerAccountId: null } }), 201);
    unmapped = await json(admin.post(`${root}/md/income-expense-categories`, { data: { code: `E2EOPU${run}`,
      name: `E2E 未對照類別 ${run}`, direction: 'Expense', ledgerAccountId: null } }), 201);
    // A new category has no map row (version 0); finance creates it through the GL map API.
    const saved = await json(apis.finance!.put(`${root}/gl/other-payment-account-map/${mapped.id}`, { data: {
      version: 0, expenseAccountCode: EXPENSE_ACCOUNT } }));
    expect(saved.changed).toBe(true);
    expect(saved.row.expenseAccount.code).toBe(EXPENSE_ACCOUNT);
    mappingVersion = saved.row.version;
    const flows = await json(admin.get(`${root}/sys/approval-flows?pageSize=100`));
    const summary: Flow | undefined = flows.items.find((flow: Flow) => flow.documentType === DOCUMENT_TYPE);
    originalFlow = summary ? await json(admin.get(`${root}/sys/approval-flows/${summary.id}`)) : undefined;
  });

  test.afterAll(async () => {
    const admin = apis.admin;
    try {
      if (admin && flowTouched) {
        const flows = await json(admin.get(`${root}/sys/approval-flows?pageSize=100`));
        const now: Flow | undefined = flows.items.find((flow: Flow) => flow.documentType === DOCUMENT_TYPE);
        if (now) {
          const current: Flow = await json(admin.get(`${root}/sys/approval-flows/${now.id}`));
          const restore = originalFlow
            ? { name: originalFlow.name, allowSelfApproval: originalFlow.allowSelfApproval, isActive: originalFlow.isActive,
                steps: originalFlow.steps, version: current.version }
            : { name: current.name, allowSelfApproval: current.allowSelfApproval, isActive: false, steps: current.steps,
                version: current.version };
          await json(admin.put(`${root}/sys/approval-flows/${now.id}`, { data: restore }));
        }
      }
    } finally {
      await Promise.all(Object.values(apis).map(api => api?.dispose()));
    }
  });

  /** Sets the AP.OTHER_PAYMENT flow: no steps (inactive) or one step approved by `manager`, self-approval off. */
  async function setFlow(steps: 'none' | 'manager'): Promise<void> {
    const admin = apis.admin!;
    flowTouched = true;
    const flows = await json(admin.get(`${root}/sys/approval-flows?pageSize=100`));
    const existing: Flow | undefined = flows.items.find((flow: Flow) => flow.documentType === DOCUMENT_TYPE);
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
    const input = { name: `E2E ${DOCUMENT_TYPE}`, allowSelfApproval: false,
      steps: [{ name: 'E2E 主管核准', approverType: 'User', roleId: null, userId: managerId, minAmount: null }] };
    if (existing) {
      const current: Flow = await json(admin.get(`${root}/sys/approval-flows/${existing.id}`));
      await json(admin.put(`${root}/sys/approval-flows/${existing.id}`, { data: { ...input, isActive: true, version: current.version } }));
    } else {
      await json(admin.post(`${root}/sys/approval-flows`, { data: { ...input, documentType: DOCUMENT_TYPE } }), 201);
    }
  }

  async function login(page: Page, username: 'finance' | 'viewer' = 'finance'): Promise<void> {
    await page.addInitScript(([key, value]) => localStorage.setItem(key, value),
      ['myerp.auth.session', JSON.stringify({ ...tokens[username], username })]);
    await page.goto('/');
    await expect(page.getByTestId('home-title')).toBeVisible();
    await expect(page.getByTestId('layout-username')).toHaveText(username);
    await expect(page.getByTestId('layout-company-select')).toContainText('TW01');
  }

  /** Filters the list to one withdrawal reference so the row is on the first page whatever else is saved. */
  async function showOnly(page: Page, reference: string): Promise<void> {
    await page.getByTestId('fi-op-keyword').fill(reference);
    await page.getByTestId('fi-op-search').click();
  }

  /** Creates a Draft through the API (finance) for tests that start from a saved document. */
  async function apiDraft(reference: string, amount: number, category = mapped): Promise<{ id: number; version: number; documentNo: string }> {
    return json(apis.finance!.post(`${root}/ap/other-payments`, { data: { payeeDescription: `E2E 收款人 ${reference}`,
      expenseCategoryId: category.id, bankAccountId: fund.id, bankReference: reference, businessDate: today(),
      amount: String(amount), remark: null } }), 201);
  }

  /** Searches and picks one option in the editor's category or fund select. */
  async function choose(page: Page, kind: 'category' | 'fund', item: Ref): Promise<void> {
    const search = page.getByPlaceholder(kind === 'category' ? '搜尋費用類別…' : '搜尋資金帳戶…');
    await search.fill(item.code);
    await search.locator('xpath=following-sibling::button[1]').click();
    await page.getByTestId(`fi-op-${kind}`).selectOption({ label: `${item.code} · ${item.name}` });
  }

  test.beforeEach(({ page }) => {
    consoleErrors = []; serverErrors = []; allowedConsole = [];
    page.on('console', message => { if (message.type() === 'error') consoleErrors.push(message.text()); });
    page.on('pageerror', error => consoleErrors.push(error.message));
    page.on('response', response => { if (response.status() >= 500) serverErrors.push(`${response.status()} ${response.url()}`); });
  });

  test.afterEach(async ({ page }) => {
    expect(await page.locator('body').innerText()).not.toMatch(/\b(fi|common|layout|errors)\.[a-zA-Z][A-Za-z_]+\.[A-Za-z][A-Za-z_.]+\b/);
    expect(serverErrors).toEqual([]);
    expect(consoleErrors.filter(message => !allowedConsole.some(pattern => pattern.test(message)))).toEqual([]);
  });

  test('FI-OP-REAL-001 finance creates and edits a draft on screen; SQL matches every field and nothing is posted', async ({ page }) => {
    const ap = apBalance(tw01);
    const amount = 10_000 + Math.floor(Math.random() * 9_000);
    const reference = `E2E-OP-${run}-A`;
    const payee = `E2E 大樓管理委員會 ${run}`;
    await login(page);
    await page.getByTestId('layout-menu-fi').click();
    await page.getByTestId('layout-menu-fi-other-payments').click();
    await expect(page).toHaveURL(/\/ap\/other-payments$/);
    await page.getByTestId('fi-op-create').click();
    await page.getByTestId('fi-op-payee').fill(`  ${payee}  `);
    await choose(page, 'category', mapped);
    await choose(page, 'fund', fund);
    await page.getByTestId('fi-op-reference').fill(`  ${reference}  `);
    await page.getByTestId('fi-op-date').fill(today());
    await page.getByTestId('fi-op-amount').fill(String(amount));
    await page.getByTestId('fi-op-save').click();
    await expect(page.getByTestId('fi-op-notice')).toContainText('付款已儲存，列表已更新。');

    const saved = savedPayment(tw01, reference);
    expect(saved, 'one draft saved with the trimmed reference').toBeDefined();
    expect(saved).toMatchObject({ expenseCategoryId: mapped.id, bankAccountId: fund.id, amount: exact(amount),
      status: 'Draft', currency: 'TWD', bankReference: reference, businessDate: today(), version: 1 });
    expect(utf16(saved!.payeeHex)).toBe(payee);
    // AP.OTHER_PAYMENT number rule seeded by ADR-212: OP + year-month + 4 digits.
    expect(saved!.documentNo).toMatch(new RegExp(`^OP${today().slice(0, 4)}${today().slice(5, 7)}\\d{4}$`));
    const editor = page.getByTestId('fi-op-editor');
    await expect(editor).toContainText(saved!.documentNo);
    await expect(editor).toContainText('草稿');
    await expect(editor).toContainText(`${grouped(amount)} TWD`);

    // Edit on screen: a new amount and payee are saved as version 2.
    const edited = amount + 500;
    await page.getByTestId('fi-op-edit').click();
    await page.getByTestId('fi-op-payee').fill(`${payee} 修`);
    await page.getByTestId('fi-op-amount').fill(String(edited));
    await page.getByTestId('fi-op-save').click();
    await expect(page.getByTestId('fi-op-notice')).toContainText('付款已儲存，列表已更新。');
    const changed = savedPayment(tw01, reference)!;
    expect(changed).toMatchObject({ id: saved!.id, amount: exact(edited), status: 'Draft', version: 2 });
    expect(utf16(changed.payeeHex)).toBe(`${payee} 修`);
    await expect(editor).toContainText(`${grouped(edited)} TWD`);

    await showOnly(page, reference);
    const row = page.getByTestId(`fi-op-row-${saved!.id}`);
    await expect(row).toContainText(saved!.documentNo);
    await expect(row).toContainText(`${grouped(edited)} TWD`);
    await expect(row).toContainText('草稿');
    await expect(page.getByTestId(`fi-op-map-${saved!.id}`)).toHaveText('可供過帳');
    expect(savedSource(tw01, saved!.id), 'a draft has no GL effect').toBeUndefined();
    expect(apBalance(tw01), 'a draft does not touch AP').toEqual(ap);
    await capture(page, 'draft-light-zh-Hant');
    draftReference = reference;
  });

  test('FI-OP-REAL-002 with no approval steps submit posts at once: Dr expense / Cr bank same amount, AP unchanged', async ({ page }) => {
    expect(draftReference, 'FI-OP-REAL-001 must pass first').toBeDefined();
    await setFlow('none');
    const draft = savedPayment(tw01, draftReference!)!;
    const ap = apBalance(tw01);
    await login(page);
    await page.goto('/ap/other-payments');
    await showOnly(page, draft.bankReference);
    await page.getByTestId(`fi-op-open-${draft.id}`).click();
    await page.getByTestId('fi-op-submit').click();
    await expect(page.getByTestId('fi-op-notice'), 'zero-step submit posts at once, so the notice must not say it waits')
      .toContainText(`付款 ${draft.documentNo} 已核准並過帳`);
    await expect(page.getByTestId('fi-op-notice')).not.toContainText('等待審批');
    const editor = page.getByTestId('fi-op-editor');
    await expect(editor).toContainText('已過帳');
    await expect(editor).toContainText('此付款正在審批或已過帳');
    await expect(page.getByTestId('fi-op-edit')).toHaveCount(0);
    await expect(page.getByTestId('fi-op-submit')).toHaveCount(0);
    await expect(page.getByTestId('fi-op-delete')).toHaveCount(0);

    expect(savedPayment(tw01, draft.bankReference)).toMatchObject({ status: 'Posted', amount: draft.amount });
    const source = savedSource(tw01, draft.id);
    expect(source).toMatchObject({ amount: draft.amount, expenseAccountCode: EXPENSE_ACCOUNT, mappingVersion,
      bankReference: draft.bankReference, baseCurrency: 'TWD' });
    expect(journalLines(tw01, source!.journalId)).toEqual([`${EXPENSE_ACCOUNT}:${draft.amount}:0.0000`,
      `${BANK_LEDGER}:0.0000:${draft.amount}`]);
    expect(apBalance(tw01), 'an other payment is not an AP payment').toEqual(ap);
    await expect(editor).toContainText(`#${source!.journalId}`);
    await expect(page.getByTestId(`fi-op-row-${draft.id}`)).toContainText(`#${source!.journalId}`);
    await expect(page.getByTestId(`fi-op-row-${draft.id}`)).toContainText('已過帳');
    await capture(page, 'posted-light-zh-Hant');

    // The API re-read agrees with the screen and SQL.
    const detail = await json(apis.finance!.get(`${root}/ap/other-payments/${draft.id}`));
    expect(detail).toMatchObject({ status: 'Posted', journalId: source!.journalId, documentNo: draft.documentNo });
  });

  test('FI-OP-REAL-003 with one SYS step submit only waits; the manager approval posts it', async ({ page }) => {
    await setFlow('manager');
    const amount = 20_000 + Math.floor(Math.random() * 9_000);
    const reference = `E2E-OP-${run}-B`;
    const draft = await apiDraft(reference, amount);
    const ap = apBalance(tw01);
    await login(page);
    await page.goto('/ap/other-payments');
    await showOnly(page, reference);
    await page.getByTestId(`fi-op-open-${draft.id}`).click();
    await page.getByTestId('fi-op-submit').click();
    await expect(page.getByTestId('fi-op-notice')).toContainText(`付款 ${draft.documentNo} 已送審，正在等待審批，尚未過帳。`);
    await expect(page.getByTestId('fi-op-editor')).toContainText('待審批');
    await expect(page.getByTestId('fi-op-withdraw')).toBeVisible();
    await expect(page.getByTestId('fi-op-edit')).toHaveCount(0);
    expect(savedPayment(tw01, reference)).toMatchObject({ status: 'Pending', amount: exact(amount) });
    expect(savedSource(tw01, draft.id), 'pending approval has no GL effect').toBeUndefined();
    await expect(page.getByTestId(`fi-op-row-${draft.id}`)).toContainText('待審批');
    await capture(page, 'pending-light-zh-Hant');

    const [instance] = sql<{ id: number }>(`SELECT TOP 1 id FROM sys_approval_instance WHERE company_id = ${tw01}
      AND document_type = '${DOCUMENT_TYPE}' AND document_id = ${draft.id} ORDER BY id DESC`);
    expect(instance).toBeDefined();
    const pending = await json(apis.manager!.get(`${root}/sys/approvals/${instance!.id}`));
    const approved = await json(apis.manager!.post(`${root}/sys/approvals/${instance!.id}/approve`, {
      data: { comment: 'E2E 核准其他付款', version: pending.version } }));
    expect(approved.status).toBe('Approved');

    await page.getByTestId('fi-op-search').click();
    await expect(page.getByTestId(`fi-op-row-${draft.id}`)).toContainText('已過帳');
    expect(savedPayment(tw01, reference)).toMatchObject({ status: 'Posted' });
    const source = savedSource(tw01, draft.id);
    expect(source).toMatchObject({ amount: exact(amount), expenseAccountCode: EXPENSE_ACCOUNT });
    expect(journalLines(tw01, source!.journalId)).toEqual([`${EXPENSE_ACCOUNT}:${exact(amount)}:0.0000`,
      `${BANK_LEDGER}:0.0000:${exact(amount)}`]);
    await expect(page.getByTestId(`fi-op-row-${draft.id}`)).toContainText(`#${source!.journalId}`);
    expect(apBalance(tw01)).toEqual(ap);
  });

  test('FI-OP-REAL-004 a stale version is refused with 409 and the newer saved values are kept', async ({ page }) => {
    const reference = `E2E-OP-${run}-C`;
    const draft = await apiDraft(reference, 3_000);
    allowedConsole.push(/Failed to load resource: the server responded with a status of 409/);
    await login(page);
    await page.goto('/ap/other-payments');
    await showOnly(page, reference);
    await page.getByTestId(`fi-op-open-${draft.id}`).click();
    await expect(page.getByTestId('fi-op-editor')).toContainText('草稿');
    // Someone else saves version 2 while the screen still holds version 1.
    await json(apis.finance!.put(`${root}/ap/other-payments/${draft.id}`, { data: { payeeDescription: `E2E 他人修改 ${run}`,
      expenseCategoryId: mapped.id, bankAccountId: fund.id, bankReference: reference, businessDate: today(), amount: '3100',
      remark: null, version: draft.version } }));
    await page.getByTestId('fi-op-edit').click();
    await page.getByTestId('fi-op-amount').fill('9999');
    await page.getByTestId('fi-op-save').click();
    await expect(page.getByTestId('fi-op-write-error')).toContainText('其他人已修改這筆付款或科目對照，請重新載入後再試。');
    await expect(page.getByTestId('fi-op-notice')).toHaveCount(0);
    await expect(page.getByTestId('fi-op-amount'), 'the typed value is kept for retry').toHaveValue('9999');
    const saved = savedPayment(tw01, reference)!;
    expect(saved).toMatchObject({ amount: '3100.0000', version: 2, status: 'Draft' });
    expect(utf16(saved.payeeHex)).toBe(`E2E 他人修改 ${run}`);
    await capture(page, 'version-conflict-light-zh-Hant');

    // Reloading (discarding the typed values) shows the newer saved version; nothing else was written.
    page.once('dialog', dialog => dialog.accept());
    await page.getByTestId('fi-op-reload').click();
    await expect(page.getByTestId('fi-op-editor')).toContainText('3,100 TWD');
    expect(savedPayment(tw01, reference)).toMatchObject({ version: 2, status: 'Draft' });
  });

  test('FI-OP-REAL-005 an unmapped category is refused at submit with 422; the draft stays unposted', async ({ page }) => {
    await setFlow('none');
    const reference = `E2E-OP-${run}-D`;
    const draft = await apiDraft(reference, 4_000, unmapped);
    const ap = apBalance(tw01);
    const journals = sql<{ n: number }>(`SELECT COUNT(*) AS n FROM gl_journal WHERE company_id = ${tw01}`)[0]!.n;
    allowedConsole.push(/Failed to load resource: the server responded with a status of 422/);
    await login(page);
    await page.goto('/ap/other-payments');
    await showOnly(page, reference);
    await expect(page.getByTestId(`fi-op-map-${draft.id}`)).toHaveText('缺少費用科目對照，請聯絡科目設定人員');
    await page.getByTestId(`fi-op-open-${draft.id}`).click();
    await page.getByTestId('fi-op-submit').click();
    await expect(page.getByTestId('fi-op-write-error')).toContainText('缺少費用科目對照；送審前請科目設定人員補上。');
    await expect(page.getByTestId('fi-op-notice')).toHaveCount(0);
    await expect(page.getByTestId('fi-op-submit')).toBeVisible();
    expect(savedPayment(tw01, reference)).toMatchObject({ status: 'Draft', version: draft.version });
    expect(savedSource(tw01, draft.id)).toBeUndefined();
    expect(sql<{ n: number }>(`SELECT COUNT(*) AS n FROM gl_journal WHERE company_id = ${tw01}`)[0]!.n).toBe(journals);
    expect(sql<{ n: number }>(`SELECT COUNT(*) AS n FROM sys_approval_instance WHERE company_id = ${tw01}
      AND document_type = '${DOCUMENT_TYPE}' AND document_id = ${draft.id}`)[0]!.n, 'the SYS round is rolled back').toBe(0);
    expect(apBalance(tw01)).toEqual(ap);
    await capture(page, 'mapping-missing-light-zh-Hant');
  });

  test('FI-OP-REAL-006 viewer reads payments and maps without any write action', async ({ page }) => {
    const reference = `E2E-OP-${run}-C`;
    const saved = savedPayment(tw01, reference);
    expect(saved, 'FI-OP-REAL-004 must create the draft first').toBeDefined();
    await login(page, 'viewer');
    await page.getByTestId('layout-menu-fi').click();
    await page.getByTestId('layout-menu-fi-other-payments').click();
    await expect(page.getByTestId('fi-op-create')).toHaveCount(0);
    await showOnly(page, reference);
    await expect(page.getByTestId(`fi-op-row-${saved!.id}`)).toContainText(saved!.documentNo);
    await page.getByTestId(`fi-op-open-${saved!.id}`).click();
    await expect(page.getByTestId('fi-op-editor')).toContainText('草稿');
    for (const action of ['edit', 'submit', 'withdraw', 'delete']) await expect(page.getByTestId(`fi-op-${action}`)).toHaveCount(0);
    await page.getByTestId('fi-op-manage-map').click();
    await expect(page.getByTestId('fi-op-map-panel')).toContainText(`${mapped.code} · ${mapped.name}`);
    await expect(page.getByTestId(`fi-op-map-edit-${mapped.id}`)).toHaveCount(0);
    // The API agrees: viewer cannot write, and nothing changed.
    const denied = await apis.viewer!.post(`${root}/ap/other-payments/${saved!.id}/submit`, { data: { version: saved!.version } });
    expect(denied.status()).toBe(403);
    expect(savedPayment(tw01, reference)).toMatchObject({ status: 'Draft', version: saved!.version });
    await capture(page, 'viewer-light-zh-Hant');
  });
});
