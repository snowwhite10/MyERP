import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, Injector, afterNextRender, computed, effect, inject, untracked, input, output, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { Subscription, TimeoutError } from 'rxjs';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { PermissionService } from '../../core/auth/permission.service';
import { apiErrorKey, apiErrorTraceId } from '../../core/http/api-error';
import { ApiErrorDisplay } from '../../shared/api-error-display';
import { SalesCreditCheck, SalesOrderDetail, SalesOrderService, SalesOrderSubmitResult } from './order.service';

/** Credit refusals that no override can pass (BR-SD-033); submit stays disabled until a recheck succeeds. */
export const CREDIT_BLOCKED_CODES = ['SAL.CREDIT.UNAVAILABLE', 'SAL.CREDIT.LIMIT_NOT_SET', 'SAL.CREDIT.INVALID_SNAPSHOT',
  'SAL.CREDIT.INVALID_AMOUNT', 'SAL.CREDIT.CUSTOMER_INACTIVE', 'SAL.ORDER.BASE_CURRENCY_CHANGED'];
/** Codes a credit preview or submit refusal is explained with; anything else is the generic message (+ trace ID). */
export const ORDER_SUBMIT_ERROR_CODES = [...CREDIT_BLOCKED_CODES, 'SAL.CREDIT.LIMIT_EXCEEDED',
  'SAL.CREDIT.OVERRIDE_FORBIDDEN', 'SAL.ORDER.NOT_FOUND', 'FI.CREDIT.LOCK_TIMEOUT', 'MD.CUSTOMER.NOT_FOUND',
  'COMMON.PERMISSION.DENIED'];
export const CREDIT_OVERRIDE_REASON_MAX = 200;

/** Measured excess the server refused with (422 SAL.CREDIT.LIMIT_EXCEEDED extensions `excess`, `currency`). */
function refusedExcess(failure: unknown): { excess: number; currency: string | null } | null {
  if (!(failure instanceof HttpErrorResponse) || failure.status !== 422) return null;
  const body = failure.error as { errorCode?: unknown; excess?: unknown; currency?: unknown } | null;
  if (body?.errorCode !== 'SAL.CREDIT.LIMIT_EXCEEDED') return null;
  const excess = Number(body.excess);
  return { excess: Number.isFinite(excess) ? excess : 0, currency: typeof body.currency === 'string' ? body.currency : null };
}

/**
 * Sales Order View Interface 2.4 submit area (WU-SD-006c1, REQ_IO_A2.4.10–15, ADR-172): advisory credit preview for the
 * saved order (ADR-170), a reason input when the order is over the limit and the user holds SAL.CREDIT.OVERRIDE, and
 * the versioned submit (ADR-143/169). Only a reply for this order with a newer version and Pending/Approved status is
 * success; uncertain outcomes pause until the saved order is reloaded.
 */
