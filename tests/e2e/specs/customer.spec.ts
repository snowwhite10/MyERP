import { E2E_AUTHORIZATION, expect, test } from '../support/fixtures';

test('待審客戶送審後可刪除，並保留版本參數', async ({ page }) => {
  const row = { id: 91, code: 'REVIEW', name: 'Review customer', categoryId: null,
    isActive: true, version: 1, reviewStatus: 'Pending' };
  let deletedVersion: string | null = null;
  await page.route('**/api/v1/md/customer-categories**', (route) => route.fulfill({ json: {
    items: [], page: 1, pageSize: 100, totalCount: 0 } }));
  await page.route('**/api/v1/md/customers**', async (route) => {
    const request = route.request();
    if (request.method() === 'GET')
      return route.fulfill({ json: { items: deletedVersion === null ? [row] : [], page: 1, pageSize: 20,
        totalCount: deletedVersion === null ? 1 : 0 } });
    if (request.method() === 'POST' && new URL(request.url()).pathname.endsWith('/submit')) {
      expect(request.postDataJSON()).toEqual({ version: 1 });
      row.reviewStatus = 'Approved'; row.version = 2;
      return route.fulfill({ json: { status: 'Approved' } });
    }
    if (request.method() === 'DELETE') {
      deletedVersion = new URL(request.url()).searchParams.get('version');
      return route.fulfill({ status: 204 });
    }
    return route.abort();
  });
  await page.goto('/md/customers');
  await expect(page.getByTestId('md-customer-row-REVIEW')).toContainText('待審');
  await page.getByTestId('md-customer-submit-REVIEW').click();
  await expect(page.getByTestId('md-customer-row-REVIEW')).toContainText('已核准');
  await page.getByTestId('md-customer-delete-REVIEW').click();
  await page.locator('.ant-popover-buttons button').last().click();
  await expect(page.getByTestId('md-customer-row-REVIEW')).toHaveCount(0);
  expect(deletedVersion).toBe('2');
});

test('客戶可按公司查看、新增、停用及搜尋', async ({ page }) => {
  const rows: Record<string, { id: number; code: string; name: string; categoryId: number | null; isActive: boolean; version: number }[]> = {
    '1': [], '2': [{ id: 20, code: 'HK01', name: 'Hong Kong customer', categoryId: null, isActive: true, version: 1 }],
  };
  await page.route('**/api/v1/md/customer-categories**', (route) => route.fulfill({ json: {
    items: [{ id: 3, code: 'RETAIL', name: 'Retail', isActive: true, version: 1 }],
    page: 1, pageSize: 100, totalCount: 1,
  } }));
  await page.route('**/api/v1/md/customers**', async (route) => {
    const request = route.request();
    const company = request.headers()['x-company-id'] ?? '';
    const data = rows[company] ??= [];
    if (request.method() === 'GET') {
      const keyword = new URL(request.url()).searchParams.get('keyword')?.toLowerCase() ?? '';
      const items = data.filter((row) => row.code.toLowerCase().includes(keyword) || row.name.toLowerCase().includes(keyword));
      await route.fulfill({ json: { items, page: 1, pageSize: 20, totalCount: items.length } });
    } else if (request.method() === 'POST') {
      const body = request.postDataJSON();
      const row = { id: 4, code: body.code, name: body.name, categoryId: body.categoryId, isActive: true, version: 1 };
      data.push(row);
      await route.fulfill({ status: 201, json: row });
    } else {
      const row = data.find((entry) => entry.id === Number(new URL(request.url()).pathname.split('/').at(-1)))!;
      Object.assign(row, request.postDataJSON(), { version: row.version + 1 });
      await route.fulfill({ json: row });
    }
  });

  await page.goto('/md/customers');
  await page.getByTestId('md-customer-create').click();
  await page.getByTestId('md-customer-code').fill('acme');
  await page.getByTestId('md-customer-name').fill(' Acme ');
  await page.getByTestId('md-customer-category').selectOption({ label: 'Retail' });
  await page.locator('.ant-modal-footer .ant-btn-primary').click();
  await expect(page.getByTestId('md-customer-row-ACME')).toContainText('Retail');
  await page.getByTestId('md-customer-edit-ACME').click();
  await page.getByTestId('md-customer-active').uncheck();
  await page.locator('.ant-modal-footer .ant-btn-primary').click();
  await expect(page.getByTestId('md-customer-row-ACME')).toContainText('停用');
  await page.getByTestId('md-customer-search').fill('missing');
  await page.getByTestId('md-customer-search-button').click();
  await expect(page.getByTestId('md-customer-row-ACME')).toHaveCount(0);
  await page.getByTestId('md-customer-search').fill('');
  await page.getByTestId('md-customer-search-button').click();
  await page.getByTestId('layout-company-select').click();
  await page.locator('nz-option-item', { hasText: 'HK01 Hong Kong' }).click();
  await expect(page.getByTestId('md-customer-row-HK01')).toBeVisible();
  await expect(page.getByTestId('md-customer-row-ACME')).toHaveCount(0);
});

