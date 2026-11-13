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
  accountUnusable, mappedAccount, SALES_MAP_CODE_MAX, SALES_MAP_FIELDS, salesMapAccountErrors, SalesInvoiceAccountMap,
  SalesMapCodes, SalesMapField, SalesMapService,
} from './sales-map.service';

type Notice = { type: 'success' | 'warning'; key: string; params: Record<string, string> };

/** i18n suffix per request field (`fi.salesMap.<suffix>` label, `<suffix>Hint` meaning). */
const FIELD_KEYS: Record<SalesMapField, string> = {
  receivableAccountCode: 'receivable', revenueAccountCode: 'revenue', outputTaxAccountCode: 'outputTax',
};

const EMPTY: SalesMapCodes = { receivableAccountCode: '', revenueAccountCode: '', outputTaxAccountCode: '' };

function codesOf(map: SalesInvoiceAccountMap | null): SalesMapCodes {
  if (!map?.configured) return { ...EMPTY };
  return { receivableAccountCode: map.receivableAccount?.code ?? '', revenueAccountCode: map.revenueAccount?.code ?? '',
    outputTaxAccountCode: map.outputTaxAccount?.code ?? '' };
}

/**
 * SD invoice account map (interface 6.23, WU-FI-002a4c1, ADR-110/111): which receivable, revenue and output-tax accounts an
 * SD invoice posts to in the current company. View needs `GL.SALES_MAP.VIEW`; the form needs `GL.SALES_MAP.EDIT`.
 * `changed=false` is a warning, a stale version re-reads the saved map, and 422 reasons are shown under each field.
 */
