// MASTER_PROMPT 第 7 節：數字驗證碼元件，輸入 4–6 位數字，可換圖。
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { provideNzIcons } from 'ng-zorro-antd/icon';
import { provideTestTranslations } from '../../../testing/i18n-fixtures';
import { AUTH_API } from '../../core/auth/auth.service';
import { APP_ICONS } from '../../core/icons';
import { Captcha, CaptchaValue, isCaptchaValueValid } from './captcha';

const IMAGE = 'data:image/svg+xml;base64,PHN2Zy8+';

@Component({
  imports: [ReactiveFormsModule, Captcha],
  template: `<erp-captcha [formControl]="control" />`,
})
class Host {
  readonly control = new FormControl<CaptchaValue | null>(null);
}

describe('Captcha', () => {
  let fixture: ComponentFixture<Host>;
  let http: HttpTestingController;

  function el<T extends HTMLElement>(testId: string): T | null {
    return fixture.nativeElement.querySelector(`[data-testid="${testId}"]`);
  }

  function flushChallenge(id: string): void {
    http.expectOne(`${AUTH_API}/captcha`).flush({ captchaId: id, image: IMAGE, length: 5, expiresInSeconds: 120 });
    fixture.detectChanges();
  }

  function type(value: string): void {
    const input = el<HTMLInputElement>('captcha-input')!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        ...provideTestTranslations(),
        provideNzIcons(APP_ICONS),
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
  });

  afterEach(() => http.verify());

  it('loads a challenge and shows the image', () => {
    flushChallenge('c1');
    expect(el<HTMLImageElement>('captcha-image')!.src).toBe(IMAGE);
    expect(el<HTMLImageElement>('captcha-image')!.alt).toBe('驗證碼圖片');
    expect(fixture.componentInstance.control.value).toEqual({ captchaId: 'c1', code: '' });
  });

  it('accepts digits only, up to 6, and reports the value', () => {
    flushChallenge('c1');
    type('12a 3-4567');
    expect(el<HTMLInputElement>('captcha-input')!.value).toBe('123456');
    expect(fixture.componentInstance.control.value).toEqual({ captchaId: 'c1', code: '123456' });
    expect(el<HTMLInputElement>('captcha-input')!.getAttribute('maxlength')).toBe('6');
    expect(el<HTMLInputElement>('captcha-input')!.getAttribute('inputmode')).toBe('numeric');
  });

  it('loads a new image and clears the code when clicked', () => {
    flushChallenge('c1');
    type('1234');
    el<HTMLButtonElement>('captcha-refresh')!.click();
    fixture.detectChanges();
    expect(fixture.componentInstance.control.value).toEqual({ captchaId: '', code: '' });

    flushChallenge('c2');
    expect(fixture.componentInstance.control.value).toEqual({ captchaId: 'c2', code: '' });
    expect(el<HTMLInputElement>('captcha-input')!.value).toBe('');
  });

  it('shows a retry message when the challenge cannot be loaded', () => {
    http.expectOne(`${AUTH_API}/captcha`).error(new ProgressEvent('error'));
    fixture.detectChanges();
    expect(el('captcha-error')!.textContent).toContain('載入失敗');

    el<HTMLButtonElement>('captcha-refresh')!.click();
    flushChallenge('c3');
    expect(el('captcha-image')).not.toBeNull();
  });

  it('can be disabled by the form', () => {
    flushChallenge('c1');
    fixture.componentInstance.control.disable();
    fixture.detectChanges();
    expect(el<HTMLInputElement>('captcha-input')!.disabled).toBe(true);
    expect(el<HTMLButtonElement>('captcha-refresh')!.disabled).toBe(true);
  });
});

describe('isCaptchaValueValid', () => {
  it.each([
    [null, false],
    [{ captchaId: '', code: '1234' }, false],
    [{ captchaId: 'c1', code: '123' }, false],
    [{ captchaId: 'c1', code: '1234' }, true],
    [{ captchaId: 'c1', code: '123456' }, true],
    [{ captchaId: 'c1', code: '1234567' }, false],
    [{ captchaId: 'c1', code: '12a4' }, false],
  ])('isCaptchaValueValid(%o) = %s', (value, expected) => {
    expect(isCaptchaValueValid(value)).toBe(expected);
  });
});
