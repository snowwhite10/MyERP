import { mkdir } from 'node:fs/promises';
import { chooseLanguage, chooseTheme, expect, test } from '../support/fixtures';

const PATH = '/ap/other-payments';
const DIRECTORY = '../../tests/qa/FI/screenshots';

test('FI-OP-UI-107 creates, edits, submits, withdraws, deletes and reports unchanged map honestly', async ({ page, pageErrors }) => {
  let saved: Record<string, unknown> | null = null;
  let version = 0;
  let writes = 0;
  let rejectSubmit = true;
  let failMap = true;
  pageErrors.allowApi5xx(/\/api\/v1\/gl\/other-payment-account-map/, 500);
  pageErrors.allow(/^console: Failed to load resource: the server responded with a status of 500/);
  pageErrors.allow(/^console: Failed to load resource: the server responded with a status of 422/);
  const detail = () => ({ id: 7, documentNo: 'OP2026110007', payeeDescription: saved?.['payeeDescription'],
    expenseCategoryId: 3, expenseCategoryCode: 'UTIL', expenseCategoryName: 'Utilities', bankAccountId: 9,
    bankAccountCode: 'BANK', bankAccountName: 'Cash', bankReference: saved?.['bankReference'],
    businessDate: saved?.['businessDate'], currency: 'TWD', amount: saved?.['amount'], remark: saved?.['remark'],
    status: saved?.['status'], version, journalId: null, createdAt: '2026-11-27T00:00:00+08:00', createdBy: 1,
    updatedAt: null, updatedBy: null });
  await page.route('**/api/v1/md/income-expense-categories**', route => route.fulfill({ json: { items: [
    { id: 3, code: 'UTIL', name: 'Utilities', direction: 'Expense', isActive: true, version: 1 }], totalCount: 1 } }));
  await page.route('**/api/v1/md/fund-accounts**', route => route.fulfill({ json: { items: [
    { id: 9, code: 'BANK', name: 'Cash', isActive: true, version: 1 }], totalCount: 1 } }));
  await page.route('**/api/v1/gl/other-payment-account-map**', route => {
    if (route.request().method() === 'PUT') {
      if (failMap) { failMap = false; return route.fulfill({ status: 500, json: { status: 500, traceId: 'op-map-trace' } }); }
      return route.fulfill({ json: { changed: false, row: { expenseCategoryId: 3, version: 1 } } });
    }
    return route.fulfill({ json: { items: [{ expenseCategoryId: 3, expenseCategoryCode: 'UTIL', expenseCategoryName: 'Utilities',
      expenseCategoryActive: true, configured: true, version: 1, expenseAccount: { id: 10, code: '6101', name: 'Expenses', isUsable: true } }] } });
  });
  await page.route('**/api/v1/ap/other-payments**', async route => {
    const request = route.request(); const url = new URL(request.url()); const method = request.method();
    if (method === 'GET' && url.pathname.endsWith('/other-payments')) return route.fulfill({ json: {
      items: saved ? [detail()] : [], totalCount: saved ? 1 : 0, page: 1, pageSize: 20 } });
    if (method === 'GET' && url.pathname.endsWith('/7')) return route.fulfill({ json: detail() });
    if (method === 'POST' && url.pathname.endsWith('/other-payments')) {
      const body = request.postDataJSON(); saved = { ...body, status: 'Draft' }; version = 1; writes++;
      return route.fulfill({ status: 201, json: detail() });
    }
    if (method === 'PUT' && url.pathname.endsWith('/7')) {
      const body = request.postDataJSON(); expect(body.version).toBe(version);
      saved = { ...body, status: 'Draft' }; version++; writes++; return route.fulfill({ json: detail() });
    }
    if (method === 'POST' && url.pathname.endsWith('/submit')) {
      if (rejectSubmit) { rejectSubmit = false; return route.fulfill({ status: 422,
        json: { errorCode: 'FI.OTHER_PAYMENT_POSTING.MAPPING_MISSING' } }); }
      expect(request.postDataJSON().version).toBe(version); saved!['status'] = 'Pending'; version++; writes++;
      return route.fulfill({ json: { id: 7, status: 'Pending', version, posting: null } });
    }
    if (method === 'POST' && url.pathname.endsWith('/withdraw')) {
      expect(request.postDataJSON().version).toBe(version); saved!['status'] = 'Draft'; version++; writes++;
      return route.fulfill({ json: { id: 7, status: 'Draft', version, posting: null } });
    }
    if (method === 'DELETE' && url.pathname.endsWith('/7')) {
      expect(url.searchParams.get('version')).toBe(String(version)); saved = null; writes++;
      return route.fulfill({ status: 204, body: '' });
    }
    return route.abort();
  });
  await page.goto(PATH);
  await page.getByTestId('fi-op-create').click();
  await mkdir(DIRECTORY, { recursive: true });
  await page.screenshot({ path: `${DIRECTORY}/6.11-create-light-zh-Hant.png`, fullPage: true });
  await chooseTheme(page, 'dark');
  await page.screenshot({ path: `${DIRECTORY}/6.11-create-dark-zh-Hant.png`, fullPage: true });
  await chooseTheme(page, 'light');
  await page.getByTestId('fi-op-save').click();
  await expect(page.getByTestId('fi-op-editor')).toContainText('請選擇費用類別');
  await expect(page.locator('label', { has: page.getByTestId('fi-op-amount') }).getByRole('alert')).toContainText('本位幣金額');
  await expect(page.locator('.choice', { has: page.getByTestId('fi-op-fund') }).getByRole('alert')).toContainText('請選擇資金帳戶');
  expect(writes).toBe(0);
  await page.screenshot({ path: `${DIRECTORY}/6.11-validation-light-zh-Hant.png`, fullPage: true });
  await chooseTheme(page, 'dark');
  await page.screenshot({ path: `${DIRECTORY}/6.11-validation-dark-zh-Hant.png`, fullPage: true });
  await chooseTheme(page, 'light');
  await page.getByTestId('fi-op-payee').fill('Electricity');
  await page.getByTestId('fi-op-category').selectOption({ label: 'UTIL · Utilities' });
  await page.getByTestId('fi-op-fund').selectOption({ label: 'BANK · Cash' });
  await page.getByTestId('fi-op-reference').fill('WD-7');
  await page.getByTestId('fi-op-date').fill('2026-11-27');
  await page.getByTestId('fi-op-amount').fill('12.3400');
  await page.getByTestId('fi-op-save').click();
  await expect(page.getByTestId('fi-op-row-7')).toContainText('12.34 TWD');
  await expect(page.getByTestId('fi-op-editor')).toContainText('Electricity');
  expect(saved?.['status']).toBe('Draft');
  await page.screenshot({ path: `${DIRECTORY}/6.11-saved-light-zh-Hant.png`, fullPage: true });
  await chooseTheme(page, 'dark');
  await page.screenshot({ path: `${DIRECTORY}/6.11-saved-dark-zh-Hant.png`, fullPage: true });
  await chooseLanguage(page, 'zh-Hans');
  await page.screenshot({ path: `${DIRECTORY}/6.11-saved-dark-zh-Hans.png`, fullPage: true });
  await chooseLanguage(page, 'en');
  await page.screenshot({ path: `${DIRECTORY}/6.11-saved-dark-en.png`, fullPage: true });
  await chooseLanguage(page, 'zh-Hant');
  await page.getByTestId('fi-op-edit').click();
  await page.screenshot({ path: `${DIRECTORY}/6.12-edit-dark-zh-Hant.png`, fullPage: true });
  await chooseTheme(page, 'light');
  await page.screenshot({ path: `${DIRECTORY}/6.12-edit-light-zh-Hant.png`, fullPage: true });
  await chooseTheme(page, 'dark');
  await page.getByTestId('fi-op-payee').fill('Electricity revised');
  await page.getByTestId('fi-op-save').click();
  await expect(page.getByTestId('fi-op-row-7')).toContainText('Electricity revised');
  expect(saved?.['payeeDescription']).toBe('Electricity revised');
  await page.getByTestId('fi-op-submit').click();
  await expect(page.getByTestId('fi-op-write-error')).toContainText('缺少費用科目對照');
  expect(saved?.['status']).toBe('Draft');
  await page.screenshot({ path: `${DIRECTORY}/6.11-submit-rejected-dark-zh-Hant.png`, fullPage: true });
  await chooseTheme(page, 'light');
  await page.screenshot({ path: `${DIRECTORY}/6.11-submit-rejected-light-zh-Hant.png`, fullPage: true });
  await chooseTheme(page, 'dark');
  await page.getByTestId('fi-op-submit').click();
  await expect(page.getByTestId('fi-op-editor')).toContainText('待審批');
  await expect(page.getByTestId('fi-op-edit')).toHaveCount(0);
  await page.getByTestId('fi-op-withdraw').click();
  await expect(page.getByTestId('fi-op-editor')).toContainText('草稿');
  page.once('dialog', dialog => dialog.accept());
  await page.getByTestId('fi-op-delete').click();
  await expect(page.getByTestId('fi-op-row-7')).toHaveCount(0);
  expect(saved).toBeNull();
  await page.getByTestId('fi-op-manage-map').click();
  await page.screenshot({ path: `${DIRECTORY}/6.32-normal-dark-zh-Hant.png`, fullPage: true });
  await page.getByTestId('fi-op-map-edit-3').click();
  await page.getByTestId('fi-op-map-code').fill('6101');
  await page.getByTestId('fi-op-map-save').click();
  await expect(page.getByTestId('fi-op-map-error')).toContainText('op-map-trace');
  await page.screenshot({ path: `${DIRECTORY}/6.32-server-error-dark-zh-Hant.png`, fullPage: true });
  await chooseTheme(page, 'light');
  await page.screenshot({ path: `${DIRECTORY}/6.32-server-error-light-zh-Hant.png`, fullPage: true });
  await chooseTheme(page, 'dark');
  await page.getByTestId('fi-op-map-save').click();
  await expect(page.getByTestId('fi-op-map-notice')).toContainText('科目對照沒有變更');
  await page.screenshot({ path: `${DIRECTORY}/6.32-unchanged-dark-zh-Hant.png`, fullPage: true });
  await chooseLanguage(page, 'en');
  await expect(page.getByTestId('fi-op-map-notice')).toContainText('No account mapping changed');
  await page.screenshot({ path: `${DIRECTORY}/6.32-unchanged-dark-en.png`, fullPage: true });
});

