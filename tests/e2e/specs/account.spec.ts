import { E2E_AUTHORIZATION, expect, test } from '../support/fixtures';

type Row = { id: number; code: string; name: string; parentId: number | null; accountType: string;
  balanceDirection: string; isActive: boolean; isPostable: boolean; version: number };

const account = (id: number, code: string, name: string, parentId: number | null, accountType: string,
  balanceDirection: string): Row => ({ id, code, name, parentId, accountType, balanceDirection, isActive: true, isPostable: true, version: 1 });

test('會計科目以樹狀顯示，可新增一級與子科目、修改、刪除並顯示科目規則錯誤', async ({ page, pageErrors }) => {
  pageErrors.allow(/status of 409/);
  pageErrors.allow(/status of 422/);
  const rows: Record<string, Row[]> = {
    '1': [account(1, '1', '資產', null, 'Asset', 'Debit'), account(2, '1101', '現金', 1, 'Asset', 'Debit'),
      account(3, '4', '收入', null, 'Revenue', 'Credit')],
    '2': [account(9, '6', '費用', null, 'Expense', 'Debit')],
  };
  const calls: { method: string; company: string; body: Record<string, unknown> | null }[] = [];
  await page.route('**/api/v1/md/accounts**', async (route) => {
    const request = route.request();
    const company = request.headers()['x-company-id'] ?? '';
    const data = rows[company] ??= [];
    const body = request.postDataJSON() as Record<string, unknown> | null;
    calls.push({ method: request.method(), company, body });
    const withPostable = () => data.map((row) => ({ ...row, isPostable: row.isActive && !data.some((x) => x.parentId === row.id) }));
    if (request.method() === 'GET') {
      await route.fulfill({ json: withPostable() });
    } else if (request.method() === 'POST') {
      const parentId = (body?.['parentId'] as number | null) ?? null;
      const parent = data.find((x) => x.id === parentId);
      if (parent?.code === '4') {
        await route.fulfill({ status: 422, contentType: 'application/problem+json',
          json: { status: 422, errorCode: 'MD.ACCOUNT.PARENT_IN_USE' } });
        return;
      }
      const row = account(10 + data.length, String(body?.['code']), String(body?.['name']), parentId,
        String(body?.['accountType'] ?? parent?.accountType), String(body?.['balanceDirection']));
      data.push(row);
      await route.fulfill({ status: 201, json: row });
    } else {
      const id = Number(new URL(request.url()).pathname.split('/').at(-1));
      const row = data.find((entry) => entry.id === id)!;
      if (request.method() === 'DELETE' && data.some((entry) => entry.parentId === id)) {
        await route.fulfill({ status: 409, contentType: 'application/problem+json',
          json: { status: 409, errorCode: 'MD.ACCOUNT.HAS_CHILDREN' } });
      } else if (request.method() === 'DELETE') {
        data.splice(data.indexOf(row), 1);
        await route.fulfill({ status: 204, body: '' });
      } else {
        Object.assign(row, body, { version: row.version + 1 });
        await route.fulfill({ json: row });
      }
    }
  });

  await page.goto('/');
  await page.getByTestId('layout-menu-md').click();
  await page.getByTestId('layout-menu-md-accounts').click();
  await expect(page).toHaveURL(/\/md\/accounts$/);
  await expect(page.getByTestId('md-account-title')).toHaveText('會計科目');
  const rowIds = page.locator('tr[data-testid^="md-account-row-"]');
  await expect(rowIds).toHaveCount(3);
  expect(await rowIds.evaluateAll((items) => items.map((item) => item.getAttribute('data-testid'))))
    .toEqual(['md-account-row-1', 'md-account-row-1101', 'md-account-row-4']);
  await expect(page.getByTestId('md-account-row-1101')).toHaveAttribute('aria-level', '2');
  await expect(page.getByTestId('md-account-row-1101')).toContainText('可過帳');
  await expect(page.getByTestId('md-account-row-1')).toContainText('彙總');
  await expect(page.getByTestId('md-account-row-4')).toContainText('貸方');

  // 一級科目：選類別帶出預設方向。
  await page.getByTestId('md-account-create').click();
  await page.getByTestId('md-account-code').fill('2');
  await page.getByTestId('md-account-name').fill(' 負債 ');
  await page.getByTestId('md-account-type').selectOption({ label: '負債' });
  await expect(page.getByTestId('md-account-direction')).toHaveValue(/.+/);
  await page.locator('.ant-modal-footer .ant-btn-primary').click();
  await expect(page.getByTestId('md-account-row-2')).toContainText('負債');
  expect(calls.find((call) => call.method === 'POST')?.body)
    .toEqual({ code: '2', name: '負債', parentId: null, accountType: 'Liability', balanceDirection: 'Credit' });

  // 子科目：代碼預填上層代碼，類別唯讀，前綴不符時提示。
  await page.getByTestId('md-account-add-child-1101').click();
  await expect(page.getByTestId('md-account-code')).toHaveValue('1101');
  await expect(page.getByTestId('md-account-type')).toBeDisabled();
  await page.getByTestId('md-account-code').fill('1201');
  await page.getByTestId('md-account-name').fill('零用金');
  await page.locator('.ant-modal-footer .ant-btn-primary').click();
  await expect(page.getByText('代碼須以 1101 開頭且比上層代碼長。')).toBeVisible();
  await page.getByTestId('md-account-code').fill('110101');
  await page.locator('.ant-modal-footer .ant-btn-primary').click();
  await expect(page.getByTestId('md-account-row-110101')).toHaveAttribute('aria-level', '3');
  await expect(page.getByTestId('md-account-row-1101')).toContainText('彙總');
  expect(calls.filter((call) => call.method === 'POST').at(-1)?.body)
    .toEqual({ code: '110101', name: '零用金', parentId: 2, balanceDirection: 'Debit' });

  await page.getByTestId('md-account-add-child-4').click();
  await page.getByTestId('md-account-code').fill('4101');
  await page.getByTestId('md-account-name').fill('銷貨收入');
  await page.locator('.ant-modal-footer .ant-btn-primary').click();
  await expect(page.getByTestId('md-account-save-error')).toContainText('上層科目已被使用，不可再新增子科目。');
  await page.locator('.ant-modal-footer .ant-btn:not(.ant-btn-primary)').click();

  await page.getByTestId('md-account-search').fill('零用');
  await page.getByTestId('md-account-search-button').click();
  await expect(rowIds).toHaveCount(3);
  await expect(page.getByTestId('md-account-row-4')).toHaveCount(0);
  await page.getByTestId('md-account-search').fill('');
  await page.getByTestId('md-account-search-button').click();

  await page.getByTestId('md-account-edit-110101').click();
  await expect(page.getByTestId('md-account-code')).toBeDisabled();
  await page.getByTestId('md-account-name').fill('零用金（台北）');
  await page.getByTestId('md-account-direction').selectOption({ label: '貸方' });
  await page.locator('.ant-modal-footer .ant-btn-primary').click();
  await expect(page.getByTestId('md-account-row-110101')).toContainText('零用金（台北）');
  expect(calls.filter((call) => call.method === 'PUT').at(-1)?.body)
    .toEqual({ name: '零用金（台北）', balanceDirection: 'Credit', isActive: true, version: 1 });

  await page.getByTestId('md-account-delete-1101').click();
  await page.getByTestId('erp-message-ok').click();
  await expect(page.getByText('此科目仍有子科目，請先刪除子科目。')).toBeVisible();
  await page.getByTestId('md-account-delete-110101').click();
  await page.getByTestId('erp-message-ok').click();
  await expect(page.getByTestId('md-account-row-110101')).toHaveCount(0);

  await page.getByTestId('layout-company-select').click();
  await page.locator('nz-option-item', { hasText: 'HK01 Hong Kong' }).click();
  await expect(page.getByTestId('md-account-row-6')).toBeVisible();
  await expect(page.getByTestId('md-account-row-1')).toHaveCount(0);
  expect(calls.filter((call) => call.method === 'DELETE').map((call) => call.company)).toEqual(['1', '1']);
});

test.describe('會計科目只讀權限', () => {
  test.use({ authorization: { ...E2E_AUTHORIZATION, isFullAccess: false,
    permissions: [{ code: 'MD.ACCOUNT.VIEW', dataScope: 'Company' }] } });

  test('隱藏寫入操作並阻擋稅別頁', async ({ page }) => {
    await page.route('**/api/v1/md/accounts**', (route) =>
      route.fulfill({ json: [account(1, '1', '資產', null, 'Asset', 'Debit')] }));
    await page.goto('/md/accounts');
    await expect(page.getByTestId('md-account-row-1')).toBeVisible();
    await expect(page.getByTestId('md-account-create')).toHaveCount(0);
    await expect(page.getByTestId('md-account-add-child-1')).toHaveCount(0);
    await expect(page.getByTestId('md-account-edit-1')).toHaveCount(0);
    await expect(page.getByTestId('md-account-delete-1')).toHaveCount(0);
    await page.goto('/md/tax-codes');
    await expect(page.getByTestId('error-page-403')).toBeVisible();
  });
});
