// REQ_F4118.24.2：API 請求帶 Bearer；401 時刷新一次後重送，刷新失敗導向登入頁。
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { seedSession, testTokens } from '../../../testing/auth-fixtures';
import { authInterceptor, isAnonymousAuthRequest } from './auth.interceptor';
import { AUTH_API, AuthService } from './auth.service';

const UNAUTHORIZED = { status: 401, statusText: 'Unauthorized' };

@Component({ template: '' })
class Blank {}

describe('authInterceptor', () => {
  let http: HttpClient;
  let controller: HttpTestingController;
  let router: Router;

  function setup(): void {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: '**', component: Blank }]),
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpClient);
    controller = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
  }

  beforeEach(() => localStorage.clear());

  afterEach(() => {
    controller.verify();
    localStorage.clear();
  });

  it('adds the bearer token to API requests when signed in', () => {
    seedSession();
    setup();
    http.get('/api/v1/sys/auth/me').subscribe();
    const req = controller.expectOne('/api/v1/sys/auth/me');
    expect(req.request.headers.get('Authorization')).toBe('Bearer access-1');
    req.flush({});
  });

  it('sends API requests without a token when signed out', () => {
    setup();
    http.get('/api/v1/sys/users').subscribe({ error: () => undefined });
    const req = controller.expectOne('/api/v1/sys/users');
    expect(req.request.headers.has('Authorization')).toBe(false);
    req.flush({}, UNAUTHORIZED);
  });

  it('never attaches the token to non-API or anonymous auth requests', () => {
    seedSession();
    setup();
    http.get('i18n/en/common.json').subscribe();
    http.get(`${AUTH_API}/captcha`).subscribe();
    for (const url of ['i18n/en/common.json', `${AUTH_API}/captcha`]) {
      const req = controller.expectOne(url);
      expect(req.request.headers.has('Authorization'), url).toBe(false);
      req.flush({});
    }
  });

  it('refreshes once on 401 and retries with the new token', async () => {
    seedSession();
    setup();
    const result = firstValueFrom(http.get<{ ok: boolean }>('/api/v1/sys/users'));

    controller.expectOne('/api/v1/sys/users').flush({}, UNAUTHORIZED);
    controller.expectOne(`${AUTH_API}/refresh`).flush(testTokens('2'));
    const retry = controller.expectOne('/api/v1/sys/users');
    expect(retry.request.headers.get('Authorization')).toBe('Bearer access-2');
    retry.flush({ ok: true });

    expect(await result).toEqual({ ok: true });
  });

  it('shares one refresh between concurrent 401 responses', async () => {
    seedSession();
    setup();
    const a = firstValueFrom(http.get('/api/v1/a'));
    const b = firstValueFrom(http.get('/api/v1/b'));

    controller.expectOne('/api/v1/a').flush({}, UNAUTHORIZED);
    controller.expectOne('/api/v1/b').flush({}, UNAUTHORIZED);
    controller.expectOne(`${AUTH_API}/refresh`).flush(testTokens('2'));
    controller.expectOne('/api/v1/a').flush('a');
    controller.expectOne('/api/v1/b').flush('b');

    expect([await a, await b]).toEqual(['a', 'b']);
  });

  it('does not refresh a second time when the retried request is also 401', async () => {
    seedSession();
    setup();
    const result = firstValueFrom(http.get('/api/v1/sys/users'));

    controller.expectOne('/api/v1/sys/users').flush({}, UNAUTHORIZED);
    controller.expectOne(`${AUTH_API}/refresh`).flush(testTokens('2'));
    controller.expectOne('/api/v1/sys/users').flush({}, UNAUTHORIZED);

    await expect(result).rejects.toMatchObject({ status: 401 });
    controller.expectNone(`${AUTH_API}/refresh`);
  });

  it('signs out and redirects to login with returnUrl when refresh fails', async () => {
    seedSession();
    setup();
    await router.navigateByUrl('/sys/users?page=2');
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    const result = firstValueFrom(http.get('/api/v1/sys/users'));

    controller.expectOne('/api/v1/sys/users').flush({}, UNAUTHORIZED);
    controller.expectOne(`${AUTH_API}/refresh`).flush({}, UNAUTHORIZED);

    await expect(result).rejects.toMatchObject({ status: 401 });
    expect(TestBed.inject(AuthService).isAuthenticated()).toBe(false);
    expect(navigate).toHaveBeenCalledWith(['/login'], { queryParams: { returnUrl: '/sys/users?page=2' } });
  });

  it('passes through errors other than 401 without refreshing', async () => {
    seedSession();
    setup();
    const result = firstValueFrom(http.get('/api/v1/sys/users'));
    controller.expectOne('/api/v1/sys/users').flush({}, { status: 403, statusText: 'Forbidden' });

    await expect(result).rejects.toMatchObject({ status: 403 });
    controller.expectNone(`${AUTH_API}/refresh`);
  });

  it.each([
    [`${AUTH_API}/captcha`, true],
    [`${AUTH_API}/login`, true],
    [`api/v1/sys/auth/refresh`, true],
    [`https://erp.example.com${AUTH_API}/login?x=1`, true],
    [`${AUTH_API}/logout`, false],
    [`${AUTH_API}/me`, false],
    ['/api/v1/sys/users', false],
  ])('isAnonymousAuthRequest(%s) = %s', (url, expected) => {
    expect(isAnonymousAuthRequest(url)).toBe(expected);
  });
});
