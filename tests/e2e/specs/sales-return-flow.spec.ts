import { mkdir } from 'node:fs/promises';
import { chooseLanguage, chooseTheme, expect, test } from '../support/fixtures';

// Sales return create / edit / submit (WU-SD-008b3c1): Interface 2.17 form against mocked APIs, Interface 2.16 detail actions.
// Every saved return echoes the posted request key and split, as the server does (matchesReturnReceipt).
type Status = 'Draft' | 'Pending' | 'Rejected' | 'Posted';
interface DraftBody {
  requestKey: string; businessDate: string; remark: string | null;
  lines: { lineNo: number; deliveryLineId: number; warehouseId: number; locationId: number | null; unbilledQuantity: number;
    billed: { invoiceLineId: number; quantity: number }[] }[];
}

const sourceLines = [
  { deliveryId: 61, deliveryDocumentNo: 'DH-0061', deliveryDate: '2026-11-21', deliveryLineId: 301, deliveryLineNo: 1, orderLineId: 91,
    orderLineNo: 1, itemId: 9, itemCode: 'ITEM-9', itemName: 'Bolt', warehouseId: 4, locationId: 12, trackingUnsupported: false,
    postedQuantity: 3, returnedQuantity: 0, returnableQuantity: 3, invoicedQuantity: 2, unbilledRemainingQuantity: 1,
    invoiceLines: [{ invoiceLineId: 401, invoiceId: 400, invoiceDocumentNo: 'SI-0400', invoiceDate: '2026-11-22', quantity: 2,
      creditedQuantity: 0, remainingQuantity: 2 }],
    warehouseCode: 'WH-1', warehouseName: 'Main', warehouseActive: true, locationCode: 'A-01', locationName: 'Aisle 1', locationActive: true },
  { deliveryId: 61, deliveryDocumentNo: 'DH-0061', deliveryDate: '2026-11-21', deliveryLineId: 302, deliveryLineNo: 2, orderLineId: 93,
    orderLineNo: 3, itemId: 11, itemCode: 'ITEM-11', itemName: 'Washer', warehouseId: 4, locationId: null, trackingUnsupported: false,
    postedQuantity: 1.5, returnedQuantity: 0, returnableQuantity: 1.5, invoicedQuantity: 0, unbilledRemainingQuantity: 1.5,
    invoiceLines: [], warehouseCode: 'WH-1', warehouseName: 'Main', warehouseActive: true }];
const source = { id: 81, documentNo: 'SO-0081', customerId: 7, customerCode: 'C-7', customerName: 'Saved customer',
  businessDate: '2026-11-20', currencyCode: 'USD', baseCurrencyCode: 'TWD', exchangeRate: 31.5, version: 2, lines: sourceLines };
const sourceList = { items: [{ id: 81, documentNo: 'SO-0081', customerId: 7, customerCode: 'C-7', customerName: 'Saved customer',
  businessDate: '2026-11-20', currencyCode: 'USD', openLineCount: 2, version: 2 }], totalCount: 1, page: 1, pageSize: 10 };
const warehouses = { items: [{ id: 4, code: 'WH-1', name: 'Main', trackingMode: null, isActive: true },
  { id: 5, code: 'WH-R', name: 'Returns', trackingMode: null, isActive: true }], totalCount: 2, page: 1, pageSize: 10 };
const locations = { items: [{ id: 20, code: 'R-01', name: 'Return rack', trackingMode: null, isActive: true }], totalCount: 1, page: 1, pageSize: 10 };
const places: Record<number, { code: string; name: string }> = { 4: { code: 'WH-1', name: 'Main' }, 5: { code: 'WH-R', name: 'Returns' } };
const bins: Record<number, { code: string; name: string }> = { 12: { code: 'A-01', name: 'Aisle 1' }, 20: { code: 'R-01', name: 'Return rack' } };

