import { expect, test } from '../support/fixtures';

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
