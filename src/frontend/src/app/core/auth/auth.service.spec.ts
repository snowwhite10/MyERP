// REQ_F4118.24.1–3：前端 Session 保存、刷新（單一請求、失敗清除）與登出。
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { seedSession, STORED_SESSION_KEY, TEST_NOW, testTokens } from '../../../testing/auth-fixtures';
import { AUTH_API, AUTH_NOW, AuthService } from './auth.service';

describe('AuthService', () => {
  let http: HttpTestingController;
  let now: number;

  function create(): AuthService {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: AUTH_NOW, useValue: () => now }],
    });
    http = TestBed.inject(HttpTestingController);
    return TestBed.inject(AuthService);
  }

  beforeEach(() => {
    localStorage.clear();
    now = TEST_NOW;
  });

  afterEach(() => {
    http.verify();
    localStorage.clear();
  });

  it('starts signed out without a stored session', () => {
    const auth = create();
    expect(auth.isAuthenticated()).toBe(false);
    expect(auth.accessToken()).toBeNull();
    expect(auth.username()).toBeNull();
  });

  it('fetches a captcha challenge', async () => {
    const auth = create();
    const result = firstValueFrom(auth.captcha());
    http
      .expectOne(`${AUTH_API}/captcha`)
      .flush({ captchaId: 'c1', image: 'data:image/svg+xml;base64,AA', length: 5, expiresInSeconds: 120 });
    expect((await result).captchaId).toBe('c1');
  });

  it('logs in with a trimmed username and stores the session', async () => {
    const auth = create();
    const result = firstValueFrom(
      auth.login({ username: '  admin ', password: 'P@ss', captchaId: 'c1', captchaCode: '12345' }),
    );
    const req = http.expectOne(`${AUTH_API}/login`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ username: 'admin', password: 'P@ss', captchaId: 'c1', captchaCode: '12345' });
    req.flush(testTokens());

    expect((await result).username).toBe('admin');
    expect(auth.isAuthenticated()).toBe(true);
    expect(auth.accessToken()).toBe('access-1');
    expect(auth.username()).toBe('admin');
    expect(JSON.parse(localStorage.getItem(STORED_SESSION_KEY)!).refreshToken).toBe('refresh-1');
  });

  it('does not store anything when login fails', async () => {
    const auth = create();
    const result = firstValueFrom(auth.login({ username: 'admin', password: 'x', captchaId: 'c1', captchaCode: '1234' }));
    http
      .expectOne(`${AUTH_API}/login`)
      .flush({ errorCode: 'SYS.AUTH.INVALID_CREDENTIALS' }, { status: 401, statusText: 'Unauthorized' });

    await expect(result).rejects.toMatchObject({ status: 401 });
    expect(auth.isAuthenticated()).toBe(false);
    expect(localStorage.getItem(STORED_SESSION_KEY)).toBeNull();
  });

  it('restores a stored session on startup', () => {
    seedSession({ username: 'buyer' });
    const auth = create();
    expect(auth.isAuthenticated()).toBe(true);
    expect(auth.username()).toBe('buyer');
  });

  it.each([
    ['not json', '{oops'],
    ['missing tokens', JSON.stringify({ username: 'admin' })],
    ['bad expiry', JSON.stringify({ ...testTokens(), username: 'admin', refreshTokenExpiresAt: 'soon' })],
  ])('discards a corrupt stored session (%s)', (_name, raw) => {
    localStorage.setItem(STORED_SESSION_KEY, raw);
    const auth = create();
    expect(auth.isAuthenticated()).toBe(false);
    expect(localStorage.getItem(STORED_SESSION_KEY)).toBeNull();
  });

  it('treats an expired refresh token as signed out and clears it', () => {
    seedSession({ refreshTokenExpiresAt: '2026-10-10T11:59:59+08:00' });
    const auth = create();
    expect(auth.isAuthenticated()).toBe(false);
    expect(auth.accessToken()).toBeNull();
    expect(localStorage.getItem(STORED_SESSION_KEY)).toBeNull();
  });

  it('expires the session exactly at refreshTokenExpiresAt', () => {
    seedSession({ refreshTokenExpiresAt: '2026-10-10T12:00:01+08:00' });
    const auth = create();
    expect(auth.isAuthenticated()).toBe(true);
    now += 1000;
    expect(auth.isAuthenticated()).toBe(false);
  });

  it('rotates tokens on refresh and keeps the username', async () => {
    seedSession();
    const auth = create();
    const result = firstValueFrom(auth.refresh());
    const req = http.expectOne(`${AUTH_API}/refresh`);
    expect(req.request.body).toEqual({ refreshToken: 'refresh-1' });
    req.flush(testTokens('2'));

    expect((await result).accessToken).toBe('access-2');
    expect(auth.accessToken()).toBe('access-2');
    expect(auth.username()).toBe('admin');
    expect(JSON.parse(localStorage.getItem(STORED_SESSION_KEY)!).refreshToken).toBe('refresh-2');
  });

  it('shares one refresh request between concurrent callers, then uses the rotated token', async () => {
    seedSession();
    const auth = create();
    const first = firstValueFrom(auth.refresh());
    const second = firstValueFrom(auth.refresh());
    http.expectOne(`${AUTH_API}/refresh`).flush(testTokens('2'));

    expect((await first).accessToken).toBe('access-2');
    expect((await second).accessToken).toBe('access-2');

    const third = firstValueFrom(auth.refresh());
    const req = http.expectOne(`${AUTH_API}/refresh`);
    expect(req.request.body).toEqual({ refreshToken: 'refresh-2' });
    req.flush(testTokens('3'));
    expect((await third).accessToken).toBe('access-3');
  });

  it('clears the session when refresh is rejected', async () => {
    seedSession();
    const auth = create();
    const result = firstValueFrom(auth.refresh());
    http
      .expectOne(`${AUTH_API}/refresh`)
      .flush({ errorCode: 'SYS.AUTH.REFRESH_INVALID' }, { status: 401, statusText: 'Unauthorized' });

    await expect(result).rejects.toMatchObject({ status: 401 });
    expect(auth.isAuthenticated()).toBe(false);
    expect(localStorage.getItem(STORED_SESSION_KEY)).toBeNull();
  });

  it('fails refresh immediately without a session', async () => {
    const auth = create();
    await expect(firstValueFrom(auth.refresh())).rejects.toThrow('No session');
  });

  it('revokes the refresh token on logout and clears the session', async () => {
    seedSession();
    const auth = create();
    const result = firstValueFrom(auth.logout());
    const req = http.expectOne(`${AUTH_API}/logout`);
    expect(req.request.body).toEqual({ refreshToken: 'refresh-1' });
    req.flush(null, { status: 204, statusText: 'No Content' });

    await result;
    expect(auth.isAuthenticated()).toBe(false);
  });

  it('still signs out locally when the server logout fails', async () => {
    seedSession();
    const auth = create();
    const result = firstValueFrom(auth.logout());
    http.expectOne(`${AUTH_API}/logout`).error(new ProgressEvent('error'));

    await expect(result).resolves.toBeUndefined();
    expect(auth.isAuthenticated()).toBe(false);
    expect(localStorage.getItem(STORED_SESSION_KEY)).toBeNull();
  });

  it('logout without a session does not call the server', async () => {
    const auth = create();
    await expect(firstValueFrom(auth.logout())).resolves.toBeUndefined();
  });
});
