import { mkdir } from 'node:fs/promises';
import { chooseLanguage, chooseTheme, expect, test } from '../support/fixtures';

/** WU-SD-005b2b: the order form looks up the dated price rule; the rule price equals the price the test keeps, so no override. */
async function mockQuote(page: import('@playwright/test').Page, unitPrice: number): Promise<void> {
  await page.route('**/api/v1/sal/price-rules/quote**', route => route.fulfill({ json: { asOf: '2028-02-29', ruleId: 5,
    ruleVersion: 1, itemId: 9, customerId: null, currencyCode: 'TWD', unitPrice, discountCeiling: 10,
    validFrom: '2028-01-01', validTo: '2028-12-31' } }));
}

async function capture(page: import('@playwright/test').Page, name: string): Promise<void> {
  const path = '../../tests/qa/SD/screenshots';
  await mkdir(path, { recursive: true });
  await page.screenshot({ path: `${path}/2.5-${name}.png`, fullPage: true, animations: 'disabled' });
}

test('SD-ORDER-UI-008/009 manual draft reports success only after the saved receipt', async ({ page }) => {
  let body = '';
  let writes = 0;
  const saved = { id: 81, documentNo: 'SO-0081', status: 'Draft', customerId: 7,
    customerCode: 'C-7', customerName: 'Customer Seven', businessDate: '2028-02-29',
    currencyCode: 'TWD', sourceQuotationId: null,
    lines: [{ id: 1, lineNo: 1, itemId: 9, itemCode: 'I-9', itemName: 'Item Nine' }] };
  await page.route('**/api/v1/md/customers**', route => route.fulfill({ json: {
    items: [{ id: 7, code: 'C-7', name: 'Customer Seven', isActive: true, reviewStatus: 'Approved' }], totalCount: 1 } }));
  await page.route('**/api/v1/md/items**', route => route.fulfill({ json: {
    items: [{ id: 9, code: 'I-9', name: 'Item Nine', isActive: true }], totalCount: 1 } }));
  await page.route('**/api/v1/md/tax-codes**', route => route.fulfill({ json: { items:
    route.request().url().includes('/rates') ? [{ id: 4, taxCodeId: 2, effectiveFrom: '2028-01-01', ratePercent: 5 }]
      : [{ id: 2, code: 'OUT', name: 'Output VAT', taxType: 'Output', isActive: true }], totalCount: 1 } }));
  await page.route('**/api/v1/sal/orders**', route => {
    if (route.request().method() === 'POST') { writes++; body = route.request().postData() ?? '';
      return route.fulfill({ status: 201, json: saved }); }
    if (route.request().url().endsWith('/81/created')) return route.fulfill({ json: saved });
    return route.fulfill({ json: { items: writes ? [saved] : [], totalCount: writes } });
  });
  await mockQuote(page, 0.000001);
  await page.goto('/sal/orders/new');
  await expect(page.getByTestId('sd-order-create')).toBeVisible();
  await expect(page.getByTestId('sd-order-create')).toContainText('訂單至少需要一行明細');
  await capture(page, 'normal-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'normal-dark-zh-Hant');
  await chooseTheme(page, 'light'); await chooseLanguage(page, 'en'); await capture(page, 'normal-light-en');
  await chooseLanguage(page, 'zh-Hans'); await capture(page, 'normal-light-zh-Hans');
  await chooseLanguage(page, 'zh-Hant');
  await page.getByTestId('sd-order-create-save').click();
  await expect(page.getByTestId('sd-order-create-errors')).toBeVisible();
  await expect(page.getByTestId('sd-order-create')).toContainText('請選擇有效的銷項稅率');
  await expect(page.getByTestId('sd-order-create')).not.toContainText('稅率 ID');
  await expect(page.getByTestId('sd-order-create-customer-pick')).toBeFocused();
  expect(writes).toBe(0);
  await capture(page, 'validation-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'validation-dark-zh-Hant');
  await chooseTheme(page, 'light');
  await page.getByTestId('sd-order-create-customer-pick').click();
  await page.getByText('C-7', { exact: true }).last().click();
  await page.getByTestId('erp-select-confirm').click();
  await page.getByTestId('sd-order-create-save').click();
  await expect(page.getByTestId('sd-order-create-date')).toBeFocused();
  expect(writes).toBe(0);
  await page.getByTestId('sd-order-create-item-pick-0').click();
  await page.getByText('I-9', { exact: true }).last().click();
  await page.getByTestId('erp-select-confirm').click();
  await page.getByTestId('sd-order-create-date').fill('2028-02-29');
  await page.getByTestId('sd-order-create-quantity-0').fill('1.000001');
  await page.getByTestId('sd-order-create-price-0').fill('0.000001');
  await page.getByTestId('sd-order-create-tax-pick-0').click();
  await page.getByText('OUT', { exact: true }).last().click();
  await page.getByTestId('erp-select-confirm').click();
  await page.getByText('5%', { exact: true }).last().click();
  await page.getByTestId('erp-select-confirm').click();
  await page.getByTestId('sd-order-create-save').click();
  await expect(page.getByTestId('sd-order-create-success')).toContainText('SO-0081');
  await expect(page.getByTestId('sd-order-create-receipt')).toContainText('Customer Seven');
  await capture(page, 'success-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'success-dark-zh-Hant');
  expect(body).toContain('"quantity":1.000001');
  expect(body).toContain('"unitPrice":0.000001');
  expect(body).toContain('"pricingRuleId":5,"pricingRuleVersion":1}');
  expect(writes).toBe(1);
});

test('SD-ORDER-UI-009/010 conversion reads a saved receipt and keeps selection on conflict', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 409/);
  let writes = 0;
  let reads = 0;
  let conflict = false;
  await page.route('**/api/v1/sal/quotations**', route => route.fulfill({ json: { items: [
    { id: 17, documentNo: 'SQ-0017', customerName: 'Customer Seven', status: 'Approved' }], totalCount: 1 } }));
  await page.route('**/api/v1/sal/orders**', route => {
    const url = route.request().url();
    if (url.endsWith('/from-quotation/17')) {
      writes++;
      if (conflict) return route.fulfill({ status: 409, json: { errorCode: 'SAL.QUOTATION.ALREADY_CONVERTED' } });
      return route.fulfill({ status: 201, json: { id: 82 } });
    }
    if (url.endsWith('/82/created')) { reads++; return route.fulfill({ json: { id: 82, documentNo: 'SO-0082', status: 'Draft',
      customerName: 'Customer Seven', sourceQuotationId: 17, lines: [{ id: 1 }] } }); }
    return route.fulfill({ json: { items: [], totalCount: 0 } });
  });
  await page.goto('/sal/orders/new');
  await page.getByRole('button', { name: /選擇報價|选择报价|Choose quotation/i }).click();
  await page.getByText('SQ-0017', { exact: true }).last().click();
  await page.getByTestId('erp-select-confirm').click();
  await page.getByTestId('sd-order-create-convert').click();
  await expect(page.getByTestId('sd-order-create-success')).toContainText('SO-0082');
  await expect(page.getByTestId('sd-order-create-receipt')).toContainText('Customer Seven');
  expect({ writes, reads }).toEqual({ writes: 1, reads: 1 });

  await page.getByRole('button', { name: /再建一張|再建一张|Create another/i }).click();
  conflict = true;
  await page.getByRole('button', { name: /選擇報價|选择报价|Choose quotation/i }).click();
  await page.getByText('SQ-0017', { exact: true }).last().click();
  await page.getByTestId('erp-select-confirm').click();
  await page.getByTestId('sd-order-create-convert').click();
  await expect(page.getByTestId('sd-order-create-quotation')).toContainText('SQ-0017');
  await expect(page.getByTestId('sd-order-create-error')).toBeVisible();
  await expect(page.getByTestId('sd-order-create-error')).not.toContainText('errors.SAL');
  await expect(page.getByTestId('sd-order-create-success')).toHaveCount(0);
  expect({ writes, reads }).toEqual({ writes: 2, reads: 1 });
  await capture(page, 'conflict-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'conflict-dark-zh-Hant');
});

for (const status of [400, 500] as const) {
  test(`SD-ORDER-UI-014 conversion ${status} keeps saved data unchanged and explains the failure`, async ({ page, pageErrors }) => {
    pageErrors.allow(new RegExp(`Failed to load resource: the server responded with a status of ${status}`));
    if (status === 500) pageErrors.allowApi5xx(/\/api\/v1\/sal\/orders\/from-quotation\/17$/, 500);
    let writes = 0;
    await page.route('**/api/v1/sal/quotations**', route => route.fulfill({ json: { items: [
      { id: 17, documentNo: 'SQ-0017', customerName: 'Customer Seven', status: 'Approved' }], totalCount: 1 } }));
    await page.route('**/api/v1/sal/orders**', route => {
      if (route.request().url().endsWith('/from-quotation/17')) {
        writes++;
        return route.fulfill({ status, json: status === 400
          ? { errorCode: 'COMMON.REQUEST.VALIDATION_FAILED', errors: { quotationId: ['Quotation is unavailable'] } }
          : { errorCode: 'COMMON.UNEXPECTED', traceId: 'trace-order-create-500' } });
      }
      return route.fulfill({ json: { items: [], totalCount: 0 } });
    });
    await page.goto('/sal/orders/new');
    await page.getByRole('button', { name: /選擇報價|选择报价|Choose quotation/i }).click();
    await page.getByText('SQ-0017', { exact: true }).last().click();
    await page.getByTestId('erp-select-confirm').click();
    await page.getByTestId('sd-order-create-convert').click();
    if (status === 400) {
      await expect(page.getByTestId('sd-order-create-error')).toContainText('報價：');
      await expect(page.getByTestId('sd-order-create-error')).not.toContainText('Quotation is unavailable');
      await expect(page.getByTestId('sd-order-create-quotation')).toContainText('SQ-0017');
    } else {
      await expect(page.getByTestId('sd-order-create-error')).toContainText('trace-order-create-500');
      await expect(page.getByTestId('sd-order-create-uncertain')).toBeVisible();
      await expect(page.getByTestId('sd-order-create-convert')).toBeDisabled();
    }
    await expect(page.getByTestId('sd-order-create-success')).toHaveCount(0);
    expect(writes).toBe(1);
    await capture(page, `server-${status}-light-zh-Hant`);
    await chooseTheme(page, 'dark'); await capture(page, `server-${status}-dark-zh-Hant`);
  });
}

test('SD-ORDER-UI-014 conversion 422 explains the rejected field without claiming a saved order', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 422/);
  let writes = 0;
  await page.route('**/api/v1/sal/quotations**', route => route.fulfill({ json: { items: [
    { id: 17, documentNo: 'SQ-0017', customerName: 'Customer Seven', status: 'Approved' }], totalCount: 1 } }));
  await page.route('**/api/v1/sal/orders**', route => {
    if (route.request().url().endsWith('/from-quotation/17')) {
      writes++;
      return route.fulfill({ status: 422, json: { errorCode: 'SAL.ORDER.INVALID_SOURCE',
        errors: { customerId: ['Customer is inactive'] } } });
    }
    return route.fulfill({ json: { items: [], totalCount: 0 } });
  });
  await page.goto('/sal/orders/new');
  await page.getByRole('button', { name: /選擇報價|选择报价|Choose quotation/i }).click();
  await page.getByText('SQ-0017', { exact: true }).last().click();
  await page.getByTestId('erp-select-confirm').click();
  await page.getByTestId('sd-order-create-convert').click();
  await expect(page.getByTestId('sd-order-create-error')).toContainText('客戶：');
  await expect(page.getByTestId('sd-order-create-error')).not.toContainText('Customer is inactive');
  await expect(page.getByTestId('sd-order-create-error')).not.toContainText('未預期');
  await expect(page.getByTestId('sd-order-create-success')).toHaveCount(0);
  await expect(page.getByTestId('sd-order-create-quotation')).toContainText('SQ-0017');
  const captureDir = '../../tests/qa/SD/screenshots';
  await mkdir(captureDir, { recursive: true });
  await page.screenshot({ path: `${captureDir}/2.5-rejected-light-zh-Hant.png`, fullPage: true, animations: 'disabled' });
  await chooseTheme(page, 'dark');
  await page.screenshot({ path: `${captureDir}/2.5-rejected-dark-zh-Hant.png`, fullPage: true, animations: 'disabled' });
  expect(writes).toBe(1);
});

