import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ANIMATION_MODULE_TYPE } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter, Router } from '@angular/router';
import { provideNzIcons } from 'ng-zorro-antd/icon';
import { provideTestTranslations } from '../../../testing/i18n-fixtures';
import { APP_ICONS } from '../../core/icons';
import { AssemblyForm } from './assembly-form';

const API = '/api/v1/inv/assemblies';
describe('INV-ASM-011 assembly form', () => {
  function open(id: string | null = null) {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      ...provideTestTranslations('en'), provideNzIcons(APP_ICONS), { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' },
      { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap(id ? { id } : {}) } } }] });
    const fixture = TestBed.createComponent(AssemblyForm); TestBed.tick(); fixture.detectChanges();
    return { form: fixture.componentInstance, http: TestBed.inject(HttpTestingController),
      navigate: vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true), fixture };
  }
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('rejects zero components, matching finished and component items, and invalid quantities before HTTP', () => {
    const { form, http } = open(); form.draft.businessDate = '2026-10-16';
    Object.assign(form.draft.finished, { itemId: 1, warehouseId: 2, quantity: '1' });
    form.save(); expect(form.error()).toBe('inv.assembly.invalid'); http.expectNone(API);
    form.addComponent(); Object.assign(form.draft.components[0], { itemId: 1, warehouseId: 2, quantity: '1' });
    form.save(); expect(form.error()).toBe('inv.assembly.invalid'); http.expectNone(API);
    form.draft.components[0].itemId = 3; form.draft.components[0].quantity = '0.0000001';
    form.save(); expect(form.error()).toBe('inv.assembly.invalid'); http.expectNone(API);
  });

  it('saves one finished line and component lines without a client cost', () => {
    const { form, http, navigate } = open(); form.draft.businessDate = '2026-10-16';
    expect(form.hasUnsavedChanges()).toBe(true);
    Object.assign(form.draft.finished, { itemId: 1, warehouseId: 2, quantity: '2' });
    form.addComponent(); Object.assign(form.draft.components[0], { itemId: 3, warehouseId: 2, quantity: '4' });
    form.save(); const request = http.expectOne(r => r.method === 'POST' && r.url === API);
    expect(request.request.body).toMatchObject({ finished: { lineNo: 1, quantity: '2' }, components: [{ lineNo: 2, quantity: '4' }] });
    expect(request.request.body.finished).not.toHaveProperty('postedUnitCost');
    request.flush({ id: 7 }, { status: 201, statusText: 'Created' });
    expect(form.hasUnsavedChanges()).toBe(false);
    expect(navigate).toHaveBeenCalledWith('/inv/assemblies');
  });

  it('loads saved inactive references and keeps a rejected draft after a version conflict', () => {
    const { form, http, fixture } = open('7');
    http.expectOne(`${API}/7/saved-references`).flush({ items: [{ id: 1, code: 'OLD', name: 'Old', isActive: false, trackingMode: 'None' }],
      warehouses: [{ id: 2, code: 'WH', name: 'Main', isActive: true }], locations: [] });
    http.expectOne(`${API}/7`).flush({ id: 7, businessDate: '2026-10-16', status: 'Rejected', remark: 'Fix', version: 3,
      lines: [{ id: 1, role: 'Finished', lineNo: 1, itemId: 1, warehouseId: 2, locationId: null, quantity: 2, tracking: [] },
        { id: 2, role: 'Component', lineNo: 2, itemId: 3, warehouseId: 2, locationId: null, quantity: 4, tracking: [] }] });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Inactive');
    form.draft.remark = 'Fixed'; form.save();
    const put = http.expectOne(r => r.method === 'PUT' && r.url === `${API}/7`);
    expect(put.request.body).toMatchObject({ version: 3, remark: 'Fixed' });
    put.flush({ errorCode: 'COMMON.CONCURRENCY.CONFLICT' }, { status: 409, statusText: 'Conflict' });
    expect(form.error()).toBe('errors.COMMON.CONCURRENCY.CONFLICT');
    expect(form.saving()).toBe(false);
  });
});