test.describe('客戶只讀權限', () => {
  test.use({ authorization: { ...E2E_AUTHORIZATION, isFullAccess: false,
    permissions: [{ code: 'MD.CUSTOMER.VIEW', dataScope: 'Company' }] } });
  test('隱藏寫入操作', async ({ page }) => {
    await page.route('**/api/v1/md/customers**', (route) => route.fulfill({ json: {
      items: [{ id: 1, code: 'VIEW', name: 'View only', categoryId: null, isActive: true, version: 1 }],
      page: 1, pageSize: 20, totalCount: 1,
    } }));
    await page.goto('/md/customers');
    await expect(page.getByTestId('md-customer-row-VIEW')).toBeVisible();
    await expect(page.getByTestId('md-customer-create')).toHaveCount(0);
    await expect(page.getByTestId('md-customer-edit-VIEW')).toHaveCount(0);
  });
});

test('客戶聯絡人與地址：新增預設、驗證、修改、找不到提示、刪除', async ({ page, pageErrors }) => {
  pageErrors.allow(/status of 404/);
  type Contact = { id: number; customerId: number; name: string; email: string | null; isDefault: boolean; isActive: boolean; version: number; [key: string]: unknown };
  const contacts: Contact[] = [{ id: 11, customerId: 1, name: '王小明', position: '採購', mobile: '0912', phone: null,
    email: null, messagingId: null, note: null, isDefault: true, isActive: true, version: 1 }];
  const addresses: Record<string, unknown>[] = [];
  const writes: { method: string; url: string; body: unknown }[] = [];
  let nextId = 30;
  await page.route('**/api/v1/md/customers?**', (route) => route.fulfill({ json: { items: [
    { id: 1, code: 'ACME', name: 'Acme', categoryId: null, isActive: true, version: 1 }], page: 1, pageSize: 20, totalCount: 1 } }));
  await page.route('**/api/v1/md/customers/1/contacts**', async (route) => {
    const request = route.request();
    const sorted = () => [...contacts].sort((a, b) => Number(b.isDefault) - Number(a.isDefault));
    if (request.method() === 'GET') return route.fulfill({ json: sorted() });
    const body = request.postDataJSON() as Contact | null;
    writes.push({ method: request.method(), url: request.url(), body });
    const id = Number(new URL(request.url()).pathname.split('/').at(-1));
    if (request.method() === 'POST') {
      if (body!.isDefault) contacts.forEach((row) => { row.isDefault = false; });
      contacts.push({ ...body!, id: nextId++, customerId: 1, isActive: true, version: 1 });
      return route.fulfill({ status: 201, json: contacts.at(-1) });
    }
    const index = contacts.findIndex((row) => row.id === id);
    if (index < 0) return route.fulfill({ status: 404, contentType: 'application/problem+json',
      json: { status: 404, errorCode: 'MD.CUSTOMER_CONTACT.NOT_FOUND' } });
    if (request.method() === 'PUT') {
      Object.assign(contacts[index]!, body, { version: contacts[index]!.version + 1 });
      return route.fulfill({ json: contacts[index] });
    }
    contacts.splice(index, 1);
    return route.fulfill({ status: 204 });
  });
  await page.route('**/api/v1/md/customers/1/addresses**', async (route) => {
    const request = route.request();
    if (request.method() === 'GET') return route.fulfill({ json: addresses });
    const body = request.postDataJSON() as Record<string, unknown>;
    writes.push({ method: request.method(), url: request.url(), body });
    addresses.push({ ...body, id: nextId++, customerId: 1, isActive: true, version: 1 });
    return route.fulfill({ status: 201, json: addresses.at(-1) });
  });

  await page.goto('/md/customers');
  await page.getByTestId('md-customer-contacts-ACME').click();
  await expect(page.getByTestId('md-contact-row-11')).toContainText('王小明');
  await expect(page.getByTestId('md-contact-default-11')).toBeVisible();

  await page.getByTestId('md-contact-name').fill(' 李美玲 ');
  await page.getByTestId('md-contact-email').fill('not-an-email');
  await page.getByTestId('md-contact-save').click();
  await expect(page.getByTestId('md-contact-form')).toContainText('請輸入一個有效的電郵地址');
  await page.getByTestId('md-contact-email').fill('mei@acme.test');
  await page.getByTestId('md-contact-default').check();
  await page.getByTestId('md-contact-save').click();
  await expect(page.getByTestId('md-contact-default-30')).toBeVisible();
  await expect(page.getByTestId('md-contact-default-11')).toHaveCount(0);
  expect(writes.at(-1)).toMatchObject({ method: 'POST', body: { name: '李美玲', email: 'mei@acme.test', mobile: null, isDefault: true } });

  await page.getByTestId('md-contact-edit-11').click();
  await page.getByTestId('md-contact-active').uncheck();
  await page.getByTestId('md-contact-save').click();
  await expect(page.getByTestId('md-contact-row-11')).toContainText('停用');
  expect(writes.at(-1)).toMatchObject({ method: 'PUT', body: { name: '王小明', isActive: false, isDefault: false, version: 1 } });

  await page.getByTestId('md-contact-edit-30').click();
  contacts.splice(contacts.findIndex((row) => row.id === 30), 1);
  await page.getByTestId('md-contact-save').click();
  await expect(page.getByTestId('md-contact-error')).toContainText('此客戶找不到該聯絡人');
  await expect(page.getByTestId('md-contact-row-30')).toHaveCount(0);
  await expect(page.getByTestId('md-contact-cancel')).toHaveCount(0);

  await page.getByTestId('md-contact-delete-11').click();
  await page.getByTestId('erp-message-ok').click();
  await expect(page.getByTestId('md-contact-row-11')).toHaveCount(0);
  expect(writes.at(-1)!.url).toContain('/customers/1/contacts/11?version=2');

  await page.getByTestId('md-address-address').fill(' 台北市信義路 1 號 ');
  await page.getByTestId('md-address-default').check();
  await page.getByTestId('md-address-save').click();
  await expect(page.getByTestId('md-address-default-31')).toBeVisible();
  expect(writes.at(-1)).toMatchObject({ method: 'POST', body: { address: '台北市信義路 1 號', postalCode: null, isDefault: true } });
});

