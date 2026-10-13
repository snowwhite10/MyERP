import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ANIMATION_MODULE_TYPE } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideNzIcons } from 'ng-zorro-antd/icon';
import { provideTestTranslations } from '../../../testing/i18n-fixtures';
import { APP_ICONS } from '../../core/icons';
import { PermissionService } from '../../core/auth/permission.service';
import { ItemPage } from './item-page';

const item = { id: 3, code: 'BOLT', name: 'Bolt', specification: 'M8', baseUomId: 1,
  brandId: 2, categoryId: 4, isActive: true, version: 2 };

describe('MD-ITEM-UI page', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(),
    ...provideTestTranslations('en'), provideNzIcons(APP_ICONS),
    { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' },
    { provide: PermissionService, useValue: { has: () => true, hasAny: () => true } }] }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('shows references, validates fields and sends create and versioned update', () => {
    const fixture = TestBed.createComponent(ItemPage);
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne((r) => r.url === '/api/v1/md/items').flush({ items: [item], totalCount: 1 });
    http.expectOne((r) => r.url === '/api/v1/md/uoms').flush({ items: [
      { id: 1, code: 'EA', name: 'Each', isActive: true }, { id: 5, code: 'OLD', name: 'Old', isActive: false }], totalCount: 2 });
    http.expectOne((r) => r.url === '/api/v1/md/item-brands').flush({ items: [
      { id: 2, code: 'ACME', name: 'Acme', isActive: true }], totalCount: 1 });
    http.expectOne('/api/v1/md/item-categories').flush([
      { id: 4, code: 'PARTS', name: 'Parts', parentId: null, isActive: true, version: 1 }]);
    fixture.detectChanges();
    const page = fixture.componentInstance;
    expect((fixture.nativeElement as HTMLElement).querySelector('[data-testid="md-item-row-BOLT"]')?.textContent).toContain('Each');
    page.openCreate(); page.form.patchValue({ code: 'bad code', name: 'Bolt', baseUomId: 1 }); page.save();
    http.expectNone((r) => r.method === 'POST');
    page.form.patchValue({ code: 'nut', name: ' Nut ', specification: '', brandId: 2, categoryId: 4 }); page.save();
    const post = http.expectOne((r) => r.url === '/api/v1/md/items' && r.method === 'POST');
    expect(post.request.body).toMatchObject({ code: 'NUT', name: 'Nut', specification: null, baseUomId: 1,
      brandId: 2, categoryId: 4 });
    post.flush({ ...item, code: 'NUT' });
    http.expectOne((r) => r.url === '/api/v1/md/items' && r.method === 'GET').flush({ items: [item], totalCount: 1 });
    page.openEdit(item); expect(page.form.controls.code.disabled).toBe(true);
    page.form.patchValue({ isActive: false, baseUomId: 5 }); page.save();
    const put = http.expectOne('/api/v1/md/items/3');
    expect(put.request.body).toMatchObject({ isActive: false, baseUomId: 5, version: 2 });
    put.flush({ ...item, isActive: false });
    http.expectOne((r) => r.url === '/api/v1/md/items' && r.method === 'GET').flush({ items: [], totalCount: 0 });
  });

  it('MD-F13 keeps an inactive unit, brand or category selectable only when it is the current value', () => {
    const fixture = TestBed.createComponent(ItemPage); TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne((r) => r.url === '/api/v1/md/items').flush({ items: [item], totalCount: 1 });
    http.expectOne((r) => r.url === '/api/v1/md/uoms').flush({ items: [
      { id: 1, code: 'EA', name: 'Each', isActive: false }, { id: 5, code: 'OLD', name: 'Old', isActive: false }], totalCount: 2 });
    http.expectOne((r) => r.url === '/api/v1/md/item-brands').flush({ items: [
      { id: 2, code: 'ACME', name: 'Acme', isActive: false }, { id: 6, code: 'GONE', name: 'Gone', isActive: false }], totalCount: 2 });
    http.expectOne('/api/v1/md/item-categories').flush([
      { id: 4, code: 'PARTS', name: 'Parts', parentId: null, isActive: false, version: 1 },
      { id: 7, code: 'OLDCAT', name: 'Old category', parentId: null, isActive: false, version: 1 }]);
    const page = fixture.componentInstance;
    // nz-modal 的內容渲染在 overlay（document.body），不在元件節點內
    const disabled = (testId: string) => [...document.querySelectorAll<HTMLOptionElement>(`[data-testid="${testId}"] option`)]
      .filter((x) => x.disabled).map((x) => x.textContent!.trim());
    page.openEdit(item); fixture.detectChanges(); TestBed.tick();
    expect(document.querySelectorAll('[data-testid="md-item-uom"] option')).toHaveLength(3);
    expect(disabled('md-item-uom')).toEqual(['OLD — Old']);
    expect(disabled('md-item-brand')).toEqual(['GONE — Gone']);
    expect(disabled('md-item-category')).toEqual(['OLDCAT — Old category']);
    page.openCreate(); fixture.detectChanges(); TestBed.tick();
    expect(disabled('md-item-uom')).toHaveLength(2);
    page.closeDialog(); fixture.detectChanges();
  });

  it('MD-F06 loads every reference page, preserves the dialog on version conflict and reloads the stale list', () => {
    const fixture = TestBed.createComponent(ItemPage); TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne((r) => r.url === '/api/v1/md/items').flush({ items: [item], totalCount: 1 });
    http.expectOne((r) => r.url === '/api/v1/md/uoms' && r.params.get('page') === '1')
      .flush({ items: [{ id: 1, code: 'EA', name: 'Each', isActive: true }], totalCount: 2 });
    http.expectOne((r) => r.url === '/api/v1/md/uoms' && r.params.get('page') === '2')
      .flush({ items: [{ id: 5, code: 'BOX', name: 'Box', isActive: true }], totalCount: 2 });
    http.expectOne((r) => r.url === '/api/v1/md/item-brands').flush({ items: [], totalCount: 0 });
    http.expectOne('/api/v1/md/item-categories').flush([]);
    const page = fixture.componentInstance;
    expect(page.uomOptions().map((x) => x.code)).toEqual(['EA', 'BOX']);
    page.openEdit(item); page.form.patchValue({ baseUomId: 5 }); page.save();
    http.expectOne('/api/v1/md/items/3').flush({ errorCode: 'COMMON.CONCURRENCY.CONFLICT' },
      { status: 409, statusText: 'Conflict' });
    expect(page.dialogOpen()).toBe(true);
    expect(page.saveError()).toBe('errors.COMMON.CONCURRENCY.CONFLICT');
    http.expectOne((r) => r.url === '/api/v1/md/items' && r.method === 'GET').flush({ items: [{ ...item, version: 3 }], totalCount: 1 });
    expect(page.rows()[0].version).toBe(3);
  });

  it('opens item conversions from the row and shows BASE_UOM_LOCKED on base unit change', () => {
    const fixture = TestBed.createComponent(ItemPage); TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne((r) => r.url === '/api/v1/md/items').flush({ items: [item], totalCount: 1 });
    http.expectOne((r) => r.url === '/api/v1/md/uoms').flush({ items: [{ id: 1, code: 'EA', name: 'Each', isActive: true }], totalCount: 1 });
    http.expectOne((r) => r.url === '/api/v1/md/item-brands').flush({ items: [], totalCount: 0 });
    http.expectOne('/api/v1/md/item-categories').flush([]);
    fixture.detectChanges();
    (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('[data-testid="md-item-conversions-BOLT"]')!.click();
    TestBed.tick();
    http.expectOne('/api/v1/md/items/3/uom-conversions').flush([]);
    expect(fixture.componentInstance.conversionItem()).toEqual(item);
    const page = fixture.componentInstance;
    page.conversionItem.set(null);
    page.openEdit(item); page.form.patchValue({ baseUomId: 5 }); page.save();
    http.expectOne('/api/v1/md/items/3').flush({ errorCode: 'MD.ITEM.BASE_UOM_LOCKED' }, { status: 409, statusText: 'Conflict' });
    expect(page.saveError()).toBe('errors.MD.ITEM.BASE_UOM_LOCKED');
  });

  it('opens the image and attribute dialogs from the row', () => {
    const fixture = TestBed.createComponent(ItemPage); TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne((r) => r.url === '/api/v1/md/items').flush({ items: [item], totalCount: 1 });
    http.expectOne((r) => r.url === '/api/v1/md/uoms').flush({ items: [], totalCount: 0 });
    http.expectOne((r) => r.url === '/api/v1/md/item-brands').flush({ items: [], totalCount: 0 });
    http.expectOne('/api/v1/md/item-categories').flush([]);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    element.querySelector<HTMLButtonElement>('[data-testid="md-item-images-BOLT"]')!.click();
    TestBed.tick();
    http.expectOne('/api/v1/md/items/3/images').flush([]);
    expect(fixture.componentInstance.imageItem()).toEqual(item);
    element.querySelector<HTMLButtonElement>('[data-testid="md-item-attributes-BOLT"]')!.click();
    TestBed.tick();
    http.expectOne('/api/v1/md/items/3/attributes').flush([]);
    http.expectOne((r) => r.url === '/api/v1/md/item-attributes').flush({ items: [], page: 1, pageSize: 200, totalCount: 0 });
    expect(fixture.componentInstance.attributeItem()).toEqual(item);
  });
});
