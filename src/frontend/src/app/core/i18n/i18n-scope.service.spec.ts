import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { EnvironmentInjector, inject, runInInjectionContext } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import {
  provideTranslateLoader,
  provideTranslateService,
  TranslateService,
} from '@ngx-translate/core';
import { firstValueFrom, isObservable, Observable } from 'rxjs';
import { I18nScopeService, i18nScopeGuard } from './i18n-scope.service';
import { CORE_I18N_SCOPES } from './languages';
import { ScopedTranslateLoader } from './scoped-translate-loader';

describe('I18nScopeService', () => {
  let http: HttpTestingController;
  let translate: TranslateService;
  let service: I18nScopeService;

  function flushCore(lang: string): void {
    for (const scope of CORE_I18N_SCOPES) {
      http.expectOne(`i18n/${lang}/${scope}.json`).flush({ title: `${lang}-${scope}` });
    }
  }

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideTranslateService({
          loader: provideTranslateLoader(() => inject(ScopedTranslateLoader)),
        }),
      ],
    });
    http = TestBed.inject(HttpTestingController);
    translate = TestBed.inject(TranslateService);
    service = TestBed.inject(I18nScopeService);
  });

  afterEach(() => http.verify());

  it('loads a module scope for the current language and merges it', async () => {
    translate.use('en').subscribe();
    flushCore('en');

    const done = firstValueFrom(service.load('sal'));
    http.expectOne('i18n/en/sal.json').flush({ order: { docNo: 'Order No.' } });
    await done;

    expect(translate.instant('sal.order.docNo')).toBe('Order No.');
    expect(translate.instant('common.title')).toBe('en-common');
  });

  it('does not request a scope twice for the same language', async () => {
    translate.use('en').subscribe();
    flushCore('en');

    const first = firstValueFrom(service.load('sal'));
    http.expectOne('i18n/en/sal.json').flush({ a: '1' });
    await first;

    await firstValueFrom(service.load('sal'));
    http.expectNone('i18n/en/sal.json');
  });

  it('loads registered module scopes for a newly selected language', async () => {
    translate.use('en').subscribe();
    flushCore('en');
    const first = firstValueFrom(service.load('sal'));
    http.expectOne('i18n/en/sal.json').flush({ a: 'en' });
    await first;

    translate.use('zh-Hans').subscribe();
    for (const scope of [...CORE_I18N_SCOPES, 'sal']) {
      http.expectOne(`i18n/zh-Hans/${scope}.json`).flush({ a: `zh-Hans-${scope}` });
    }

    expect(translate.instant('sal.a')).toBe('zh-Hans-sal');
  });

  it('fills in scopes missing from a language loaded before the scope was registered', async () => {
    translate.use('zh-Hans').subscribe();
    flushCore('zh-Hans');
    translate.use('en').subscribe();
    flushCore('en');

    const first = firstValueFrom(service.load('sal'));
    http.expectOne('i18n/en/sal.json').flush({ a: 'en' });
    await first;

    translate.use('zh-Hans').subscribe();
    http.expectOne('i18n/zh-Hans/sal.json').flush({ a: 'zh-Hans' });

    expect(translate.instant('sal.a')).toBe('zh-Hans');
  });

  it('only registers the scope when no language is active yet', async () => {
    await firstValueFrom(service.load('inv'));

    http.expectNone('i18n/en/inv.json');
    expect(TestBed.inject(ScopedTranslateLoader).registeredScopes).toContain('inv');
  });

  it('route guard loads the scope and allows navigation', async () => {
    translate.use('en').subscribe();
    flushCore('en');

    const guard = i18nScopeGuard('pur');
    const result = runInInjectionContext(TestBed.inject(EnvironmentInjector), () =>
      guard({} as never, {} as never),
    );
    expect(isObservable(result)).toBe(true);
    const allowed = firstValueFrom(result as Observable<boolean>);
    http.expectOne('i18n/en/pur.json').flush({});

    expect(await allowed).toBe(true);
  });
});