test('SD-ORDER-UI-012/013 hidden or no longer approved quote cannot produce a receipt', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 404/);
  let writes = 0;
  let receiptReads = 0;
  await page.route('**/api/v1/sal/quotations**', route => {
    expect(new URL(route.request().url()).searchParams.get('status')).toBe('Approved');
    return route.fulfill({ json: { items: [
      { id: 17, documentNo: 'SQ-0017', customerName: 'Customer Seven', status: 'Approved' }], totalCount: 1 } });
  });
  await page.route('**/api/v1/sal/orders**', route => {
    if (route.request().url().endsWith('/from-quotation/17')) {
      writes++;
      return route.fulfill({ status: 404, json: { errorCode: 'SAL.QUOTATION.NOT_FOUND' } });
    }
    if (route.request().url().endsWith('/created')) receiptReads++;
    return route.fulfill({ json: { items: [], totalCount: 0 } });
  });
  await page.goto('/sal/orders/new');
  await page.getByRole('button', { name: '選擇報價' }).click();
  await page.getByText('SQ-0017', { exact: true }).last().click();
  await page.getByTestId('erp-select-confirm').click();
  await page.getByTestId('sd-order-create-convert').click();
  await expect(page.getByTestId('sd-order-create-error')).toBeVisible();
  await expect(page.getByTestId('sd-order-create-error')).not.toContainText('errors.SAL');
  await expect(page.getByTestId('sd-order-create-quotation')).toContainText('SQ-0017');
  await expect(page.getByTestId('sd-order-create-success')).toHaveCount(0);
  expect({ writes, receiptReads }).toEqual({ writes: 1, receiptReads: 0 });
  await capture(page, 'hidden-quotation-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'hidden-quotation-dark-zh-Hant');
});

