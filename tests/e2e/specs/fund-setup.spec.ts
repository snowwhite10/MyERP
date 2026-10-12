import { E2E_AUTHORIZATION, expect, test } from '../support/fixtures';

const pageResult = (items: unknown[]) => ({ items, page: 1, pageSize: 20, totalCount: items.length });

test('收支類別與資金帳戶依類型顯示欄位，並送出正確建立資料', async ({ page }) => {
  const calls: { path: string; body: Record<string, unknown> }[] = [];
  await page.route('**/api/v1/md/accounts**', route => route.fulfill({ json: [
    { id: 1, code: '1001', name: '現金', accountType: 'Asset', isActive: true, isPostable: true },
    { id: 2, code: '4001', name: '收入', accountType: 'Revenue', isActive: true, isPostable: true },
    { id: 3, code: '6001', name: '費用', accountType: 'Expense', isActive: true, isPostable: true },
  ] }));
  await page.route('**/api/v1/md/currencies**', route => route.fulfill({ json: pageResult([
    { id: 1, code: 'TWD', name: '新臺幣', isActive: true },
  ]) }));
  for (const path of ['fund-accounts', 'income-expense-categories']) {
    await page.route(`**/api/v1/md/${path}**`, route => {
      if (route.request().method() === 'POST') {
        const body = route.request().postDataJSON() as Record<string, unknown>;
        calls.push({ path, body });
        return route.fulfill({ status: 201, json: { id: 1, ...body, isActive: true, version: 1 } });
      }
      return route.fulfill({ json: pageResult([]) });
    });
  }
  await page.goto('/md/income-expense-categories');
  await expect(page.getByTestId('md-fund-title')).toHaveText('收支類別');
  await page.getByTestId('md-fund-create').click();
  await page.getByTestId('md-fund-code').fill('inc');
  await page.getByTestId('md-fund-name').fill('收入類');
  await page.getByTestId('md-fund-type').selectOption('Income');
  await expect(page.getByTestId('md-fund-ledger').locator('option')).toHaveCount(2);
  await page.getByTestId('md-fund-ledger').selectOption({ label: '4001 — 收入' });
  await page.locator('.ant-modal-footer .ant-btn-primary').click();
  await expect.poll(() => calls.length).toBe(1);
  expect(calls[0].body).toMatchObject({ code: 'INC', direction: 'Income', ledgerAccountId: 2 });

  await page.goto('/md/fund-accounts');
  await page.getByTestId('md-fund-create').click();
  await expect(page.getByTestId('md-fund-bank-name')).toHaveCount(0);
  await page.getByTestId('md-fund-type').selectOption('Bank');
  await expect(page.getByTestId('md-fund-bank-name')).toBeVisible();
  await expect(page.getByTestId('md-fund-ledger').locator('option')).toHaveCount(2);
  await page.getByTestId('md-fund-code').fill('bank');
  await page.getByTestId('md-fund-name').fill('銀行帳戶');
  await page.getByTestId('md-fund-currency').selectOption({ label: 'TWD — 新臺幣' });
  await page.getByTestId('md-fund-bank-name').fill('銀行');
  await page.getByTestId('md-fund-bank-no').fill('123-456');
  await page.locator('.ant-modal-footer .ant-btn-primary').click();
  await expect.poll(() => calls.length).toBe(2);
  expect(calls[1].body).toMatchObject({ code: 'BANK', fundType: 'Bank', currencyId: 1,
    bankName: '銀行', bankAccountNo: '123-456' });
});

