import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AssemblyService, validAssemblyDraft } from './assembly.service';

describe('INV-ASM-010 assembly client contract', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  const draft = { businessDate: '2026-10-16', remark: '  kit  ',
    finished: { itemId: 1, warehouseId: 2, locationId: null, quantity: '2', tracking: [] },
    components: [{ itemId: 3, warehouseId: 2, locationId: null, quantity: '4.000001', tracking: [] }] };

  it('requires a finished item, a distinct component and six-place positive quantities', () => {
    expect(validAssemblyDraft(draft)).toBe(true);
    expect(validAssemblyDraft({ ...draft, components: [] })).toBe(false);
    expect(validAssemblyDraft({ ...draft, components: [{ ...draft.components[0], itemId: 1 }] })).toBe(false);
    expect(validAssemblyDraft({ ...draft, finished: { ...draft.finished, quantity: '0.0000001' } })).toBe(false);
    expect(validAssemblyDraft({ ...draft, finished: { ...draft.finished, quantity: '0' } })).toBe(false);
  });

  it('uses server filters, saved references, line numbering and versioned actions', () => {
    const api = TestBed.inject(AssemblyService);
    const http = TestBed.inject(HttpTestingController);
    api.list({ page: 2, pageSize: 20, status: 'Draft', finishedItemId: 1, from: '2026-10-01', to: '2026-10-31' }).subscribe();
    const list = http.expectOne(r => r.url === '/api/v1/inv/assemblies');
    expect(list.request.params.get('page')).toBe('2');
    expect(list.request.params.get('finishedItemId')).toBe('1');
    expect(list.request.params.get('status')).toBe('Draft');
    list.flush({ items: [], totalCount: 0 });
    api.savedReferences(7).subscribe();
    http.expectOne('/api/v1/inv/assemblies/7/saved-references').flush({ items: [], warehouses: [], locations: [] });
    api.create(draft).subscribe();
    const create = http.expectOne(r => r.method === 'POST' && r.url === '/api/v1/inv/assemblies');
    expect(create.request.body).toMatchObject({ remark: 'kit', finished: { lineNo: 1, quantity: '2' },
      components: [{ lineNo: 2, quantity: '4.000001' }] });
    expect(create.request.body.finished).not.toHaveProperty('postedUnitCost');
    create.flush({});
    api.update(7, 3, draft).subscribe();
    const update = http.expectOne(r => r.method === 'PUT' && r.url === '/api/v1/inv/assemblies/7');
    expect(update.request.body.version).toBe(3);
    update.flush({});
    api.submit(7, 4).subscribe();
    const submit = http.expectOne('/api/v1/inv/assemblies/7/submit');
    expect(submit.request.body).toEqual({ version: 4 });
    submit.flush({});
    api.remove(7, 5).subscribe();
    const remove = http.expectOne(r => r.method === 'DELETE' && r.url === '/api/v1/inv/assemblies/7');
    expect(remove.request.params.get('version')).toBe('5');
    remove.flush(null, { status: 204, statusText: 'No Content' });
  });
});
