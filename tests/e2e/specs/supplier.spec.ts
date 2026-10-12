import { E2E_AUTHORIZATION, expect, test } from '../support/fixtures';

test('供應商可按公司查看、新增、停用及搜尋，重複代碼顯示錯誤', async ({ page, pageErrors }) => {
  pageErrors.allow(/status of 409/);
  const rows: Record<string, { id: number; code: string; name: string; categoryId: number | null; isActive: boolean; version: number }[]> = {
    '1': [], '2': [{ id: 20, code: 'HKSUP', name: 'Hong Kong supplier', categoryId: null, isActive: true, version: 1 }],
  };
  await page.route('**/api/v1/md/supplier-categories**', (route) => route.fulfill({ json: {
    items: [{ id: 5, code: 'RAW', name: 'Raw material', isActive: true, version: 1 }],
    page: 1, pageSize: 100, totalCount: 1,
  } }));
  await page.route('**/api/v1/md/suppliers**', async (route) => {
    const request = route.request();
    const company = request.headers()['x-company-id'] ?? '';
    const data = rows[company] ??= [];
    if (request.method() === 'GET') {
      const keyword = new URL(request.url()).searchParams.get('keyword')?.toLowerCase() ?? '';
      const items = data.filter((row) => row.code.toLowerCase().includes(keyword) || row.name.toLowerCase().includes(keyword));
      await route.fulfill({ json: { items, page: 1, pageSize: 20, totalCount: items.length } });
    } else if (request.method() === 'POST') {
      const body = request.postDataJSON();
      if (data.some((row) => row.code === body.code)) {
        await route.fulfill({ status: 409, json: { errorCode: 'MD.SUPPLIER.DUPLICATE_CODE' } });
        return;
      }
      const row = { id: 9, code: body.code, name: body.name, categoryId: body.categoryId, isActive: true, version: 1 };
      data.push(row);
      await route.fulfill({ status: 201, json: row });
    } else {
      const row = data.find((entry) => entry.id === Number(new URL(request.url()).pathname.split('/').at(-1)))!;
      Object.assign(row, request.postDataJSON(), { version: row.version + 1 });
      await route.fulfill({ json: row });
    }
  });

  await page.goto('/');
  await page.getByTestId('layout-menu-md').click();
  await page.getByTestId('layout-menu-md-suppliers').click();
  await expect(page.getByTestId('md-supplier-title')).toHaveText('供應商');
  await page.getByTestId('md-supplier-create').click();
  await page.getByTestId('md-supplier-code').fill('steel');
  await page.getByTestId('md-supplier-name').fill(' Steel Co ');
  await page.getByTestId('md-supplier-category').selectOption({ label: 'Raw material' });
  await page.locator('.ant-modal-footer .ant-btn-primary').click();
  await expect(page.getByTestId('md-supplier-row-STEEL')).toContainText('Raw material');
  await page.getByTestId('md-supplier-create').click();
  await page.getByTestId('md-supplier-code').fill('STEEL');
  await page.getByTestId('md-supplier-name').fill('Again');
  await page.locator('.ant-modal-footer .ant-btn-primary').click();
  await expect(page.getByTestId('md-supplier-save-error')).toContainText('此公司已使用該供應商代碼');
  await page.locator('.ant-modal-footer .ant-btn:not(.ant-btn-primary)').click();
  await page.getByTestId('md-supplier-edit-STEEL').click();
  await page.getByTestId('md-supplier-active').uncheck();
  await page.locator('.ant-modal-footer .ant-btn-primary').click();
  await expect(page.getByTestId('md-supplier-row-STEEL')).toContainText('停用');
  await page.getByTestId('md-supplier-search').fill('missing');
  await page.getByTestId('md-supplier-search-button').click();
  await expect(page.getByTestId('md-supplier-row-STEEL')).toHaveCount(0);
  await page.getByTestId('md-supplier-search').fill('');
  await page.getByTestId('md-supplier-search-button').click();
  await page.getByTestId('layout-company-select').click();
  await page.locator('nz-option-item', { hasText: 'HK01 Hong Kong' }).click();
  await expect(page.getByTestId('md-supplier-row-HKSUP')).toBeVisible();
  await expect(page.getByTestId('md-supplier-row-STEEL')).toHaveCount(0);
});

test.describe('供應商只讀權限', () => {
  test.use({ authorization: { ...E2E_AUTHORIZATION, isFullAccess: false,
    permissions: [{ code: 'MD.SUPPLIER.VIEW', dataScope: 'Company' }] } });
  test('隱藏寫入操作且不載入分類', async ({ page }) => {
    let categoryRequests = 0;
    await page.route('**/api/v1/md/supplier-categories**', (route) => { categoryRequests++; return route.abort(); });
    await page.route('**/api/v1/md/suppliers**', (route) => route.fulfill({ json: {
      items: [{ id: 1, code: 'VIEW', name: 'View only', categoryId: null, isActive: true, version: 1 }],
      page: 1, pageSize: 20, totalCount: 1,
    } }));
    await page.goto('/md/suppliers');
    await expect(page.getByTestId('md-supplier-row-VIEW')).toBeVisible();
    await expect(page.getByTestId('md-supplier-create')).toHaveCount(0);
    await expect(page.getByTestId('md-supplier-edit-VIEW')).toHaveCount(0);
    expect(categoryRequests).toBe(0);
  });
});

