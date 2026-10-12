import type { Page } from '@playwright/test';
import { E2E_AUTHORIZATION, expect, test } from '../support/fixtures';

/** 介面 8.15／8.16（REQ_F4118.44.1–4、45.1–6，ADR-045）：物料圖片、物料屬性與屬性定義頁。 */
const PNG = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==', 'base64');
const BOLT = { id: 1, code: 'BOLT', name: '螺栓', specification: null, baseUomId: 1, brandId: null, categoryId: null, isActive: true, version: 1 };

async function mockItemPage(page: Page): Promise<void> {
  await page.route('**/api/v1/md/items?**', (route) => route.fulfill({ json: { items: [BOLT], totalCount: 1 } }));
  await page.route('**/api/v1/md/uoms**', (route) => route.fulfill({ json: { items: [], totalCount: 0 } }));
  await page.route('**/api/v1/md/item-brands**', (route) => route.fulfill({ json: { items: [], totalCount: 0 } }));
  await page.route('**/api/v1/md/item-categories**', (route) => route.fulfill({ json: [] }));
}

test('MD-IMG-UI 物料圖片：預覽、上傳、格式檢查、設主圖與刪除', async ({ page, pageErrors }) => {
  pageErrors.allow(/status of 422/);
  type Image = { id: number; itemId: number; contentType: string; fileName: string; sizeBytes: number; width: number;
    height: number; sortOrder: number; isPrimary: boolean; version: number };
  const images: Image[] = [{ id: 5, itemId: 1, contentType: 'image/png', fileName: 'front.png', sizeBytes: PNG.length,
    width: 1, height: 1, sortOrder: 1, isPrimary: true, version: 1 }];
  const writes: { method: string; url: string; body?: unknown }[] = [];
  await mockItemPage(page);
  await page.route('**/api/v1/md/items/1/images**', async (route) => {
    const request = route.request(); const url = new URL(request.url());
    if (url.pathname.endsWith('/content')) return route.fulfill({ body: PNG, contentType: 'image/png' });
    if (request.method() === 'GET') return route.fulfill({ json: [...images].sort((a, b) => Number(b.isPrimary) - Number(a.isPrimary) || a.sortOrder - b.sortOrder) });
    if (request.method() === 'POST') {
      const body = request.postDataBuffer()!.toString('latin1');
      writes.push({ method: 'POST', url: request.url(), body });
      if (body.includes('tiny.png')) return route.fulfill({ status: 422, contentType: 'application/problem+json',
        json: { status: 422, errorCode: 'MD.ITEM_IMAGE.INVALID_DIMENSIONS' } });
      images.push({ ...images[0], id: 6, fileName: 'side.png', sortOrder: 2, isPrimary: false });
      return route.fulfill({ status: 201, json: images.at(-1) });
    }
    if (request.method() === 'PUT') {
      const body = request.postDataJSON() as { sortOrder: number; isPrimary: boolean; version: number };
      writes.push({ method: 'PUT', url: request.url(), body });
      images.forEach((x) => { x.isPrimary = x.id === 6; x.version++; });
      return route.fulfill({ json: images.find((x) => x.id === 6) });
    }
    writes.push({ method: 'DELETE', url: request.url() });
    images.splice(images.findIndex((x) => url.pathname.endsWith(`/${x.id}`)), 1);
    return route.fulfill({ status: 204 });
  });
  await page.goto('/md/items');
  await page.getByTestId('md-item-images-BOLT').click();
  await expect(page.getByTestId('md-item-image-preview-5')).toHaveAttribute('src', /^data:image\/png;base64,/);
  await expect(page.getByTestId('md-item-image-primary-5')).toContainText('主圖');
  // 用戶端先擋下非 PNG／JPEG
  await page.getByTestId('md-item-image-file').setInputFiles({ name: 'a.gif', mimeType: 'image/gif', buffer: Buffer.from('GIF89a') });
  await expect(page.getByTestId('md-item-image-error')).toContainText('只接受 PNG 和 JPEG 圖片');
  await page.getByTestId('md-item-image-file').setInputFiles({ name: 'tiny.png', mimeType: 'image/png', buffer: PNG });
  await expect(page.getByTestId('md-item-image-error')).toContainText('16–4096 像素');
  await page.getByTestId('md-item-image-file').setInputFiles({ name: 'side.png', mimeType: 'image/png', buffer: PNG });
  await expect(page.getByTestId('md-item-image-preview-6')).toBeVisible();
  expect(writes.at(-1)!.body).toContain('name="isPrimary"');
  await page.getByTestId('md-item-image-set-primary-6').click();
  await expect(page.getByTestId('md-item-image-primary-6')).toBeVisible();
  expect(writes.at(-1)).toMatchObject({ method: 'PUT', body: { sortOrder: 2, isPrimary: true, version: 1 } });
  await page.getByTestId('md-item-image-delete-5').click();
  await page.getByTestId('erp-message-ok').click();
  await expect(page.getByTestId('md-item-image-row-5')).toHaveCount(0);
  expect(writes.at(-1)!.url).toContain('/images/5?version=2');
});

