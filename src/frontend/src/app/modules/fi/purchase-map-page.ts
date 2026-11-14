import { afterNextRender, ChangeDetectionStrategy, Component, DestroyRef, effect, ElementRef, HostListener, Injector, inject, signal, untracked } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { HasPermissionDirective } from '../../core/auth/has-permission.directive';
import { PermissionService } from '../../core/auth/permission.service';
import { CompanyContextService } from '../../core/company/company-context.service';
import { apiErrorTraceId, apiValidationDetails } from '../../core/http/api-error';
import { ApiErrorDisplay } from '../../shared/api-error-display';
import { displayTime } from '../../shared/date';
import { HasUnsavedChanges } from '../../shared/document-page/unsaved-changes.guard';
import {
  purchaseAccountUnusable, purchaseMappedAccount, PURCHASE_MAP_CODE_MAX, PURCHASE_MAP_FIELDS, purchaseMapAccountErrors, PurchaseInvoiceAccountMap,
  PurchaseMapCodes, PurchaseMapField, PurchaseMapService,
} from './purchase-map.service';

type Notice = { type: 'success' | 'warning'; key: string; params: Record<string, string> };

/** i18n suffix per request field (`fi.purchaseMap.<suffix>` label, `<suffix>Hint` meaning). */
const FIELD_KEYS: Record<PurchaseMapField, string> = {
  payableAccountCode: 'payable', purchaseAccountCode: 'purchase', inputTaxAccountCode: 'inputTax',
};

const EMPTY: PurchaseMapCodes = { payableAccountCode: '', purchaseAccountCode: '', inputTaxAccountCode: '' };

function codesOf(map: PurchaseInvoiceAccountMap | null): PurchaseMapCodes {
  if (!map?.configured) return { ...EMPTY };
  return { payableAccountCode: map.payableAccount?.code ?? '', purchaseAccountCode: map.purchaseAccount?.code ?? '',
    inputTaxAccountCode: map.inputTaxAccount?.code ?? '' };
}

/**
 * PUR purchase invoice account map (interface 6.24, WU-FI-003a2d, ADR-118/119): which payable, purchases-or-expense and
 * input-tax accounts a purchase invoice posts to in the current company; same layout as the sales map (6.23, ADR-111).
 * View needs `GL.PURCHASE_MAP.VIEW`; the form needs `GL.PURCHASE_MAP.EDIT`.
 * `changed=false` is a warning, a stale version re-reads the saved map, and 422 reasons are shown under each field.
 */
