import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, signal, untracked } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { forkJoin } from 'rxjs';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { CompanyContextService } from '../../core/company/company-context.service';
import { PermissionService } from '../../core/auth/permission.service';
import { ErpDialogService } from '../../shared/dialogs/dialog.service';
import { HasPermissionDirective } from '../../core/auth/has-permission.directive';
import { apiErrorTraceId, apiValidationDetails } from '../../core/http/api-error';
import { ApiErrorDisplay } from '../../shared/api-error-display';
import { ComparisonOffer, QuotationComparison, RfqAwardContext, RfqConversionGroup, RfqService,
  conversionGroups } from './rfq.service';

/** Interface 4.15: live costs and permanent, authorized line award snapshots. */
@Component({
  selector: 'erp-comparison-page',
  imports: [FormsModule, RouterLink, TranslatePipe, NzButtonModule, NzTagModule, NzAlertModule,
    HasPermissionDirective, ApiErrorDisplay],
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
        [details]="errorDetails()" data-testid="pur-comparison-error" /> }
      @if (notice()) { <div role="status" aria-live="polite" data-testid="pur-award-notice">
        <nz-alert nzType="success" nzShowIcon [nzMessage]="notice()! | translate: noticeParams()" /></div> }
      @if (data(); as comparison) {
        @if (context(); as awardContext) {
          <p class="meta">{{ awardContext.documentNo }} · {{ 'pur.comparison.version' | translate }} {{ awardContext.version }}
            · {{ ('pur.rfq.' + awardContext.status.toLowerCase()) | translate }}</p>
        }
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
                <th scope="col">{{ 'pur.comparison.eligibility' | translate }} / {{ 'pur.comparison.awardAction' | translate }}</th></tr></thead>
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
                    @if (!offer.isComparable) { <span class="reason">{{ reasonKey(offer.unavailableReason) | translate }}</span> }
                    @if (awardFor(line.rfqLineId); as saved) {
                      @if (saved.quotationId === offer.quotationId) { <nz-tag nzColor="success">{{ 'pur.comparison.awarded' | translate }}</nz-tag> }
                    } @else if (canAward() && context()?.status === 'Sent' && offer.isComparable) {
                      <button nz-button type="button" [disabled]="busy()" (click)="select(line.rfqLineId, offer.quotationId)"
                        [attr.data-testid]="'pur-award-select-' + offer.quotationId">{{ 'pur.comparison.select' | translate }}</button>
                    }
                  </td>
                </tr> }</tbody>
            </table></div> }
            @if (awardFor(line.rfqLineId); as saved) {
              <div class="snapshot" [attr.data-testid]="'pur-award-snapshot-' + line.lineNo">
                <strong>{{ 'pur.comparison.savedAward' | translate }} · #{{ saved.id }}</strong>
                @if (saved.purchaseOrderNo) { <nz-tag nzColor="processing" [attr.data-testid]="'pur-award-converted-' + line.lineNo">
                  {{ 'pur.comparison.convertedTo' | translate: { number: saved.purchaseOrderNo } }}</nz-tag> }
                <p>{{ 'pur.comparison.supplier' | translate }} #{{ saved.supplierId }} ·
                  {{ 'pur.comparison.unitPrice' | translate }} {{ formatNumber(saved.unitPrice, 6) }} {{ saved.currencyCode }} ·
                  {{ 'pur.comparison.gross' | translate }} {{ formatNumber(saved.baseGross, 4) }} {{ saved.baseCurrencyCode }}</p>
                <p>{{ 'pur.comparison.reason' | translate }}: {{ saved.reason }}</p>
                <p>{{ 'pur.comparison.savedEvidence' | translate }}: {{ saved.comparisonDate }} ·
                  {{ 'pur.comparison.exchange' | translate }} {{ formatNumber(saved.exchangeRate, 8) }}
                  ({{ saved.exchangeRateFrom }}@if (saved.exchangeRateTo) { – {{ saved.exchangeRateTo }} })</p>
              </div>
            } @else if (canAward() && context()?.status !== 'Sent') {
              <p class="empty">{{ 'pur.comparison.notAwardable' | translate }}</p>
            }
          </section>
        }
      }
      @if (context(); as awardContext) { @if (canConvert()) {
        <section class="convert" data-testid="pur-convert">
          <h2>{{ 'pur.comparison.convertTitle' | translate }}</h2>
          <p class="hint">{{ 'pur.comparison.convertHint' | translate }}</p>
          @if (awardContext.awards.length === 0) {
            <p class="warn" role="status" data-testid="pur-convert-no-awards">{{ 'pur.comparison.noAwards' | translate }}</p>
          } @else if (awardContext.status !== 'Sent') {
            <p class="warn" data-testid="pur-convert-not-open">{{ 'pur.comparison.convertNotOpen' | translate }}</p>
          } @else if (groups().length === 0) {
            <p class="warn" role="status" data-testid="pur-convert-all-done">{{ 'pur.comparison.allConverted' | translate }}</p>
          } @else {
            @for (group of groups(); track group.key; let index = $index) {
              <div class="group" [attr.data-testid]="'pur-convert-group-' + index">
                <strong>{{ 'pur.comparison.supplier' | translate }} #{{ group.supplierId }} · {{ group.currencyCode }} ·
                  {{ 'pur.comparison.exchange' | translate }} {{ formatNumber(group.exchangeRate, 8) }}</strong>
                <ul>@for (award of group.awards; track award.id) {
                  <li><label><input type="checkbox" [attr.name]="'convertAward' + award.id" [checked]="!excluded().has(award.id)" [disabled]="busy()"
                    (change)="toggle(award.id)" [attr.data-testid]="'pur-convert-check-' + award.id">
                    {{ 'pur.comparison.line' | translate }} {{ lineNo(award.rfqLineId) }} · #{{ award.id }} ·
                    {{ formatNumber(award.quantity, 4) }} × {{ formatNumber(award.unitPrice, 6) }} {{ award.currencyCode }} ·
                    {{ formatNumber(award.baseGross, 4) }} {{ award.baseCurrencyCode }}</label></li> }</ul>
                <div class="actions"><button nz-button nzType="primary" type="button"
                  [disabled]="busy() || chosen(group).length === 0" (click)="convert(group)"
                  [attr.data-testid]="'pur-convert-' + index">
                  {{ (busy() ? 'pur.comparison.converting' : 'pur.comparison.convert') | translate: { count: chosen(group).length } }}</button>
                  @if (chosen(group).length === 0) { <small [attr.data-testid]="'pur-convert-none-' + index">
                    {{ 'pur.comparison.chooseOne' | translate }}</small> }</div>
              </div>
            }
          }
          @if (convertedAwards().length > 0) {
            <ul class="converted" data-testid="pur-converted-list">@for (award of convertedAwards(); track award.id) {
              <li [attr.data-testid]="'pur-converted-' + award.id">{{ 'pur.comparison.line' | translate }} {{ lineNo(award.rfqLineId) }} ·
                #{{ award.id }}: {{ 'pur.comparison.convertedTo' | translate: { number: award.purchaseOrderNo } }}</li> }</ul>
          }
        </section>
      } }
      @if (selectedLineId() !== null) {
        <section class="award-form" data-testid="pur-award-form">
          <h2>{{ 'pur.comparison.awardAction' | translate }}</h2>
          <label for="pur-award-reason">{{ 'pur.comparison.reason' | translate }}</label>
          <textarea id="pur-award-reason" name="awardReason" autocomplete="off" maxlength="500"
            [(ngModel)]="reason" data-testid="pur-award-reason"></textarea>
          @if (reasonError()) { <p role="alert" class="reason-error">{{ 'pur.comparison.reasonRequired' | translate }}</p> }
          <div><button nz-button nzType="primary" type="button" [disabled]="busy()" (click)="saveAward()"
            data-testid="pur-award-save">{{ (busy() ? 'pur.comparison.saving' : 'pur.comparison.confirmAward') | translate }}</button>
            <button nz-button type="button" (click)="cancelAward()">{{ 'pur.comparison.cancel' | translate }}</button></div>
        </section>
      }
    </section>`,
  styles: `.comparison{display:grid;gap:16px;color:var(--erp-color-text)}header,nav{display:flex;gap:12px;align-items:center;justify-content:space-between;flex-wrap:wrap}h1{font-size:20px;margin:0;text-wrap:balance}h2{font-size:16px;margin:0 0 12px;text-wrap:balance}p{margin:4px 0}.meta,.empty,.snapshot,.award-form{padding:12px;background:var(--erp-color-bg-container);border:1px solid var(--erp-color-border);border-radius:4px}.line{min-width:0;padding:16px;border:1px solid var(--erp-color-border);border-radius:4px}.snapshot{margin-top:12px;overflow-wrap:anywhere}.award-form{display:grid;gap:8px}.convert{display:grid;gap:8px;padding:16px;border:1px solid var(--erp-color-border);border-radius:4px}.convert h2{margin:0}.convert ul{margin:0;padding-left:0;list-style:none;display:grid;gap:4px}.convert .group{display:grid;gap:8px;padding:12px;background:var(--erp-color-bg-container);border:1px solid var(--erp-color-border);border-radius:4px;overflow-wrap:anywhere}.convert .actions{display:flex;gap:8px;align-items:center;flex-wrap:wrap}.convert .hint,.convert small{color:var(--erp-color-text-secondary)}.convert .warn{padding:8px 12px;border:1px solid var(--erp-color-border);border-left:4px solid var(--erp-color-warning, #faad14);border-radius:4px}.convert input{margin-right:6px}.converted{overflow-wrap:anywhere}.award-form textarea{width:100%;min-height:96px;background:var(--erp-color-bg-container);color:var(--erp-color-text);border:1px solid var(--erp-color-border)}.award-form div{display:flex;gap:8px}.reason-error{color:var(--erp-color-error-text)}.table-scroll{overflow-x:auto}table{width:100%;border-collapse:collapse;min-width:1200px}th,td{padding:8px;text-align:left;vertical-align:top;border-bottom:1px solid var(--erp-color-border)}th{white-space:nowrap}th:last-child,td:last-child{position:sticky;right:0;min-width:180px;width:180px;max-width:180px;background:var(--erp-color-bg-container);box-shadow:-4px 0 8px -7px var(--erp-color-text)}td.number,th.number{text-align:right;font-variant-numeric:tabular-nums}td small{display:block;white-space:nowrap;color:var(--erp-color-text-secondary)}.reason{display:block;overflow-wrap:anywhere}button:focus-visible,a:focus-visible,textarea:focus-visible,input:focus-visible{outline:2px solid var(--erp-color-primary);outline-offset:2px}`,
})
export class ComparisonPage {
  private readonly api = inject(RfqService);
  private readonly route = inject(ActivatedRoute);
  private readonly company = inject(CompanyContextService);
  private readonly permissions = inject(PermissionService);
  private readonly dialogs = inject(ErpDialogService);
  private readonly translate = inject(TranslateService);
  private readonly destroyRef = inject(DestroyRef);
  readonly rfqId = Number(this.route.snapshot.paramMap.get('id'));
  readonly data = signal<QuotationComparison | null>(null);
  readonly context = signal<RfqAwardContext | null>(null);
  readonly loading = signal(false);
  readonly busy = signal(false);
  readonly notice = signal<string | null>(null);
  readonly noticeParams = signal<Record<string, unknown>>({});
  /** Awards the user unticked; every unconverted award starts selected. */
  readonly excluded = signal<ReadonlySet<number>>(new Set());
  readonly groups = computed(() => conversionGroups(this.context()?.awards ?? []));
  readonly convertedAwards = computed(() => (this.context()?.awards ?? []).filter(x => x.purchaseOrderId != null));
  readonly selectedLineId = signal<number | null>(null);
  readonly selectedQuotationId = signal<number | null>(null);
  readonly reasonError = signal(false);
  reason = '';
  readonly error = signal<string | null>(null);
  readonly traceId = signal<string | null>(null);
  readonly errorDetails = signal<string[]>([]);
  private generation = 0;
  private pendingConversion: { number: string; count: number; awardIds: number[]; orderId: number } | null = null;
  constructor() {
    this.destroyRef.onDestroy(() => ++this.generation);
    effect(() => { this.company.current(); untracked(() => this.load()); });
  }
  load(preserveError = false, preserveNotice = false): void {
    if (this.loading()) ++this.generation;
    const current = ++this.generation;
    this.data.set(null); this.context.set(null); this.cancelAward(); this.busy.set(false); this.excluded.set(new Set());
    if (!preserveError) { this.error.set(null); this.traceId.set(null); this.errorDetails.set([]); }
    if (!preserveNotice) { this.notice.set(null); this.noticeParams.set({}); this.pendingConversion = null; }
    this.loading.set(true);
    forkJoin({ comparison: this.api.compare(this.rfqId), context: this.api.awardContext(this.rfqId) }).subscribe({
      next: result => { if (current !== this.generation) return;
        this.data.set(result.comparison); this.context.set(result.context); this.loading.set(false);
        const pending = this.pendingConversion;
        if (pending) {
          this.pendingConversion = null;
          const saved = pending.awardIds.every(id => result.context.awards.some(award =>
            award.id === id && award.purchaseOrderId === pending.orderId && award.purchaseOrderNo === pending.number));
          if (saved) { this.notice.set('pur.comparison.converted');
            this.noticeParams.set({ number: pending.number, count: pending.count }); }
          else { this.notice.set(null); this.error.set('pur.comparison.verifyFailed'); }
        }
      },
      error: error => { if (current !== this.generation) return;
        this.pendingConversion = null; this.notice.set(null);
        this.error.set(this.api.errorKey(error)); this.traceId.set(apiErrorTraceId(error));
        this.errorDetails.set(apiValidationDetails(error)); this.loading.set(false); }
    });
  }
  canAward(): boolean { return this.permissions.has('PUR.RFQ.AWARD'); }
  awardFor(lineId: number) { return this.context()?.awards.find(x => x.rfqLineId === lineId); }
  select(lineId: number, quotationId: number): void {
    if (!this.canAward() || this.context()?.status !== 'Sent' || this.awardFor(lineId)) return;
    this.selectedLineId.set(lineId); this.selectedQuotationId.set(quotationId);
    this.reason = ''; this.reasonError.set(false); this.error.set(null); this.errorDetails.set([]); this.notice.set(null);
  }
  cancelAward(): void { this.selectedLineId.set(null); this.selectedQuotationId.set(null);
    this.reason = ''; this.reasonError.set(false); }
  saveAward(): void {
    const lineId = this.selectedLineId(); const quotationId = this.selectedQuotationId();
    const context = this.context(); const comparison = this.data();
    if (this.busy() || !this.canAward() || !context || !comparison || context.status !== 'Sent' ||
      lineId === null || quotationId === null || this.awardFor(lineId) ||
      !comparison.lines.find(x => x.rfqLineId === lineId)?.offers.some(x => x.quotationId === quotationId && x.isComparable)) return;
    const reason = this.reason.trim();
    if (reason.length < 1 || reason.length > 500) { this.reasonError.set(true);
      document.getElementById('pur-award-reason')?.focus(); return; }
    this.reasonError.set(false);
    this.dialogs.confirm({ titleKey: 'pur.comparison.confirmAward', contentKey: 'pur.comparison.awardExplanation',
      okKey: 'pur.comparison.confirmAward' }).subscribe(confirmed => {
      if (!confirmed || this.context() !== context || this.busy()) return;
      const current = this.generation; this.busy.set(true); this.error.set(null); this.errorDetails.set([]);
      this.api.award(this.rfqId, { version: context.version, rfqLineId: lineId, quotationId, reason }).subscribe({
        next: () => { if (current !== this.generation) return;
          this.notice.set('pur.comparison.awardSaved'); this.noticeParams.set({}); this.load(false, true); },
        error: error => { if (current !== this.generation) return;
          this.busy.set(false); this.error.set(this.api.errorKey(error)); this.traceId.set(apiErrorTraceId(error));
          this.errorDetails.set(apiValidationDetails(error));
          if (error.status === 409 || error.status === 422 || error.status === 404) this.load(true); }
      });
    });
  }
  canConvert(): boolean { return this.permissions.has('PUR.RFQ.CONVERT'); }
  lineNo(rfqLineId: number): number | string {
    return this.data()?.lines.find(x => x.rfqLineId === rfqLineId)?.lineNo ?? '—';
  }
  chosen(group: RfqConversionGroup): number[] {
    const excluded = this.excluded();
    return group.awards.map(x => x.id).filter(id => !excluded.has(id));
  }
  toggle(awardId: number): void {
    const next = new Set(this.excluded());
    if (!next.delete(awardId)) next.add(awardId);
    this.excluded.set(next);
  }
  convert(group: RfqConversionGroup): void {
    const context = this.context(); const awardIds = this.chosen(group);
    if (this.busy() || !this.canConvert() || !context || context.status !== 'Sent' || awardIds.length === 0) return;
    this.dialogs.confirm({ titleKey: 'pur.comparison.confirmConvert', contentKey: 'pur.comparison.convertExplanation',
      params: { count: awardIds.length, supplier: group.supplierId, currency: group.currencyCode },
      okKey: 'pur.comparison.confirmConvert' }).subscribe(confirmed => {
      if (!confirmed || this.context() !== context || this.busy()) return;
      const current = this.generation; this.busy.set(true);
      this.error.set(null); this.traceId.set(null); this.errorDetails.set([]); this.notice.set(null);
      this.api.convert(this.rfqId, { version: context.version, supplierId: group.supplierId, awardIds }).subscribe({
        next: result => { if (current !== this.generation) return;
          if (result.lineCount !== awardIds.length || result.awardIds.length !== awardIds.length ||
            !awardIds.every(id => result.awardIds.includes(id)) || !result.orderId || !result.documentNo) {
            this.busy.set(false); this.error.set('pur.comparison.verifyFailed'); return;
          }
          this.pendingConversion = { number: result.documentNo, count: result.lineCount,
            awardIds, orderId: result.orderId };
          this.load(false, true); },
        error: error => { if (current !== this.generation) return;
          this.busy.set(false); this.error.set(this.api.errorKey(error)); this.traceId.set(apiErrorTraceId(error));
          this.errorDetails.set(apiValidationDetails(error));
          // Re-read so each line shows whether it is already converted and into which order.
          if (error.status === 409 || error.status === 422 || error.status === 404) this.load(true); }
      });
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
