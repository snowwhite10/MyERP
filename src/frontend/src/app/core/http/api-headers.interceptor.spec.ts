import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { provideTestTranslations } from '../../../testing/i18n-fixtures';
import { CompanyContextService } from '../company/company-context.service';
import { LanguageService } from '../i18n/language.service';
import { apiHeadersInterceptor, COMPANY_HEADER, isApiRequest } from './api-headers.interceptor';

describe('apiHeadersInterceptor', () => {
  let http: HttpClient;
  let controller: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        ...provideTestTranslations(),
        provideHttpClient(withInterceptors([apiHeadersInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpClient);
    controller = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    controller.verify();
    localStorage.clear();
  });

  it('adds Accept-Language and X-Company-Id to API requests', async () => {
    await firstValueFrom(TestBed.inject(LanguageService).use('en'));
    TestBed.inject(CompanyContextService).setCompanies([{ id: 7, code: 'TW01', name: 'Taipei' }]);

    http.get('/api/v1/sys/users').subscribe();
    const req = controller.expectOne('/api/v1/sys/users');

    expect(req.request.headers.get('Accept-Language')).toBe('en');
    expect(req.request.headers.get(COMPANY_HEADER)).toBe('7');
    req.flush([]);
  });

  it('omits X-Company-Id when no company is selected', () => {
    http.get('/api/v1/sys/companies').subscribe();
    const req = controller.expectOne('/api/v1/sys/companies');

    expect(req.request.headers.get('Accept-Language')).toBe('zh-Hant');
    expect(req.request.headers.has(COMPANY_HEADER)).toBe(false);
    req.flush([]);
  });

  it('keeps an explicit X-Company-Id header', () => {
    TestBed.inject(CompanyContextService).setCompanies([{ id: 7, code: 'TW01', name: 'Taipei' }]);

    http.get('/api/v1/sys/users', { headers: { [COMPANY_HEADER]: '9' } }).subscribe();
    const req = controller.expectOne('/api/v1/sys/users');

    expect(req.request.headers.get(COMPANY_HEADER)).toBe('9');
    req.flush([]);
  });

  it('does not touch non-API requests such as language files', () => {
    TestBed.inject(CompanyContextService).setCompanies([{ id: 7, code: 'TW01', name: 'Taipei' }]);

    http.get('i18n/en/common.json').subscribe();
    const req = controller.expectOne('i18n/en/common.json');

    expect(req.request.headers.has('Accept-Language')).toBe(false);
    expect(req.request.headers.has(COMPANY_HEADER)).toBe(false);
    req.flush({});
  });

  it.each([
    ['/api/v1/sal/orders', true],
    ['api/v1/sal/orders', true],
    ['https://erp.example.com/api/v1/sal/orders', true],
    ['i18n/en/common.json', false],
    ['/apis/other', false],
    ['https://cdn.example.com/lib.js', false],
  ])('isApiRequest(%s) = %s', (url, expected) => {
    expect(isApiRequest(url)).toBe(expected);
  });
});
