import { ChangeDetectionStrategy, Component, inject, input, signal, viewChild } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, ValidatorFn, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzIconModule } from 'ng-zorro-antd/icon';
import { NzInputModule } from 'ng-zorro-antd/input';
import { safeReturnUrl } from '../../core/auth/auth.guard';
import { AuthService } from '../../core/auth/auth.service';
import { apiErrorKey } from '../../core/http/api-error';
import { LanguageService } from '../../core/i18n/language.service';
import { ErpLanguage } from '../../core/i18n/languages';
import { Captcha, CaptchaValue, isCaptchaValueValid } from '../../shared/captcha/captcha';

/** 後端已知的登入錯誤代碼（api.md「登入與 Token」）；其他錯誤顯示通用訊息。 */
export const LOGIN_ERROR_CODES = [
  'SYS.AUTH.INVALID_CREDENTIALS',
  'SYS.AUTH.CAPTCHA_INVALID',
  'SYS.AUTH.PASSWORD_EXPIRED',
  'COMMON.REQUEST.IP_NOT_ALLOWED',
] as const;

const captchaValidator: ValidatorFn = (control) =>
  isCaptchaValueValid(control.value as CaptchaValue | null) ? null : { captcha: true };

/** 將登入失敗轉成 i18n key：已知 errorCode → `errors.<code>`；429 → 請求過於頻繁；連不到伺服器 → network；其他 → unknown。 */
export function loginErrorKey(error: unknown): string {
  return apiErrorKey(error, LOGIN_ERROR_CODES);
}

