import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { assertValidScope, ScopedTranslateLoader } from './scoped-translate-loader';

describe('ScopedTranslateLoader', () => {
  let loader: ScopedTranslateLoader;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    loader = TestBed.inject(ScopedTranslateLoader);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('loads every core scope and nests content under the scope name', async () => {
    const result = firstValueFrom(loader.getTranslation('en'));

    http.expectOne('i18n/en/common.json').flush({ action: { save: 'Save' } });
    http.expectOne('i18n/en/layout.json').flush({ menu: { home: 'Home' } });
    http.expectOne('i18n/en/errors.json').flush({ http: { unknown: 'Oops' } });

    expect(await result).toEqual({
      common: { action: { save: 'Save' } },
      layout: { menu: { home: 'Home' } },
      errors: { http: { unknown: 'Oops' } },
    });
  });

  it('uses an empty object when a scope file fails to load', async () => {
    const result = firstValueFrom(loader.fetchScopes('zh-Hant', ['common', 'layout']));

    http.expectOne('i18n/zh-Hant/common.json').flush({ a: 'b' });
    http
      .expectOne('i18n/zh-Hant/layout.json')
      .flush('missing', { status: 404, statusText: 'Not Found' });

    expect(await result).toEqual({ common: { a: 'b' }, layout: {} });
  });

  it('returns an empty object for an empty scope list without requests', async () => {
    expect(await firstValueFrom(loader.fetchScopes('en', []))).toEqual({});
  });

  it('includes registered module scopes on later loads', async () => {
    expect(loader.registerScope('sal')).toBe(true);
    expect(loader.registerScope('sal')).toBe(false);
    expect(loader.registeredScopes).toEqual(['common', 'layout', 'errors', 'sal']);

    const result = firstValueFrom(loader.getTranslation('en'));
    for (const scope of loader.registeredScopes) {
      http.expectOne(`i18n/en/${scope}.json`).flush({});
    }
    expect(Object.keys(await result)).toEqual(['common', 'layout', 'errors', 'sal']);
  });

  it.each(['../secret', 'Sal', 'sal/order', '', '1abc'])('rejects invalid scope "%s"', (scope) => {
    expect(() => assertValidScope(scope)).toThrowError(/Invalid i18n scope/);
    expect(() => loader.registerScope(scope)).toThrowError(/Invalid i18n scope/);
  });
});
