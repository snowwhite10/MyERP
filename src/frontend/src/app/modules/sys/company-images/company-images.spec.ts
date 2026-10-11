// 介面 8.57（REQ_F4118.19.1–2）：Logo 與印章頁、API 服務、用戶端檔案檢查、預覽、上傳／替換／刪除與錯誤處理（ADR-028）。
import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting, TestRequest } from '@angular/common/http/testing';
import { ANIMATION_MODULE_TYPE } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNzIcons } from 'ng-zorro-antd/icon';
import { firstValueFrom } from 'rxjs';
import { seedSession } from '../../../../testing/auth-fixtures';
import { provideTestTranslations } from '../../../../testing/i18n-fixtures';
import { AUTHORIZATION_API, PermissionService } from '../../../core/auth/permission.service';
import { APP_ICONS } from '../../../core/icons';
import { ErpDialogService } from '../../../shared/dialogs/dialog.service';
import {
  COMPANY_IMAGES_API, CompanyImageDetail, companyImageErrorKey, CompanyImageService, IMAGE_MAX_BYTES, imageFileError, toDataUrl,
} from './company-image.service';
import { CompanyImages } from './company-images';

const PNG = new Uint8Array([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);
const detail = (overrides: Partial<CompanyImageDetail> = {}): CompanyImageDetail => ({
  kind: 'Logo', contentType: 'image/png', fileName: 'logo.png', sizeBytes: 2048, width: 240, height: 80, version: 1,
  updatedAt: '2026-10-11T16:30:12.123456', ...overrides,
});
const file = (name: string, type: string, size = 10): File => new File([new Uint8Array(size)], name, { type });
const httpError = (status: number, body: object) => new HttpErrorResponse({ status, error: body });

describe('SYS company images', () => {
  let controller: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    seedSession();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), ...provideTestTranslations('en'), provideNzIcons(APP_ICONS),
        { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' }],
    });
    controller = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    TestBed.inject(ErpDialogService).closeAll();
    controller.verify();
    localStorage.clear();
  });

  async function grant(codes: string[]): Promise<void> {
    const done = firstValueFrom(TestBed.inject(PermissionService).load());
    controller.expectOne(AUTHORIZATION_API).flush({
      companies: [{ id: 1, code: 'TW01', name: 'Taipei', isDefault: true }],
      companyId: 1, isFullAccess: false, permissions: codes.map((code) => ({ code, dataScope: 'Company' })),
    });
    await done;
  }
  const expectList = (): TestRequest => controller.expectOne((r) => r.method === 'GET' && r.url === COMPANY_IMAGES_API);
  const expectContent = (kind: string): TestRequest =>
    controller.expectOne((r) => r.method === 'GET' && r.url === `${COMPANY_IMAGES_API}/${kind}/content`);

  describe('service and helpers', () => {
    it('lists, downloads as blob, uploads multipart with version and deletes with version', () => {
      const api = TestBed.inject(CompanyImageService);
      api.list().subscribe();
      expectList().flush([]);
      api.content('Stamp').subscribe();
      const content = expectContent('stamp');
      expect(content.request.responseType).toBe('blob');
      content.flush(new Blob([PNG], { type: 'image/png' }));

      const image = file('logo.png', 'image/png');
      api.upload('Logo', image, 3).subscribe();
      const put = controller.expectOne((r) => r.method === 'PUT' && r.url === `${COMPANY_IMAGES_API}/logo`);
      const form = put.request.body as FormData;
      expect((form.get('file') as File).name).toBe('logo.png');
      expect(form.get('version')).toBe('3');
      put.flush(detail());

      api.remove('Stamp', 2).subscribe();
      const removal = controller.expectOne((r) => r.method === 'DELETE' && r.url === `${COMPANY_IMAGES_API}/stamp`);
      expect(removal.request.params.get('version')).toBe('2');
      removal.flush(null, { status: 204, statusText: 'No Content' });
    });

    it('checks the selected file type and size before uploading', () => {
      expect(imageFileError(file('a.png', 'image/png'))).toBeNull();
      expect(imageFileError(file('a.jpg', 'image/jpeg', IMAGE_MAX_BYTES))).toBeNull();
      expect(imageFileError(file('a.gif', 'image/gif'))).toBe('errors.SYS.COMPANY_IMAGE.UNSUPPORTED_TYPE');
      expect(imageFileError(file('a.svg', 'image/svg+xml'))).toBe('errors.SYS.COMPANY_IMAGE.UNSUPPORTED_TYPE');
      expect(imageFileError(file('a.png', 'image/png', IMAGE_MAX_BYTES + 1))).toBe('errors.SYS.COMPANY_IMAGE.TOO_LARGE');
      expect(imageFileError(file('a.png', 'image/png', 0))).toBe('errors.COMMON.REQUEST.VALIDATION_FAILED');
    });

    it('maps API errors and converts only PNG/JPEG blobs to data URLs', async () => {
      expect(companyImageErrorKey(httpError(422, { errorCode: 'SYS.COMPANY_IMAGE.INVALID_DIMENSIONS' })))
        .toBe('errors.SYS.COMPANY_IMAGE.INVALID_DIMENSIONS');
      expect(companyImageErrorKey(httpError(409, { errorCode: 'COMMON.CONCURRENCY.CONFLICT' }))).toBe('errors.COMMON.CONCURRENCY.CONFLICT');
      expect(companyImageErrorKey(httpError(500, {}))).toBe('errors.http.unknown');
      expect(await toDataUrl(new Blob([PNG], { type: 'image/png' }))).toMatch(/^data:image\/png;base64,iVBORw0KGgo/);
      expect(await toDataUrl(new Blob(['<svg/>'], { type: 'image/svg+xml' }))).toBeNull();
    });
  });

  describe('page', () => {
    let fixture: ComponentFixture<CompanyImages>;
    const el = (testId: string): HTMLElement | null => fixture.nativeElement.querySelector(`[data-testid="${testId}"]`);
    async function render(): Promise<void> { fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges(); }
    async function open(codes = ['SYS.PARAMETER.VIEW', 'SYS.PARAMETER.EDIT'], images = [detail()]): Promise<CompanyImages> {
      await grant(codes);
      fixture = TestBed.createComponent(CompanyImages);
      await render();
      expectList().flush(images);
      await render();
      for (const image of images) expectContent(image.kind.toLowerCase()).flush(new Blob([PNG], { type: image.contentType }));
      await vi.waitFor(() => expect(Object.keys(fixture.componentInstance.previews())).toHaveLength(images.length));
      await render();
      return fixture.componentInstance;
    }

    it('shows the uploaded logo with its details and an empty stamp card', async () => {
      await open();
      expect((el('image-preview-Logo') as HTMLImageElement).src).toMatch(/^data:image\/png;base64,/);
      expect(el('image-preview-Logo')!.getAttribute('alt')).toBe('Company logo');
      expect(el('image-meta-Logo')!.textContent).toContain('logo.png');
      expect(el('image-meta-Logo')!.textContent).toContain('2.0 KB · 240 × 80 px');
      expect(el('image-meta-Logo')!.textContent).toContain('2026-10-11 16:30');
      expect(el('image-empty-Stamp')!.textContent).toContain('Not uploaded');
      expect(el('image-upload-Logo')!.textContent).toContain('Replace');
      expect(el('image-upload-Stamp')!.textContent).toContain('Upload');
      expect(el('image-remove-Stamp')).toBeNull();
      expect(el('image-hint')!.textContent).toContain('PNG or JPEG, up to 1 MB');
    });

    it('uploads a new stamp with version 0, replaces the logo with its version and reloads the preview', async () => {
      const page = await open();
      page.upload('Stamp', file('stamp.png', 'image/png'));
      const put = controller.expectOne((r) => r.method === 'PUT' && r.url === `${COMPANY_IMAGES_API}/stamp`);
      expect((put.request.body as FormData).get('version')).toBe('0');
      expect(page.busy()).toBe('Stamp');
      put.flush(detail({ kind: 'Stamp', fileName: 'stamp.png', width: 120, height: 120 }));
      expectContent('stamp').flush(new Blob([PNG], { type: 'image/png' }));
      await vi.waitFor(() => expect(page.previews().Stamp).toBeDefined());
      await render();
      expect(page.busy()).toBeNull();
      expect(el('image-message-Stamp')!.textContent).toContain('Image saved.');
      expect(el('image-meta-Stamp')!.textContent).toContain('120 × 120 px');

      page.upload('Logo', file('new.jpg', 'image/jpeg'));
      const replace = controller.expectOne((r) => r.method === 'PUT' && r.url === `${COMPANY_IMAGES_API}/logo`);
      expect((replace.request.body as FormData).get('version')).toBe('1');
      replace.flush(detail({ contentType: 'image/jpeg', fileName: 'new.jpg', version: 2 }));
      expectContent('logo').flush(new Blob([PNG], { type: 'image/jpeg' }));
      await vi.waitFor(() => expect(page.previews().Logo).toMatch(/^data:image\/jpeg;base64,/));
      expect(page.details().Logo!.version).toBe(2);
    });

    it('rejects unsupported files before sending and shows server errors with a reload action on conflict', async () => {
      const page = await open();
      page.upload('Stamp', file('stamp.gif', 'image/gif'));
      await render();
      controller.expectNone((r) => r.method === 'PUT');
      expect(el('image-message-Stamp')!.textContent).toContain('Only PNG and JPEG images are accepted.');

      page.upload('Stamp', file('small.png', 'image/png'));
      controller.expectOne((r) => r.method === 'PUT').flush({ errorCode: 'SYS.COMPANY_IMAGE.INVALID_DIMENSIONS' }, { status: 422, statusText: 'Unprocessable' });
      await render();
      expect(el('image-message-Stamp')!.textContent).toContain('16–2048 pixels');
      expect(page.message('Stamp')!.conflict).toBe(false);
      expect(page.busy()).toBeNull();

      page.upload('Logo', file('logo.png', 'image/png'));
      controller.expectOne((r) => r.method === 'PUT').flush({ errorCode: 'COMMON.CONCURRENCY.CONFLICT' }, { status: 409, statusText: 'Conflict' });
      await render();
      expect(page.message('Logo')!.conflict).toBe(true);
      expect(el('image-message-Logo')!.querySelector('button')).not.toBeNull();
    });

    it('selects a file through the hidden input and clears it so the same file can be chosen again', async () => {
      const page = await open();
      const input = el('image-file-Stamp') as HTMLInputElement;
      Object.defineProperty(input, 'files', { value: [file('stamp.png', 'image/png')], configurable: true });
      input.dispatchEvent(new Event('change'));
      expect(input.value).toBe('');
      controller.expectOne((r) => r.method === 'PUT' && r.url === `${COMPANY_IMAGES_API}/stamp`).flush(detail({ kind: 'Stamp' }));
      expectContent('stamp').flush(new Blob([PNG], { type: 'image/png' }));
      await vi.waitFor(() => expect(page.previews().Stamp).toBeDefined());
    });

    it('deletes after confirmation with the current version', async () => {
      const page = await open();
      const click = (testId: string) => (document.querySelector(`[data-testid="${testId}"]`) as HTMLElement).click();
      click('image-remove-Logo');
      await render();
      expect(document.querySelector('[data-testid="erp-message-title"]')?.textContent).toContain('Delete the company logo?');
      click('erp-message-cancel');
      await vi.waitFor(() => expect(document.querySelector('[data-testid="erp-message-dialog"]')).toBeNull());
      controller.expectNone((r) => r.method === 'DELETE');

      click('image-remove-Logo');
      await render();
      click('erp-message-ok');
      const removal = await vi.waitFor(() => controller.expectOne((r) => r.method === 'DELETE' && r.url === `${COMPANY_IMAGES_API}/logo`));
      expect(removal.request.params.get('version')).toBe('1');
      removal.flush(null, { status: 204, statusText: 'No Content' });
      await render();
      expect(page.details().Logo).toBeUndefined();
      expect(el('image-empty-Logo')!.textContent).toContain('Not uploaded');
      expect(el('image-message-Logo')!.textContent).toContain('Image deleted.');
    });

    it('is read-only without edit permission and shows a placeholder when the preview cannot be loaded', async () => {
      await grant(['SYS.PARAMETER.VIEW']);
      fixture = TestBed.createComponent(CompanyImages);
      await render();
      expectList().flush([detail()]);
      await render();
      expectContent('logo').flush(null, { status: 404, statusText: 'Not Found' });
      await render();
      expect(el('image-read-only')).not.toBeNull();
      expect(el('image-upload-Logo')).toBeNull();
      expect(el('image-remove-Logo')).toBeNull();
      expect(el('image-empty-Logo')!.textContent).toContain('Preview unavailable');
      fixture.componentInstance.upload('Logo', file('x.png', 'image/png'));
      fixture.componentInstance.remove('Logo');
      controller.expectNone((r) => r.method === 'PUT' || r.method === 'DELETE');
    });

    it('shows a load error', async () => {
      await grant(['SYS.PARAMETER.VIEW']);
      fixture = TestBed.createComponent(CompanyImages);
      await render();
      expectList().flush({ errorCode: 'COMMON.PERMISSION.DENIED' }, { status: 403, statusText: 'Forbidden' });
      await render();
      expect(el('image-load-error')!.textContent).toContain('You do not have permission');
    });
  });
});