/** The saved return the server answers for a draft body (amounts are illustrative; INV movements once Posted). */
function detail(body: DraftBody, status: Status, version: number) {
  const lines = body.lines.map((line, index) => {
    const facts = sourceLines.find(candidate => candidate.deliveryLineId === line.deliveryLineId)!;
    const billed = line.billed.reduce((sum, portion) => sum + portion.quantity, 0);
    const location = line.locationId == null ? null : bins[line.locationId];
    return { id: 800 + index, lineNo: line.lineNo, deliveryId: 61, deliveryDocumentNo: 'DH-0061', deliveryLineId: line.deliveryLineId,
      orderLineId: facts.orderLineId, orderLineNo: facts.orderLineNo, itemId: facts.itemId, itemCode: facts.itemCode, itemName: facts.itemName,
      warehouseId: line.warehouseId, locationId: line.locationId, quantity: line.unbilledQuantity + billed,
      unbilledQuantity: line.unbilledQuantity, billedQuantity: billed, originalOutboundMovementId: 5000 + index,
      invMovementId: status === 'Posted' ? 9000 + index : null, originalNet: 10, originalTax: 0.5, originalGross: 10.5, baseNet: 315,
      baseTax: 15.75, baseGross: 330.75, allocations: line.billed.map((portion, at) => ({ id: 900 + at, invoiceId: 400,
        invoiceDocumentNo: 'SI-0400', invoiceLineId: portion.invoiceLineId, quantity: portion.quantity, originalNet: 5, originalTax: 0.25,
        originalGross: 5.25, baseNet: 157.5, baseTax: 7.88, baseGross: 165.38 })),
      warehouseCode: places[line.warehouseId].code, warehouseName: places[line.warehouseId].name, warehouseActive: true,
      locationCode: location?.code ?? null, locationName: location?.name ?? null, locationActive: location ? true : null };
  });
  return { id: 71, documentNo: 'SR-0071', requestKey: body.requestKey, orderId: 81, orderDocumentNo: 'SO-0081', customerId: 7,
    customerCode: 'C-7', customerName: 'Saved customer', businessDate: body.businessDate, currencyCode: 'USD', baseCurrencyCode: 'TWD',
    exchangeRate: 31.5, status, remark: body.remark, approvalInstanceId: status === 'Pending' ? 501 : null,
    postedAt: status === 'Posted' ? '2026-11-23T10:15:00' : null, fiCreditNoteId: status === 'Posted' ? 7001 : null,
    originalNet: 10 * lines.length, originalTax: 0.5 * lines.length, originalGross: 10.5 * lines.length, baseNet: 315 * lines.length,
    baseTax: 15.75 * lines.length, baseGross: 330.75 * lines.length, version, lines };
}
type Detail = ReturnType<typeof detail>;
const summary = (current: Detail | null) => ({ items: current ? [{ id: 71, documentNo: 'SR-0071', orderId: 81, orderDocumentNo: 'SO-0081',
  customerId: 7, customerName: 'Saved customer', businessDate: current.businessDate, currencyCode: 'USD', baseCurrencyCode: 'TWD',
  status: current.status, originalGross: current.originalGross, baseGross: current.baseGross, postedAt: current.postedAt,
  lineCount: current.lines.length, version: current.version }] : [], totalCount: current ? 1 : 0, page: 1, pageSize: 20 });

async function capture(page: import('@playwright/test').Page, name: string): Promise<void> {
  const path = '../../tests/qa/SD/screenshots';
  await mkdir(path, { recursive: true });
  await page.screenshot({ path: `${path}/2.17-flow-${name}.png`, fullPage: true, animations: 'disabled' });
}

/** Answers the form reads (source orders, destination options); returns false for the return document routes. */
function answerOptions(route: import('@playwright/test').Route, path: string): boolean {
  if (path.endsWith('/source-orders/81')) { void route.fulfill({ json: source }); return true; }
  if (path.endsWith('/source-orders')) { void route.fulfill({ json: sourceList }); return true; }
  if (path.endsWith('/warehouse-options/5/locations')) { void route.fulfill({ json: locations }); return true; }
  if (path.endsWith('/warehouse-options')) { void route.fulfill({ json: warehouses }); return true; }
  return false;
}

