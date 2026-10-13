import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzModalModule } from 'ng-zorro-antd/modal';
import { LanguageService } from '../../core/i18n/language.service';
import { isoDate } from '../../shared/date';
import { latestRequest } from '../../shared/list-page/latest-request';
import { TaxCalculation, TaxCode, TaxDirection, TaxService } from './tax.service';

/** 前端限 11 位整數、4 位小數（15 位有效數字內 JSON 數值不失真），可為負數（退貨／折讓）。 */
export const TAX_AMOUNT_PATTERN = /^-?\d{1,11}(\.\d{1,4})?$/;

/**
 * 單行稅額試算（介面 8.86，ADR-038）：按業務日期與方向取稅率，再依幣別小數位計算未稅、稅額與含稅金額。
 * 只需 `MD.TAX_CODE.VIEW`；方向選項依稅別類型限制。
 */
@Component({
  selector: 'erp-tax-calculator-dialog',
  imports: [ReactiveFormsModule, TranslatePipe, NzAlertModule, NzButtonModule, NzInputModule, NzModalModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <nz-modal [nzVisible]="taxCode() !== null" [nzTitle]="'md.taxCalc.title' | translate: { code: taxCode()?.code }"
      [nzFooter]="null" nzWidth="560px" (nzOnCancel)="closed.emit()">
      <ng-container *nzModalContent>
        <form class="calc" [formGroup]="form" (ngSubmit)="calculate()" data-testid="md-tax-calc-form">
          <p class="hint">{{ 'md.taxCalc.hint' | translate }}</p>
          @if (error()) { <nz-alert nzType="error" nzShowIcon data-testid="md-tax-calc-error" [nzMessage]="error()! | translate" /> }
          <label for="tax-calc-direction">{{ 'md.taxCalc.direction' | translate }}</label>
          <select id="tax-calc-direction" data-testid="md-tax-calc-direction" formControlName="direction">
            @for (direction of directions(); track direction) { <option [value]="direction">{{ 'md.taxCode.type' + direction | translate }}</option> }
          </select>
          <label for="tax-calc-date">{{ 'md.taxCalc.date' | translate }}</label>
          <input nz-input id="tax-calc-date" type="date" data-testid="md-tax-calc-date" formControlName="date" />
          @if (form.controls.date.touched && form.controls.date.invalid) { <p role="alert">{{ 'md.taxCalc.invalidDate' | translate }}</p> }
          <label for="tax-calc-currency">{{ 'md.taxCalc.currency' | translate }}</label>
          <input nz-input id="tax-calc-currency" data-testid="md-tax-calc-currency" formControlName="currencyCode" maxlength="3" />
          @if (form.controls.currencyCode.touched && form.controls.currencyCode.invalid) { <p role="alert">{{ 'md.taxCalc.invalidCurrency' | translate }}</p> }
          <label for="tax-calc-amount">{{ 'md.taxCalc.amount' | translate }}</label>
          <input nz-input id="tax-calc-amount" data-testid="md-tax-calc-amount" formControlName="amount" inputmode="decimal" maxlength="17" />
          @if (form.controls.amount.touched && form.controls.amount.invalid) { <p role="alert" data-testid="md-tax-calc-amount-error">{{ 'md.taxCalc.invalidAmount' | translate }}</p> }
          <label><input type="checkbox" formControlName="priceIncludesTax" data-testid="md-tax-calc-includes" />{{ 'md.taxCalc.priceIncludesTax' | translate }}</label>
          <button nz-button nzType="primary" type="submit" data-testid="md-tax-calc-submit" [nzLoading]="loading()">{{ 'md.taxCalc.calculate' | translate }}</button>
          @if (result(); as r) {
            <dl class="result" data-testid="md-tax-calc-result">
              <dt>{{ 'md.taxCalc.ratePercent' | translate }}</dt><dd data-testid="md-tax-calc-rate">{{ r.ratePercent }}%</dd>
              <dt>{{ 'md.taxCalc.net' | translate }}</dt><dd data-testid="md-tax-calc-net">{{ amount(r.net) }}</dd>
              <dt>{{ 'md.taxCalc.tax' | translate }}</dt><dd data-testid="md-tax-calc-tax">{{ amount(r.tax) }}</dd>
              <dt>{{ 'md.taxCalc.gross' | translate }}</dt><dd data-testid="md-tax-calc-gross">{{ amount(r.gross) }}</dd>
            </dl>
          }
        </form>
      </ng-container>
    </nz-modal>
  `,
  styles: `
    .calc { display: grid; gap: 8px; color: var(--erp-color-text); }
    .hint { margin: 0; color: var(--erp-color-text-secondary); }
    .calc p[role="alert"] { color: var(--erp-status-error); margin: 0; }
    .result { display: grid; grid-template-columns: max-content 1fr; gap: 4px 16px; margin: 8px 0 0; }
    .result dt { color: var(--erp-color-text-secondary); } .result dd { margin: 0; font-variant-numeric: tabular-nums; }
  `,
})
export class TaxCalculatorDialog {
  readonly taxCode = input<TaxCode | null>(null);
  readonly closed = output<void>();
  private readonly api = inject(TaxService);
  private readonly request = latestRequest();
  private readonly language = inject(LanguageService);
  readonly directions = signal<TaxDirection[]>(['Output', 'Input']);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly result = signal<TaxCalculation | null>(null);
  readonly form = new FormGroup({
    direction: new FormControl<TaxDirection>('Output', { nonNullable: true }),
    date: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    currencyCode: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.pattern(/^\s*[A-Za-z]{3}\s*$/)] }),
    amount: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.pattern(TAX_AMOUNT_PATTERN)] }),
    priceIncludesTax: new FormControl(false, { nonNullable: true }),
  });

  constructor() {
    effect(() => {
      const taxCode = this.taxCode();
      untracked(() => {
        const directions: TaxDirection[] = taxCode?.taxType === 'Input' ? ['Input']
          : taxCode?.taxType === 'Output' ? ['Output'] : ['Output', 'Input'];
        this.request.cancel(); this.directions.set(directions); this.result.set(null); this.error.set(null); this.loading.set(false);
        this.form.reset({ direction: directions[0], date: isoDate(new Date()), currencyCode: this.form.controls.currencyCode.value,
          amount: '', priceIncludesTax: false });
      });
    });
    // 輸入改變後清除舊結果，避免誤讀。
    this.form.valueChanges.subscribe(() => this.result.set(null));
  }

  /** 依介面語言加千分位，並按後端回傳的幣別小數位補零（MD-F12）。 */
  private readonly amountFormat = computed(() => {
    const digits = this.result()?.decimalPlaces ?? 0;
    return new Intl.NumberFormat(this.language.current(), { minimumFractionDigits: digits, maximumFractionDigits: digits });
  });
  amount(value: number): string { return this.amountFormat().format(value); }

  calculate(): void {
    const taxCode = this.taxCode();
    if (!taxCode) return;
    this.form.markAllAsTouched(); if (this.form.invalid) return;
    const draft = this.form.getRawValue();
    this.loading.set(true); this.error.set(null);
    this.request.run(this.api.calculate({ taxCode: taxCode.code, direction: draft.direction, date: draft.date,
      amount: Number(draft.amount), priceIncludesTax: draft.priceIncludesTax, currencyCode: draft.currencyCode }), {
      next: (result) => { this.loading.set(false); this.result.set(result); },
      error: (error: unknown) => { this.loading.set(false); this.result.set(null); this.error.set(this.api.errorKey(error)); },
    });
  }
}
