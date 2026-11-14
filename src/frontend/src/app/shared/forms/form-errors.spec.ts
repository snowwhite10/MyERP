// WU-UI-ERRFOCUS：表單送出驗證失敗時聚焦第一個錯誤欄位，錯誤訊息以 aria-describedby 關聯欄位。
import { Component, forwardRef } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ControlValueAccessor, FormControl, FormGroup, NG_VALUE_ACCESSOR, ReactiveFormsModule, Validators } from '@angular/forms';
import { ErpFormErrors, focusFirstInvalid } from './form-errors';

/** 模擬 nz-select／nz-input-number：宿主不可聚焦，內有輸入框。 */
@Component({
  selector: 'erp-test-wrapped',
  template: '<span class="wrap"><input data-testid="inner" (input)="onChange($any($event.target).value)" /></span>',
  providers: [{ provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => WrappedInput), multi: true }],
})
class WrappedInput implements ControlValueAccessor {
  onChange: (value: string) => void = () => undefined;
  writeValue(): void { /* 測試不需要 */ }
  registerOnChange(fn: (value: string) => void): void { this.onChange = fn; }
  registerOnTouched(): void { /* 測試不需要 */ }
}

@Component({
  imports: [ReactiveFormsModule, ErpFormErrors, WrappedInput],
  template: `
    <button type="button" data-testid="outside">outside</button>
    <form [formGroup]="form" erpFormErrors (ngSubmit)="save()">
      <label for="code">Code</label>
      <input id="code" data-testid="code" formControlName="code" aria-describedby="code-hint" />
      <small id="code-hint">hint</small>
      @if (form.controls.code.touched && form.controls.code.invalid) { <p role="alert" data-testid="code-error">code required</p> }
      <label for="name">Name</label>
      <input id="name" data-testid="name" formControlName="name" />
      @if (form.controls.name.touched && form.controls.name.invalid) { <p role="alert" data-testid="name-error">name required</p> }
      <label>Qty</label>
      <erp-test-wrapped formControlName="qty" />
      @if (form.controls.qty.touched && form.controls.qty.invalid) { <p role="alert" data-testid="qty-error">qty required</p> }
      <label for="confirm">Confirm</label>
      <input id="confirm" data-testid="confirm" formControlName="confirm" />
      @if (form.touched && form.hasError('mismatch')) { <p role="alert" data-testid="mismatch">mismatch</p> }
    </form>
  `,
})
class Host {
  readonly form = new FormGroup({
    code: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    name: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    qty: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    confirm: new FormControl('', { nonNullable: true }),
  }, { validators: (group) => (group.value.confirm === 'x' ? { mismatch: true } : null) });
  saved = 0;

  save(): void {
    this.form.markAllAsTouched();
    if (this.form.invalid) { focusFirstInvalid(this.form); return; }
    this.saved++;
  }
}

describe('ErpFormErrors', () => {
  let fixture: ComponentFixture<Host>;
  const el = (testId: string) => fixture.nativeElement.querySelector(`[data-testid="${testId}"]`) as HTMLElement;

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  async function submit(): Promise<void> {
    el('outside').focus();
    fixture.componentInstance.save();
    await settle();
  }

  beforeEach(async () => {
    fixture = TestBed.createComponent(Host);
    document.body.appendChild(fixture.nativeElement);
    await settle();
  });

  afterEach(() => fixture.nativeElement.remove());

  it('UI-ERRFOCUS-U01 送出全部空白：焦點移到第一個錯誤欄位，所有錯誤欄位 aria-invalid 並關聯錯誤訊息', async () => {
    await submit();
    expect(document.activeElement).toBe(el('code'));
    for (const [field, error] of [['code', 'code-error'], ['name', 'name-error'], ['inner', 'qty-error']]) {
      expect(el(field).getAttribute('aria-invalid')).toBe('true');
      expect(el(error).id).toMatch(/^erp-field-error-\d+$/);
      expect(el(field).getAttribute('aria-describedby')?.split(' ')).toContain(el(error).id);
    }
    expect(el('confirm').hasAttribute('aria-invalid')).toBe(false);
    expect(fixture.componentInstance.saved).toBe(0);
  });

  it('UI-ERRFOCUS-U02 保留欄位原有的 aria-describedby，錯誤消失後只移除錯誤 id', async () => {
    await submit();
    const errorId = el('code-error').id;
    expect(el('code').getAttribute('aria-describedby')).toBe(`code-hint ${errorId}`);
    fixture.componentInstance.form.controls.code.setValue('A01');
    await settle();
    expect(el('code-error')).toBeNull();
    expect(el('code').getAttribute('aria-describedby')).toBe('code-hint');
    expect(el('code').hasAttribute('aria-invalid')).toBe(false);
  });

  it('UI-ERRFOCUS-U03 首個錯誤在後面：跳過已填欄位；宿主不可聚焦時聚焦其內輸入框', async () => {
    const form = fixture.componentInstance.form;
    form.patchValue({ code: 'A01', name: 'Name' });
    await submit();
    expect(document.activeElement).toBe(el('inner'));
    expect(el('code').hasAttribute('aria-invalid')).toBe(false);
  });

  it('UI-ERRFOCUS-U04 只有表單層級錯誤（密碼不一致）：聚焦訊息前面的欄位並關聯訊息', async () => {
    fixture.componentInstance.form.patchValue({ code: 'A01', name: 'Name', qty: '1', confirm: 'x' });
    await submit();
    expect(document.activeElement).toBe(el('confirm'));
    expect(el('confirm').getAttribute('aria-invalid')).toBe('true');
    expect(el('confirm').getAttribute('aria-describedby')).toBe(el('mismatch').id);
  });

  it('UI-ERRFOCUS-U05 表單有效時不移動焦點，照常保存', async () => {
    fixture.componentInstance.form.patchValue({ code: 'A01', name: 'Name', qty: '1' });
    await submit();
    expect(document.activeElement).toBe(el('outside'));
    expect(fixture.componentInstance.saved).toBe(1);
    expect(fixture.nativeElement.querySelector('[aria-invalid="true"]')).toBeNull();
  });

  it('UI-ERRFOCUS-U06 未送出前不標示錯誤；只觸碰一欄時只標示該欄', async () => {
    expect(fixture.nativeElement.querySelector('[aria-invalid]')).toBeNull();
    fixture.componentInstance.form.controls.name.markAsTouched();
    await settle();
    expect(el('name').getAttribute('aria-invalid')).toBe('true');
    expect(el('code').hasAttribute('aria-invalid')).toBe(false);
  });

  it('UI-ERRFOCUS-U07 停用的欄位即使無效也不標示、不聚焦', async () => {
    fixture.componentInstance.form.controls.code.disable();
    await submit();
    expect(el('code').hasAttribute('aria-invalid')).toBe(false);
    expect(document.activeElement).toBe(el('name'));
  });

  it('UI-ERRFOCUS-U08 沒有套用指令的表單呼叫 focusFirstInvalid 不出錯', () => {
    expect(() => focusFirstInvalid(new FormGroup({}))).not.toThrow();
  });
});
