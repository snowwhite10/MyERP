import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ANIMATION_MODULE_TYPE } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideNzIcons } from 'ng-zorro-antd/icon';
import { provideTestTranslations } from '../../../testing/i18n-fixtures';
import { APP_ICONS } from '../../core/icons';
import { PartnerImportDialog } from './partner-import-dialog';

const root = '/api/v1/md/customers/import';
const file = new File(['code,name\nA,Acme'], 'customers.csv', { type: 'text/csv' });
const preview = { rows: [{ rowNumber: 2, code: 'A', name: 'Acme', status: 'Create', errors: [] }],
  createdCount: 1, skippedCount: 0, errorCount: 0 };

describe('MD-IMP-005 customer import dialog', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(),
    ...provideTestTranslations('en'), provideNzIcons(APP_ICONS),
    { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' }] }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('previews the selected file, submits the same bytes, then reports completion', () => {
    const fixture = TestBed.createComponent(PartnerImportDialog);
    fixture.componentRef.setInput('open', true); TestBed.tick();
    const dialog = fixture.componentInstance;
    dialog.selectFile(file); dialog.preview();
    const http = TestBed.inject(HttpTestingController);
    const request = http.expectOne(`${root}/preview`);
    expect(request.request.body.get('file')).toBe(file);
    request.flush(preview);
    expect(dialog.canCommit()).toBe(true);
    dialog.commit();
    const commit = http.expectOne(root);
    expect(commit.request.body.get('file')).toBe(file);
    commit.flush(preview);
    expect(dialog.completed()).toBe(true);
  });

  it('blocks rows with errors and resets preview when the file changes', () => {
    const fixture = TestBed.createComponent(PartnerImportDialog);
    fixture.componentRef.setInput('open', true); TestBed.tick();
    const dialog = fixture.componentInstance;
    dialog.selectFile(file); dialog.preview();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(`${root}/preview`).flush({ rows: [{ rowNumber: 4, code: 'B', name: '', status: 'Error',
      errors: [{ field: 'name', code: 'REQUIRED' }] }], createdCount: 0, skippedCount: 0, errorCount: 1 });
    fixture.detectChanges();
    expect(dialog.canCommit()).toBe(false);
    expect(document.body.querySelector('[data-testid="md-customer-import-row-4"]')?.textContent).toContain('name');
    dialog.commit(); http.expectNone(root);
    dialog.selectFile(new File(['code,name\nC,Co'], 'next.csv'));
    expect(dialog.result()).toBeNull();
  });

  it('handles a commit conflict and allows another preview', () => {
    const fixture = TestBed.createComponent(PartnerImportDialog);
    fixture.componentRef.setInput('open', true); TestBed.tick();
    const dialog = fixture.componentInstance;
    dialog.selectFile(file); dialog.preview();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(`${root}/preview`).flush(preview);
    dialog.commit();
    http.expectOne(root).flush({ errorCode: 'MD.CUSTOMER.DUPLICATE_CODE' }, { status: 409, statusText: 'Conflict' });
    expect(dialog.error()).toBe('errors.MD.CUSTOMER.DUPLICATE_CODE');
    expect(dialog.canCommit()).toBe(false);
    dialog.preview(); http.expectOne(`${root}/preview`).flush(preview);
    expect(dialog.canCommit()).toBe(true);
  });

  it('shows a 422 file error and ignores a stale preview after selecting a different file', () => {
    const fixture = TestBed.createComponent(PartnerImportDialog);
    fixture.componentRef.setInput('open', true); TestBed.tick();
    const dialog = fixture.componentInstance;
    dialog.selectFile(file); dialog.preview();
    const http = TestBed.inject(HttpTestingController);
    const old = http.expectOne(`${root}/preview`);
    dialog.selectFile(new File(['code,name\nB,Second'], 'second.csv'));
    old.flush(preview);
    expect(dialog.result()).toBeNull();
    dialog.preview();
    http.expectOne(`${root}/preview`).flush({ errorCode: 'COMMON.IMPORT.MISSING_COLUMN' },
      { status: 422, statusText: 'Unprocessable Entity' });
    expect(dialog.error()).toBe('errors.COMMON.IMPORT.MISSING_COLUMN');
    expect(dialog.canCommit()).toBe(false);
  });
});