@Component({
  selector: 'erp-sales-map-page',
  imports: [ReactiveFormsModule, TranslatePipe, NzAlertModule, NzButtonModule, NzInputModule, NzTagModule,
    HasPermissionDirective, ApiErrorDisplay],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="map-page">
      <header><h1>{{ 'fi.salesMap.title' | translate }}</h1></header>
      <p class="hint">{{ 'fi.salesMap.intro' | translate }}</p>
      @if (loading()) { <p class="muted" role="status" data-testid="fi-sales-map-loading">{{ 'fi.salesMap.loading' | translate }}</p> }
      @if (error()) {
        <erp-api-error-display data-testid="fi-sales-map-error" [messageKey]="error()!" [traceId]="traceId()" />
        <button nz-button type="button" class="retry" data-testid="fi-sales-map-retry" (click)="load()">{{ 'fi.salesMap.retry' | translate }}</button>
      }
      <div class="live" role="status" aria-live="polite">@if (notice(); as message) { <nz-alert [nzType]="message.type" nzShowIcon nzCloseable
        data-testid="fi-sales-map-notice" [attr.data-type]="message.type" [nzMessage]="message.key | translate: message.params" (nzOnClose)="notice.set(null)" /> }</div>
      @if (map(); as saved) {
        @if (!saved.configured) {
          <nz-alert nzType="warning" nzShowIcon data-testid="fi-sales-map-unconfigured" [nzMessage]="'fi.salesMap.unconfigured' | translate"
            [nzDescription]="(canEdit() ? 'fi.salesMap.unconfiguredEdit' : 'fi.salesMap.unconfiguredView') | translate" />
        } @else {
          @if (unusable(saved)) { <nz-alert nzType="warning" nzShowIcon data-testid="fi-sales-map-unusable"
            [nzMessage]="'fi.salesMap.unusable' | translate" /> }
          <dl class="accounts" data-testid="fi-sales-map-saved">
            @for (field of fields; track field) { @if (account(saved, field); as item) {
              <div class="account" [attr.data-testid]="'fi-sales-map-saved-' + field">
                <dt>{{ label(field) | translate }}</dt>
                <dd><span class="code" translate="no" [attr.data-testid]="'fi-sales-map-code-' + field">{{ item.code }}</span>
                  @if (item.name !== null) { <span [attr.data-testid]="'fi-sales-map-name-' + field">{{ item.name }}</span> }
                  @else { <nz-tag nzColor="error" [attr.data-testid]="'fi-sales-map-missing-' + field">{{ 'fi.salesMap.missing' | translate }}</nz-tag> }
                  @if (item.name !== null && !item.isActive) { <nz-tag nzColor="error" [attr.data-testid]="'fi-sales-map-inactive-' + field">{{ 'fi.salesMap.inactive' | translate }}</nz-tag> }
                  @else if (item.name !== null && !item.isPostable) { <nz-tag nzColor="warning" [attr.data-testid]="'fi-sales-map-not-postable-' + field">{{ 'fi.salesMap.notPostable' | translate }}</nz-tag> }
                  <span class="muted">{{ hint(field) | translate }}</span></dd>
              </div>
            } }
          </dl>
          <p class="muted" data-testid="fi-sales-map-version">{{ 'fi.salesMap.versionInfo' | translate: { version: saved.version, time: changedAt(saved) } }}</p>
        }
        <form class="map-form" *erpHasPermission="'GL.SALES_MAP.EDIT'" [formGroup]="form" (ngSubmit)="save()" data-testid="fi-sales-map-form" novalidate>
          <h2>{{ (saved.configured ? 'fi.salesMap.editTitle' : 'fi.salesMap.createTitle') | translate }}</h2>
          @if (saveError()) { <erp-api-error-display data-testid="fi-sales-map-save-error" [messageKey]="saveError()!"
            [traceId]="saveTraceId()" [details]="saveDetails()" /> }
          @for (field of fields; track field) {
            <div class="form-field">
              <label [for]="'fi-sales-map-input-' + field">{{ label(field) | translate }} <span class="required" aria-hidden="true">*</span></label>
              <input nz-input [id]="'fi-sales-map-input-' + field" [attr.name]="field" [formControlName]="field" autocomplete="off" spellcheck="false"
                [attr.maxlength]="maxLength" required [attr.data-testid]="'fi-sales-map-input-' + field"
                [attr.aria-invalid]="!!fieldError(field)" [nzStatus]="fieldError(field) ? 'error' : ''" [attr.aria-describedby]="'fi-sales-map-help-' + field" />
              <span class="muted" [id]="'fi-sales-map-help-' + field">{{ hint(field) | translate }}</span>
              @if (fieldError(field); as reason) { <p class="field-error" role="alert" [attr.data-testid]="'fi-sales-map-field-error-' + field">{{ reason | translate: { max: maxLength } }}</p> }
            </div>
          }
          <div class="form-actions">
            <button nz-button nzType="primary" type="submit" data-testid="fi-sales-map-save" [nzLoading]="saving()">{{ 'fi.salesMap.save' | translate }}</button>
            <button nz-button type="button" data-testid="fi-sales-map-undo" [disabled]="saving() || !form.dirty"
              [attr.title]="form.dirty ? null : ('fi.salesMap.undoDisabled' | translate)" (click)="undo()">{{ 'fi.salesMap.undo' | translate }}</button>
          </div>
          <p class="muted">{{ 'fi.salesMap.saveHint' | translate }}</p>
        </form>
      }
    </section>`,
  styles: `.live{display:contents}.map-page{display:grid;gap:16px;color:var(--erp-color-text);min-width:0;max-width:880px}h1,h2{margin:0}h2{font-size:16px}.hint,.muted{margin:0;color:var(--erp-color-text-secondary)}.muted{font-size:12px}.retry{justify-self:start}.accounts{display:grid;gap:12px;margin:0;padding:16px;border:1px solid var(--erp-color-border);border-radius:8px;background:var(--erp-color-bg-container)}.account{display:grid;grid-template-columns:minmax(120px,180px) 1fr;gap:8px}.account dt{font-weight:600}.account dd{margin:0;display:flex;flex-wrap:wrap;gap:8px;align-items:center;min-width:0;overflow-wrap:anywhere}.account dd .muted{flex-basis:100%}.code{font-variant-numeric:tabular-nums;font-weight:600}.map-form{display:grid;gap:12px;padding:16px;border:1px solid var(--erp-color-border);border-radius:8px;background:var(--erp-color-bg-container)}.form-field{display:grid;gap:4px;max-width:420px}.form-actions{display:flex;gap:8px;flex-wrap:wrap}.required,.field-error{color:var(--erp-color-error-text)}.field-error{margin:0}button:focus-visible,input:focus-visible{outline:2px solid var(--erp-color-primary);outline-offset:2px}@media (max-width:600px){.account{grid-template-columns:1fr}}`,
})
export class SalesMapPage implements HasUnsavedChanges {
  private readonly api = inject(SalesMapService);
  private readonly permissions = inject(PermissionService);
  private readonly company = inject(CompanyContextService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  readonly fields = SALES_MAP_FIELDS;
  readonly maxLength = SALES_MAP_CODE_MAX;
  readonly map = signal<SalesInvoiceAccountMap | null>(null);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly traceId = signal<string | null>(null);
  readonly notice = signal<Notice | null>(null);
  readonly saving = signal(false);
  readonly saveError = signal<string | null>(null);
  readonly saveTraceId = signal<string | null>(null);
  readonly saveDetails = signal<string[]>([]);
  /** Server reasons from the last 422, cleared for a field as soon as the user edits it. */
  readonly serverErrors = signal<Partial<Record<SalesMapField, string>>>({});
  readonly submitted = signal(false);
  readonly form = new FormGroup({
    receivableAccountCode: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(SALES_MAP_CODE_MAX)] }),
    revenueAccountCode: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(SALES_MAP_CODE_MAX)] }),
    outputTaxAccountCode: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(SALES_MAP_CODE_MAX)] }),
  });
  private readEpoch = 0;
  private writeEpoch = 0;

  constructor() {
    inject(DestroyRef).onDestroy(() => { this.readEpoch++; this.writeEpoch++; });
    for (const field of SALES_MAP_FIELDS) {
      this.form.controls[field].valueChanges.subscribe(() => {
        if (this.serverErrors()[field]) this.serverErrors.update(errors => { const next = { ...errors }; delete next[field]; return next; });
      });
    }
    // The map belongs to one company: a switch drops feedback and input and reads the new company's map.
    effect(() => { this.company.current(); untracked(() => { this.notice.set(null); this.load(); }); });
  }

  canEdit(): boolean { return this.permissions.has('GL.SALES_MAP.EDIT'); }
  account(map: SalesInvoiceAccountMap, field: SalesMapField) { return mappedAccount(map, field); }
  unusable(map: SalesInvoiceAccountMap): boolean { return SALES_MAP_FIELDS.some(field => accountUnusable(mappedAccount(map, field))); }
  label(field: SalesMapField): string { return `fi.salesMap.${FIELD_KEYS[field]}`; }
  hint(field: SalesMapField): string { return `fi.salesMap.${FIELD_KEYS[field]}Hint`; }
  changedAt(map: SalesInvoiceAccountMap): string { return displayTime(map.updatedAt ?? map.createdAt); }

  /** Client rule first (after a save attempt or once touched), then the server reason for the saved attempt. */
  fieldError(field: SalesMapField): string | null {
    const control = this.form.controls[field];
    if ((this.submitted() || control.touched) && control.invalid) {
      return control.hasError('required') || !control.value.trim() ? 'fi.salesMap.required' : 'fi.salesMap.tooLong';
    }
    const reason = this.serverErrors()[field];
    return reason ? `fi.salesMap.reason.${reason}` : null;
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
    for (const field of SALES_MAP_FIELDS) {
      const control = this.form.controls[field];
      if (control.value.trim() === '' && !control.hasError('required')) control.setErrors({ required: true });
    }
    if (this.form.invalid) { this.focusFirstError(); return; }
    const codes = this.form.getRawValue();
    const trimmed: SalesMapCodes = { receivableAccountCode: codes.receivableAccountCode.trim(),
      revenueAccountCode: codes.revenueAccountCode.trim(), outputTaxAccountCode: codes.outputTaxAccountCode.trim() };
    const epoch = ++this.writeEpoch;
    this.saving.set(true); this.notice.set(null); this.clearSaveError();
    this.api.save(saved.version, trimmed).subscribe({
      next: result => { if (epoch !== this.writeEpoch) return;
        this.saving.set(false); this.apply(result.map);
        this.notice.set(result.changed
          ? { type: 'success', key: saved.configured ? 'fi.salesMap.updated' : 'fi.salesMap.created', params: { version: String(result.map.version) } }
          : { type: 'warning', key: 'fi.salesMap.unchanged', params: { version: String(result.map.version) } }); },
      error: (failure: unknown) => { if (epoch !== this.writeEpoch) return;
        this.saving.set(false);
        const key = this.api.errorKey(failure);
        if (key === 'errors.FI.SALES_MAP.VERSION_CONFLICT') {
          // Someone else saved first: show their map; the user's input was not saved and is replaced by the latest codes.
          this.load(key); return;
        }
        this.saveError.set(key); this.saveTraceId.set(apiErrorTraceId(failure));
        this.serverErrors.set(salesMapAccountErrors(failure));
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

  private apply(map: SalesInvoiceAccountMap): void { this.map.set(map); this.reset(map); }

  private reset(map: SalesInvoiceAccountMap | null): void {
    this.form.reset(codesOf(map)); this.submitted.set(false); this.serverErrors.set({}); this.clearSaveError();
  }

  private clearSaveError(): void { this.saveError.set(null); this.saveTraceId.set(null); this.saveDetails.set([]); }
}
