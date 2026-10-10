import { TestBed } from '@angular/core/testing';
import { PreferenceStorage } from '../storage/preference-storage';
import { COMPANY_STORAGE_KEY, CompanyContextService, CompanyOption } from './company-context.service';

const COMPANIES: CompanyOption[] = [
  { id: 1, code: 'TW01', name: 'Taipei' },
  { id: 2, code: 'HK01', name: 'Hong Kong' },
];

describe('CompanyContextService', () => {
  let service: CompanyContextService;
  let storage: PreferenceStorage;

  beforeEach(() => {
    localStorage.clear();
    storage = TestBed.inject(PreferenceStorage);
    service = TestBed.inject(CompanyContextService);
  });

  afterEach(() => localStorage.clear());

  it('has no current company before companies are loaded', () => {
    expect(service.companies()).toEqual([]);
    expect(service.current()).toBeNull();
  });

  it('selects the first company by default', () => {
    service.setCompanies(COMPANIES);

    expect(service.current()?.code).toBe('TW01');
    expect(storage.get(COMPANY_STORAGE_KEY)).toBe('1');
  });

  it('restores the stored company when it is still accessible', () => {
    storage.set(COMPANY_STORAGE_KEY, '2');

    service.setCompanies(COMPANIES);

    expect(service.current()?.code).toBe('HK01');
  });

  it('ignores a stored company that is no longer accessible', () => {
    storage.set(COMPANY_STORAGE_KEY, '99');

    service.setCompanies(COMPANIES);

    expect(service.current()?.id).toBe(1);
  });

  it('ignores a non-numeric stored value', () => {
    storage.set(COMPANY_STORAGE_KEY, 'abc');

    service.setCompanies(COMPANIES);

    expect(service.current()?.id).toBe(1);
  });

  it('switches company and persists the choice', () => {
    service.setCompanies(COMPANIES);

    expect(service.select(2)).toBe(true);
    expect(service.current()?.id).toBe(2);
    expect(storage.get(COMPANY_STORAGE_KEY)).toBe('2');
  });

  it('rejects switching to an inaccessible company', () => {
    service.setCompanies(COMPANIES);

    expect(service.select(42)).toBe(false);
    expect(service.current()?.id).toBe(1);
  });

  it('keeps the current selection when the list is refreshed', () => {
    service.setCompanies(COMPANIES);
    service.select(2);

    service.setCompanies([...COMPANIES, { id: 3, code: 'CN01', name: 'Shanghai' }]);

    expect(service.current()?.id).toBe(2);
  });

  it('removes the stored company when the list becomes empty', () => {
    service.setCompanies(COMPANIES);

    service.setCompanies([]);

    expect(service.current()).toBeNull();
    expect(storage.get(COMPANY_STORAGE_KEY)).toBeNull();
  });

  it('clears the context (logout)', () => {
    service.setCompanies(COMPANIES);

    service.clear();

    expect(service.companies()).toEqual([]);
    expect(service.current()).toBeNull();
  });
});
