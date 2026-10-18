import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { DisassemblyService, validDisassemblyDraft } from './disassembly.service';

describe('DisassemblyService', () => {
  let service: DisassemblyService;
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(DisassemblyService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('keeps allocation weights in draft and sends them as decimal strings', () => {
    const draft = { businessDate: '2026-10-18', remark: '',
      finished: { itemId: 1, warehouseId: 2, locationId: null, quantity: '1' },
      components: [{ itemId: 3, warehouseId: 2, locationId: null, quantity: '2', allocationWeight: '1.250000' }] };
    expect(validDisassemblyDraft(draft)).toBe(true);
    service.create(draft).subscribe();
    const request = http.expectOne('/api/v1/inv/disassemblies');
    expect(request.request.body.components[0].allocationWeight).toBe('1.250000');
    request.flush({});
    expect(validDisassemblyDraft({ ...draft, components: [{ ...draft.components[0], allocationWeight: '0' }] })).toBe(false);
    expect(validDisassemblyDraft({ ...draft, components: [] })).toBe(false);
    expect(validDisassemblyDraft({ ...draft, components: [{ ...draft.components[0], itemId: 1 }] })).toBe(false);
    expect(validDisassemblyDraft({ ...draft, finished: { ...draft.finished, quantity: '10000000000000' } })).toBe(false);
    expect(validDisassemblyDraft({ ...draft, components: [{ ...draft.components[0], allocationWeight: '10000000000000' }] })).toBe(false);
    expect(validDisassemblyDraft({ ...draft, finished: { ...draft.finished, quantity: '9999999999999.999999' } })).toBe(true);
  });
});
