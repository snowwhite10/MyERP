// REQ_F4118.24.1：三語登入頁、錯誤代碼訊息、失敗後換新驗證碼、成功後回到 returnUrl。
import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { provideNzIcons } from 'ng-zorro-antd/icon';
import { firstValueFrom } from 'rxjs';
import { STORED_SESSION_KEY, testTokens } from '../../../testing/auth-fixtures';
import { provideTestTranslations } from '../../../testing/i18n-fixtures';
import { AUTH_API } from '../../core/auth/auth.service';
import { LanguageService } from '../../core/i18n/language.service';
import { APP_ICONS } from '../../core/icons';
import { LoginPage, loginErrorKey } from './login.page';

const IMAGE = 'data:image/svg+xml;base64,PHN2Zy8+';

describe('LoginPage', () => {
  let fixture: ComponentFixture<LoginPage>;
  let http: HttpTestingController;
  let navigate: ReturnType<typeof vi.spyOn>;

  function el<T extends HTMLElement>(testId: string): T {
    return fixture.nativeElement.querySelector(`[data-testid="${testId}"]`);
  }

  function flushCaptcha(id: string): void {
    http.expectOne(`${AUTH_API}/captcha`).flush({ captchaId: id, image: IMAGE, length: 5, expiresInSeconds: 120 });
    fixture.detectChanges();
  }

  function type(testId: string, value: string): void {
    const input = el<HTMLInputElement>(testId);
    input.value = value;
    input.dispatchEvent(new Event('input'));
  }

  function fillAndSubmit(username = 'admin', password = 'Passw0rd!2026', code = '12345'): void {
    type('login-username', username);
    type('login-password', password);
    type('captcha-input', code);
    fixture.detectChanges();
    el<HTMLButtonElement>('login-submit').click();
    fixture.detectChanges();
  }

  async function create(returnUrl?: string): Promise<void> {
    fixture = TestBed.createComponent(LoginPage);
    if (returnUrl !== undefined) fixture.componentRef.setInput('returnUrl', returnUrl);
    fixture.detectChanges();
    await fixture.whenStable();
    flushCaptcha('c1');
  }

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        ...provideTestTranslations(),
        provideNzIcons(APP_ICONS),
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpTestingController);
    navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
  });

  afterEach(() => {
    http.verify();
    localStorage.clear();
  });

  it('renders the sign-in form in Traditional Chinese', async () => {
    await create();
    expect(el('login-title').textContent).toContain('登入');
    expect(el<HTMLInputElement>('login-password').type).toBe('password');
    expect(el<HTMLInputElement>('login-username').autocomplete).toBe('username');
    expect(el('captcha-image')).not.toBeNull();
  });

  it('shows required messages and does not call the server when empty', async () => {
    await create();
    el<HTMLButtonElement>('login-submit').click();
    fixture.detectChanges();

    const alerts = Array.from(fixture.nativeElement.querySelectorAll('[role="alert"]')).map((e) => (e as HTMLElement).textContent?.trim());
    expect(alerts).toEqual(['請輸入帳號', '請輸入密碼', '請輸入 4–6 位數字驗證碼']);
  });

  it('rejects a whitespace-only username and a 3-digit captcha', async () => {
    await create();
    fillAndSubmit('   ', 'x', '123');
    const alerts = Array.from(fixture.nativeElement.querySelectorAll('[role="alert"]')).map((e) => (e as HTMLElement).textContent?.trim());
    expect(alerts).toEqual(['請輸入帳號', '請輸入 4–6 位數字驗證碼']);
  });

  it('logs in and navigates to a safe returnUrl', async () => {
    await create('/sys/users');
    fillAndSubmit();

    const req = http.expectOne(`${AUTH_API}/login`);
    expect(req.request.body).toEqual({ username: 'admin', password: 'Passw0rd!2026', captchaId: 'c1', captchaCode: '12345' });
    req.flush(testTokens());
    fixture.detectChanges();

    expect(navigate).toHaveBeenCalledWith('/sys/users');
    expect(localStorage.getItem(STORED_SESSION_KEY)).not.toBeNull();
  });

  it('ignores an external returnUrl', async () => {
    await create('https://evil.example/phish');
    fillAndSubmit();
    http.expectOne(`${AUTH_API}/login`).flush(testTokens());
    expect(navigate).toHaveBeenCalledWith('/');
  });

  it('accepts the 6-digit development master code length', async () => {
    await create();
    fillAndSubmit('admin', 'Passw0rd!2026', '123456');
    const req = http.expectOne(`${AUTH_API}/login`);
    expect(req.request.body.captchaCode).toBe('123456');
    req.flush(testTokens());
  });

  it('shows the translated error, clears the password and reloads the captcha after failure', async () => {
    await create();
    fillAndSubmit();
    http
      .expectOne(`${AUTH_API}/login`)
      .flush({ errorCode: 'SYS.AUTH.INVALID_CREDENTIALS' }, { status: 401, statusText: 'Unauthorized' });
    fixture.detectChanges();

    expect(el('login-error').textContent).toContain('帳號或密碼錯誤');
    expect(el<HTMLInputElement>('login-password').value).toBe('');
    expect(el<HTMLInputElement>('login-username').value).toBe('admin');
    // 失敗後不立即顯示欄位必填訊息
    expect(fixture.nativeElement.querySelectorAll('[role="alert"]').length).toBe(0);
    flushCaptcha('c2');

    expect(navigate).not.toHaveBeenCalled();
    fillAndSubmit('admin', 'Passw0rd!2026', '54321');
    const retry = http.expectOne(`${AUTH_API}/login`);
    expect(retry.request.body.captchaId).toBe('c2');
    retry.flush(testTokens());
    expect(navigate).toHaveBeenCalledWith('/');
  });

  it('switches the page language', async () => {
    await create();
    el<HTMLButtonElement>('login-language-en').click();
    await firstValueFrom(TestBed.inject(LanguageService).use('en'));
    fixture.detectChanges();
    expect(el('login-title').textContent).toContain('Sign in');
    expect(el('login-language-en').getAttribute('aria-pressed')).toBe('true');
  });

  it.each([
    [new HttpErrorResponse({ status: 401, error: { errorCode: 'SYS.AUTH.INVALID_CREDENTIALS' } }), 'errors.SYS.AUTH.INVALID_CREDENTIALS'],
    [new HttpErrorResponse({ status: 400, error: { errorCode: 'SYS.AUTH.CAPTCHA_INVALID' } }), 'errors.SYS.AUTH.CAPTCHA_INVALID'],
    [new HttpErrorResponse({ status: 403, error: { errorCode: 'SYS.AUTH.PASSWORD_EXPIRED' } }), 'errors.SYS.AUTH.PASSWORD_EXPIRED'],
    [new HttpErrorResponse({ status: 400, error: { errorCode: 'COMMON.REQUEST.VALIDATION_FAILED' } }), 'errors.COMMON.REQUEST.VALIDATION_FAILED'],
    [new HttpErrorResponse({ status: 429, error: { errorCode: 'COMMON.REQUEST.RATE_LIMITED' } }), 'errors.COMMON.REQUEST.RATE_LIMITED'],
    [new HttpErrorResponse({ status: 0 }), 'errors.http.network'],
    [new HttpErrorResponse({ status: 500, error: { errorCode: 'SOMETHING.ELSE' } }), 'errors.http.unknown'],
    [new Error('boom'), 'errors.http.unknown'],
  ])('loginErrorKey maps %s', (error, expected) => {
    expect(loginErrorKey(error)).toBe(expected);
  });
});
