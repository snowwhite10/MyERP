import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal, viewChild } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, ValidatorFn, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzInputModule } from 'ng-zorro-antd/input';
import { AuthService } from '../../core/auth/auth.service';
import { apiErrorKey } from '../../core/http/api-error';
import { Captcha, CaptchaValue, isCaptchaValueValid } from '../../shared/captcha/captcha';

const captchaValidator: ValidatorFn = (control) => isCaptchaValueValid(control.value as CaptchaValue | null) ? null : { captcha: true };
const ERRORS = ['SYS.AUTH.CURRENT_PASSWORD_INVALID', 'SYS.AUTH.PASSWORD_REUSED', 'SYS.AUTH.INVALID_CREDENTIALS',
  'SYS.AUTH.CAPTCHA_INVALID', 'COMMON.CONCURRENCY.CONFLICT', 'COMMON.REQUEST.IP_NOT_ALLOWED'] as const;

@Component({
  selector: 'erp-change-password-page',
  imports: [ReactiveFormsModule, RouterLink, TranslatePipe, NzAlertModule, NzButtonModule, NzInputModule, Captcha],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <main class="erp-password" data-testid="change-password-page">
      <section class="erp-password__card">
        <h1>{{ 'sys.password.title' | translate }}</h1>
        @if (isExpired()) { <p>{{ 'sys.password.expiredHint' | translate }}</p> }
        <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
          @if (isExpired()) {
            <label for="password-username">{{ 'sys.auth.username' | translate }}</label>
            <input nz-input id="password-username" data-testid="password-username" formControlName="username" autocomplete="username" maxlength="100" />
          }
          <label for="password-current">{{ 'sys.password.current' | translate }}</label>
          <input nz-input id="password-current" data-testid="password-current" type="password" formControlName="current" autocomplete="current-password" maxlength="128" />
          <label for="password-new">{{ 'sys.password.new' | translate }}</label>
          <input nz-input id="password-new" data-testid="password-new" type="password" formControlName="next" autocomplete="new-password" maxlength="128" />
          <label for="password-confirm">{{ 'sys.password.confirm' | translate }}</label>
          <input nz-input id="password-confirm" data-testid="password-confirm" type="password" formControlName="confirm" autocomplete="new-password" maxlength="128" />
          @if (isExpired()) {
            <label for="password-captcha">{{ 'common.captcha.label' | translate }}</label>
            <erp-captcha inputId="password-captcha" formControlName="captcha" />
          }
          @if (showValidation()) { <p role="alert" class="erp-password__error">{{ validationKey() | translate }}</p> }
          @if (errorKey(); as key) { <nz-alert nzType="error" nzShowIcon [nzMessage]="key | translate" data-testid="password-error" /> }
          @if (success()) { <nz-alert nzType="success" nzShowIcon [nzMessage]="'sys.password.success' | translate" data-testid="password-success" /> }
          <button nz-button nzType="primary" type="submit" data-testid="password-submit" [nzLoading]="submitting()">{{ 'sys.password.submit' | translate }}</button>
          <a routerLink="/">{{ 'sys.password.back' | translate }}</a>
        </form>
      </section>
    </main>
  `,
  styles: `
    .erp-password { min-height: 100vh; display: flex; justify-content: center; align-items: center; padding: 24px; background: var(--erp-color-bg-body); }
    .erp-password__card { width: 100%; max-width: 440px; padding: 32px; border-radius: var(--erp-radius-card); background: var(--erp-color-bg-container); color: var(--erp-color-text); border: 1px solid var(--erp-color-border); }
    form { display: grid; gap: 10px; }
    label { margin-top: 8px; }
    button { margin-top: 16px; }
    .erp-password__error { color: var(--erp-status-error); margin: 0; }
  `,
})
export class ChangePasswordPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly captcha = viewChild(Captcha);
  readonly expired = input<string>();
  readonly username = input<string>();
  readonly isExpired = computed(() => this.expired() === '1');
  readonly submitting = signal(false);
  readonly errorKey = signal<string | null>(null);
  readonly success = signal(false);
  readonly attempted = signal(false);
  readonly form = new FormGroup({
    username: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.pattern(/\S/)] }),
    current: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    next: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    confirm: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    captcha: new FormControl<CaptchaValue | null>(null, { validators: [captchaValidator] }),
  });

  constructor() {
    effect(() => { if (this.username()) this.form.controls.username.setValue(this.username()!); });
  }

  showValidation(): boolean { return this.attempted() && this.validationKey() !== ''; }
  validationKey(): string {
    if (!this.attempted()) return '';
    const value = this.form.getRawValue();
    if (!value.current || !value.next || !value.confirm || (this.isExpired() && (!value.username.trim() || !isCaptchaValueValid(value.captcha)))) return 'sys.password.required';
    if (value.next !== value.confirm) return 'sys.password.mismatch';
    if (value.next === value.current) return 'sys.password.reused';
    return '';
  }

  submit(): void {
    this.attempted.set(true);
    if (this.validationKey() || this.submitting()) return;
    const value = this.form.getRawValue();
    this.submitting.set(true);
    this.errorKey.set(null);
    const request = this.isExpired()
      ? this.auth.changeExpiredPassword({ username: value.username.trim(), currentPassword: value.current, newPassword: value.next,
          captchaId: value.captcha!.captchaId, captchaCode: value.captcha!.code })
      : this.auth.changePassword(value.current, value.next);
    request.subscribe({
      next: () => {
        this.submitting.set(false);
        this.success.set(true);
        this.form.controls.current.reset('');
        this.form.controls.next.reset('');
        this.form.controls.confirm.reset('');
        void this.router.navigateByUrl('/');
      },
      error: (error: unknown) => {
        this.submitting.set(false);
        this.errorKey.set(apiErrorKey(error, ERRORS));
        this.form.controls.current.reset('');
        this.form.controls.next.reset('');
        this.form.controls.confirm.reset('');
        this.attempted.set(false);
        if (this.isExpired()) this.captcha()?.reload();
      },
    });
  }
}
