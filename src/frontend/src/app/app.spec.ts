import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { seedSession } from '../testing/auth-fixtures';
import { FakeChartRegistry, provideFakeCharts } from '../testing/fake-chart';
import { I18N_FIXTURES } from '../testing/i18n-fixtures';
import { App } from './app';
import { appConfig } from './app.config';
import { AUTHORIZATION_API } from './core/auth/permission.service';
import { CompanyContextService } from './core/company/company-context.service';
import { CORE_I18N_SCOPES } from './core/i18n/languages';

describe('App (full configuration)', () => {
  let http: HttpTestingController;

  function flushLanguage(lang: 'zh-Hant' | 'zh-Hans' | 'en'): void {
    for (const scope of CORE_I18N_SCOPES) {
      http.expectOne(`i18n/${lang}/${scope}.json`).flush(I18N_FIXTURES[lang][scope]);
    }
  }

  beforeEach(() => {
    localStorage.clear();
    window.matchMedia = undefined as unknown as typeof window.matchMedia;
    vi.spyOn(navigator, 'languages', 'get').mockReturnValue(['zh-TW']);
    TestBed.configureTestingModule({
      providers: [
        ...appConfig.providers,
        provideHttpClient(),
        provideHttpClientTesting(),
        provideFakeCharts(new FakeChartRegistry()),
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    vi.restoreAllMocks();
    localStorage.clear();
  });

  it('creates the root component', () => {
    const fixture = TestBed.createComponent(App);
    flushLanguage('zh-Hant');

    expect(fixture.componentInstance).toBeTruthy();
  });

  it('redirects signed-out users to the login page and loads the sys language file', async () => {
    const harness = await RouterTestingHarness.create();
    flushLanguage('zh-Hant');

    const navigation = harness.navigateByUrl('/forbidden');
    await new Promise((resolve) => setTimeout(resolve));
    http.expectOne('i18n/zh-Hant/sys.json').flush(I18N_FIXTURES['zh-Hant']['sys']);
    await navigation;
    harness.detectChanges();

    expect(TestBed.inject(Router).url).toBe('/login?returnUrl=%2Fforbidden');
    expect(harness.routeNativeElement?.querySelector('[data-testid="login-title"]')?.textContent).toContain('登入');
    expect(document.querySelector('erp-main-layout')).toBeNull();
    http.expectOne('/api/v1/sys/auth/captcha').flush({ captchaId: 'c1', image: 'data:image/svg+xml;base64,PHN2Zy8+', length: 5, expiresInSeconds: 120 });
  });

  /** 主框架守衛先載入權限與公司（WU-SYS-004）。 */
  async function flushAuthorization(): Promise<void> {
    await new Promise((resolve) => setTimeout(resolve));
    http.expectOne(AUTHORIZATION_API).flush({
      companies: [{ id: 1, code: 'TW01', name: 'Taipei', isDefault: true }],
      companyId: 1,
      isFullAccess: true,
      permissions: [],
    });
  }

  it('routes "/" to the home page inside the main layout after loading permissions', async () => {
    seedSession();
    const harness = await RouterTestingHarness.create();
    flushLanguage('zh-Hant');

    const navigation = harness.navigateByUrl('/');
    await flushAuthorization();
    await navigation;
    harness.detectChanges();

    expect(harness.routeNativeElement?.querySelector('[data-testid="home-title"]')).not.toBeNull();
    expect(document.querySelector('erp-main-layout')).not.toBeNull();
    expect(TestBed.inject(CompanyContextService).current()?.code).toBe('TW01');
  });

  it('routes unknown paths to the 404 page and /forbidden to 403', async () => {
    seedSession();
    const harness = await RouterTestingHarness.create();
    flushLanguage('zh-Hant');

    const navigation = harness.navigateByUrl('/does-not-exist');
    await flushAuthorization();
    await navigation;
    harness.detectChanges();
    expect(harness.routeNativeElement?.querySelector('[data-testid="error-page-404"]')).not.toBeNull();

    await harness.navigateByUrl('/forbidden');
    harness.detectChanges();
    expect(harness.routeNativeElement?.querySelector('[data-testid="error-page-403"]')).not.toBeNull();
    expect(TestBed.inject(Router).url).toBe('/forbidden');
  });
});