async function pick(page: import('@playwright/test').Page, testId: string, text: string): Promise<void> {
  await page.getByTestId(testId).click();
  await page.getByTestId('erp-select-dialog').getByText(text, { exact: true }).click();
  await page.getByTestId('erp-select-confirm').click();
}

/** Order SO-0081, date 2026-11-23, line 301 = 1 not invoiced + 0.5 credited on SI-0400, line 302 = 1.5 not invoiced. */
async function fillReturn(page: import('@playwright/test').Page): Promise<void> {
  await pick(page, 'sd-return-create-order-pick', 'SO-0081');
  await expect(page.getByTestId('sd-return-create-order')).toContainText('SO-0081 · C-7 · Saved customer');
  await expect(page.getByTestId('sd-return-create-rate')).toHaveText('31.5');
  await page.getByTestId('sd-return-create-date').fill('2026-11-23');
  await page.getByTestId('sd-return-create-select-301').check();
  await page.getByTestId('sd-return-create-unbilled-301').fill('1');
  await page.getByTestId('sd-return-create-billed-301-401').fill('0.5');
  await page.getByTestId('sd-return-create-select-302').check();
  await page.getByTestId('sd-return-create-unbilled-302').fill('1.5');
}

const parsed = (body: string): DraftBody => JSON.parse(body) as DraftBody;
const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;