test('編輯固定幣別與方向，停用及刪除帶版本，列表依類型篩選', async ({ page }) => {
  const fund = { id: 1, code: 'BANK', name: '銀行', fundType: 'Bank', currencyId: 1,
    bankName: '原銀行', bankAccountNo: '100', ledgerAccountId: null, isActive: true, version: 3 };
  const category = { id: 2, code: 'IN', name: '收入', direction: 'Income',
    ledgerAccountId: null, isActive: true, version: 5 };
  const calls: { method: string; path: string; body: Record<string, unknown> | null; query: string }[] = [];
  await page.route('**/api/v1/md/accounts**', route => route.fulfill({ json: [] }));
  await page.route('**/api/v1/md/currencies**', route => route.fulfill({ json: pageResult([
    { id: 1, code: 'TWD', name: '新臺幣', isActive: true },
  ]) }));
  for (const [path, row] of [['fund-accounts', fund], ['income-expense-categories', category]] as const) {
    await page.route(`**/api/v1/md/${path}**`, route => {
      const request = route.request(), url = new URL(request.url());
      calls.push({ method: request.method(), path, body: request.postDataJSON() as Record<string, unknown> | null,
        query: url.search });
      if (request.method() === 'PUT') return route.fulfill({ json: { ...row, ...(request.postDataJSON() as object) } });
      if (request.method() === 'DELETE') return route.fulfill({ status: 204, body: '' });
      return route.fulfill({ json: pageResult([row]) });
    });
  }
  await page.goto('/md/fund-accounts');
  await page.getByTestId('md-fund-filter').selectOption('Bank');
  await expect.poll(() => calls.some(x => x.query.includes('fundType=Bank'))).toBe(true);
  await page.getByTestId('md-fund-row-BANK').getByRole('button', { name: '編輯' }).click();
  await expect(page.getByTestId('md-fund-currency')).toBeDisabled();
  await page.getByTestId('md-fund-type').selectOption('Cash');
  await expect(page.getByTestId('md-fund-bank-name')).toHaveCount(0);
  await page.getByTestId('md-fund-name').fill('現金');
  await page.locator('.ant-modal-footer .ant-btn-primary').click();
  await expect.poll(() => calls.filter(x => x.method === 'PUT').length).toBe(1);
  expect(calls.find(x => x.method === 'PUT')?.body).toMatchObject({ name: '現金', fundType: 'Cash',
    bankName: null, bankAccountNo: null, version: 3 });
  expect(calls.find(x => x.method === 'PUT')?.body).not.toHaveProperty('currencyId');
  await page.getByTestId('md-fund-row-BANK').getByRole('button', { name: '刪除' }).click();
  await page.getByRole('dialog').getByRole('button', { name: '刪除' }).click();
  await expect.poll(() => calls.some(x => x.method === 'DELETE' && x.query.includes('version=3'))).toBe(true);

  await page.goto('/md/income-expense-categories');
  await page.getByTestId('md-fund-filter').selectOption('Income');
  await expect.poll(() => calls.some(x => x.query.includes('direction=Income'))).toBe(true);
  await page.getByTestId('md-fund-row-IN').getByRole('button', { name: '編輯' }).click();
  await expect(page.getByTestId('md-fund-type')).toBeDisabled();
  await page.locator('.ant-modal-footer .ant-btn-primary').click();
  await expect.poll(() => calls.filter(x => x.method === 'PUT').length).toBe(2);
  expect(calls.filter(x => x.method === 'PUT')[1].body).not.toHaveProperty('direction');
});

test('銀行帳號無效時不送出，已引用刪除顯示三語錯誤碼', async ({ page, pageErrors }) => {
  pageErrors.allow(/status of 409/);
  let writes = 0;
  await page.route('**/api/v1/md/accounts**', route => route.fulfill({ json: [] }));
  await page.route('**/api/v1/md/currencies**', route => route.fulfill({ json: pageResult([
    { id: 1, code: 'TWD', name: '新臺幣', isActive: true },
  ]) }));
  await page.route('**/api/v1/md/fund-accounts**', route => {
    if (route.request().method() === 'DELETE') {
      writes++;
      return route.fulfill({ status: 409, contentType: 'application/problem+json',
        json: { status: 409, errorCode: 'MD.FUND_ACCOUNT.IN_USE' } });
    }
    if (route.request().method() === 'PUT') writes++;
    return route.fulfill({ json: pageResult([{ id: 1, code: 'BANK', name: '銀行', fundType: 'Bank',
      currencyId: 1, bankName: '原銀行', bankAccountNo: '100', ledgerAccountId: null, isActive: true, version: 1 }]) });
  });
  await page.goto('/md/fund-accounts');
  await page.getByTestId('md-fund-row-BANK').getByRole('button', { name: '編輯' }).click();
  await page.getByTestId('md-fund-bank-no').fill('bad/slash');
  await page.locator('.ant-modal-footer .ant-btn-primary').click();
  await expect(page.getByRole('alert')).toContainText('最多 50 位英數');
  expect(writes).toBe(0);
  await page.locator('.ant-modal-footer .ant-btn:not(.ant-btn-primary)').click();
  await page.getByTestId('md-fund-row-BANK').getByRole('button', { name: '刪除' }).click();
  await page.getByRole('dialog').getByRole('button', { name: '刪除' }).click();
  await expect(page.getByText('資料已被使用。')).toBeVisible();
  expect(writes).toBe(1);
});

test.describe('只讀權限', () => {
  test.use({ authorization: { ...E2E_AUTHORIZATION, isFullAccess: false,
    permissions: [{ code: 'MD.FUND_ACCOUNT.VIEW', dataScope: 'Company' }] } });
  test('不查無權限參照 API，且隱藏維護動作', async ({ page }) => {
    let referenceCalls = 0;
    await page.route('**/api/v1/md/accounts**', route => { referenceCalls++; return route.fulfill({ json: [] }); });
    await page.route('**/api/v1/md/currencies**', route => { referenceCalls++; return route.fulfill({ json: pageResult([]) }); });
    await page.route('**/api/v1/md/fund-accounts**', route => route.fulfill({ json: pageResult([
      { id: 1, code: 'CASH', name: '現金', fundType: 'Cash', currencyId: 1,
        bankName: null, bankAccountNo: null, ledgerAccountId: null, isActive: true, version: 1 },
    ]) }));
    await page.goto('/md/fund-accounts');
    await expect(page.getByTestId('md-fund-row-CASH')).toBeVisible();
    await expect(page.getByTestId('md-fund-create')).toHaveCount(0);
    await expect(page.getByTestId('md-fund-row-CASH').getByRole('button')).toHaveCount(0);
    expect(referenceCalls).toBe(0);
    await page.goto('/md/income-expense-categories');
    await expect(page.getByTestId('error-page-403')).toBeVisible();
  });
});