test.describe('客戶聯絡人只讀權限', () => {
  test.use({ authorization: { ...E2E_AUTHORIZATION, isFullAccess: false,
    permissions: [{ code: 'MD.CUSTOMER.VIEW', dataScope: 'Company' }] } });
  test('可查看聯絡人與地址但不可維護', async ({ page }) => {
    await page.route('**/api/v1/md/customers?**', (route) => route.fulfill({ json: { items: [
      { id: 1, code: 'VIEW', name: 'View only', categoryId: null, isActive: true, version: 1 }], page: 1, pageSize: 20, totalCount: 1 } }));
    await page.route('**/api/v1/md/customers/1/contacts', (route) => route.fulfill({ json: [
      { id: 11, customerId: 1, name: '王小明', position: null, mobile: null, phone: null, email: null, messagingId: null,
        note: null, isDefault: true, isActive: true, version: 1 }] }));
    await page.route('**/api/v1/md/customers/1/addresses', (route) => route.fulfill({ json: [
      { id: 21, customerId: 1, address: '台北市', postalCode: null, recipient: null, phone: null, isDefault: false, isActive: true, version: 1 }] }));
    await page.goto('/md/customers');
    await page.getByTestId('md-customer-contacts-VIEW').click();
    await expect(page.getByTestId('md-contact-row-11')).toBeVisible();
    await expect(page.getByTestId('md-address-row-21')).toContainText('台北市');
    await expect(page.getByTestId('md-contact-form')).toHaveCount(0);
    await expect(page.getByTestId('md-address-form')).toHaveCount(0);
    await expect(page.getByTestId('md-contact-edit-11')).toHaveCount(0);
  });
});