test('SD-ORDER-UI-008 browser stops at 1000 lines and refuses an incomplete draft', async ({ page }) => {
  test.setTimeout(60_000);
  let writes = 0;
  await page.route('**/api/v1/sal/orders**', route => {
    if (route.request().method() === 'POST') writes++;
    return route.fulfill({ json: { items: [], totalCount: 0 } });
  });
  await page.goto('/sal/orders/new');
  await expect(page.getByTestId('sd-order-create-add-line')).toBeVisible();
  await page.evaluate(() => {
    const button = document.querySelector<HTMLButtonElement>('[data-testid="sd-order-create-add-line"]');
    if (!button) throw new Error('Add-line button is missing');
    for (let index = 1; index < 1000; index++) button.click();
  });
  await expect(page.getByTestId('sd-order-create-item-pick-999')).toBeVisible();
  await expect(page.getByTestId('sd-order-create-add-line')).toBeDisabled();
  await expect(page.getByTestId('sd-order-create-line-limit')).toBeVisible();
  await page.getByTestId('sd-order-create-save').click();
  await expect(page.getByTestId('sd-order-create-errors')).toBeVisible();
  expect(writes).toBe(0);
});

for (const language of ['zh-Hans', 'en'] as const) {
  test(`SD-ORDER-UI-014 ${language} rejection and server error remain translated in both themes`, async ({ page, pageErrors }) => {
    pageErrors.allow(/Failed to load resource: the server responded with a status of (422|500)/);
    pageErrors.allowApi5xx(/\/api\/v1\/sal\/orders\/from-quotation\/17$/, 500);
    let status = 422;
    let writes = 0;
    await page.route('**/api/v1/sal/quotations**', route => route.fulfill({ json: { items: [
      { id: 17, documentNo: 'SQ-0017', customerName: 'Customer Seven', status: 'Approved' }], totalCount: 1 } }));
    await page.route('**/api/v1/sal/orders**', route => {
      if (route.request().url().endsWith('/from-quotation/17')) {
        writes++;
        return route.fulfill({ status, json: status === 422
          ? { errorCode: 'SAL.ORDER.INVALID_SOURCE', errors: { customerId: ['Customer is inactive'] } }
          : { errorCode: 'COMMON.UNEXPECTED', traceId: `trace-${language}-500` } });
      }
      return route.fulfill({ json: { items: [], totalCount: 0 } });
    });
    await page.goto('/sal/orders/new');
    await chooseLanguage(page, language);
    await page.getByRole('button', { name: /選擇報價|选择报价|Choose quotation/i }).click();
    await page.getByText('SQ-0017', { exact: true }).last().click();
    await page.getByTestId('erp-select-confirm').click();
    await page.getByTestId('sd-order-create-convert').click();
    const reason = language === 'en' ? 'Customer is inactive. Choose an active customer.' : '客户已停用，请重新选择有效客户。';
    await expect(page.getByTestId('sd-order-create-error')).toContainText(reason);
    await expect(page.getByTestId('sd-order-create-success')).toHaveCount(0);
    await capture(page, `rejected-light-${language}`);
    await chooseTheme(page, 'dark'); await capture(page, `rejected-dark-${language}`);
    await page.getByRole('button', { name: /選擇報價|选择报价|Choose quotation/i }).click();
    await page.getByText('SQ-0017', { exact: true }).last().click();
    await page.getByTestId('erp-select-confirm').click();
    status = 500;
    await page.getByTestId('sd-order-create-convert').click();
    await expect(page.getByTestId('sd-order-create-error')).toContainText(`trace-${language}-500`);
    await expect(page.getByTestId('sd-order-create-uncertain')).toBeVisible();
    await expect(page.getByTestId('sd-order-create-success')).toHaveCount(0);
    expect(writes).toBe(2);
    await capture(page, `server-500-dark-${language}`);
    await chooseTheme(page, 'light'); await capture(page, `server-500-light-${language}`);
  });
}