test('FI-OP-UI-108 notices follow the saved result: duplicate reference, stale version, pending, posted, withdrawn, deleted', async ({ page, pageErrors }) => {
  const saved: Record<string, unknown> = { payeeDescription: 'Water', bankReference: 'WD-8', businessDate: '2026-11-27',
    amount: '88.0000', remark: null, status: 'Draft' };
  let version = 1;
  let journalId: number | null = null;
  let deleted = false;
  const next: string[] = ['duplicate', 'stale', 'server', 'pending', 'withdraw', 'posted'];
  pageErrors.allowApi5xx(/\/api\/v1\/ap\/other-payments\/8\/submit/, 500);
  pageErrors.allow(/^console: Failed to load resource: the server responded with a status of (409|500)/);
  const detail = () => ({ id: 8, documentNo: 'OP2026110008', expenseCategoryId: 3, expenseCategoryCode: 'UTIL',
    expenseCategoryName: 'Utilities', bankAccountId: 9, bankAccountCode: 'BANK', bankAccountName: 'Cash', currency: 'TWD',
    ...saved, version, journalId, createdAt: '2026-11-27T00:00:00+08:00', createdBy: 1, updatedAt: null, updatedBy: null });
  await page.route('**/api/v1/md/income-expense-categories**', route => route.fulfill({ json: { items: [
    { id: 3, code: 'UTIL', name: 'Utilities', direction: 'Expense', isActive: true, version: 1 }], totalCount: 1 } }));
  await page.route('**/api/v1/md/fund-accounts**', route => route.fulfill({ json: { items: [
    { id: 9, code: 'BANK', name: 'Cash', isActive: true, version: 1 }], totalCount: 1 } }));
  await page.route('**/api/v1/gl/other-payment-account-map**', route => route.fulfill({ json: { items: [{ expenseCategoryId: 3,
    expenseCategoryCode: 'UTIL', expenseCategoryName: 'Utilities', expenseCategoryActive: true, configured: true, version: 1,
    expenseAccount: { id: 10, code: '6101', name: 'Expenses', isUsable: true } }] } }));
  await page.route('**/api/v1/ap/other-payments**', async route => {
    const request = route.request(); const url = new URL(request.url()); const method = request.method();
    if (method === 'GET' && url.pathname.endsWith('/other-payments')) return route.fulfill({ json: {
      items: deleted ? [] : [detail()], totalCount: deleted ? 0 : 1, page: 1, pageSize: 20 } });
    if (method === 'GET' && url.pathname.endsWith('/8')) return route.fulfill({ json: detail() });
    if (method === 'PUT' && url.pathname.endsWith('/8')) {
      const step = next.shift();
      if (step === 'duplicate') return route.fulfill({ status: 409, json: { status: 409,
        errorCode: 'FI.OTHER_PAYMENT.BANK_REFERENCE_DUPLICATE', traceId: 'op-dup' } });
      if (step === 'stale') { version++; return route.fulfill({ status: 409, json: { status: 409,
        errorCode: 'FI.OTHER_PAYMENT.VERSION_CONFLICT', traceId: 'op-stale' } }); }
      throw new Error(`unexpected PUT at ${step}`);
    }
    if (method === 'POST' && url.pathname.endsWith('/submit')) {
      const step = next.shift();
      if (step === 'server') return route.fulfill({ status: 500, json: { status: 500, traceId: 'op-submit-500' } });
      expect(request.postDataJSON().version).toBe(version); version++;
      if (step === 'pending') { saved['status'] = 'Pending';
        return route.fulfill({ json: { id: 8, documentNo: 'OP2026110008', status: 'Pending', version, approval: {}, posting: null } }); }
      saved['status'] = 'Posted'; journalId = 501;
      return route.fulfill({ json: { id: 8, documentNo: 'OP2026110008', status: 'Posted', version, approval: null, posting: { journalId } } });
    }
    if (method === 'POST' && url.pathname.endsWith('/withdraw')) {
      expect(next.shift()).toBe('withdraw'); saved['status'] = 'Draft'; version++;
      return route.fulfill({ json: { id: 8, documentNo: 'OP2026110008', status: 'Draft', version, approval: null, posting: null } });
    }
    if (method === 'DELETE' && url.pathname.endsWith('/8')) { deleted = true; return route.fulfill({ status: 204, body: '' }); }
    return route.abort();
  });
  const shot = async (name: string) => {
    await page.screenshot({ path: `${DIRECTORY}/${name}-light-zh-Hant.png`, fullPage: true });
    await chooseTheme(page, 'dark');
    await page.screenshot({ path: `${DIRECTORY}/${name}-dark-zh-Hant.png`, fullPage: true });
    await chooseTheme(page, 'light');
  };
  await mkdir(DIRECTORY, { recursive: true });
  await page.goto(PATH);
  await page.getByTestId('fi-op-open-8').click();
  await page.getByTestId('fi-op-edit').click();
  await page.getByTestId('fi-op-reference').fill('WD-USED');
  await page.getByTestId('fi-op-save').click();
  await expect(page.getByTestId('fi-op-write-error')).toContainText('提款參考');
  await expect(page.getByTestId('fi-op-reference')).toHaveValue('WD-USED');
  await expect(page.getByTestId('fi-op-reference')).toBeFocused();
  await expect(page.getByTestId('fi-op-reference')).toHaveAttribute('aria-invalid', 'true');
  await expect(page.locator('label', { has: page.getByTestId('fi-op-reference') }).getByTestId('fi-op-reference-error'),
    'the reason sits in the reference field, not in the neighbouring grid cell').toContainText('提款參考已被使用');
  await expect(page.getByTestId('fi-op-reload')).toHaveCount(0);
  expect(saved['bankReference']).toBe('WD-8');
  await shot('6.12-duplicate-reference');
  await page.getByTestId('fi-op-save').click();
  await expect(page.getByTestId('fi-op-write-error')).toContainText('請重新載入後再試');
  const reload = page.getByTestId('fi-op-reload');
  const box = (await reload.boundingBox())!; const editorBox = (await page.getByTestId('fi-op-editor').boundingBox())!;
  expect(box.width).toBeLessThan(editorBox.width / 2);
  await shot('6.12-stale-version');
  page.once('dialog', dialog => dialog.accept());
  await reload.click();
  await expect(page.getByTestId('fi-op-write-error')).toHaveCount(0);
  await page.getByTestId('fi-op-submit').click();
  await expect(page.getByTestId('fi-op-write-error')).toContainText('op-submit-500');
  expect(saved['status']).toBe('Draft');
  await shot('6.12-submit-server-error');
  await page.getByTestId('fi-op-submit').click();
  await expect(page.getByTestId('fi-op-notice')).toContainText('OP2026110008 已送審，正在等待審批，尚未過帳');
  await expect(page.getByTestId('fi-op-row-8')).toContainText('待審批');
  await shot('6.12-submitted-pending');
  await page.getByTestId('fi-op-withdraw').click();
  await expect(page.getByTestId('fi-op-notice')).toContainText('OP2026110008 已撤回，回到草稿');
  await expect(page.getByTestId('fi-op-row-8')).toContainText('草稿');
  await shot('6.12-withdrawn');
  await page.getByTestId('fi-op-submit').click();
  await expect(page.getByTestId('fi-op-notice')).toContainText('OP2026110008 已核准並過帳');
  await expect(page.getByTestId('fi-op-editor')).toContainText('#501');
  await expect(page.getByTestId('fi-op-row-8')).toContainText('已過帳');
  await expect(page.getByTestId('fi-op-submit')).toHaveCount(0);
  expect(saved['status']).toBe('Posted');
  await shot('6.12-submitted-posted');
  await chooseLanguage(page, 'en');
  await expect(page.getByTestId('fi-op-notice')).toContainText('Payment OP2026110008 was approved and posted');
  await page.screenshot({ path: `${DIRECTORY}/6.12-submitted-posted-light-en.png`, fullPage: true });
  await chooseLanguage(page, 'zh-Hans');
  await expect(page.getByTestId('fi-op-notice')).toContainText('已核准并过账');
  await page.screenshot({ path: `${DIRECTORY}/6.12-submitted-posted-light-zh-Hans.png`, fullPage: true });
  await chooseLanguage(page, 'zh-Hant');
  saved['status'] = 'Draft'; journalId = null;
  await page.getByTestId('fi-op-close').click();
  await page.getByTestId('fi-op-search').click();
  await page.getByTestId('fi-op-open-8').click();
  page.once('dialog', dialog => dialog.accept());
  await page.getByTestId('fi-op-delete').click();
  await expect(page.getByTestId('fi-op-deleted')).toContainText('草稿付款 OP2026110008 已刪除，提款參考已釋出');
  await expect(page.getByTestId('fi-op-row-8')).toHaveCount(0);
  expect(deleted).toBe(true);
  await shot('6.12-deleted');
});