@Component({
  selector: 'erp-sales-order-submit-panel',
  imports: [FormsModule, TranslatePipe, NzAlertModule, NzButtonModule, NzTagModule, ApiErrorDisplay],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (!opened()) {
      <button nz-button nzType="primary" type="button" (click)="start()" data-testid="sd-order-submit">{{ 'sd.order.submit.action' | translate }}</button>
    } @else { <section class="panel" aria-labelledby="sd-order-submit-title" data-testid="sd-order-submit-panel">
      <h3 id="sd-order-submit-title">{{ 'sd.order.submit.title' | translate: { number: order().documentNo } }}</h3>
      <p class="hint">{{ 'sd.order.submit.intro' | translate }}</p>
      <div class="credit" aria-live="polite" data-testid="sd-order-credit">
        @if (previewLoading()) { <p role="status">{{ 'sd.order.submit.checking' | translate }}</p> }
        @if (previewError(); as failure) {
          <erp-api-error-display [messageKey]="failure.key" [traceId]="failure.traceId" data-testid="sd-order-credit-error" />
          <p class="hint">{{ (blockedKey() ? 'sd.order.submit.blockedHint' : 'sd.order.submit.previewFailedHint') | translate }}</p> }
        @if (preview(); as credit) {
          <p><strong>{{ 'sd.order.submit.creditTitle' | translate: { date: credit.evaluationDate, currency: credit.baseCurrency ?? '' } }}</strong>
            <nz-tag [nzColor]="credit.status === 'WithinLimit' ? 'green' : 'red'" data-testid="sd-order-credit-status">{{ ('sd.order.submit.status' + credit.status) | translate }}</nz-tag></p>
          <dl class="figures">
            <dt>{{ 'sd.order.submit.limit' | translate }}</dt><dd data-testid="sd-order-credit-limit">{{ money(credit.limit) }}</dd>
            <dt>{{ 'sd.order.submit.postedOpenAr' | translate }}</dt><dd>{{ money(credit.postedOpenAr) }}</dd>
            <dt>{{ 'sd.order.submit.otherCommitments' | translate }}</dt><dd>{{ money(credit.otherCommitments) }}</dd>
            <dt>{{ 'sd.order.submit.candidate' | translate }}</dt><dd>{{ money(credit.candidate) }}</dd>
            <dt>{{ 'sd.order.submit.headroom' | translate }}</dt><dd data-testid="sd-order-credit-headroom">{{ money(credit.headroom) }}</dd>
          </dl>
          <p class="hint">{{ 'sd.order.submit.recheckNotice' | translate }}</p> }
        @if (excess(); as over) { <nz-alert nzType="warning" nzShowIcon data-testid="sd-order-credit-excess"
          [nzMessage]="'sd.order.submit.excess' | translate: { amount: money(over.excess), currency: over.currency ?? '' }" /> }
      </div>
      @if (excess()) { @if (canOverride()) {
        <label class="reason" for="sd-order-override-reason">{{ 'sd.order.submit.reason' | translate }} <span class="required">*</span></label>
        <textarea id="sd-order-override-reason" rows="3" [maxlength]="reasonMax" [(ngModel)]="reason" name="creditOverrideReason" autocomplete="off"
          [attr.aria-invalid]="!!reasonError()" aria-describedby="sd-order-override-hint" data-testid="sd-order-override-reason"></textarea>
        <p id="sd-order-override-hint" class="hint">{{ 'sd.order.submit.reasonHint' | translate: { count: reason.length, max: reasonMax } }}</p>
        @if (reasonError()) { <p class="field-error" role="alert" data-testid="sd-order-override-error">{{ reasonError()! | translate }}</p> }
      } @else { <p class="hint" data-testid="sd-order-override-missing">{{ 'sd.order.submit.needOverride' | translate }}</p> } }
      <div class="actions">
        <button nz-button nzType="primary" type="button" (click)="confirm()" [nzLoading]="submitting()" [disabled]="!!disabledKey()"
          aria-describedby="sd-order-submit-hint" data-testid="sd-order-submit-confirm">{{ 'sd.order.submit.confirm' | translate }}</button>
        <button nz-button type="button" (click)="check()" [disabled]="submitting() || previewLoading() || !!stale()" data-testid="sd-order-credit-recheck">{{ 'sd.order.submit.recheck' | translate }}</button>
        <button nz-button type="button" (click)="cancel()" [disabled]="submitting()" data-testid="sd-order-submit-cancel">{{ 'sd.order.submit.cancel' | translate }}</button>
      </div>
      @if (disabledKey()) { <p id="sd-order-submit-hint" class="hint" data-testid="sd-order-submit-hint">{{ disabledKey()! | translate }}</p> }
      @if (stale()) { <nz-alert nzType="warning" nzShowIcon role="status" data-testid="sd-order-submit-stale" [nzMessage]="stale()! | translate" />
        <div class="actions"><button nz-button type="button" (click)="refreshRequested.emit()" data-testid="sd-order-submit-refresh">{{ 'sd.order.submit.refresh' | translate }}</button></div> }
      @if (error(); as failure) { <erp-api-error-display [messageKey]="failure.key" [traceId]="failure.traceId" data-testid="sd-order-submit-error" />
        @if (!stale()) { <p class="hint" role="status" data-testid="sd-order-submit-unchanged">{{ 'sd.order.submit.unchanged' | translate }}</p> } }
    </section> }`,
  styles: `.panel{border:1px solid var(--erp-color-border);border-radius:4px;padding:12px;display:grid;gap:8px;min-width:0}h3{margin:0}.hint{color:var(--erp-color-text-secondary);margin:0;overflow-wrap:anywhere}.figures{display:grid;max-width:480px;grid-template-columns:max-content auto;gap:4px 16px;margin:0}.figures dd{margin:0;text-align:right;font-variant-numeric:tabular-nums}.credit{display:grid;gap:8px}.credit p{margin:0}.reason{font-weight:600}.required{color:var(--erp-color-error,#ff4d4f)}textarea{width:100%;max-width:560px;padding:4px;background:var(--erp-color-bg-container);color:var(--erp-color-text);border:1px solid var(--erp-color-border);border-radius:4px}textarea[aria-invalid=true]{border-color:var(--erp-color-error,#ff4d4f)}.field-error{color:var(--erp-color-error,#ff4d4f);margin:0}.actions{display:flex;gap:8px;flex-wrap:wrap}button:focus-visible,textarea:focus-visible{outline:2px solid var(--erp-color-primary);outline-offset:2px}`,
})
export class SalesOrderSubmitPanel {
  private readonly api = inject(SalesOrderService);
  private readonly permissions = inject(PermissionService);
  private readonly host = inject(ElementRef<HTMLElement>);
  private readonly injector = inject(Injector);
  readonly order = input.required<SalesOrderDetail>();
  readonly submitted = output<SalesOrderSubmitResult>();
  readonly refreshRequested = output<void>();
  readonly reasonMax = CREDIT_OVERRIDE_REASON_MAX;
  readonly opened = signal(false);
  readonly preview = signal<SalesCreditCheck | null>(null);
  readonly previewLoading = signal(false);
  readonly previewError = signal<{ key: string; traceId: string | null } | null>(null);
  /** Non-overridable credit block from the preview or a refusal; cleared by a successful recheck. */
  readonly blockedKey = signal<string | null>(null);
  /** Excess the server refused with; takes precedence over the preview's excess. */
  private readonly refusal = signal<{ excess: number; currency: string | null } | null>(null);
  readonly submitting = signal(false);
  readonly stale = signal<string | null>(null);
  readonly error = signal<{ key: string; traceId: string | null } | null>(null);
  readonly reasonError = signal<string | null>(null);
  reason = '';
  private previewRequest?: Subscription; private submitRequest?: Subscription;
  readonly canOverride = computed(() => this.permissions.has('SAL.CREDIT.OVERRIDE'));
  readonly excess = computed(() => {
    const refused = this.refusal();
    if (refused) return refused;
    const credit = this.preview();
    return credit?.status === 'OverLimit' ? { excess: credit.excess ?? 0, currency: credit.baseCurrency } : null;
  });
  /** Why confirm is disabled (MASTER_PROMPT 20.1); null when the submit can be sent. */
  readonly disabledKey = computed(() => this.submitting() ? 'sd.order.submit.busy' : this.stale() ? 'sd.order.submit.refreshFirst' :
    this.previewLoading() ? 'sd.order.submit.waitCheck' : this.blockedKey() ? 'sd.order.submit.blocked' :
    this.excess() && !this.canOverride() ? 'sd.order.submit.needOverride' : null);

  constructor() {
    inject(DestroyRef).onDestroy(() => { this.previewRequest?.unsubscribe(); this.submitRequest?.unsubscribe(); });
    // A freshly loaded saved order (even the same version after an uncertain outcome) starts a clean submit area.
    effect(() => { this.order(); untracked(() => { this.cancel(); this.stale.set(null); }); });
  }
  money(value: number | null | undefined): string {
    return value == null ? '—' : value.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 4 });
  }
  start(): void { this.opened.set(true); this.check(); }
  cancel(): void {
    this.previewRequest?.unsubscribe(); this.opened.set(false); this.preview.set(null); this.previewLoading.set(false);
    this.previewError.set(null); this.blockedKey.set(null); this.refusal.set(null); this.error.set(null);
    this.reasonError.set(null); this.reason = '';
  }
  /** Advisory preview with this saved order as candidate; the refusal excess is dropped in favour of fresh figures. */
  check(): void {
    const saved = this.order();
    this.previewRequest?.unsubscribe();
    this.preview.set(null); this.previewError.set(null); this.blockedKey.set(null); this.refusal.set(null);
    this.previewLoading.set(true);
    this.previewRequest = this.api.creditCheck(saved.customerId, saved.id).subscribe({
      next: credit => { this.previewLoading.set(false); this.preview.set(credit); },
      error: failure => {
        this.previewLoading.set(false);
        const key = failure instanceof TimeoutError ? 'sd.order.submit.checkTimeout' : apiErrorKey(failure, ORDER_SUBMIT_ERROR_CODES);
        this.previewError.set({ key, traceId: apiErrorTraceId(failure) });
        if (CREDIT_BLOCKED_CODES.some(code => key === `errors.${code}`)) this.blockedKey.set(key);
      },
    });
  }
  confirm(): void {
    if (this.disabledKey()) return;
    const saved = this.order(), version = saved.version;
    let reason: string | undefined;
    if (this.excess()) {
      reason = this.reason.trim();
      if (!reason) { this.reasonError.set('sd.order.submit.reasonRequired'); this.focus('sd-order-override-reason'); return; }
    }
    this.reasonError.set(null); this.error.set(null); this.submitting.set(true);
    this.submitRequest = this.api.submit(saved.id, version, reason).subscribe({
      next: reply => { this.submitting.set(false);
        const order = reply?.order;
        if (order?.id !== saved.id || order.documentNo !== saved.documentNo || !(order.version > version) ||
            (order.status !== 'Pending' && order.status !== 'Approved')) { this.pause('sd.order.submit.unknown'); return; }
        this.submitted.emit(reply);
      },
      error: failure => { this.submitting.set(false); this.fail(failure); },
    });
  }
  /** 409/404/status change/uncertain → pause until the saved order is reloaded; other refusals say nothing changed. */
  private fail(failure: unknown): void {
    const status = (failure as { status?: number })?.status;
    const traceId = apiErrorTraceId(failure);
    if (failure instanceof TimeoutError || status === 0 || (status != null && status >= 500)) {
      this.pause('sd.order.submit.unknown');
      if (status != null && status >= 500) this.error.set({ key: 'sd.order.submit.serverError', traceId });
      return;
    }
    const code = (failure as HttpErrorResponse).error?.errorCode;
    if (status === 409 && code !== 'FI.CREDIT.LOCK_TIMEOUT') { this.pause('sd.order.submit.conflict'); return; }
    if (status === 404) { this.pause('sd.order.submit.unavailable'); return; }
    if (status === 422 && code === 'SAL.ORDER.INVALID_STATUS') { this.pause('sd.order.submit.statusChanged'); return; }
    const fields = (failure as HttpErrorResponse).error?.errors;
    if (status === 400 && fields && typeof fields === 'object' &&
        Object.keys(fields).some(field => field.toLowerCase() === 'creditoverridereason')) {
      this.reasonError.set('sd.order.submit.reasonInvalid'); this.focus('sd-order-override-reason'); return;
    }
    const key = apiErrorKey(failure, ORDER_SUBMIT_ERROR_CODES);
    const refused = refusedExcess(failure);
    if (refused) { this.refusal.set(refused); if (this.canOverride()) this.focus('sd-order-override-reason'); }
    if (CREDIT_BLOCKED_CODES.some(blocked => key === `errors.${blocked}`)) this.blockedKey.set(key);
    this.error.set({ key, traceId });
  }
  private pause(key: string): void { this.stale.set(key); this.focus('sd-order-submit-refresh'); }
  private focus(testId: string): void {
    afterNextRender(() => this.host.nativeElement.querySelector(`[data-testid="${testId}"]`)?.focus(), { injector: this.injector });
  }
}