for (const language of ['zh-Hans', 'en'] as const) {
  test(`SD-ORDER-UI-014 ${language} validation and conversion conflict explain no saved order`, async ({ page, pageErrors }) => {
    pageErrors.allow(/Failed to load resource: the server responded with a status of (400|409)/);
    let status = 400;
    let writes = 0;
    await page.route('**/api/v1/sal/quotations**', route => route.fulfill({ json: { items: [
      { id: 17, documentNo: 'SQ-0017', customerName: 'Customer Seven', status: 'Approved' }], totalCount: 1 } }));
    await page.route('**/api/v1/sal/orders**', route => {
      if (route.request().url().endsWith('/from-quotation/17')) {
        writes++;
        return route.fulfill({ status, json: status === 400
          ? { errorCode: 'COMMON.REQUEST.VALIDATION_FAILED', errors: { quotationId: ['Quotation is unavailable'] } }
          : { errorCode: 'SAL.QUOTATION.ALREADY_CONVERTED' } });
      }
      return route.fulfill({ json: { items: [], totalCount: 0 } });
    });
    await page.goto('/sal/orders/new');
    await chooseLanguage(page, language);
    await page.getByRole('button', { name: /選擇報價|选择报价|Choose quotation/i }).click();
    await page.getByText('SQ-0017', { exact: true }).last().click();
    await page.getByTestId('erp-select-confirm').click();
    await page.getByTestId('sd-order-create-convert').click();
    await expect(page.getByTestId('sd-order-create-error')).toContainText(
      language === 'en' ? 'Quotation is unavailable' : '报价不可使用');
    await expect(page.getByTestId('sd-order-create-success')).toHaveCount(0);
    await capture(page, `validation-400-light-${language}`);
    await chooseTheme(page, 'dark'); await capture(page, `validation-400-dark-${language}`);
    status = 409;
    await page.getByTestId('sd-order-create-convert').click();
    await expect(page.getByTestId('sd-order-create-error')).toContainText(
      language === 'en' ? 'already converted' : '已转成订单');
    await expect(page.getByTestId('sd-order-create-quotation')).toContainText('SQ-0017');
    await expect(page.getByTestId('sd-order-create-success')).toHaveCount(0);
    expect(writes).toBe(2);
    await capture(page, `conflict-409-dark-${language}`);
    await chooseTheme(page, 'light'); await capture(page, `conflict-409-light-${language}`);
  });
}