/** 登入頁（介面 8.62，REQ_F4118.24.1）：帳號、密碼、數字驗證碼；成功後回到 returnUrl。 */
@Component({
  selector: 'erp-login-page',
  imports: [ReactiveFormsModule, RouterLink, TranslatePipe, NzAlertModule, NzButtonModule, NzIconModule, NzInputModule, Captcha],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <main class="erp-login">
      <section class="erp-login__card" data-testid="login-page">
        <header class="erp-login__header">
          <span class="erp-login__logo">M</span>
          <h1 data-testid="login-title">{{ 'sys.auth.title' | translate }}</h1>
          <p>{{ 'common.app.title' | translate }}</p>
        </header>

        <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
          <label for="login-username">{{ 'sys.auth.username' | translate }}</label>
          <nz-input-wrapper>
            <nz-icon nzInputPrefix nzType="user" />
            <input
              nz-input
              id="login-username"
              data-testid="login-username"
              formControlName="username"
              autocomplete="username"
              maxlength="100"
            />
          </nz-input-wrapper>
          @if (showError('username')) {
            <p class="erp-login__field-error" role="alert">{{ 'sys.auth.usernameRequired' | translate }}</p>
          }

          <label for="login-password">{{ 'sys.auth.password' | translate }}</label>
          <nz-input-wrapper>
            <nz-icon nzInputPrefix nzType="lock" />
            <input
              nz-input
              id="login-password"
              type="password"
              data-testid="login-password"
              formControlName="password"
              autocomplete="current-password"
              maxlength="128"
            />
          </nz-input-wrapper>
          @if (showError('password')) {
            <p class="erp-login__field-error" role="alert">{{ 'sys.auth.passwordRequired' | translate }}</p>
          }

          <label for="login-captcha">{{ 'common.captcha.label' | translate }}</label>
          <erp-captcha inputId="login-captcha" formControlName="captcha" />
          @if (showError('captcha')) {
            <p class="erp-login__field-error" role="alert">{{ 'common.captcha.invalid' | translate }}</p>
          }

          @if (errorKey(); as key) {
            <nz-alert nzType="error" nzShowIcon [nzMessage]="key | translate" data-testid="login-error" />
          }
          @if (errorKey() === 'errors.SYS.AUTH.PASSWORD_EXPIRED') {
            <a data-testid="login-change-expired" [routerLink]="['/change-password']" [queryParams]="{ expired: '1', username: form.controls.username.value.trim() }">{{ 'sys.auth.changeExpired' | translate }}</a>
          }

          <button
            nz-button
            nzType="primary"
            nzBlock
            type="submit"
            data-testid="login-submit"
            [nzLoading]="submitting()"
          >
            {{ 'sys.auth.submit' | translate }}
          </button>
        </form>

        <nav class="erp-login__languages" [attr.aria-label]="'sys.auth.language' | translate">
          @for (lang of language.languages; track lang.code) {
            <button
              type="button"
              nz-button
              nzType="link"
              nzSize="small"
              [attr.data-testid]="'login-language-' + lang.code"
              [attr.aria-pressed]="lang.code === language.current()"
              (click)="changeLanguage(lang.code)"
            >
              {{ lang.nativeName }}
            </button>
          }
        </nav>
      </section>
    </main>
  `,
  styles: `
    .erp-login {
      min-height: 100vh; display: flex; align-items: center; justify-content: center;
      padding: 24px; background: var(--erp-color-bg-body);
    }
    .erp-login__card {
      width: 100%; max-width: 400px; padding: 32px; border-radius: var(--erp-radius-card);
      background: var(--erp-color-bg-container); border: 1px solid var(--erp-color-border);
      color: var(--erp-color-text);
    }
    .erp-login__header { text-align: center; margin-bottom: 24px; }
    .erp-login__header h1 { margin: 12px 0 4px; font-size: 22px; color: var(--erp-color-text); }
    .erp-login__header p { margin: 0; color: var(--erp-color-text-secondary); }
    .erp-login__logo {
      display: inline-flex; width: 44px; height: 44px; align-items: center; justify-content: center;
      border-radius: 12px; background: var(--erp-color-primary); color: var(--erp-status-text);
      font-size: 22px; font-weight: 600;
    }
    form { display: grid; gap: 8px; }
    label { margin-top: 8px; }
    nz-alert { margin-top: 8px; }
    button[type='submit'] { margin-top: 16px; }
    .erp-login__field-error { margin: 0; color: var(--erp-status-error); font-size: 13px; }
    .erp-login__languages { display: flex; justify-content: center; gap: 4px; margin-top: 16px; }
  `,
})
export class LoginPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  protected readonly language = inject(LanguageService);

  /** 由查詢字串綁定（withComponentInputBinding） */
  readonly returnUrl = input<string | undefined>();

  private readonly captcha = viewChild.required(Captcha);
  protected readonly submitting = signal(false);
  protected readonly errorKey = signal<string | null>(null);

  readonly form = new FormGroup({
    username: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.pattern(/\S/)] }),
    password: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    captcha: new FormControl<CaptchaValue | null>(null, { validators: [captchaValidator] }),
  });

  protected showError(name: 'username' | 'password' | 'captcha'): boolean {
    const control = this.form.controls[name];
    return control.touched && control.invalid;
  }

  protected changeLanguage(code: ErpLanguage): void {
    this.language.use(code).subscribe();
  }

  submit(): void {
    this.form.markAllAsTouched();
    if (this.form.invalid || this.submitting()) return;
    const { username, password, captcha } = this.form.getRawValue();
    this.submitting.set(true);
    this.errorKey.set(null);
    this.auth
      .login({ username, password, captchaId: captcha!.captchaId, captchaCode: captcha!.code })
      .subscribe({
        next: () => {
          this.submitting.set(false);
          void this.router.navigateByUrl(safeReturnUrl(this.returnUrl()));
        },
        error: (error: unknown) => {
          this.submitting.set(false);
          this.errorKey.set(loginErrorKey(error));
          // Captcha 驗證一次即作廢（不論對錯），失敗後必須換新圖；密碼也清空
          this.form.controls.password.reset('');
          this.form.controls.password.markAsUntouched();
          this.form.controls.captcha.markAsUntouched();
          this.captcha().reload();
        },
      });
  }
}
