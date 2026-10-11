import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { provideNzIcons } from 'ng-zorro-antd/icon';
import { seedSession, STORED_SESSION_KEY, testTokens } from '../../../testing/auth-fixtures';
import { provideTestTranslations } from '../../../testing/i18n-fixtures';
import { AUTH_API } from '../../core/auth/auth.service';
import { APP_ICONS } from '../../core/icons';
import { ChangePasswordPage } from './change-password.page';

describe('ChangePasswordPage', () => {
  let http: HttpTestingController;
  const input = (id: string, value: string, root: HTMLElement) => {
    const element = root.querySelector<HTMLInputElement>(`[data-testid="${id}"]`)!;
    element.value = value;
    element.dispatchEvent(new Event('input'));
  };

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ providers: [
      ...provideTestTranslations(), provideNzIcons(APP_ICONS), provideRouter([]), provideHttpClient(), provideHttpClientTesting(),
    ] });
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
  });

  afterEach(() => { http.verify(); localStorage.clear(); });

  it('requires matching new passwords and replaces the session token', async () => {
    seedSession();
    const fixture = TestBed.createComponent(ChangePasswordPage);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    input('password-current', 'old', root);
    input('password-new', 'new', root);
    input('password-confirm', 'other', root);
    root.querySelector<HTMLButtonElement>('[data-testid="password-submit"]')!.click();
    fixture.detectChanges();
    expect(root.textContent).toContain('兩次輸入的新密碼不一致');
    http.expectNone(`${AUTH_API}/change-password`);
    input('password-confirm', 'new', root);
    root.querySelector<HTMLButtonElement>('[data-testid="password-submit"]')!.click();
    const req = http.expectOne(`${AUTH_API}/change-password`);
    expect(req.request.body.refreshToken).toBe('refresh-1');
    req.flush(testTokens('2'));
    fixture.detectChanges();
    expect(JSON.parse(localStorage.getItem(STORED_SESSION_KEY)!).refreshToken).toBe('refresh-2');
  });

  it('uses captcha on the expired password route and signs in on success', async () => {
    const fixture = TestBed.createComponent(ChangePasswordPage);
    fixture.componentRef.setInput('expired', '1');
    fixture.componentRef.setInput('username', 'admin');
    fixture.detectChanges();
    await fixture.whenStable();
    http.expectOne(`${AUTH_API}/captcha`).flush({ captchaId: 'c1', image: 'data:image/svg+xml;base64,PHN2Zy8+', length: 5, expiresInSeconds: 120 });
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    input('password-current', 'old', root);
    input('password-new', 'new', root);
    input('password-confirm', 'new', root);
    input('captcha-input', '12345', root);
    fixture.detectChanges();
    root.querySelector<HTMLButtonElement>('[data-testid="password-submit"]')!.click();
    const req = http.expectOne(`${AUTH_API}/change-expired-password`);
    expect(req.request.body).toMatchObject({ username: 'admin', captchaId: 'c1', captchaCode: '12345' });
    req.flush(testTokens());
    expect(JSON.parse(localStorage.getItem(STORED_SESSION_KEY)!).username).toBe('admin');
  });
});