test('SD-ORDER-UI-012 MD customer picker follows the active company and clears an old choice', async ({ page }) => {
  const companies: string[] = [];
  await page.route('**/api/v1/md/customers**', route => {
    const company = route.request().headers()['x-company-id'] ?? '';
    companies.push(company);
    const customer = company === '2'
      ? { id: 8, code: 'HK-C', name: 'Hong Kong Customer', isActive: true, reviewStatus: 'Approved' }
      : { id: 7, code: 'TW-C', name: 'Taipei Customer', isActive: true, reviewStatus: 'Approved' };
    return route.fulfill({ json: { items: [customer], totalCount: 1 } });
  });
  await page.goto('/sal/orders/new');
  await page.getByTestId('sd-order-create-customer-pick').click();
  await expect(page.getByRole('dialog')).toContainText('TW-C');
  await page.getByText('TW-C', { exact: true }).last().click();
  await page.getByTestId('erp-select-confirm').click();
  await expect(page.getByTestId('sd-order-create-customer')).toContainText('TW-C');
  await page.getByTestId('layout-company-select').click();
  await page.locator('nz-option-item', { hasText: 'HK01 Hong Kong' }).click();
  await expect(page.getByTestId('sd-order-create-customer')).toContainText('尚未選擇客戶');
  await page.getByTestId('sd-order-create-customer-pick').click();
  await expect(page.getByRole('dialog')).toContainText('HK-C');
  await expect(page.getByRole('dialog')).not.toContainText('TW-C');
  expect(companies).toContain('1');
  expect(companies).toContain('2');
  await capture(page, 'company-picker-light-zh-Hant');
  await page.getByTestId('erp-select-cancel').click();
  await chooseTheme(page, 'dark');
  await page.getByTestId('sd-order-create-customer-pick').click();
  await expect(page.getByRole('dialog')).toContainText('HK-C');
  await capture(page, 'company-picker-dark-zh-Hant');
});

