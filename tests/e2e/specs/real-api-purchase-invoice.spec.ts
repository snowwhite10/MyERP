import { mkdir, readFile } from 'node:fs/promises';
import { expect, request, test, type APIRequestContext, type Page } from '@playwright/test';
import { chooseLanguage, chooseTheme } from '../support/fixtures';
import { realApiLogin, type RealApiTokens } from '../support/real-api';
import { companyId, sql, sqlArrange, utf16 } from '../support/real-api-sql';

// WU-PUR-008c3c3 (PUR-INV-REAL-001–006): screen 4.19 MATCH / RESOLVE / CSV export against the running API and SQL Server.
// Purchase orders and receipts have no HTTP API yet (006/007), so each test source is arranged with sqlcmd (ADR-204);
// invoices are created through POST /api/v1/pur/invoices and every later step goes through the screen. Each screen result
// is checked against the rows SQL Server saved. Invoice numbers carry a per-run prefix: invoices are never deleted.
// Run: & ./tools/scripts/Invoke-RealApiE2E.ps1 -Specs specs/real-api-purchase-invoice
const apiUrl = process.env['E2E_API_URL'];
const RUN = `RE2E-${Date.now().toString(36).toUpperCase()}`;
const TODAY = '2026-11-26';
const PO_PRICE = 2;
const TAX = 5;

type Source = { orderId: number; receiptLineId: number };
type Invoice = { id: number; version: number; number: string; source: Source };
type SavedInvoice = { status: string; version: number; totalGross: string };
type SavedLine = { number: string; status: string; businessDate: string; supplierId: number; orderId: number; currency: string;
  lineNo: number; receiptLineId: number; quantity: string; unitPrice: string; taxRate: string; net: string; tax: string; gross: string };

const savedInvoice = (id: number) => sql<SavedInvoice>(`SELECT status, version, CONVERT(varchar(40), total_gross) AS totalGross
  FROM pur_purchase_invoice WHERE id = ${id}`)[0]!;
const reserved = (receiptLineId: number) => Number(sql<{ q: string }>(`SELECT CONVERT(varchar(40), matched_reserved) AS q
  FROM pur_purchase_receipt_line WHERE id = ${receiptLineId}`)[0]!.q);
const resolutions = (id: number) => sql<{ action: string; invoiceVersion: number; lineNo: number; reasonHex: string; gross: string }>(
  `SELECT action, invoice_version AS invoiceVersion, line_no AS [lineNo], CONVERT(varchar(40), gross_difference) AS gross,
   CONVERT(varchar(max), CONVERT(varbinary(max), reason), 2) AS reasonHex FROM pur_purchase_invoice_resolution
   WHERE invoice_id = ${id} ORDER BY id`);

async function capture(page: Page, name: string): Promise<void> {
  const directory = '../../tests/qa/PUR/screenshots';
  await mkdir(directory, { recursive: true });
  await page.screenshot({ path: `${directory}/4.19-real-${name}.png`, fullPage: true, animations: 'disabled' });
}

