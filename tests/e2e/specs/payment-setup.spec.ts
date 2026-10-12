import { E2E_AUTHORIZATION, expect, test } from '../support/fixtures';

test('結算方式列表與付款條件規則、到期日試算', async ({ page }) => {
  await page.route('**/api/v1/md/settlement-methods**', (route) => route.fulfill({ json: {
    items: [{ id: 1, code: 'BANK', name: '轉帳', isActive: true, version: 1 }],
    page: 1, pageSize: 20, totalCount: 1,
  } }));
  let created: Record<string, unknown> | null = null;
  await page.route('**/api/v1/md/payment-terms**', async (route) => {
    const url = new URL(route.request().url());
    if (url.pathname.endsWith('/due-date')) {
      await route.fulfill({ json: { code: 'NET30', documentDate: '2026-10-12', dueDate: '2026-11-11' } });
    } else if (route.request().method() === 'POST') {
      created = route.request().postDataJSON() as Record<string, unknown>;
      await route.fulfill({ status: 201, json: { id: 2, ...created, isActive: true, version: 1 } });
    } else {
      const rows = [{ id: 1, code: 'NET30', name: '30 天', dueType: 'NetDays', days: 30,
        closingDay: null, months: null, paymentDay: null, isActive: true, version: 1 }];
      await route.fulfill({ json: { items: rows, page: 1, pageSize: 20, totalCount: rows.length } });
    }
  });
  await page.goto('/md/settlement-methods');
  await expect(page.getByTestId('md-category-row-BANK')).toBeVisible();
  await page.goto('/md/payment-terms');
  await expect(page.getByTestId('md-payment-term-row-NET30')).toContainText('單據日後 30 天');
  await page.getByTestId('md-payment-term-create').click();
  await page.getByTestId('md-payment-term-code').fill('month');
  await page.getByTestId('md-payment-term-name').fill('月結');
  await page.getByTestId('md-payment-term-type').selectOption('ClosingMonthDay');
  await expect(page.getByTestId('md-payment-term-days')).toHaveCount(0);
  await page.getByTestId('md-payment-term-closing-day').fill('20');
  await page.getByTestId('md-payment-term-months').fill('0');
  await page.getByTestId('md-payment-term-payment-day').fill('20');
  await page.locator('.ant-modal-footer .ant-btn-primary').click();
  await expect(page.getByRole('alert')).toContainText('同月付款日必須大於結帳日');
  await page.getByTestId('md-payment-term-payment-day').fill('31');
  await page.locator('.ant-modal-footer .ant-btn-primary').click();
  expect(created).toMatchObject({ code: 'MONTH', dueType: 'ClosingMonthDay', days: null,
    closingDay: 20, months: 0, paymentDay: 31 });
  await page.getByTestId('md-payment-term-calc-NET30').click();
  await page.getByTestId('md-payment-term-date').fill('2026-10-12');
  await page.locator('.ant-modal-footer .ant-btn-primary').click();
  await expect(page.getByTestId('md-payment-term-due-date')).toContainText('2026-11-11');
});

test.describe('結算設定只讀權限', () => {
  test.use({ authorization: { ...E2E_AUTHORIZATION, isFullAccess: false,
    permissions: [{ code: 'MD.PAYMENT_TERM.VIEW', dataScope: 'Company' }] } });

  test('付款條件只有查看與試算，結算方式拒絕進入', async ({ page }) => {
    await page.route('**/api/v1/md/payment-terms**', (route) => route.fulfill({ json: {
      items: [{ id: 1, code: 'NET30', name: '30 天', dueType: 'NetDays', days: 30,
        closingDay: null, months: null, paymentDay: null, isActive: true, version: 1 }],
      page: 1, pageSize: 20, totalCount: 1,
    } }));
    await page.goto('/md/payment-terms');
    await expect(page.getByTestId('md-payment-term-row-NET30')).toBeVisible();
    await expect(page.getByTestId('md-payment-term-create')).toHaveCount(0);
    await expect(page.getByTestId('md-payment-term-row-NET30').getByRole('button')).toHaveCount(1);
    await page.goto('/md/settlement-methods');
    await expect(page.getByTestId('error-page-403')).toBeVisible();
  });
});
