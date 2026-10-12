// 介面 8.15（REQ_F4118.44.1–4）：物料圖片對話框的列表與預覽、上傳、排序號、主圖、刪除及錯誤處理（ADR-045）。
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ANIMATION_MODULE_TYPE } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideNzIcons } from 'ng-zorro-antd/icon';
import { of } from 'rxjs';
import { provideTestTranslations } from '../../../testing/i18n-fixtures';
import { PermissionService } from '../../core/auth/permission.service';
import { APP_ICONS } from '../../core/icons';
import { ErpDialogService } from '../../shared/dialogs/dialog.service';
import { ItemImageDialog } from './item-image-dialog';
import { ITEM_IMAGE_MAX_BYTES, ItemImage, itemImageFileError, MAX_IMAGES_PER_ITEM } from './item-image.service';

const item = { id: 3, code: 'BOLT', name: 'Bolt', specification: null, baseUomId: 1, brandId: null, categoryId: null, isActive: true, version: 2 };
const API = '/api/v1/md/items/3/images';
const PNG = new Uint8Array([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);
const image = (id: number, overrides: Partial<ItemImage> = {}): ItemImage => ({ id, itemId: 3, contentType: 'image/png',
  fileName: `p${id}.png`, sizeBytes: 2048, width: 320, height: 240, sortOrder: id, isPrimary: id === 1, version: 1, ...overrides });
const file = (name: string, type: string, size = 10): File => new File([new Uint8Array(size)], name, { type });

describe('MD-IMG-UI item image file check', () => {
  it('checks file type and size like the server', () => {
    expect(itemImageFileError(file('a.png', 'image/png', ITEM_IMAGE_MAX_BYTES))).toBeNull();
    expect(itemImageFileError(file('a.gif', 'image/gif'))).toBe('errors.MD.ITEM_IMAGE.UNSUPPORTED_TYPE');
    expect(itemImageFileError(file('a.png', 'image/png', ITEM_IMAGE_MAX_BYTES + 1))).toBe('errors.MD.ITEM_IMAGE.TOO_LARGE');
    expect(itemImageFileError(file('a.png', 'image/png', 0))).toBe('errors.COMMON.REQUEST.VALIDATION_FAILED');
  });
});

describe('MD-IMG-UI item image dialog', () => {
  const confirm = vi.fn(() => of(true));
  let granted: Set<string>;
  function setup(permissions: string[]) {
    confirm.mockClear();
    granted = new Set(permissions);
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(),
      ...provideTestTranslations('en'), provideNzIcons(APP_ICONS),
      { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' },
      { provide: ErpDialogService, useValue: { confirm } },
      { provide: PermissionService, useValue: { has: (code: string) => granted.has(code),
        hasAny: (codes: string[]) => codes.some((code) => granted.has(code)) } }] });
    const fixture = TestBed.createComponent(ItemImageDialog);
    fixture.componentRef.setInput('item', item);
    TestBed.tick();
    return { fixture, dialog: fixture.componentInstance, http: TestBed.inject(HttpTestingController) };
  }
  async function open(permissions: string[], rows = [image(1), image(2)]) {
    const context = setup(permissions);
    context.http.expectOne(r => r.method === 'GET' && r.url === API).flush(rows);
    flushContents(context.http, rows);
    await vi.waitFor(() => expect(Object.keys(context.dialog.previews())).toHaveLength(rows.length));
    context.fixture.detectChanges();
    return context;
  }
  function flushContents(http: HttpTestingController, rows: ItemImage[]) {
    for (const row of rows) {
      const content = http.expectOne(`${API}/${row.id}/content`);
      expect(content.request.responseType).toBe('blob');
      content.flush(new Blob([PNG], { type: row.contentType }));
    }
  }
  const el = (testId: string) => document.querySelector(`[data-testid="${testId}"]`);
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('previews images as data URLs and is read-only without edit permission', async () => {
    const { dialog } = await open(['MD.ITEM.VIEW']);
    expect((el('md-item-image-preview-1') as HTMLImageElement).src).toMatch(/^data:image\/png;base64,/);
    expect(el('md-item-image-primary-1')?.textContent).toContain('Primary');
    expect(el('md-item-image-row-2')?.textContent).toContain('2.0 KB · 320 × 240 px');
    expect(el('md-item-image-upload')).toBeNull();
    expect(el('md-item-image-delete-1')).toBeNull();
    expect(el('md-item-image-set-primary-2')).toBeNull();
    dialog.upload(file('x.png', 'image/png'));
    dialog.setPrimary(image(2));
  });

  it('uploads multipart with the primary flag and reloads only new previews', async () => {
    const { dialog, http } = await open(['MD.ITEM.VIEW', 'MD.ITEM.EDIT']);
    dialog.uploadPrimary = true;
    dialog.upload(file('new.jpg', 'image/jpeg'));
    const post = http.expectOne(r => r.method === 'POST' && r.url === API);
    const form = post.request.body as FormData;
    expect((form.get('file') as File).name).toBe('new.jpg');
    expect(form.get('isPrimary')).toBe('true');
    expect(form.has('sortOrder')).toBe(false);
    post.flush(image(3, { isPrimary: true }));
    expect(dialog.uploadPrimary).toBe(false);
    const rows = [image(3, { isPrimary: true, contentType: 'image/jpeg' }), image(1, { isPrimary: false }), image(2)];
    http.expectOne(r => r.method === 'GET' && r.url === API).flush(rows);
    flushContents(http, [rows[0]]);
    await vi.waitFor(() => expect(dialog.previews()[3]).toMatch(/^data:image\/jpeg;base64,/));
    expect(dialog.rows().map(x => x.id)).toEqual([3, 1, 2]);
  });

  it('rejects invalid files and the 11th image before sending', async () => {
    const full = Array.from({ length: MAX_IMAGES_PER_ITEM }, (_, i) => image(i + 1));
    const { dialog, fixture } = await open(['MD.ITEM.VIEW', 'MD.ITEM.EDIT'], full);
    expect((el('md-item-image-upload') as HTMLButtonElement).disabled).toBe(true);
    dialog.upload(file('a.png', 'image/png'));
    expect(dialog.error()).toBe('errors.MD.ITEM_IMAGE.LIMIT_REACHED');
    dialog.rows.set([image(1)]);
    dialog.upload(file('a.gif', 'image/gif'));
    fixture.detectChanges();
    expect(el('md-item-image-error')?.textContent).toContain('Only PNG and JPEG');
  });

  it('changes sort order and primary with versions, and validates the sort order', async () => {
    const { dialog, http, fixture } = await open(['MD.ITEM.VIEW', 'MD.ITEM.EDIT']);
    const input = el('md-item-image-sort-2') as HTMLInputElement;
    input.value = '10000';
    input.dispatchEvent(new Event('change'));
    expect(dialog.error()).toBe('md.itemImage.invalidSortOrder');
    expect(input.value).toBe('2');
    input.value = '2';
    input.dispatchEvent(new Event('change'));
    http.expectNone(r => r.method === 'PUT');
    input.value = '7';
    input.dispatchEvent(new Event('change'));
    const put = http.expectOne(r => r.method === 'PUT' && r.url === `${API}/2`);
    expect(put.request.body).toEqual({ sortOrder: 7, isPrimary: false, version: 1 });
    put.flush(image(2, { sortOrder: 7, version: 2 }));
    http.expectOne(r => r.method === 'GET' && r.url === API).flush([image(1), image(2, { sortOrder: 7, version: 2 })]);
    fixture.detectChanges();
    (el('md-item-image-set-primary-2') as HTMLButtonElement).click();
    const primary = http.expectOne(r => r.method === 'PUT' && r.url === `${API}/2`);
    expect(primary.request.body).toEqual({ sortOrder: 7, isPrimary: true, version: 2 });
    primary.flush({ errorCode: 'COMMON.CONCURRENCY.CONFLICT' }, { status: 409, statusText: 'Conflict' });
    expect(dialog.error()).toBe('errors.COMMON.CONCURRENCY.CONFLICT');
    http.expectOne(r => r.method === 'GET' && r.url === API).flush([image(1), image(2, { sortOrder: 7, version: 3 })]);
    expect(dialog.rows()[1].version).toBe(3);
    expect(dialog.error()).toBe('errors.COMMON.CONCURRENCY.CONFLICT');
  });

  it('deletes after confirmation and shows server errors', async () => {
    const { dialog, http, fixture } = await open(['MD.ITEM.VIEW', 'MD.ITEM.EDIT']);
    confirm.mockReturnValueOnce(of(false));
    dialog.confirmDelete(image(2));
    http.expectNone(r => r.method === 'DELETE');
    dialog.confirmDelete(image(2));
    expect(confirm).toHaveBeenLastCalledWith(expect.objectContaining({ params: { name: 'p2.png' } }));
    const del = http.expectOne(r => r.method === 'DELETE' && r.url === `${API}/2`);
    expect(del.request.params.get('version')).toBe('1');
    del.flush(null, { status: 204, statusText: 'No Content' });
    http.expectOne(r => r.method === 'GET' && r.url === API).flush([image(1)]);
    expect(dialog.previews()[2]).toBeUndefined();

    dialog.upload(file('tiny.png', 'image/png'));
    http.expectOne(r => r.method === 'POST').flush({ errorCode: 'MD.ITEM_IMAGE.INVALID_DIMENSIONS' }, { status: 422, statusText: 'Unprocessable' });
    fixture.detectChanges();
    expect(el('md-item-image-error')?.textContent).toContain('16–4096 pixels');
    expect(dialog.busy()).toBe(false);
  });

  it('shows a placeholder when a preview cannot be loaded and a list error', async () => {
    const { fixture, http, dialog } = setup(['MD.ITEM.VIEW']);
    http.expectOne(r => r.method === 'GET' && r.url === API).flush([image(1)]);
    http.expectOne(`${API}/1/content`).flush(null, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();
    expect(el('md-item-image-row-1')?.textContent).toContain('Preview unavailable');
    dialog.reload();
    http.expectOne(r => r.method === 'GET' && r.url === API).flush({ errorCode: 'MD.ITEM.NOT_FOUND' }, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();
    expect(el('md-item-image-error')?.textContent).toContain('Item not found');
  });

  it('keeps a new item busy when an old upload finishes', async () => {
    const { fixture, dialog, http } = await open(['MD.ITEM.VIEW', 'MD.ITEM.EDIT'], []);
    dialog.upload(file('old.png', 'image/png'));
    const oldUpload = http.expectOne(r => r.method === 'POST' && r.url === API);
    fixture.componentRef.setInput('item', { ...item, id: 4, code: 'NUT' });
    TestBed.tick();
    http.expectOne('/api/v1/md/items/4/images').flush([]);
    dialog.upload(file('new.png', 'image/png'));
    const newUpload = http.expectOne(r => r.method === 'POST' && r.url === '/api/v1/md/items/4/images');
    oldUpload.flush(image(3));
    expect(dialog.busy()).toBe(true);
    newUpload.flush({ ...image(4), itemId: 4 });
    http.expectOne('/api/v1/md/items/4/images').flush([]);
    expect(dialog.busy()).toBe(false);
  });
});