for (const caseInfo of [
  { code: 'SAL.QUOTATION.INVALID_STATUS', reason: '報價狀態已改變' },
  { code: 'SAL.QUOTATION.EXPIRED', reason: '報價已過期' },
] as const) {
  test(`SD-ORDER-UI-013 ${caseInfo.code} shows its reason after source state changes`, async ({ page, pageErrors }) => {
    pageErrors.allow(/Failed to load resource: the server responded with a status of 422/);
    let writes = 0;
    await page.route('**/api/v1/sal/quotations**', route => route.fulfill({ json: { items: [
      { id: 17, documentNo: 'SQ-0017', customerName: 'Customer Seven', status: 'Approved' }], totalCount: 1 } }));
    await page.route('**/api/v1/sal/orders**', route => {
      if (route.request().url().endsWith('/from-quotation/17')) {
        writes++;
        return route.fulfill({ status: 422, json: { errorCode: caseInfo.code } });
      }
      return route.fulfill({ json: { items: [], totalCount: 0 } });
    });
    await page.goto('/sal/orders/new');
    await page.getByRole('button', { name: '選擇報價' }).click();
    await page.getByText('SQ-0017', { exact: true }).last().click();
    await page.getByTestId('erp-select-confirm').click();
    await page.getByTestId('sd-order-create-convert').click();
    await expect(page.getByTestId('sd-order-create-error')).toContainText(caseInfo.reason);
    await expect(page.getByTestId('sd-order-create-success')).toHaveCount(0);
    await expect(page.getByTestId('sd-order-create-quotation')).toContainText('SQ-0017');
    expect(writes).toBe(1);
    await capture(page, `${caseInfo.code.endsWith('EXPIRED') ? 'expired' : 'status-changed'}-light-zh-Hant`);
    await chooseTheme(page, 'dark');
    await capture(page, `${caseInfo.code.endsWith('EXPIRED') ? 'expired' : 'status-changed'}-dark-zh-Hant`);
  });
}

test.describe('SD-ORDER-UI-009/012 CREATE-only browser access', () => {
  test.use({ authorization: { companies: [{ id: 1, code: 'TW01', name: 'Taipei', isDefault: true }],
    companyId: 1, isFullAccess: false,
    permissions: [{ code: 'SAL.ORDER.CREATE', dataScope: 'All' }] } });
  test('opens Create without a VIEW-only back link or order-list GET', async ({ page }) => {
    let listReads = 0;
    await page.route('**/api/v1/sal/orders**', route => {
      if (route.request().method() === 'GET') listReads++;
      return route.fulfill({ json: { items: [], totalCount: 0 } });
    });
    await page.goto('/sal/orders/new');
    await expect(page.getByTestId('sd-order-create')).toBeVisible();
    await expect(page.getByRole('link', { name: /返回訂單|返回订单|Back to orders/i })).toHaveCount(0);
    expect(listReads).toBe(0);
    await capture(page, 'create-only-light-zh-Hant');
    await chooseTheme(page, 'dark'); await capture(page, 'create-only-dark-zh-Hant');
  });
});

test('SD-ORDER-UI-016 empty approved-quotation selector explains disabled conversion', async ({ page }) => {
  let writes = 0;
  await page.route('**/api/v1/sal/quotations**', route => route.fulfill({ json: { items: [], totalCount: 0 } }));
  await page.route('**/api/v1/sal/orders**', route => {
    if (route.request().method() === 'POST') writes++;
    return route.fulfill({ json: { items: [], totalCount: 0 } });
  });
  await page.goto('/sal/orders/new');
  await expect(page.getByTestId('sd-order-create-convert')).toBeDisabled();
  await expect(page.getByTestId('sd-order-create-convert-reason')).toContainText('請先選擇已核准報價');
  await page.getByRole('button', { name: '選擇報價' }).click();
  await expect(page.getByTestId('erp-select-confirm')).toBeDisabled();
  await expect(page.getByRole('dialog')).toContainText('暫無資料');
  await capture(page, 'empty-quotation-light-zh-Hant');
  await page.getByTestId('erp-select-cancel').click();
  await chooseTheme(page, 'dark');
  await page.getByRole('button', { name: '選擇報價' }).click();
  await expect(page.getByRole('dialog')).toContainText('暫無資料');
  await capture(page, 'empty-quotation-dark-zh-Hant');
  expect(writes).toBe(0);
});

