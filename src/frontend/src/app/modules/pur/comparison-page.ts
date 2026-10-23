import { ChangeDetectionStrategy, Component, DestroyRef, effect, inject, signal, untracked } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { CompanyContextService } from '../../core/company/company-context.service';
import { HasPermissionDirective } from '../../core/auth/has-permission.directive';
import { apiErrorTraceId } from '../../core/http/api-error';
import { ApiErrorDisplay } from '../../shared/api-error-display';
import { ComparisonOffer, QuotationComparison, RfqService } from './rfq.service';

/** Interface 4.15: live, read-only costs from the authorized comparison endpoint. */
@Component({
  selector: 'erp-comparison-page',
  imports: [RouterLink, TranslatePipe, NzButtonModule, NzTagModule, HasPermissionDirective, ApiErrorDisplay],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="comparison" data-testid="pur-comparison">
      <header>
        <div><h1>{{ 'pur.comparison.title' | translate }} · {{ rfqId }}</h1>
          <p>{{ 'pur.comparison.live' | translate }}</p></div>
        <nav>
          <a nz-button routerLink="/pur/rfqs" *erpHasPermission="'PUR.RFQ.VIEW'">{{ 'pur.comparison.back' | translate }}</a>
          <button nz-button type="button" (click)="load()" [disabled]="loading()" data-testid="pur-comparison-refresh">
            {{ 'pur.comparison.refresh' | translate }}</button>
        </nav>
      </header>
      @if (loading()) { <p role="status">{{ 'pur.comparison.loading' | translate }}</p> }
      @if (error(); as key) { <erp-api-error-display [messageKey]="key" [traceId]="traceId()"
        data-testid="pur-comparison-error" /> }
      @if (data(); as comparison) {
        <p class="meta">{{ 'pur.comparison.asOf' | translate }}: {{ comparison.comparisonDate }} ·
          {{ 'pur.comparison.baseCurrency' | translate }}: {{ comparison.baseCurrencyCode }}</p>
        @if (comparison.lines.length === 0) { <p class="empty" data-testid="pur-comparison-no-lines">
          {{ 'pur.comparison.noLines' | translate }}</p> }
        @for (line of comparison.lines; track line.rfqLineId) {
          <section class="line" [attr.data-testid]="'pur-comparison-line-' + line.lineNo">
            <h2>{{ 'pur.comparison.line' | translate }} {{ line.lineNo }} ·
              {{ 'pur.comparison.quantity' | translate }} {{ formatNumber(line.quantity, 4) }}</h2>
            @if (line.offers.length === 0) { <p class="empty" data-testid="pur-comparison-no-offers">
              {{ 'pur.comparison.noOffers' | translate }}</p> }
            @else { <div class="table-scroll"><table>
              <thead><tr><th scope="col">{{ 'pur.comparison.supplier' | translate }}</th>
                <th scope="col">{{ 'pur.comparison.status' | translate }}</th>
                <th scope="col" class="number">{{ 'pur.comparison.unitPrice' | translate }}</th>
                <th scope="col" class="number">{{ 'pur.comparison.gross' | translate }}</th>
                <th scope="col">{{ 'pur.comparison.exchange' | translate }}</th>
                <th scope="col">{{ 'pur.comparison.tax' | translate }}</th>
                <th scope="col" class="number">{{ 'pur.comparison.net' | translate }}</th>
                <th scope="col" class="number">{{ 'pur.comparison.taxAmount' | translate }}</th>
                <th scope="col">{{ 'pur.comparison.eligibility' | translate }}</th></tr></thead>
              <tbody>@for (offer of line.offers; track offer.quotationId) {
                <tr [attr.data-testid]="'pur-comparison-offer-' + offer.quotationId">
                  <td>{{ offer.supplierId }} · #{{ offer.quotationId }}</td>
                  <td>{{ statusKey(offer) | translate }}</td>
                  <td class="number">{{ formatNumber(offer.unitPrice, 6) }} {{ offer.currencyCode }}</td>
                  <td class="number">{{ offer.baseGross === null ? '—' : formatNumber(offer.baseGross, 4) }}
                    @if (offer.baseGross !== null) { {{ comparison.baseCurrencyCode }} }</td>
                  <td>{{ offer.exchangeRate === null ? '—' : formatNumber(offer.exchangeRate, 8) }}
                    @if (offer.exchangeRateFrom) { <small>{{ 'pur.comparison.from' | translate }} {{ offer.exchangeRateFrom }}
                      @if (offer.exchangeRateTo) { – {{ offer.exchangeRateTo }} }
                      · {{ 'pur.comparison.source' | translate }} #{{ offer.exchangeRateId ?? '—' }}</small> }
                  </td>
                  <td>{{ offer.taxRatePercent === null ? '—' : formatNumber(offer.taxRatePercent, 4) + '%' }}
                    @if (offer.taxRateFrom) { <small>{{ 'pur.comparison.from' | translate }} {{ offer.taxRateFrom }}
                      @if (offer.taxRateTo) { – {{ offer.taxRateTo }} }
                      · #{{ offer.taxRateId }}</small> }
                  </td>
                  <td class="number">{{ offer.baseNet === null ? '—' : formatNumber(offer.baseNet, 4) }}</td>
                  <td class="number">{{ offer.baseTax === null ? '—' : formatNumber(offer.baseTax, 4) }}</td>
                  <td><nz-tag [nzColor]="offer.isComparable ? 'success' : 'warning'">
                    {{ (offer.isComparable ? 'pur.comparison.comparable' : 'pur.comparison.unavailable') | translate }}</nz-tag>
                    @if (!offer.isComparable) { <span class="reason">{{ reasonKey(offer.unavailableReason) | translate }}</span> }</td>
                </tr> }</tbody>
            </table></div> }
          </section>
        }
      }
    </section>`,
  styles: `.comparison{display:grid;gap:16px;color:var(--erp-color-text)}header,nav{display:flex;gap:12px;align-items:center;justify-content:space-between;flex-wrap:wrap}h1{font-size:20px;margin:0;text-wrap:balance}h2{font-size:16px;margin:0 0 12px}p{margin:4px 0}.meta,.empty{padding:12px;background:var(--erp-color-bg-container);border:1px solid var(--erp-color-border);border-radius:4px}.line{min-width:0;padding:16px;border:1px solid var(--erp-color-border);border-radius:4px}.table-scroll{overflow-x:auto}table{width:100%;border-collapse:collapse;min-width:1200px}th,td{padding:8px;text-align:left;vertical-align:top;border-bottom:1px solid var(--erp-color-border)}th{white-space:nowrap}th:last-child,td:last-child{position:sticky;right:0;min-width:160px;width:160px;max-width:160px;background:var(--erp-color-bg-container);box-shadow:-4px 0 8px -7px var(--erp-color-text)}td.number,th.number{text-align:right;font-variant-numeric:tabular-nums}td small{display:block;white-space:nowrap;color:var(--erp-color-text-secondary)}.reason{display:block;overflow-wrap:anywhere}button:focus-visible,a:focus-visible{outline:2px solid var(--erp-color-primary);outline-offset:2px}`,
})
export class ComparisonPage {
  private readonly api = inject(RfqService);
  private readonly route = inject(ActivatedRoute);
  private readonly company = inject(CompanyContextService);
  private readonly translate = inject(TranslateService);
  private readonly destroyRef = inject(DestroyRef);
  readonly rfqId = Number(this.route.snapshot.paramMap.get('id'));
  readonly data = signal<QuotationComparison | null>(null);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly traceId = signal<string | null>(null);
  private generation = 0;
  constructor() {
    this.destroyRef.onDestroy(() => ++this.generation);
    effect(() => { this.company.current(); untracked(() => this.load()); });
  }
  load(): void {
    if (this.loading()) ++this.generation;
    const current = ++this.generation;
    this.data.set(null); this.error.set(null); this.traceId.set(null); this.loading.set(true);
    this.api.compare(this.rfqId).subscribe({
      next: result => { if (current !== this.generation) return;
        this.data.set(result); this.loading.set(false); },
      error: error => { if (current !== this.generation) return;
        this.error.set(this.api.errorKey(error)); this.traceId.set(apiErrorTraceId(error)); this.loading.set(false); }
    });
  }
  statusKey(offer: ComparisonOffer): string { return 'pur.quotation.' + offer.status.toLowerCase(); }
  formatNumber(value: number, maximumFractionDigits: number): string {
    const language = this.translate.currentLang() || 'zh-Hant';
    const locale = language === 'zh-Hans' ? 'zh-CN' : language === 'en' ? 'en-US' : 'zh-TW';
    return new Intl.NumberFormat(locale, { maximumFractionDigits }).format(value);
  }
  reasonKey(reason: string | null): string {
    const keys: Record<string, string> = {
      'PUR.QUOTATION.WITHDRAWN': 'withdrawn', 'PUR.QUOTATION.EXPIRED': 'expired',
      'PUR.QUOTATION.NOT_YET_VALID': 'notYetValid',
      'PUR.QUOTATION.AMOUNT_OVERFLOW': 'amountOverflow',
      'MD.CURRENCY.NOT_FOUND': 'currencyMissing', 'MD.CURRENCY.INACTIVE': 'currencyMissing',
      'MD.EXCHANGE_RATE.NOT_FOUND': 'rateMissing',
      'MD.TAX_RATE.NOT_FOUND': 'taxMissing', 'MD.TAX_RATE.INACTIVE': 'taxMissing',
      'MD.TAX_RATE.DIRECTION_MISMATCH': 'taxMissing'
    };
    return 'pur.comparison.' + (keys[reason ?? ''] ?? 'unknownReason');
  }
}
