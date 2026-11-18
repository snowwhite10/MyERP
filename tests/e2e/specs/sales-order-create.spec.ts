import { mkdir } from 'node:fs/promises';
import { chooseTheme, expect, test } from '../support/fixtures';

test('SD-ORDER-UI-008/009 manual draft reports success only after the saved receipt', async ({ page }) => {
  let body = '';
  let writes = 0;
  const saved = { id: 81, documentNo: 'SO-0081', status: 'Draft', customerId: 7,
    customerCode: 'C-7', customerName: 'Customer Seven', businessDate: '2028-02-29',
    lines: [{ id: 1, lineNo: 1, itemCode: 'I-9', itemName: 'Item Nine' }] };
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
  await page.goto('/sal/orders/new');
  await page.getByTestId('sd-order-create-save').click();
  await expect(page.getByTestId('sd-order-create-errors')).toBeVisible();
  expect(writes).toBe(0);
  await page.getByTestId('sd-order-create-customer-pick').click();
  await page.getByText('C-7', { exact: true }).last().click();
  await page.getByTestId('erp-select-confirm').click();
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
  expect(body).toContain('"quantity":1.000001');
  expect(body).toContain('"unitPrice":0.000001');
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
    if (url.endsWith('/82/created')) { reads++; return route.fulfill({ json: { id: 82, documentNo: 'SO-0082',
      customerName: 'Customer Seven', lines: [{ id: 1 }] } }); }
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
      await expect(page.getByTestId('sd-order-create-error')).toContainText('quotationId: Quotation is unavailable');
      await expect(page.getByTestId('sd-order-create-quotation')).toContainText('SQ-0017');
    } else {
      await expect(page.getByTestId('sd-order-create-error')).toContainText('trace-order-create-500');
      await expect(page.getByTestId('sd-order-create-uncertain')).toBeVisible();
      await expect(page.getByTestId('sd-order-create-convert')).toBeDisabled();
    }
    await expect(page.getByTestId('sd-order-create-success')).toHaveCount(0);
    expect(writes).toBe(1);
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
  await expect(page.getByTestId('sd-order-create-error')).toContainText('customerId: Customer is inactive');
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
  });
}