test.describe('真實 API 採購發票比對／差異決定／匯出（SQL 讀回雙斷言）', () => {
  test.describe.configure({ mode: 'serial' });
  test.skip(!apiUrl, '需要 E2E_API_URL：請用 tools/scripts/Invoke-RealApiE2E.ps1 執行');
  test.setTimeout(180_000);

  const tokens: Partial<Record<'purchase' | 'viewer', RealApiTokens>> = {};
  let company = 0; let supplierId = 0; let materialId = 0; let userId = 0;
  const invoices: Record<'reject' | 'accept' | 'exact' | 'first' | 'second', Invoice> = {} as never;
  let consoleErrors: string[]; let serverErrors: string[]; let allowedConsole: RegExp[];

  /** An Approved order line (qty × 2 TWD, 5 %) and a Posted receipt line that received `received`. */
  function arrangeSource(received: number): Source {
    return sqlArrange<Source>(`
      DECLARE @o bigint, @ol bigint, @r bigint, @rl bigint;
      INSERT dbo.pur_purchase_order(company_id, supplier_id, currency, status, is_deleted, created_at, created_by, version)
        VALUES (${company}, ${supplierId}, 'TWD', 'Approved', 0, '${TODAY}', ${userId}, 1); SET @o = SCOPE_IDENTITY();
      INSERT dbo.pur_purchase_order_line(company_id, purchase_order_id, line_no, material_id, quantity, unit_price, tax_rate_percent,
        created_at, created_by, version) VALUES (${company}, @o, 1, ${materialId}, ${received}, ${PO_PRICE}, ${TAX}, '${TODAY}', ${userId}, 1);
      SET @ol = SCOPE_IDENTITY();
      INSERT dbo.pur_purchase_receipt(company_id, purchase_order_id, supplier_id, currency, status, is_deleted, created_at, created_by, version)
        VALUES (${company}, @o, ${supplierId}, 'TWD', 'Posted', 0, '${TODAY}', ${userId}, 1); SET @r = SCOPE_IDENTITY();
      INSERT dbo.pur_purchase_receipt_line(company_id, purchase_receipt_id, purchase_order_line_id, line_no, material_id, posted_received,
        posted_returned, pending_return_reserved, posted_invoiced, matched_reserved, created_at, created_by, version)
        VALUES (${company}, @r, @ol, 1, ${materialId}, ${received}, 0, 0, 0, 0, '${TODAY}', ${userId}, 1); SET @rl = SCOPE_IDENTITY()`,
    'SELECT @o AS orderId, @rl AS receiptLineId')[0]!;
  }

  async function createDraft(api: APIRequestContext, suffix: string, source: Source, quantity: number, unitPrice: number): Promise<Invoice> {
    const net = quantity * unitPrice; const tax = net * TAX / 100;
    const number = `${RUN}-${suffix}`;
    const response = await api.post('/api/v1/pur/invoices', { data: { supplierId, purchaseOrderId: source.orderId, currency: 'TWD',
      businessDate: TODAY, supplierInvoiceNumber: number, lines: [{ lineNumber: 1, receiptLineId: source.receiptLineId, quantity,
        unitPrice, taxRatePercent: TAX, net, tax, gross: net + tax }] } });
    expect(response.status(), await response.text()).toBe(201);
    const saved = await response.json() as { id: number; version: number };
    return { ...saved, number, source };
  }

  test.beforeAll(async () => {
    test.setTimeout(240_000); // realApiLogin may wait out the login rate-limit window
    tokens.purchase = await realApiLogin(apiUrl!, 'purchase', 'Demo@2026');
    tokens.viewer = await realApiLogin(apiUrl!, 'viewer', 'Demo@2026');
    company = companyId('TW01');
    supplierId = sql<{ id: number }>(`SELECT TOP 1 id FROM md_supplier WHERE company_id = ${company} AND is_deleted = 0 ORDER BY id`)[0]?.id ?? 0;
    materialId = sql<{ id: number }>(`SELECT TOP 1 id FROM md_item WHERE company_id = ${company} AND is_deleted = 0 ORDER BY id`)[0]?.id ?? 0;
    userId = sql<{ id: number }>(`SELECT id FROM sys_user WHERE normalized_username = 'PURCHASE'`)[0]!.id;
    expect(supplierId, 'TW01 supplier: run --seed').toBeGreaterThan(0);
    expect(materialId, 'TW01 item: run --seed').toBeGreaterThan(0);
    const api = await request.newContext({ baseURL: apiUrl,
      extraHTTPHeaders: { Authorization: `Bearer ${tokens.purchase.accessToken}`, 'X-Company-Id': String(company) } });
    try {
      // 0.5 above the order price → Disputed on MATCH; the order price → Matched; two drafts of 3 share a receipt of 4.
      invoices.reject = await createDraft(api, 'REJ', arrangeSource(10), 4, 2.5);
      invoices.accept = await createDraft(api, 'ACC', arrangeSource(10), 4, 2.5);
      invoices.exact = await createDraft(api, 'EXA', arrangeSource(10), 4, PO_PRICE);
      const shared = arrangeSource(4);
      invoices.first = await createDraft(api, 'SH1', shared, 3, PO_PRICE);
      invoices.second = await createDraft(api, 'SH2', shared, 3, PO_PRICE);
    } finally { await api.dispose(); }
  });

  async function login(page: Page, username: 'purchase' | 'viewer'): Promise<void> {
    await page.addInitScript(([key, value]) => localStorage.setItem(key, value),
      ['myerp.auth.session', JSON.stringify({ ...tokens[username], username })]);
    await page.goto('/');
    await expect(page.getByTestId('layout-username')).toHaveText(username);
    await expect(page.getByTestId('layout-company-select')).toContainText('TW01');
  }

  /** Opens screen 4.19 filtered to this run and the detail of one invoice. */
  async function open(page: Page, invoice: Invoice): Promise<void> {
    await page.goto('/pur/purchase-invoices');
    await page.getByTestId('pur-invoice-keyword').fill(RUN);
    await page.getByTestId('pur-invoice-search').click();
    await page.getByTestId(`pur-invoice-view-${invoice.id}`).click();
    await expect(page.getByTestId('pur-invoice-detail')).toHaveAttribute('aria-label', invoice.number);
  }

  async function match(page: Page): Promise<void> {
    await page.getByTestId('pur-invoice-match').click();
    await page.getByRole('dialog').getByRole('button', { name: '比對' }).click();
  }

  test.beforeEach(({ page }) => {
    consoleErrors = []; serverErrors = []; allowedConsole = [];
    page.on('console', message => { if (message.type() === 'error') consoleErrors.push(message.text()); });
    page.on('pageerror', error => consoleErrors.push(error.message));
    page.on('response', response => { if (response.status() >= 500) serverErrors.push(`${response.status()} ${response.url()}`); });
  });

  test.afterEach(async ({ page }) => {
    expect(await page.locator('body').innerText()).not.toMatch(/\b(pur|common|layout|errors)\.[a-z][A-Za-z]+\.[a-z][A-Za-z.]+\b/);
    expect(serverErrors).toEqual([]);
    expect(consoleErrors.filter(message => !allowedConsole.some(pattern => pattern.test(message)))).toEqual([]);
  });

  test('PUR-INV-REAL-001 MATCH with a price difference disputes; rejecting returns Draft and releases the reservation', async ({ page }) => {
    const invoice = invoices.reject;
    await login(page, 'purchase');
    await open(page, invoice);
    await expect(page.getByTestId('pur-invoice-detail-status')).toHaveText('草稿');
    await capture(page, 'draft-light-zh-Hant');
    await match(page);
    await expect(page.getByText(`發票 ${invoice.number} 有單價或稅額差異，請在下方逐項接受或拒絕。`)).toBeVisible();
    await expect(page.getByTestId('pur-invoice-detail-status')).toHaveText('有差異');
    await expect(page.getByTestId('pur-invoice-dispute-1')).toContainText('單價 2 → 2.5 (+0.5)');
    // Data: Disputed, version +1 and the receipt quantity reserved.
    const disputed = savedInvoice(invoice.id);
    expect(disputed).toMatchObject({ status: 'Disputed', version: invoice.version + 1 });
    expect(reserved(invoice.source.receiptLineId)).toBe(4);
    await expect(page.getByTestId(`pur-invoice-status-${invoice.id}`)).toHaveText('有差異');
    await capture(page, 'disputed-light-zh-Hant');
    await chooseTheme(page, 'dark'); await capture(page, 'disputed-dark-zh-Hant'); await chooseTheme(page, 'light');

    await page.getByTestId('pur-invoice-reject-1').check();
    await page.getByTestId('pur-invoice-reason-1').fill('  單價未經同意  ');
    await page.getByTestId('pur-invoice-resolve-submit').click();
    await expect(page.getByRole('dialog')).toContainText('接受 0 項、拒絕 1 項');
    await page.getByRole('dialog').getByRole('button', { name: '提交決定' }).click();
    await expect(page.getByText(`已保存決定。發票 ${invoice.number} 已退回草稿並釋回預留數量，請修訂發票後再比對。`)).toBeVisible();
    await expect(page.getByTestId('pur-invoice-detail-status')).toHaveText('草稿');
    await expect(page.getByTestId('pur-invoice-resolve')).toHaveCount(0);
    // Data: Draft again, reservation released, one Reject decision with the trimmed reason against the disputed version.
    expect(savedInvoice(invoice.id).status).toBe('Draft');
    expect(reserved(invoice.source.receiptLineId)).toBe(0);
    const [decision, ...more] = resolutions(invoice.id);
    expect(more).toEqual([]);
    expect(decision).toMatchObject({ action: 'Reject', invoiceVersion: disputed.version, lineNo: 1 });
    expect(utf16(decision!.reasonHex)).toBe('單價未經同意');
    expect(Number(decision!.gross)).toBe(2.1); // (2.5 − 2) × 4 × 1.05
    const history = page.getByTestId('pur-invoice-history').locator('tbody tr');
    await expect(history).toHaveCount(1);
    await expect(history.first()).toContainText('拒絕');
    await expect(history.first()).toContainText('單價未經同意');
    await expect(history.first()).toContainText('+2.1');
    await expect(page.getByTestId('pur-invoice-match')).toBeEnabled();
    await capture(page, 'rejected-draft-light-zh-Hant');
  });

  test('PUR-INV-REAL-002 accepting every difference returns Matched and keeps the reservation', async ({ page }) => {
    const invoice = invoices.accept;
    await login(page, 'purchase');
    await open(page, invoice);
    await match(page);
    await expect(page.getByTestId('pur-invoice-detail-status')).toHaveText('有差異');
    await page.getByTestId('pur-invoice-accept-1').check();
    await page.getByTestId('pur-invoice-reason-1').fill('供應商附加包裝費，已確認');
    await page.getByTestId('pur-invoice-resolve-submit').click();
    await expect(page.getByRole('dialog')).toContainText('接受 1 項、拒絕 0 項');
    await page.getByRole('dialog').getByRole('button', { name: '提交決定' }).click();
    await expect(page.getByText(`已保存決定。發票 ${invoice.number} 已比對，尚未過帳到應付帳款。`)).toBeVisible();
    await expect(page.getByTestId('pur-invoice-detail-status')).toHaveText('已比對');
    await expect(page.getByTestId('pur-invoice-match')).toHaveCount(0);
    expect(savedInvoice(invoice.id).status).toBe('Matched');
    expect(reserved(invoice.source.receiptLineId)).toBe(4);
    expect(resolutions(invoice.id).map(r => r.action)).toEqual(['Accept']);
    await expect(page.getByTestId('pur-invoice-history').locator('tbody tr')).toHaveCount(1);
    await capture(page, 'accepted-matched-light-zh-Hant');
  });

  test('PUR-INV-REAL-003 the order price matches directly; a second draft over the remaining receipt is refused unchanged', async ({ page }) => {
    allowedConsole.push(/Failed to load resource: the server responded with a status of 422/);
    await login(page, 'purchase');
    await open(page, invoices.exact);
    await match(page);
    await expect(page.getByText(`發票 ${invoices.exact.number} 已比對，尚未過帳到應付帳款。`)).toBeVisible();
    await expect(page.getByTestId('pur-invoice-detail-status')).toHaveText('已比對');
    expect(savedInvoice(invoices.exact.id).status).toBe('Matched');
    expect(resolutions(invoices.exact.id)).toEqual([]);
    await expect(page.getByTestId('pur-invoice-no-history')).toBeVisible();

    // Receipt of 4: the first draft of 3 reserves it, the second (also 3) is short.
    await open(page, invoices.first);
    await match(page);
    await expect(page.getByTestId('pur-invoice-detail-status')).toHaveText('已比對');
    expect(reserved(invoices.first.source.receiptLineId)).toBe(3);
    await open(page, invoices.second);
    await match(page);
    await expect(page.getByTestId('pur-invoice-action-error')).toContainText('收貨可開票數量不足');
    await expect(page.getByTestId('pur-invoice-detail-status')).toHaveText('草稿');
    expect(savedInvoice(invoices.second.id)).toMatchObject({ status: 'Draft', version: invoices.second.version });
    expect(reserved(invoices.second.source.receiptLineId)).toBe(3);
    await capture(page, 'shortage-light-zh-Hant');
  });

  test('PUR-INV-REAL-004 the exported file equals the filtered list and the saved lines, in two languages', async ({ page }) => {
    const saved = sql<SavedLine>(`SELECT i.supplier_invoice_number AS number, i.status, CONVERT(varchar(10), i.business_date, 23) AS businessDate,
      i.supplier_id AS supplierId, i.purchase_order_id AS orderId, i.currency, l.line_no AS [lineNo], l.receipt_line_id AS receiptLineId,
      CONVERT(varchar(40), l.quantity) AS quantity, CONVERT(varchar(40), l.unit_price) AS unitPrice,
      CONVERT(varchar(40), l.tax_rate_percent) AS taxRate, CONVERT(varchar(40), l.net) AS net, CONVERT(varchar(40), l.tax) AS tax,
      CONVERT(varchar(40), l.gross) AS gross
      FROM pur_purchase_invoice i JOIN pur_purchase_invoice_line l ON l.invoice_id = i.id AND l.company_id = i.company_id
      WHERE i.company_id = ${company} AND i.is_deleted = 0 AND i.supplier_invoice_number LIKE '${RUN}-%'`);
    expect(saved).toHaveLength(5);
    const statusText: Record<string, Record<string, string>> = {
      'zh-Hant': { Draft: '草稿', Matched: '已比對', Disputed: '有差異' }, en: { Draft: 'Draft', Matched: 'Matched', Disputed: 'Disputed' } };
    await login(page, 'purchase');
    await page.goto('/pur/purchase-invoices');
    await page.getByTestId('pur-invoice-keyword').fill(RUN);
    await page.getByTestId('pur-invoice-search').click();
    const rows = page.locator('[data-testid^="pur-invoice-row-"]');
    await expect(rows).toHaveCount(5);
    const listed = await page.locator('[data-testid^="pur-invoice-view-"]').allInnerTexts();
    for (const language of ['zh-Hant', 'en'] as const) {
      if (language === 'en') await chooseLanguage(page, 'en');
      const download = page.waitForEvent('download');
      await page.getByTestId('pur-invoice-export').click();
      const file = await download;
      expect(file.suggestedFilename()).toBe('purchase-invoices.csv');
      const text = (await readFile((await file.path())!)).toString('utf8');
      expect(text.startsWith('﻿')).toBe(true);
      const [header, ...lines] = text.slice(1).split('\r\n').filter(line => line.length > 0);
      expect(header!.split(',')).toHaveLength(14);
      // Data: one row per saved line, in the list order, with the saved values and the status in this language.
      expect(lines).toHaveLength(saved.length);
      expect([...new Set(lines.map(line => line.split(',')[0]))]).toEqual(listed);
      const expected = saved.map(s => [s.number, s.businessDate, statusText[language]![s.status], s.supplierId, s.orderId, s.currency,
        s.lineNo, s.receiptLineId, s.quantity, s.unitPrice, s.taxRate, s.net, s.tax, s.gross].join(','));
      expect([...lines].sort()).toEqual([...expected].sort());
      // Screen: success style with the same line count.
      const notice = page.getByTestId('pur-invoice-export-notice');
      await expect(notice.locator('.ant-alert-success')).toHaveCount(1);
      await expect(notice).toContainText(String(saved.length));
      await capture(page, `export-light-${language}`);
    }
  });

  test('PUR-INV-REAL-005 viewer reads the run without MATCH, RESOLVE or export', async ({ page }) => {
    // A disputed invoice for the read-only view: MATCH the first-rejected draft again through the API as purchase.
    const api = await request.newContext({ baseURL: apiUrl,
      extraHTTPHeaders: { Authorization: `Bearer ${tokens.purchase!.accessToken}`, 'X-Company-Id': String(company) } });
    try {
      const current = savedInvoice(invoices.reject.id);
      const response = await api.post(`/api/v1/pur/invoices/${invoices.reject.id}/match?version=${current.version}`);
      expect(response.status(), await response.text()).toBe(200);
    } finally { await api.dispose(); }
    expect(savedInvoice(invoices.reject.id).status).toBe('Disputed');
    await login(page, 'viewer');
    await page.goto('/pur/purchase-invoices');
    await page.getByTestId('pur-invoice-keyword').fill(RUN);
    await page.getByTestId('pur-invoice-search').click();
    await expect(page.locator('[data-testid^="pur-invoice-row-"]')).toHaveCount(5);
    await expect(page.getByTestId('pur-invoice-export')).toHaveCount(0);
    await page.getByTestId(`pur-invoice-view-${invoices.second.id}`).click();
    await expect(page.getByTestId('pur-invoice-detail-status')).toHaveText('草稿');
    await expect(page.getByTestId('pur-invoice-match')).toHaveCount(0);
    await page.getByTestId(`pur-invoice-view-${invoices.reject.id}`).click();
    await expect(page.getByTestId('pur-invoice-detail-status')).toHaveText('有差異');
    await expect(page.getByTestId('pur-invoice-resolve-permission')).toBeVisible();
    await expect(page.getByTestId('pur-invoice-resolve-submit')).toHaveCount(0);
    await expect(page.getByTestId('pur-invoice-reject-1')).toHaveCount(0);
    await capture(page, 'viewer-disputed-light-zh-Hant');
  });
});