for (const receipt of [
  { id: 99, documentNo: 'SO-0099', status: 'Draft', customerName: 'Customer Seven', lines: [{ id: 1 }] },
  { id: 82, documentNo: 'SO-0082', status: 'Draft', customerName: 'Customer Seven', lines: [] },
  { id: 82, documentNo: 'SO-0082', status: 'Draft', customerName: 'Customer Seven',
    sourceQuotationId: 18, lines: [{ id: 1 }] },
] as const) {
  test(`SD-ORDER-UI-011 mismatched or empty receipt ${receipt.id}/${receipt.lines.length}/${'sourceQuotationId' in receipt ? receipt.sourceQuotationId : 'none'} never claims success`, async ({ page }) => {
    let writes = 0;
    await page.route('**/api/v1/sal/quotations**', route => route.fulfill({ json: { items: [
      { id: 17, documentNo: 'SQ-0017', customerName: 'Customer Seven', status: 'Approved' }], totalCount: 1 } }));
    await page.route('**/api/v1/sal/orders**', route => {
      const url = route.request().url();
      if (url.endsWith('/from-quotation/17')) { writes++; return route.fulfill({ status: 201, json: { id: 82 } }); }
      if (url.endsWith('/82/created')) return route.fulfill({ json: receipt });
      return route.fulfill({ json: { items: [], totalCount: 0 } });
    });
    await page.goto('/sal/orders/new');
    await page.getByRole('button', { name: '選擇報價' }).click();
    await page.getByText('SQ-0017', { exact: true }).last().click();
    await page.getByTestId('erp-select-confirm').click();
    await page.getByTestId('sd-order-create-convert').click();
    await expect(page.getByTestId('sd-order-create-uncertain')).toBeVisible();
    await expect(page.getByTestId('sd-order-create-success')).toHaveCount(0);
    await expect(page.getByTestId('sd-order-create-convert')).toBeDisabled();
    expect(writes).toBe(1);
  });
}

test('SD-ORDER-UI-014 receipt read 500 keeps outcome uncertain and exposes trace ID', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 500/);
  pageErrors.allowApi5xx(/\/api\/v1\/sal\/orders\/82\/created$/, 500);
  let writes = 0;
  await page.route('**/api/v1/sal/quotations**', route => route.fulfill({ json: { items: [
    { id: 17, documentNo: 'SQ-0017', customerName: 'Customer Seven', status: 'Approved' }], totalCount: 1 } }));
  await page.route('**/api/v1/sal/orders**', route => {
    const url = route.request().url();
    if (url.endsWith('/from-quotation/17')) { writes++; return route.fulfill({ status: 201, json: { id: 82 } }); }
    if (url.endsWith('/82/created')) return route.fulfill({ status: 500, json: { traceId: 'trace-receipt-read' } });
    return route.fulfill({ json: { items: [], totalCount: 0 } });
  });
  await page.goto('/sal/orders/new');
  await page.getByRole('button', { name: '選擇報價' }).click();
  await page.getByText('SQ-0017', { exact: true }).last().click();
  await page.getByTestId('erp-select-confirm').click();
  await page.getByTestId('sd-order-create-convert').click();
  await expect(page.getByTestId('sd-order-create-uncertain')).toBeVisible();
  await expect(page.getByTestId('sd-order-create-error')).toContainText('trace-receipt-read');
  await expect(page.getByTestId('sd-order-create-success')).toHaveCount(0);
  await expect(page.getByTestId('sd-order-create-convert')).toBeDisabled();
  expect(writes).toBe(1);
});

