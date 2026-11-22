import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { Subscription, TimeoutError } from 'rxjs';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { PermissionService } from '../../core/auth/permission.service';
import { UNKNOWN_ERROR_KEY, apiErrorKey, apiErrorTraceId } from '../../core/http/api-error';
import { ApiErrorDisplay } from '../../shared/api-error-display';
import { SalesCreditFigures, creditMoney } from './credit-figures';
import { CREDIT_BLOCKED_CODES, ORDER_SUBMIT_ERROR_CODES } from './order-submit-panel';
import { SalesCreditCheck, SalesOrderService } from './order.service';

/**
 * Read-only credit panel for a just-saved order on Sales Order Create 2.5 / Edit 2.6 (WU-SD-006c3a, REQ_IO_A2.5.21,
 * REQ_IO_A2.6.18, ADR-174). It previews the saved version as the candidate order; nothing is submitted or saved, and the
 * figures are never presented as an approval. Submitting (with any override reason) stays on the 2.4 detail.
 */
@Component({
  selector: 'erp-sales-order-credit-panel',
  imports: [TranslatePipe, NzAlertModule, NzButtonModule, ApiErrorDisplay, SalesCreditFigures],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="panel" aria-labelledby="sd-order-saved-credit-title" aria-live="polite" data-testid="sd-order-saved-credit">
      <h2 id="sd-order-saved-credit-title">{{ 'sd.order.credit.title' | translate: { number: documentNo() } }}</h2>
      @if (loading()) { <p role="status">{{ 'sd.order.submit.checking' | translate }}</p> }
      @if (failure(); as problem) {
        <erp-api-error-display [messageKey]="problem.key" [traceId]="problem.traceId" data-testid="sd-order-saved-credit-error" />
        <p class="hint" data-testid="sd-order-saved-credit-error-hint">{{ (blocked() ? 'sd.order.credit.blockedHint' : 'sd.order.credit.failedHint') | translate }}</p> }
      @if (credit(); as value) {
        <erp-sales-credit-figures [credit]="value" />
        @if (value.status === 'OverLimit') { <nz-alert nzType="warning" nzShowIcon data-testid="sd-order-saved-credit-excess"
          [nzMessage]="'sd.order.submit.excess' | translate: { amount: money(value.excess), currency: value.baseCurrency ?? '' }" />
          @if (canSubmit()) { <p class="hint" data-testid="sd-order-saved-credit-override-hint">{{ (canOverride() ? 'sd.order.credit.overrideHint' : 'sd.order.submit.needOverride') | translate }}</p> } }
        <p class="hint" data-testid="sd-order-saved-credit-notice">{{ 'sd.order.credit.notice' | translate }}</p> }
      <div><button nz-button type="button" (click)="check()" [disabled]="loading()" data-testid="sd-order-saved-credit-recheck">{{ 'sd.order.submit.recheck' | translate }}</button></div>
    </section>`,
  styles: `.panel{border:1px solid var(--erp-color-border);border-radius:4px;padding:12px;display:grid;gap:8px;min-width:0}h2{margin:0;font-size:16px}.hint{color:var(--erp-color-text-secondary);margin:0;overflow-wrap:anywhere}p{margin:0}button:focus-visible{outline:2px solid var(--erp-color-primary);outline-offset:2px}`,
})
export class SalesOrderCreditPanel {
  private readonly api = inject(SalesOrderService);
  private readonly permissions = inject(PermissionService);
  readonly orderId = input.required<number>();
  readonly customerId = input.required<number>();
  readonly documentNo = input<string>('');
  readonly credit = signal<SalesCreditCheck | null>(null);
  readonly loading = signal(false);
  readonly failure = signal<{ key: string; traceId: string | null } | null>(null);
  readonly blocked = signal(false);
  readonly canSubmit = computed(() => this.permissions.has('SAL.ORDER.SUBMIT'));
  readonly canOverride = computed(() => this.permissions.has('SAL.CREDIT.OVERRIDE'));
  readonly money = creditMoney;
  private request?: Subscription;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.request?.unsubscribe());
    // Every newly saved order (id or customer) gets a fresh preview; the previous request is dropped.
    effect(() => { this.orderId(); this.customerId(); untracked(() => this.check()); });
  }
  check(): void {
    this.request?.unsubscribe();
    this.credit.set(null); this.failure.set(null); this.blocked.set(false);
    const customerId = this.customerId(), orderId = this.orderId();
    // A receipt without valid ids cannot be previewed; say so instead of throwing (the order itself is saved).
    if (!Number.isSafeInteger(customerId) || customerId <= 0 || !Number.isSafeInteger(orderId) || orderId <= 0) {
      this.failure.set({ key: UNKNOWN_ERROR_KEY, traceId: null }); return;
    }
    this.loading.set(true);
    this.request = this.api.creditCheck(customerId, orderId).subscribe({
      next: credit => { this.loading.set(false); this.credit.set(credit); },
      error: failure => {
        this.loading.set(false);
        const key = failure instanceof TimeoutError ? 'sd.order.submit.checkTimeout' : apiErrorKey(failure, ORDER_SUBMIT_ERROR_CODES);
        this.failure.set({ key, traceId: apiErrorTraceId(failure) });
        this.blocked.set(CREDIT_BLOCKED_CODES.some(code => key === `errors.${code}`));
      },
    });
  }
}