test('SD-RETURN-E2E-001 creates a return with a new destination, then submits it and it posts with no approval step, in three languages and two themes', async ({ page }) => {
  let current: Detail | null = null;
  const writes: string[] = [];
  await page.route('**/api/v1/sal/returns**', route => {
    const path = new URL(route.request().url()).pathname;
    if (answerOptions(route, path)) return;
    const method = route.request().method();
    if (method === 'POST' && path.endsWith('/71/submit')) {
      writes.push(`submit ${route.request().postData()}`);
      current = postedFrom(current!);
      return route.fulfill({ json: current });
    }
    if (method === 'POST') {
      writes.push(`create ${route.request().postData()}`);
      current = detail(parsed(route.request().postData() ?? ''), 'Draft', 1);
      return route.fulfill({ status: 201, json: current });
    }
    if (path.endsWith('/71')) return route.fulfill({ json: current });
    return route.fulfill({ json: summary(current) });
  });
  await page.goto('/sal/returns');
  await expect(page.getByTestId('sd-return-table')).toContainText('尚無銷退單');
  await page.getByTestId('sd-return-new').click();
  await expect(page).toHaveURL(/\/sal\/returns\/new$/);
  await expect(page.getByTestId('sd-return-create-intro')).toContainText('儲存只會建立草稿');
  await capture(page, 'create-empty-light-zh-Hant');
  await fillReturn(page);
  await expect(page.getByTestId('sd-return-create-line-301')).toContainText('未開票（最多 1）');
  await expect(page.getByTestId('sd-return-create-line-301')).toContainText('沖減 SI-0400（2026-11-22，最多 2）');
  await expect(page.getByTestId('sd-return-create-total-301')).toHaveText('1.5');
  await expect(page.getByTestId('sd-return-create-count')).toContainText('已選 2 / 2 行');
  await expect(page.getByTestId('sd-return-create-destination-text-301')).toHaveText('WH-1 · Main / A-01 · Aisle 1');
  // Another warehouse clears the location; the new location is then picked from that warehouse.
  await pick(page, 'sd-return-create-warehouse-pick-301', 'WH-R');
  await expect(page.getByTestId('sd-return-create-destination-text-301')).toHaveText('WH-R · Returns');
  await pick(page, 'sd-return-create-location-pick-301', 'R-01');
  await expect(page.getByTestId('sd-return-create-destination-text-301')).toHaveText('WH-R · Returns / R-01 · Return rack');
  await capture(page, 'create-filled-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'create-filled-dark-zh-Hant'); await chooseTheme(page, 'light');
  await chooseLanguage(page, 'en');
  await expect(page.getByTestId('sd-return-create-line-301')).toContainText('Not invoiced (up to 1)');
  await capture(page, 'create-filled-light-en');
  await chooseLanguage(page, 'zh-Hans');
  await expect(page.getByTestId('sd-return-create-line-301')).toContainText('未开票（最多 1）');
  await capture(page, 'create-filled-light-zh-Hans');
  await chooseLanguage(page, 'zh-Hant');

  await page.getByTestId('sd-return-create-submit').click();
  await expect(page.locator('.ant-message')).toContainText('銷售退貨 SR-0071 已儲存為草稿。');
  // The list opens the saved return from `?open=71` and then drops the parameter from the address.
  await expect(page.getByTestId('sd-return-detail-title')).toContainText('SR-0071');
  await expect(page).toHaveURL(/\/sal\/returns$/);
  await expect(page.getByTestId('sd-return-status-71')).toContainText('草稿');
  await expect(page.getByTestId('sd-return-state')).toContainText('尚未影響庫存與應收');
  await expect(page.getByTestId('sd-return-destination-800')).toContainText('WH-R · Returns');
  expect(writes).toHaveLength(1);
  const created = parsed(writes[0].slice('create '.length));
  expect(created.requestKey).toMatch(UUID);
  expect({ ...created, requestKey: 'k' }).toEqual({ requestKey: 'k', businessDate: '2026-11-23', remark: null, lines: [
    { lineNo: 1, deliveryLineId: 301, warehouseId: 5, locationId: 20, unbilledQuantity: 1, billed: [{ invoiceLineId: 401, quantity: 0.5 }] },
    { lineNo: 2, deliveryLineId: 302, warehouseId: 4, locationId: null, unbilledQuantity: 1.5, billed: [] }] });
  await capture(page, 'draft-detail-light-zh-Hant');

  await page.getByTestId('sd-return-submit').click();
  await expect(page.getByTestId('erp-message-title')).toContainText('SR-0071');
  await page.getByTestId('erp-message-ok').click();
  await expect(page.getByTestId('sd-return-action-notice')).toContainText('無需審批，已過帳（版本 3）');
  await expect(page.getByTestId('sd-return-status-71')).toContainText('已過帳');
  await expect(page.getByTestId('sd-return-state')).toContainText('貨品已入庫');
  await expect(page.getByTestId('sd-return-fi-credit')).toContainText('7001');
  await expect(page.getByTestId('sd-return-submit')).toHaveCount(0);
  await expect(page.getByTestId('sd-return-edit')).toHaveCount(0);
  expect(writes[1]).toBe('submit {"version":1}');
  await capture(page, 'posted-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'posted-dark-zh-Hant'); await chooseTheme(page, 'light');
  await chooseLanguage(page, 'en');
  await expect(page.getByTestId('sd-return-action-notice')).toContainText('needed no approval and was posted (version 3)');
  await capture(page, 'posted-light-en');
  await chooseLanguage(page, 'zh-Hans');
  await expect(page.getByTestId('sd-return-action-notice')).toContainText('无需审批，已过账（版本 3）');
  await capture(page, 'posted-light-zh-Hans');
});

/** SUBMIT with no applicable SYS step: Posted two versions on (Pending, then Posted in the same request, as the real API answers), INV receipts and the FI credit note. */
function postedFrom(saved: Detail): Detail {
  return { ...saved, status: 'Posted', version: saved.version + 2, postedAt: '2026-11-23T10:15:00', fiCreditNoteId: 7001,
    lines: saved.lines.map((line, index) => ({ ...line, invMovementId: 9000 + index })) };
}

test('SD-RETURN-E2E-002 revises a Rejected return back to Draft with the saved request key, then submits it for approval', async ({ page }) => {
  const savedKey = 'key-rejected-71';
  let current: Detail = detail({ requestKey: savedKey, businessDate: '2026-11-23', remark: 'Damaged', lines: [
    { lineNo: 1, deliveryLineId: 301, warehouseId: 4, locationId: 12, unbilledQuantity: 1, billed: [{ invoiceLineId: 401, quantity: 0.5 }] }] },
  'Rejected', 3);
  const writes: string[] = [];
  await page.route('**/api/v1/sal/returns**', route => {
    const path = new URL(route.request().url()).pathname;
    if (answerOptions(route, path)) return;
    const method = route.request().method();
    if (method === 'PUT') {
      writes.push(`put ${route.request().postData()}`);
      const sent = JSON.parse(route.request().postData() ?? '') as { version: number; draft: DraftBody };
      current = detail(sent.draft, 'Draft', sent.version + 1);
      return route.fulfill({ json: current });
    }
    if (method === 'POST' && path.endsWith('/71/submit')) {
      writes.push(`submit ${route.request().postData()}`);
      current = { ...current, status: 'Pending', approvalInstanceId: 501, version: current.version + 1 };
      return route.fulfill({ json: current });
    }
    if (path.endsWith('/71')) return route.fulfill({ json: current });
    return route.fulfill({ json: summary(current) });
  });
  await page.goto('/sal/returns');
  await page.getByTestId('sd-return-open-71').click();
  await expect(page.getByTestId('sd-return-state')).toContainText('已駁回');
  await expect(page.getByTestId('sd-return-submit')).toHaveCount(0);
  await expect(page.getByTestId('sd-return-rejected-hint')).toBeVisible();
  await page.getByTestId('sd-return-edit').click();
  await expect(page).toHaveURL(/\/sal\/returns\/71\/edit$/);
  await expect(page.getByTestId('sd-return-edit-identity')).toContainText('SR-0071');
  await expect(page.getByTestId('sd-return-edit-rejected')).toContainText('儲存變更後會回到草稿');
  await expect(page.getByTestId('sd-return-create-unbilled-301')).toHaveValue('1');
  await expect(page.getByTestId('sd-return-create-billed-301-401')).toHaveValue('0.5');
  await expect(page.getByTestId('sd-return-create-remark')).toHaveValue('Damaged');
  await capture(page, 'edit-rejected-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'edit-rejected-dark-zh-Hant'); await chooseTheme(page, 'light');
  await page.getByTestId('sd-return-create-unbilled-301').fill('0.5');
  await pick(page, 'sd-return-create-warehouse-pick-301', 'WH-R');
  await page.getByTestId('sd-return-create-submit').click();
  await expect(page.locator('.ant-message')).toContainText('銷退單 SR-0071 已修訂並回到草稿');
  // The list opens the saved return from `?open=71` and then drops the parameter from the address.
  await expect(page.getByTestId('sd-return-detail-title')).toContainText('SR-0071');
  await expect(page).toHaveURL(/\/sal\/returns$/);
  await expect(page.getByTestId('sd-return-status-71')).toContainText('草稿');
  await expect(page.getByTestId('sd-return-destination-800')).toContainText('WH-R · Returns');
  expect(writes).toEqual([`put {"version":3,"draft":{"requestKey":"${savedKey}","businessDate":"2026-11-23","remark":"Damaged","lines":[` +
    '{"lineNo":1,"deliveryLineId":301,"warehouseId":5,"locationId":null,"unbilledQuantity":0.5,"billed":[{"invoiceLineId":401,"quantity":0.5}]}]}}']);
  await capture(page, 'revised-light-zh-Hant');

  await page.getByTestId('sd-return-submit').click();
  await page.getByTestId('erp-message-ok').click();
  await expect(page.getByTestId('sd-return-action-notice')).toContainText('已送審，等待核准（版本 5）');
  await expect(page.getByTestId('sd-return-status-71')).toContainText('審批中');
  await expect(page.getByTestId('sd-return-edit')).toHaveCount(0);
  expect(writes[1]).toBe('submit {"version":4}');
  await capture(page, 'pending-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'pending-dark-zh-Hant'); await chooseTheme(page, 'light');
  await chooseLanguage(page, 'en');
  await expect(page.getByTestId('sd-return-action-notice')).toContainText('submitted and is pending approval (version 5)');
  await capture(page, 'pending-light-en');
});

test('SD-RETURN-E2E-003 an inactive destination warehouse is refused and focused; an unknown 5xx is resent with the same request key and replays', async ({ page, pageErrors }) => {
  pageErrors.allowApi5xx(/\/api\/v1\/sal\/returns/, 500);
  pageErrors.allow(/Failed to load resource: the server responded with a status of (422|500)/);
  let current: Detail | null = null;
  const bodies: string[] = [];
  const replies: ({ status: number; json: object } | 'save')[] = [
    { status: 422, json: { status: 422, errorCode: 'MD.WAREHOUSE.INACTIVE', lineNo: 1, traceId: 't-wh' } },
    { status: 500, json: { status: 500, traceId: 'trace-sr-500' } },
    'save'];
  await page.route('**/api/v1/sal/returns**', route => {
    const path = new URL(route.request().url()).pathname;
    if (answerOptions(route, path)) return;
    if (route.request().method() === 'POST') {
      const body = route.request().postData() ?? '';
      bodies.push(body);
      const reply = replies.shift()!;
      if (reply !== 'save') return route.fulfill(reply);
      current = detail(parsed(body), 'Draft', 1);
      return route.fulfill({ status: 200, json: current });
    }
    if (path.endsWith('/71')) return route.fulfill({ json: current });
    return route.fulfill({ json: summary(current) });
  });
  await page.goto('/sal/returns/new');
  await fillReturn(page);
  await pick(page, 'sd-return-create-warehouse-pick-301', 'WH-R');
  await page.getByTestId('sd-return-create-submit').click();
  await expect(page.getByTestId('sd-return-create-error')).toContainText('此倉庫已停用。');
  await expect(page.getByTestId('sd-return-create-line-error-301')).toContainText('此倉庫已停用。');
  await expect(page.getByTestId('sd-return-create-warehouse-pick-301')).toBeFocused();
  await expect(page.getByTestId('sd-return-create-uncertain')).toHaveCount(0);
  await capture(page, 'inactive-warehouse-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'inactive-warehouse-dark-zh-Hant'); await chooseTheme(page, 'light');
  await chooseLanguage(page, 'en');
  await expect(page.getByTestId('sd-return-create-line-error-301')).toContainText('inactive');
  await capture(page, 'inactive-warehouse-light-en');
  await chooseLanguage(page, 'zh-Hant');

  // Pick another warehouse; the server then fails without a code, so the outcome is unknown.
  await pick(page, 'sd-return-create-warehouse-pick-301', 'WH-1');
  await page.getByTestId('sd-return-create-submit').click();
  await expect(page.getByTestId('sd-return-create-uncertain')).toContainText('無法確定是否已儲存');
  await expect(page.getByTestId('sd-return-create-uncertain-trace')).toContainText('trace-sr-500');
  await expect(page).toHaveURL(/\/sal\/returns\/new$/);
  await capture(page, 'uncertain-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'uncertain-dark-zh-Hant'); await chooseTheme(page, 'light');
  await page.getByTestId('sd-return-create-resend').click();
  await expect(page.locator('.ant-message')).toContainText('銷售退貨 SR-0071 先前已儲存，沒有重複建立。');
  // The list opens the saved return from `?open=71` and then drops the parameter from the address.
  await expect(page.getByTestId('sd-return-detail-title')).toContainText('SR-0071');
  await expect(page).toHaveURL(/\/sal\/returns$/);
  await expect(page.getByTestId('sd-return-status-71')).toContainText('草稿');

  expect(bodies).toHaveLength(3);
  const [refused, unknown, resent] = bodies.map(parsed);
  // A definite refusal starts a new request key; the resend after an unknown outcome repeats the identical request.
  expect(unknown.requestKey).not.toBe(refused.requestKey);
  expect(bodies[2]).toBe(bodies[1]);
  expect(refused.lines[0]).toMatchObject({ deliveryLineId: 301, warehouseId: 5, locationId: null });
  expect(resent.lines[0]).toMatchObject({ deliveryLineId: 301, warehouseId: 4, locationId: null });
});