test('供應商聯絡人：只有聯絡人段，新增預設、修改、找不到提示、刪除', async ({ page, pageErrors }) => {
  pageErrors.allow(/status of 404/);
  type Contact = { id: number; supplierId: number; name: string; isDefault: boolean; isActive: boolean; version: number; [key: string]: unknown };
  const contacts: Contact[] = [{ id: 11, supplierId: 1, name: '陳大文', position: '業務', mobile: null, phone: '02-1234',
    email: null, messagingId: null, note: null, isDefault: true, isActive: true, version: 1 }];
  const writes: { method: string; url: string; body: unknown }[] = [];
  let addressRequests = 0;
  await page.route('**/api/v1/md/suppliers?**', (route) => route.fulfill({ json: { items: [
    { id: 1, code: 'STEEL', name: 'Steel Co', categoryId: null, isActive: true, version: 1 }], page: 1, pageSize: 20, totalCount: 1 } }));
  await page.route('**/api/v1/md/suppliers/1/addresses**', (route) => { addressRequests++; return route.abort(); });
  await page.route('**/api/v1/md/suppliers/1/contacts**', async (route) => {
    const request = route.request();
    if (request.method() === 'GET') {
      return route.fulfill({ json: [...contacts].sort((a, b) => Number(b.isDefault) - Number(a.isDefault)) });
    }
    const body = request.postDataJSON() as Contact | null;
    writes.push({ method: request.method(), url: request.url(), body });
    if (request.method() === 'POST') {
      if (body!.isDefault) contacts.forEach((row) => { row.isDefault = false; });
      contacts.push({ ...body!, id: 30, supplierId: 1, isActive: true, version: 1 });
      return route.fulfill({ status: 201, json: contacts.at(-1) });
    }
    const index = contacts.findIndex((row) => row.id === Number(new URL(request.url()).pathname.split('/').at(-1)));
    if (index < 0) return route.fulfill({ status: 404, contentType: 'application/problem+json',
      json: { status: 404, errorCode: 'MD.SUPPLIER_CONTACT.NOT_FOUND' } });
    if (request.method() === 'PUT') {
      Object.assign(contacts[index]!, body, { version: contacts[index]!.version + 1 });
      return route.fulfill({ json: contacts[index] });
    }
    contacts.splice(index, 1);
    return route.fulfill({ status: 204 });
  });

  await page.goto('/md/suppliers');
  await page.getByTestId('md-supplier-contacts-STEEL').click();
  await expect(page.getByTestId('md-supplier-contact-dialog')).toBeVisible();
  await expect(page.locator('.ant-modal-title')).toContainText('STEEL 的聯絡人');
  await expect(page.getByTestId('md-contact-row-11')).toContainText('陳大文');
  await expect(page.getByTestId('md-address-table')).toHaveCount(0);
  await expect(page.getByTestId('md-address-form')).toHaveCount(0);

  await page.getByTestId('md-contact-name').fill(' 林小華 ');
  await page.getByTestId('md-contact-email').fill('hua@steel.test');
  await page.getByTestId('md-contact-default').check();
  await page.getByTestId('md-contact-save').click();
  await expect(page.getByTestId('md-contact-default-30')).toBeVisible();
  await expect(page.getByTestId('md-contact-default-11')).toHaveCount(0);
  expect(writes.at(-1)).toMatchObject({ method: 'POST', body: { name: '林小華', email: 'hua@steel.test', isDefault: true } });
  expect(writes.at(-1)!.url).toContain('/suppliers/1/contacts');

  await page.getByTestId('md-contact-edit-11').click();
  await page.getByTestId('md-contact-active').uncheck();
  await page.getByTestId('md-contact-save').click();
  await expect(page.getByTestId('md-contact-row-11')).toContainText('停用');

  await page.getByTestId('md-contact-edit-30').click();
  contacts.splice(contacts.findIndex((row) => row.id === 30), 1);
  await page.getByTestId('md-contact-save').click();
  await expect(page.getByTestId('md-contact-error')).toContainText('此供應商找不到該聯絡人');
  await expect(page.getByTestId('md-contact-row-30')).toHaveCount(0);

  await page.getByTestId('md-contact-delete-11').click();
  await page.getByTestId('erp-message-ok').click();
  await expect(page.getByTestId('md-contact-row-11')).toHaveCount(0);
  expect(writes.at(-1)!.url).toContain('/suppliers/1/contacts/11?version=2');
  expect(addressRequests).toBe(0);
});

test.describe('供應商聯絡人只讀權限', () => {
  test.use({ authorization: { ...E2E_AUTHORIZATION, isFullAccess: false,
    permissions: [{ code: 'MD.SUPPLIER.VIEW', dataScope: 'Company' }] } });
  test('可查看聯絡人但不可維護', async ({ page }) => {
    await page.route('**/api/v1/md/suppliers?**', (route) => route.fulfill({ json: { items: [
      { id: 1, code: 'VIEW', name: 'View only', categoryId: null, isActive: true, version: 1 }], page: 1, pageSize: 20, totalCount: 1 } }));
    await page.route('**/api/v1/md/suppliers/1/contacts', (route) => route.fulfill({ json: [
      { id: 11, supplierId: 1, name: '陳大文', position: null, mobile: null, phone: null, email: null, messagingId: null,
        note: null, isDefault: true, isActive: true, version: 1 }] }));
    await page.goto('/md/suppliers');
    await page.getByTestId('md-supplier-contacts-VIEW').click();
    await expect(page.getByTestId('md-contact-row-11')).toBeVisible();
    await expect(page.getByTestId('md-contact-form')).toHaveCount(0);
    await expect(page.getByTestId('md-contact-edit-11')).toHaveCount(0);
  });
});