test('MD-ATTR-UI 物料屬性：只列啟用屬性、伺服器列錯誤與整批儲存', async ({ page, pageErrors }) => {
  pageErrors.allow(/status of 400/);
  let saved: unknown;
  await mockItemPage(page);
  await page.route('**/api/v1/md/item-attributes**', (route) => route.fulfill({ json: { page: 1, pageSize: 200, totalCount: 3, items: [
    { id: 1, code: 'COLOR', name: '顏色', sortOrder: 1, itemCount: 1, isActive: true, version: 1 },
    { id: 2, code: 'SIZE', name: '尺寸', sortOrder: 2, itemCount: 0, isActive: true, version: 1 },
    { id: 3, code: 'OLD', name: '舊屬性', sortOrder: 3, itemCount: 0, isActive: false, version: 1 }] } }));
  await page.route('**/api/v1/md/items/1/attributes', async (route) => {
    if (route.request().method() === 'GET') return route.fulfill({ json: [
      { attributeId: 1, attributeCode: 'COLOR', attributeName: '顏色', sortOrder: 1, isAttributeActive: true, value: '紅' }] });
    const body = route.request().postDataJSON() as { attributes: { attributeId: number; value: string }[] };
    if (body.attributes.some((x) => x.value === '重複')) return route.fulfill({ status: 400, contentType: 'application/problem+json',
      json: { status: 400, errorCode: 'COMMON.REQUEST.VALIDATION_FAILED', errors: { 'attributes[1].value': ['bad'] } } });
    saved = body;
    return route.fulfill({ json: [] });
  });
  await page.goto('/md/items');
  await page.getByTestId('md-item-attributes-BOLT').click();
  await expect(page.getByTestId('md-item-attribute-input-0')).toHaveValue('紅');
  await page.getByTestId('md-item-attribute-add').click();
  const select = page.getByTestId('md-item-attribute-select-1');
  await expect(select.locator('option', { hasText: 'OLD' })).toHaveCount(0);
  await expect(select.locator('option', { hasText: 'COLOR' })).toHaveCount(0);
  await select.selectOption({ label: 'SIZE — 尺寸' });
  await page.getByTestId('md-item-attribute-input-1').fill('重複');
  await page.getByRole('button', { name: '儲存' }).click();
  await expect(page.getByTestId('md-item-attribute-row-error-1')).toContainText('請輸入最多 200 字的值');
  await page.getByTestId('md-item-attribute-input-1').fill(' M ');
  await page.getByRole('button', { name: '儲存' }).click();
  await expect(page.getByTestId('md-item-attribute-dialog')).toHaveCount(0);
  expect(saved).toEqual({ attributes: [{ attributeId: 1, value: '紅' }, { attributeId: 2, value: 'M' }] });
});

test('MD-ATTR-UI 屬性定義頁：選單、排序號、物料數與使用中不可刪除', async ({ page, pageErrors }) => {
  pageErrors.allow(/status of 409/);
  const writes: { method: string; url: string; body?: unknown }[] = [];
  await page.route('**/api/v1/md/item-attributes**', async (route) => {
    const request = route.request();
    if (request.method() === 'GET') return route.fulfill({ json: { page: 1, pageSize: 20, totalCount: 1, items: [
      { id: 1, code: 'COLOR', name: '顏色', sortOrder: 1, itemCount: 4, isActive: true, version: 2 }] } });
    writes.push({ method: request.method(), url: request.url(), body: request.method() === 'DELETE' ? undefined : request.postDataJSON() });
    if (request.method() === 'DELETE') return route.fulfill({ status: 409, contentType: 'application/problem+json',
      json: { status: 409, errorCode: 'MD.ITEM_ATTRIBUTE.IN_USE' } });
    return route.fulfill({ status: 201, json: { id: 2, code: 'SIZE', name: '尺寸', sortOrder: 5, itemCount: 0, isActive: true, version: 1 } });
  });
  await page.goto('/');
  await page.getByTestId('layout-menu-md').click();
  await page.getByTestId('layout-menu-md-item-attributes').click();
  await expect(page.getByTestId('md-process-title')).toHaveText('物料屬性');
  await expect(page.getByTestId('md-process-row-COLOR')).toContainText('4');
  await page.getByTestId('md-process-create').click();
  await page.getByTestId('md-process-code').fill('size');
  await page.getByTestId('md-process-name').fill('尺寸');
  await page.getByTestId('md-process-sort').fill('5');
  await page.getByRole('button', { name: '儲存' }).click();
  await expect.poll(() => writes.length).toBe(1);
  expect(writes[0]).toMatchObject({ method: 'POST', body: { code: 'SIZE', name: '尺寸', sortOrder: 5 } });
  await page.getByTestId('md-process-row-COLOR').getByRole('button', { name: '刪除' }).click();
  await page.getByTestId('erp-message-ok').click();
  await expect(page.getByText('已有物料使用此屬性，請改為停用')).toBeVisible();
  expect(writes.at(-1)!.url).toContain('/item-attributes/1?version=2');
});

test.describe('物料圖片與屬性只讀', () => {
  test.use({ authorization: { ...E2E_AUTHORIZATION, isFullAccess: false,
    permissions: [{ code: 'MD.ITEM.VIEW', dataScope: 'Company' }] } });
  test('沒有 MD.ITEM.EDIT 時只能查看，沒有屬性定義權限時不能進入定義頁', async ({ page }) => {
    await mockItemPage(page);
    await page.route('**/api/v1/md/items/1/images**', (route) => route.fulfill({ json: [] }));
    await page.route('**/api/v1/md/items/1/attributes', (route) => route.fulfill({ json: [
      { attributeId: 1, attributeCode: 'COLOR', attributeName: '顏色', sortOrder: 1, isAttributeActive: true, value: '紅' }] }));
    await page.goto('/md/items');
    await page.getByTestId('md-item-images-BOLT').click();
    await expect(page.getByTestId('md-item-image-dialog')).toBeVisible();
    await expect(page.getByTestId('md-item-image-upload')).toHaveCount(0);
    await page.keyboard.press('Escape');
    await page.getByTestId('md-item-attributes-BOLT').click();
    await expect(page.getByTestId('md-item-attribute-value-COLOR')).toHaveText('紅');
    await expect(page.getByTestId('md-item-attribute-form')).toHaveCount(0);
    await page.goto('/md/item-attributes');
    await expect(page.getByTestId('error-page-403')).toBeVisible();
  });
});