describe('MD-SUP-IMP-002 supplier import dialog', () => {
  const supplierRoot = '/api/v1/md/suppliers/import';
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(),
    ...provideTestTranslations('en'), provideNzIcons(APP_ICONS),
    { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' }] }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('uses supplier endpoints, title, test ids and error codes', () => {
    const fixture = TestBed.createComponent(PartnerImportDialog);
    fixture.componentRef.setInput('kind', 'supplier');
    fixture.componentRef.setInput('open', true); TestBed.tick(); fixture.detectChanges();
    const dialog = fixture.componentInstance;
    expect(document.body.textContent).toContain('Import suppliers');
    expect(document.body.querySelector('[data-testid="md-supplier-import-preview"]')).not.toBeNull();
    dialog.selectFile(new File(['code,name\nS1,Steel'], 'suppliers.csv')); dialog.preview();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(`${supplierRoot}/preview`).flush(preview);
    dialog.commit();
    http.expectOne(supplierRoot).flush({ errorCode: 'MD.SUPPLIER_IMPORT.HAS_ERRORS' },
      { status: 422, statusText: 'Unprocessable Entity' });
    expect(dialog.error()).toBe('errors.MD.SUPPLIER_IMPORT.HAS_ERRORS');
    expect(dialog.canCommit()).toBe(false);
  });

  it('downloads the supplier template', () => {
    const fixture = TestBed.createComponent(PartnerImportDialog);
    fixture.componentRef.setInput('kind', 'supplier');
    fixture.componentRef.setInput('open', true); TestBed.tick();
    const create = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:supplier');
    const revoke = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    fixture.componentInstance.download('csv');
    const request = TestBed.inject(HttpTestingController).expectOne(`${supplierRoot}/template?format=csv`);
    request.flush(new Blob(['code,name']));
    expect(click).toHaveBeenCalled();
    expect(revoke).toHaveBeenCalledWith('blob:supplier');
    create.mockRestore(); revoke.mockRestore(); click.mockRestore();
  });
});

describe('MD-ITEM-IMP-UI item import dialog', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(),
    ...provideTestTranslations('en'), provideNzIcons(APP_ICONS),
    { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' }] }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('uses the item endpoint, shows row errors, and requires another preview after a conflict', () => {
    const fixture = TestBed.createComponent(PartnerImportDialog);
    fixture.componentRef.setInput('kind', 'item'); fixture.componentRef.setInput('open', true);
    TestBed.tick(); fixture.detectChanges();
    const dialog = fixture.componentInstance;
    expect(document.body.textContent).toContain('Import items');
    dialog.selectFile(new File(['code,name,baseUomCode\nI1,Item,PCS'], 'items.csv'));
    dialog.preview();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/v1/md/items/import/preview').flush({ rows: [{ rowNumber: 3, code: 'I1', name: 'Item',
      status: 'Error', errors: [{ field: 'baseUomCode', code: 'NOT_FOUND' }] }],
      createdCount: 0, skippedCount: 0, errorCount: 1 });
    fixture.detectChanges();
    expect(document.body.querySelector('[data-testid="md-item-import-row-3"]')?.textContent).toContain('baseUomCode');
    expect(dialog.canCommit()).toBe(false);
    dialog.preview(); http.expectOne('/api/v1/md/items/import/preview').flush(preview);
    dialog.commit(); http.expectOne('/api/v1/md/items/import').flush({ errorCode: 'MD.ITEM.DUPLICATE_CODE' },
      { status: 409, statusText: 'Conflict' });
    expect(dialog.canCommit()).toBe(false);
    expect(dialog.error()).toBe('errors.MD.ITEM.DUPLICATE_CODE');
    dialog.preview(); http.expectOne('/api/v1/md/items/import/preview').flush(preview);
    expect(dialog.canCommit()).toBe(true);
  });
});
