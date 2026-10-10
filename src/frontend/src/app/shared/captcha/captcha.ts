import { ChangeDetectionStrategy, Component, DestroyRef, forwardRef, inject, input, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { NzIconModule } from 'ng-zorro-antd/icon';
import { NzInputModule } from 'ng-zorro-antd/input';
import { AuthService, CaptchaChallenge } from '../../core/auth/auth.service';

export interface CaptchaValue {
  captchaId: string;
  code: string;
}

/** 輸入框長度 4–6 位數字（MASTER_PROMPT 第 7 節；Development 萬用碼為 6 位）。 */
export const CAPTCHA_CODE_PATTERN = /^\d{4,6}$/;
export const CAPTCHA_MAX_LENGTH = 6;

/**
 * 數字驗證碼元件（登入、註冊、忘記密碼共用）。值為 `{ captchaId, code }`；
 * 尚未取得圖片時 captchaId 為空字串。後端驗證一次即作廢，所以每次送出失敗後呼叫 `reload()`。
 */
@Component({
  selector: 'erp-captcha',
  imports: [TranslatePipe, NzInputModule, NzIconModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [{ provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => Captcha), multi: true }],
  template: `
    <div class="erp-captcha">
      <input
        nz-input
        [attr.id]="inputId()"
        data-testid="captcha-input"
        inputmode="numeric"
        autocomplete="off"
        [attr.maxlength]="maxLength"
        [attr.aria-label]="'common.captcha.label' | translate"
        [placeholder]="'common.captcha.placeholder' | translate"
        [value]="code()"
        [disabled]="disabled()"
        (input)="onInput($event)"
        (blur)="onTouched()"
      />
      <button
        type="button"
        class="erp-captcha__image"
        data-testid="captcha-refresh"
        [disabled]="disabled() || loading()"
        [attr.aria-label]="'common.captcha.refresh' | translate"
        [title]="'common.captcha.refresh' | translate"
        (click)="reload()"
      >
        @if (challenge(); as c) {
          <img data-testid="captcha-image" [src]="c.image" [alt]="'common.captcha.imageAlt' | translate" />
        } @else if (failed()) {
          <span data-testid="captcha-error">{{ 'common.captcha.loadFailed' | translate }}</span>
        } @else {
          <nz-icon nzType="loading" />
        }
      </button>
    </div>
  `,
  styles: `
    .erp-captcha { display: flex; gap: 8px; align-items: stretch; }
    .erp-captcha input { flex: 1; min-width: 0; }
    .erp-captcha__image {
      display: inline-flex; align-items: center; justify-content: center;
      width: 132px; height: 40px; padding: 0; cursor: pointer;
      border: 1px solid var(--erp-color-border); border-radius: 6px;
      background: #fff; color: var(--erp-status-error);
    }
    .erp-captcha__image img { width: 100%; height: 100%; object-fit: contain; }
    .erp-captcha__image span { font-size: 12px; }
  `,
})
export class Captcha implements ControlValueAccessor, OnInit {
  private readonly auth = inject(AuthService);
  private readonly destroyRef = inject(DestroyRef);

  readonly inputId = input<string>('captcha-code');
  readonly maxLength = CAPTCHA_MAX_LENGTH;

  protected readonly challenge = signal<CaptchaChallenge | null>(null);
  protected readonly code = signal('');
  protected readonly loading = signal(false);
  protected readonly failed = signal(false);
  protected readonly disabled = signal(false);

  private onChange: (value: CaptchaValue) => void = () => undefined;
  protected onTouched: () => void = () => undefined;

  ngOnInit(): void {
    this.reload();
  }

  /** 取得新圖片並清空輸入。 */
  reload(): void {
    this.loading.set(true);
    this.failed.set(false);
    this.challenge.set(null);
    this.code.set('');
    this.emit();
    this.auth
      .captcha()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (challenge) => {
          this.challenge.set(challenge);
          this.loading.set(false);
          this.emit();
        },
        error: () => {
          this.failed.set(true);
          this.loading.set(false);
        },
      });
  }

  protected onInput(event: Event): void {
    const target = event.target as HTMLInputElement;
    // 只留數字，貼上「12 34」之類也可用
    const digits = target.value.replace(/\D/g, '').slice(0, CAPTCHA_MAX_LENGTH);
    target.value = digits;
    this.code.set(digits);
    this.emit();
  }

  writeValue(value: CaptchaValue | null): void {
    this.code.set(value?.code ?? '');
  }

  registerOnChange(fn: (value: CaptchaValue) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(isDisabled: boolean): void {
    this.disabled.set(isDisabled);
  }

  private emit(): void {
    this.onChange({ captchaId: this.challenge()?.captchaId ?? '', code: this.code() });
  }
}

/** 表單驗證：已有 captchaId 且輸入 4–6 位數字。 */
export function isCaptchaValueValid(value: CaptchaValue | null | undefined): boolean {
  return !!value && value.captchaId.length > 0 && CAPTCHA_CODE_PATTERN.test(value.code);
}