@Component({
  selector: 'erp-purchase-map-page',
  imports: [ReactiveFormsModule, TranslatePipe, NzAlertModule, NzButtonModule, NzInputModule, NzTagModule,
    HasPermissionDirective, ApiErrorDisplay],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="map-page">
      <header><h1>{{ 'fi.purchaseMap.title' | translate }}</h1></header>
      <p class="hint">{{ 'fi.purchaseMap.intro' | translate }}</p>
      @if (loading()) { <p class="muted" role="status" data-testid="fi-purchase-map-loading">{{ 'fi.purchaseMap.loading' | translate }}</p> }
      @if (error()) {
        <erp-api-error-display data-testid="fi-purchase-map-error" [messageKey]="error()!" [traceId]="traceId()" />
        <button nz-button type="button" class="retry" data-testid="fi-purchase-map-retry" (click)="load()">{{ 'fi.purchaseMap.retry' | translate }}</button>
      }
      <div class="live" role="status" aria-live="polite">@if (notice(); as message) { <nz-alert [nzType]="message.type" nzShowIcon nzCloseable
        data-testid="fi-purchase-map-notice" [attr.data-type]="message.type" [nzMessage]="message.key | translate: message.params" (nzOnClose)="notice.set(null)" /> }</div>
      @if (map(); as saved) {
        @if (!saved.configured) {
          <nz-alert nzType="warning" nzShowIcon data-testid="fi-purchase-map-unconfigured" [nzMessage]="'fi.purchaseMap.unconfigured' | translate"
            [nzDescription]="(canEdit() ? 'fi.purchaseMap.unconfiguredEdit' : 'fi.purchaseMap.unconfiguredView') | translate" />
        } @else {
          @if (unusable(saved)) { <nz-alert nzType="warning" nzShowIcon data-testid="fi-purchase-map-unusable"
            [nzMessage]="'fi.purchaseMap.unusable' | translate" /> }
          <dl class="accounts" data-testid="fi-purchase-map-saved">
            @for (field of fields; track field) { @if (account(saved, field); as item) {
              <div class="account" [attr.data-testid]="'fi-purchase-map-saved-' + field">
                <dt>{{ label(field) | translate }}</dt>
                <dd><span class="code" translate="no" [attr.data-testid]="'fi-purchase-map-code-' + field">{{ item.code }}</span>
                  @if (item.name !== null) { <span [attr.data-testid]="'fi-purchase-map-name-' + field">{{ item.name }}</span> }
                  @else { <nz-tag nzColor="error" [attr.data-testid]="'fi-purchase-map-missing-' + field">{{ 'fi.purchaseMap.missing' | translate }}</nz-tag> }
                  @if (item.name !== null && !item.isActive) { <nz-tag nzColor="error" [attr.data-testid]="'fi-purchase-map-inactive-' + field">{{ 'fi.purchaseMap.inactive' | translate }}</nz-tag> }
                  @else if (item.name !== null && !item.isPostable) { <nz-tag nzColor="warning" [attr.data-testid]="'fi-purchase-map-not-postable-' + field">{{ 'fi.purchaseMap.notPostable' | translate }}</nz-tag> }
                  <span class="muted">{{ hint(field) | translate }}</span></dd>
              </div>
            } }
          </dl>
          <p class="muted" data-testid="fi-purchase-map-version">{{ 'fi.purchaseMap.versionInfo' | translate: { version: saved.version, time: changedAt(saved) } }}</p>
        }
        <form class="map-form" *erpHasPermission="'GL.PURCHASE_MAP.EDIT'" [formGroup]="form" (ngSubmit)="save()" data-testid="fi-purchase-map-form" novalidate>
          <h2>{{ (saved.configured ? 'fi.purchaseMap.editTitle' : 'fi.purchaseMap.createTitle') | translate }}</h2>
          @if (saveError()) { <erp-api-error-display data-testid="fi-purchase-map-save-error" [messageKey]="saveError()!"
            [traceId]="saveTraceId()" [details]="saveDetails()" /> }
          @for (field of fields; track field) {
            <div class="form-field">
              <label [for]="'fi-purchase-map-input-' + field">{{ label(field) | translate }} <span class="required" aria-hidden="true">*</span></label>
              <input nz-input [id]="'fi-purchase-map-input-' + field" [attr.name]="field" [formControlName]="field" autocomplete="off" spellcheck="false"
                [attr.maxlength]="maxLength" required [attr.data-testid]="'fi-purchase-map-input-' + field"
                [attr.aria-invalid]="!!fieldError(field)" [nzStatus]="fieldError(field) ? 'error' : ''" [attr.aria-describedby]="'fi-purchase-map-help-' + field" />
              <span class="muted" [id]="'fi-purchase-map-help-' + field">{{ hint(field) | translate }}</span>
              @if (fieldError(field); as reason) { <p class="field-error" role="alert" [attr.data-testid]="'fi-purchase-map-field-error-' + field">{{ reason | translate: { max: maxLength } }}</p> }
            </div>
          }
          <div class="form-actions">
            <button nz-button nzType="primary" type="submit" data-testid="fi-purchase-map-save" [nzLoading]="saving()">{{ 'fi.purchaseMap.save' | translate }}</button>
            <button nz-button type="button" data-testid="fi-purchase-map-undo" [disabled]="saving() || !form.dirty"
              [attr.title]="form.dirty ? null : ('fi.purchaseMap.undoDisabled' | translate)" (click)="undo()">{{ 'fi.purchaseMap.undo' | translate }}</button>
          </div>
          <p class="muted">{{ 'fi.purchaseMap.saveHint' | translate }}</p>
        </form>
      }
    </section>`,
  styles: `.live{display:contents}.map-page{display:grid;gap:16px;color:var(--erp-color-text);min-width:0;max-width:880px}h1,h2{margin:0}h2{font-size:16px}.hint,.muted{margin:0;color:var(--erp-color-text-secondary)}.muted{font-size:12px}.retry{justify-self:start}.accounts{display:grid;gap:12px;margin:0;padding:16px;border:1px solid var(--erp-color-border);border-radius:8px;background:var(--erp-color-bg-container)}.account{display:grid;grid-template-columns:minmax(120px,180px) 1fr;gap:8px}.account dt{font-weight:600}.account dd{margin:0;display:flex;flex-wrap:wrap;gap:8px;align-items:center;min-width:0;overflow-wrap:anywhere}.account dd .muted{flex-basis:100%}.code{font-variant-numeric:tabular-nums;font-weight:600}.map-form{display:grid;gap:12px;padding:16px;border:1px solid var(--erp-color-border);border-radius:8px;background:var(--erp-color-bg-container)}.form-field{display:grid;gap:4px;max-width:420px}.form-actions{display:flex;gap:8px;flex-wrap:wrap}.required,.field-error{color:var(--erp-color-error-text)}.field-error{margin:0}button:focus-visible,input:focus-visible{outline:2px solid var(--erp-color-primary);outline-offset:2px}@media (max-width:600px){.account{grid-template-columns:1fr}}`,
})
export class PurchaseMapPage implements HasUnsavedChanges {
  private readonly api = inject(PurchaseMapService);
  private readonly permissions = inject(PermissionService);
  private readonly company = inject(CompanyContextService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  readonly fields = PURCHASE_MAP_FIELDS;
  readonly maxLength = PURCHASE_MAP_CODE_MAX;
  readonly map = signal<PurchaseInvoiceAccountMap | null>(null);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly traceId = signal<string | null>(null);
  readonly notice = signal<Notice | null>(null);
  readonly saving = signal(false);
  readonly saveError = signal<string | null>(null);
  readonly saveTraceId = signal<string | null>(null);
  readonly saveDetails = signal<string[]>([]);
  /** Server reasons from the last 422, cleared for a field as soon as the user edits it. */
  readonly serverErrors = signal<Partial<Record<PurchaseMapField, string>>>({});
  readonly submitted = signal(false);
  readonly form = new FormGroup({
    payableAccountCode: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(PURCHASE_MAP_CODE_MAX)] }),
    purchaseAccountCode: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(PURCHASE_MAP_CODE_MAX)] }),
    inputTaxAccountCode: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(PURCHASE_MAP_CODE_MAX)] }),
  });
  private readEpoch = 0;
  private writeEpoch = 0;

  constructor() {
    inject(DestroyRef).onDestroy(() => { this.readEpoch++; this.writeEpoch++; });
    for (const field of PURCHASE_MAP_FIELDS) {
      this.form.controls[field].valueChanges.subscribe(() => {
        if (this.serverErrors()[field]) this.serverErrors.update(errors => { const next = { ...errors }; delete next[field]; return next; });
      });
    }
    // The map belongs to one company: a switch drops feedback and input and reads the new company's map.
    effect(() => { this.company.current(); untracked(() => { this.notice.set(null); this.load(); }); });
  }

  canEdit(): boolean { return this.permissions.has('GL.PURCHASE_MAP.EDIT'); }
  account(map: PurchaseInvoiceAccountMap, field: PurchaseMapField) { return purchaseMappedAccount(map, field); }
  unusable(map: PurchaseInvoiceAccountMap): boolean { return PURCHASE_MAP_FIELDS.some(field => purchaseAccountUnusable(purchaseMappedAccount(map, field))); }
  label(field: PurchaseMapField): string { return `fi.purchaseMap.${FIELD_KEYS[field]}`; }
  hint(field: PurchaseMapField): string { return `fi.purchaseMap.${FIELD_KEYS[field]}Hint`; }
  changedAt(map: PurchaseInvoiceAccountMap): string { return displayTime(map.updatedAt ?? map.createdAt); }

  /** Client rule first (after a save attempt or once touched), then the server reason for the saved attempt. */
  fieldError(field: PurchaseMapField): string | null {
    const control = this.form.controls[field];
    if ((this.submitted() || control.touched) && control.invalid) {
      return control.hasError('required') || !control.value.trim() ? 'fi.purchaseMap.required' : 'fi.purchaseMap.tooLong';
    }
    const reason = this.serverErrors()[field];
    return reason ? `fi.purchaseMap.reason.${reason}` : null;
  }

  /** Reads the saved map; after a version conflict the conflict message stays visible above the latest codes. */
  load(conflictKey: string | null = null): void {
    const epoch = ++this.readEpoch;
    this.writeEpoch++; this.saving.set(false);
    this.loading.set(true); this.error.set(null); this.traceId.set(null);
    this.api.get().subscribe({
      next: map => { if (epoch !== this.readEpoch) return; this.loading.set(false); this.apply(map); this.saveError.set(conflictKey); },
      error: (failure: unknown) => { if (epoch !== this.readEpoch) return;
        this.map.set(null); this.loading.set(false);
        this.error.set(this.api.errorKey(failure)); this.traceId.set(apiErrorTraceId(failure)); },
    });
  }

  save(): void {
    const saved = this.map();
    if (!saved || this.saving()) return;
    this.submitted.set(true); this.form.markAllAsTouched();
    for (const field of PURCHASE_MAP_FIELDS) {
      const control = this.form.controls[field];
      if (control.value.trim() === '' && !control.hasError('required')) control.setErrors({ required: true });
    }
    if (this.form.invalid) { this.focusFirstError(); return; }
    const codes = this.form.getRawValue();
    const trimmed: PurchaseMapCodes = { payableAccountCode: codes.payableAccountCode.trim(),
      purchaseAccountCode: codes.purchaseAccountCode.trim(), inputTaxAccountCode: codes.inputTaxAccountCode.trim() };
    const epoch = ++this.writeEpoch;
    this.saving.set(true); this.notice.set(null); this.clearSaveError();
    this.api.save(saved.version, trimmed).subscribe({
      next: result => { if (epoch !== this.writeEpoch) return;
        this.saving.set(false); this.apply(result.map);
        this.notice.set(result.changed
          ? { type: 'success', key: saved.configured ? 'fi.purchaseMap.updated' : 'fi.purchaseMap.created', params: { version: String(result.map.version) } }
          : { type: 'warning', key: 'fi.purchaseMap.unchanged', params: { version: String(result.map.version) } }); },
      error: (failure: unknown) => { if (epoch !== this.writeEpoch) return;
        this.saving.set(false);
        const key = this.api.errorKey(failure);
        if (key === 'errors.FI.PURCHASE_MAP.VERSION_CONFLICT') {
          // Someone else saved first: show their map; the user's input was not saved and is replaced by the latest codes.
          this.load(key); return;
        }
        this.saveError.set(key); this.saveTraceId.set(apiErrorTraceId(failure));
        this.serverErrors.set(purchaseMapAccountErrors(failure));
        this.saveDetails.set(Object.keys(this.serverErrors()).length ? [] : apiValidationDetails(failure));
        if (Object.keys(this.serverErrors()).length) this.focusFirstError(); },
    });
  }

  undo(): void { this.reset(this.map()); }

  hasUnsavedChanges(): boolean { return this.form.dirty; }

  @HostListener('window:beforeunload', ['$event'])
  beforeUnload(event: BeforeUnloadEvent): void {
    if (this.hasUnsavedChanges()) { event.preventDefault(); event.returnValue = ''; }
  }

  /** Moves focus (and scroll) to the first field with an error once it is rendered (ui-conventions B4). */
  private focusFirstError(): void {
    afterNextRender(() => this.host.nativeElement.querySelector<HTMLInputElement>('input[aria-invalid="true"]')?.focus(), { injector: this.injector });
  }

  private apply(map: PurchaseInvoiceAccountMap): void { this.map.set(map); this.reset(map); }

  private reset(map: PurchaseInvoiceAccountMap | null): void {
    this.form.reset(codesOf(map)); this.submitted.set(false); this.serverErrors.set({}); this.clearSaveError();
  }

  private clearSaveError(): void { this.saveError.set(null); this.saveTraceId.set(null); this.saveDetails.set([]); }
}