test.describe('SD-ORDER-UI-012 missing CREATE permission', () => {
  test.use({ authorization: { companies: [{ id: 1, code: 'TW01', name: 'Taipei', isDefault: true }],
    companyId: 1, isFullAccess: false,
    permissions: [{ code: 'SAL.ORDER.VIEW', dataScope: 'All' }] } });
  test('blocks direct navigation before a write', async ({ page }) => {
    let writes = 0;
    await page.route('**/api/v1/sal/orders**', route => {
      if (route.request().method() === 'POST') writes++;
      return route.fulfill({ json: { items: [], totalCount: 0 } });
    });
    await page.goto('/sal/orders/new');
    await expect(page).toHaveURL(/\/forbidden/);
    expect(writes).toBe(0);
    await capture(page, 'no-permission-light-zh-Hant');
    await chooseTheme(page, 'dark'); await capture(page, 'no-permission-dark-zh-Hant');
  });
});

test.describe('SD-ORDER-UI-014 unknown outcome for an order creator without VIEW', () => {
  test.use({ authorization: { companies: [{ id: 1, code: 'TW01', name: 'Taipei', isDefault: true }],
    companyId: 1, isFullAccess: false,
    permissions: [{ code: 'SAL.ORDER.CREATE', dataScope: 'All' },
      { code: 'SAL.QUOTATION.VIEW', dataScope: 'All' }] } });
  test('explains who can verify a failed conversion without offering a forbidden list', async ({ page, pageErrors }) => {
    pageErrors.allow(/Failed to load resource: the server responded with a status of 500/);
    pageErrors.allowApi5xx(/\/api\/v1\/sal\/orders\/from-quotation\/17$/, 500);
    let writes = 0;
    await page.route('**/api/v1/sal/quotations**', route => route.fulfill({ json: { items: [
      { id: 17, documentNo: 'SQ-0017', customerName: 'Customer Seven', status: 'Approved' }], totalCount: 1 } }));
    await page.route('**/api/v1/sal/orders**', route => {
      if (route.request().url().endsWith('/from-quotation/17')) {
        writes++;
        return route.fulfill({ status: 500, json: { traceId: 'trace-create-only-500' } });
      }
      return route.fulfill({ json: { items: [], totalCount: 0 } });
    });
    await page.goto('/sal/orders/new');
    await page.getByRole('button', { name: '選擇報價' }).click();
    await page.getByText('SQ-0017', { exact: true }).last().click();
    await page.getByTestId('erp-select-confirm').click();
    await page.getByTestId('sd-order-create-convert').click();
    await expect(page.getByTestId('sd-order-create-uncertain')).toContainText('你沒有查看訂單的權限');
    await expect(page.getByTestId('sd-order-create-error')).toContainText('trace-create-only-500');
    await expect(page.getByTestId('sd-order-create-convert')).toBeDisabled();
    await expect(page.getByRole('link', { name: '返回訂單列表' })).toHaveCount(0);
    expect(writes).toBe(1);
    await capture(page, 'create-only-500-light-zh-Hant');
    await chooseTheme(page, 'dark'); await capture(page, 'create-only-500-dark-zh-Hant');
  });
});

for (const mode of ['network', 'timeout'] as const) {
  test(`SD-ORDER-UI-014 ${mode} result locks repeat conversion until verified`, async ({ page, pageErrors }) => {
    if (mode === 'network') pageErrors.allow(/net::ERR_FAILED/);
    let writes = 0;
    await page.route('**/api/v1/sal/quotations**', route => route.fulfill({ json: { items: [
      { id: 17, documentNo: 'SQ-0017', customerName: 'Customer Seven', status: 'Approved' }], totalCount: 1 } }));
    await page.route('**/api/v1/sal/orders**', route => {
      if (route.request().url().endsWith('/from-quotation/17')) {
        writes++;
        return mode === 'network' ? route.abort('failed') : new Promise(() => {});
      }
      return route.fulfill({ json: { items: [], totalCount: 0 } });
    });
    await page.goto('/sal/orders/new');
    await page.getByRole('button', { name: /選擇報價|选择报价|Choose quotation/i }).click();
    await page.getByText('SQ-0017', { exact: true }).last().click();
    await page.getByTestId('erp-select-confirm').click();
    await page.getByTestId('sd-order-create-convert').click();
    await expect(page.getByTestId('sd-order-create-uncertain')).toBeVisible({ timeout: 10_000 });
    await expect(page.getByTestId('sd-order-create-convert')).toBeDisabled();
    await expect(page.getByTestId('sd-order-create-success')).toHaveCount(0);
    await page.getByTestId('sd-order-create-convert').click({ force: true });
    expect(writes).toBe(1);
    await capture(page, `${mode}-light-zh-Hant`);
    await chooseTheme(page, 'dark'); await capture(page, `${mode}-dark-zh-Hant`);
  });
}
