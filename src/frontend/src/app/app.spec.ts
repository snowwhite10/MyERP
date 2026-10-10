import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { FakeChartRegistry, provideFakeCharts } from '../testing/fake-chart';
import { I18N_FIXTURES } from '../testing/i18n-fixtures';
import { App } from './app';
import { appConfig } from './app.config';
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

  it('routes "/" to the home page inside the main layout', async () => {
    const harness = await RouterTestingHarness.create();
    flushLanguage('zh-Hant');

    await harness.navigateByUrl('/');
    harness.detectChanges();

    expect(harness.routeNativeElement?.querySelector('[data-testid="home-title"]')).not.toBeNull();
    expect(document.querySelector('erp-main-layout')).not.toBeNull();
  });

  it('routes unknown paths to the 404 page and /forbidden to 403', async () => {
    const harness = await RouterTestingHarness.create();
    flushLanguage('zh-Hant');

    await harness.navigateByUrl('/does-not-exist');
    harness.detectChanges();
    expect(harness.routeNativeElement?.querySelector('[data-testid="error-page-404"]')).not.toBeNull();

    await harness.navigateByUrl('/forbidden');
    harness.detectChanges();
    expect(harness.routeNativeElement?.querySelector('[data-testid="error-page-403"]')).not.toBeNull();
    expect(TestBed.inject(Router).url).toBe('/forbidden');
  });
});
