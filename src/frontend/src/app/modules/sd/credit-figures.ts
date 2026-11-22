import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { SalesCreditCheck } from './order.service';

/** Formats a credit amount for display; missing values show an em dash. */
export function creditMoney(value: number | null | undefined): string {
  return value == null ? '—' : value.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 4 });
}

/**
 * Advisory credit figures (ADR-170) shared by the 2.4 submit area and the 2.5/2.6 saved credit panel (ADR-174):
 * evaluation date, base currency, status tag, limit, posted open AR, other approved orders, this order, remaining credit.
 */
@Component({
  selector: 'erp-sales-credit-figures',
  imports: [TranslatePipe, NzTagModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p><strong>{{ 'sd.order.submit.creditTitle' | translate: { date: credit().evaluationDate, currency: credit().baseCurrency ?? '' } }}</strong>
      <nz-tag [nzColor]="credit().status === 'WithinLimit' ? 'green' : 'red'" data-testid="sd-order-credit-status">{{ ('sd.order.submit.status' + credit().status) | translate }}</nz-tag></p>
    <dl class="figures">
      <dt>{{ 'sd.order.submit.limit' | translate }}</dt><dd data-testid="sd-order-credit-limit">{{ money(credit().limit) }}</dd>
      <dt>{{ 'sd.order.submit.postedOpenAr' | translate }}</dt><dd>{{ money(credit().postedOpenAr) }}</dd>
      <dt>{{ 'sd.order.submit.otherCommitments' | translate }}</dt><dd>{{ money(credit().otherCommitments) }}</dd>
      <dt>{{ 'sd.order.submit.candidate' | translate }}</dt><dd data-testid="sd-order-credit-candidate">{{ money(credit().candidate) }}</dd>
      <dt>{{ 'sd.order.submit.headroom' | translate }}</dt><dd data-testid="sd-order-credit-headroom">{{ money(credit().headroom) }}</dd>
    </dl>`,
  styles: `:host{display:grid;gap:8px}p{margin:0}.figures{display:grid;max-width:480px;grid-template-columns:max-content auto;gap:4px 16px;margin:0}.figures dd{margin:0;text-align:right;font-variant-numeric:tabular-nums}`,
})
export class SalesCreditFigures {
  readonly credit = input.required<SalesCreditCheck>();
  readonly money = creditMoney;
}
