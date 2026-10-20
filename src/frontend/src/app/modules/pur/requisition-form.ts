import { ChangeDetectionStrategy, ChangeDetectorRef, Component, effect, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { CompanyContextService } from '../../core/company/company-context.service';
import { PermissionService } from '../../core/auth/permission.service';
import { apiErrorTraceId } from '../../core/http/api-error';
import { ApiErrorDisplay } from '../../shared/api-error-display';
import { ErpDialogService } from '../../shared/dialogs/dialog.service';
import { HasUnsavedChanges } from '../../shared/document-page/unsaved-changes.guard';
import { RequisitionDraft, RequisitionItemOption, RequisitionLineDraft, RequisitionService,
  requisitionQuantityPattern, validRequisitionDate } from './requisition.service';

const newLine = (): RequisitionLineDraft => ({ itemId: null, requiredDate: '', quantity: '', remark: '' });
const newDraft = (): RequisitionDraft => ({ businessDate: '', remark: '', lines: [newLine()] });

/** Return field paths in display order so all invalid fields can be explained at once. */
export function requisitionFieldErrors(draft: RequisitionDraft): string[] {
  const errors: string[] = [];
  if (!validRequisitionDate(draft.businessDate)) errors.push('businessDate');
  if (draft.remark.trim().length > 500) errors.push('remark');
  if (draft.lines.length < 1 || draft.lines.length > 1000) errors.push('lines');
  draft.lines.forEach((line, index) => {
    const key = `lines.${index}`;
    if (!Number.isSafeInteger(line.itemId) || (line.itemId ?? 0) <= 0) errors.push(`${key}.itemId`);
    if (!validRequisitionDate(line.requiredDate) ||
      (validRequisitionDate(draft.businessDate) && line.requiredDate < draft.businessDate)) errors.push(`${key}.requiredDate`);
    if (!requisitionQuantityPattern.test(line.quantity.trim()) || !/[1-9]/.test(line.quantity)) errors.push(`${key}.quantity`);
    if (line.remark.trim().length > 500) errors.push(`${key}.remark`);
  });
  return errors;
}

/** 介面 4.9／4.10：整單保存，物料經 PUR 權限保護的 MD 選項讀取。 */
@Component({
  selector: 'erp-requisition-form',
  imports: [FormsModule, RouterLink, TranslatePipe, NzButtonModule, ApiErrorDisplay],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="requisition-form" data-testid="pur-requisition-form">
      <header><h1>{{ (id ? 'pur.requisition.edit' : 'pur.requisition.create') | translate }}</h1>
        <a routerLink="/pur/requisitions">{{ 'pur.requisition.back' | translate }}</a></header>
      @if (error()) { <erp-api-error-display [messageKey]="error()!" [traceId]="errorTrace()" [details]="errorDetails()" data-testid="pur-form-error" /> }
      @if (conflicted()) { <button nz-button type="button" (click)="confirmReload()" data-testid="pur-form-reload">{{ 'pur.requisition.reload' | translate }}</button> }
      @if (savedNumber()) { <p role="status" class="success" data-testid="pur-form-success">{{ 'pur.requisition.saved' | translate:{ number: savedNumber() } }}</p> }
      @if (!editable()) { <p role="status" class="field-error" data-testid="pur-form-not-editable">{{ 'pur.requisition.notEditable' | translate }}</p> }
      <div class="header-fields">
        <label><span>{{ 'pur.requisition.date' | translate }} <span class="required" aria-hidden="true">*</span></span>
          <input type="date" name="businessDate" autocomplete="off" [(ngModel)]="draft.businessDate" [disabled]="!editable()" [attr.aria-invalid]="fieldError('businessDate')" data-testid="pur-form-date" /></label>
        @if (fieldError('businessDate')) { <p class="field-error" role="alert">{{ 'pur.requisition.invalidDate' | translate }}</p> }
        @if (serverFieldError('businessDate'); as message) { <p class="field-error" role="alert">{{ message }}</p> }
        <label>{{ 'pur.requisition.remark' | translate }}
          <input name="remark" autocomplete="off" [(ngModel)]="draft.remark" maxlength="501" [disabled]="!editable()" [attr.aria-invalid]="fieldError('remark')" data-testid="pur-form-remark" /></label>
        @if (fieldError('remark')) { <p class="field-error" role="alert">{{ 'pur.requisition.maxRemark' | translate }}</p> }
        @if (serverFieldError('remark'); as message) { <p class="field-error" role="alert">{{ message }}</p> }
      </div>
      <h2>{{ 'pur.requisition.lines' | translate }}</h2>
      @if (fieldError('lines')) { <p class="field-error" role="alert">{{ 'pur.requisition.lineRange' | translate }}</p> }
      @for (line of draft.lines; track line; let index = $index) {
        <div class="line" [attr.data-testid]="'pur-form-line-' + index">
          <div><label><span>{{ 'pur.requisition.itemChoice' | translate }} <span class="required" aria-hidden="true">*</span></span>
            <button nz-button type="button" (click)="chooseItem(line)" [disabled]="saving() || !editable()" [attr.data-testid]="'pur-form-item-' + index"
              [attr.title]="!editable() ? ('pur.requisition.notEditable' | translate) : itemLabel(line.itemId)" [attr.aria-label]="'pur.requisition.chooseItem' | translate">
              {{ itemLabel(line.itemId) }}</button></label>
            @if (itemInactive(line.itemId)) { <p class="field-error">{{ 'pur.requisition.inactiveItem' | translate }}</p> }
            @if (fieldError('lines.' + index + '.itemId')) { <p class="field-error" role="alert">{{ 'pur.requisition.requiredItem' | translate }}</p> }
            @if (serverFieldError('lines.' + index + '.itemId'); as message) { <p class="field-error" role="alert">{{ message }}</p> }
          </div>
          <div><label><span>{{ 'pur.requisition.requiredDate' | translate }} <span class="required" aria-hidden="true">*</span></span>
            <input type="date" [name]="'requiredDate' + index" autocomplete="off" [(ngModel)]="line.requiredDate" [disabled]="!editable()" [attr.data-testid]="'pur-form-required-' + index" [attr.aria-invalid]="fieldError('lines.' + index + '.requiredDate')" /></label>
            @if (fieldError('lines.' + index + '.requiredDate')) { <p class="field-error" role="alert">{{ 'pur.requisition.invalidRequiredDate' | translate }}</p> }
            @if (serverFieldError('lines.' + index + '.requiredDate'); as message) { <p class="field-error" role="alert">{{ message }}</p> }
          </div>
          <div><label><span>{{ 'pur.requisition.quantity' | translate }} <span class="required" aria-hidden="true">*</span></span>
            <input type="text" inputmode="decimal" [name]="'quantity' + index" autocomplete="off" [(ngModel)]="line.quantity" [disabled]="!editable()" [attr.data-testid]="'pur-form-quantity-' + index" [attr.aria-invalid]="fieldError('lines.' + index + '.quantity')" /></label>
            @if (fieldError('lines.' + index + '.quantity')) { <p class="field-error" role="alert">{{ 'pur.requisition.invalidQuantity' | translate }}</p> }
            @if (serverFieldError('lines.' + index + '.quantity'); as message) { <p class="field-error" role="alert">{{ message }}</p> }
          </div>
          <div><label>{{ 'pur.requisition.remark' | translate }}
            <input [name]="'lineRemark' + index" autocomplete="off" [(ngModel)]="line.remark" maxlength="501" [disabled]="!editable()" [attr.data-testid]="'pur-form-line-remark-' + index" [attr.aria-invalid]="fieldError('lines.' + index + '.remark')" /></label>
            @if (fieldError('lines.' + index + '.remark')) { <p class="field-error" role="alert">{{ 'pur.requisition.maxRemark' | translate }}</p> }
            @if (serverFieldError('lines.' + index + '.remark'); as message) { <p class="field-error" role="alert">{{ message }}</p> }
          </div>
          <button nz-button type="button" (click)="removeLine(index)" [disabled]="draft.lines.length <= 1 || saving() || !editable()"
            [attr.title]="!editable() ? ('pur.requisition.notEditable' | translate) : draft.lines.length <= 1 ? ('pur.requisition.minLine' | translate) : null">{{ 'pur.requisition.removeLine' | translate }}</button>
        </div>
      }
      <div class="actions"><button nz-button type="button" (click)="addLine()" [disabled]="draft.lines.length >= 1000 || saving() || !editable()"
        [attr.title]="!editable() ? ('pur.requisition.notEditable' | translate) : draft.lines.length >= 1000 ? ('pur.requisition.maxLines' | translate) : null" data-testid="pur-form-add">
        {{ 'pur.requisition.addLine' | translate }}</button>
        <button nz-button nzType="primary" type="button" (click)="save()" [disabled]="saving() || !editable()"
          [attr.title]="!editable() ? ('pur.requisition.notEditable' | translate) : saving() ? ('pur.requisition.busy' | translate) : null"
          data-testid="pur-form-save">{{ 'pur.requisition.save' | translate }}</button></div>
    </section>`,
  styles: `.requisition-form{display:grid;gap:16px;color:var(--erp-color-text)}header,.header-fields,.actions{display:flex;gap:16px;align-items:center;flex-wrap:wrap}header{justify-content:space-between}h1{margin:0;font-size:20px;text-wrap:balance}h2{margin:0;font-size:16px}.header-fields label,.line label{display:grid;gap:4px}.required{color:var(--erp-color-error)}.line{display:grid;grid-template-columns:repeat(4,minmax(140px,1fr)) auto;gap:10px;padding:12px;border:1px solid var(--erp-color-border);border-radius:4px}.line>div{min-width:0}.line input,.header-fields input,.line button{min-height:32px;width:100%;background:var(--erp-color-bg-container);color:var(--erp-color-text);border:1px solid var(--erp-color-border);border-radius:4px}.line input:focus-visible,.header-fields input:focus-visible{outline:2px solid var(--erp-color-primary)}.line button{overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.line button:disabled{opacity:.55;cursor:not-allowed}.line input[inputmode=decimal]{font-variant-numeric:tabular-nums}.field-error{color:var(--erp-color-error);margin:4px 0;font-size:12px}.success{color:var(--erp-color-success)}@media(max-width:900px){.line{grid-template-columns:repeat(2,minmax(0,1fr))}}`,
})
export class RequisitionForm implements HasUnsavedChanges {
  private readonly route = inject(ActivatedRoute); private readonly router = inject(Router);
  private readonly api = inject(RequisitionService); private readonly dialogs = inject(ErpDialogService);
  private readonly translate = inject(TranslateService);
  private readonly company = inject(CompanyContextService); private readonly cdr = inject(ChangeDetectorRef);
  private readonly permissions = inject(PermissionService);
  readonly id = Number(this.route.snapshot.paramMap.get('id')) || null;
  readonly error = signal<string | null>(null); readonly errorTrace = signal<string | null>(null);
  readonly errorDetails = signal<string[]>([]); readonly fieldErrors = signal<string[]>([]);
  readonly serverFieldErrors = signal<Record<string, string>>({});
  readonly savedNumber = signal<string | null>(null); readonly saving = signal(false); readonly editable = signal(true);
  readonly conflicted = signal(false);
  draft = newDraft(); private version = 0; private baseline = ''; private revision = 0;
  private lastServerError: unknown = null;
  private readonly items = new Map<number, RequisitionItemOption>();
  constructor() { this.translate.onLangChange.pipe(takeUntilDestroyed()).subscribe(() => {
      if (!this.lastServerError) return;
      const validation = this.validationDetails(this.lastServerError);
      this.errorDetails.set(validation.details); this.serverFieldErrors.set(validation.fields);
    });
    effect(() => { this.company.current(); const revision = ++this.revision;
    this.draft = newDraft(); this.version = 0; this.items.clear(); this.editable.set(true);
    this.error.set(null); this.errorTrace.set(null); this.errorDetails.set([]); this.fieldErrors.set([]);
    this.serverFieldErrors.set({}); this.lastServerError = null; this.savedNumber.set(null);
    this.conflicted.set(false);
    this.baseline = JSON.stringify(this.draft); this.cdr.markForCheck();
    if (this.id) this.load(revision);
  }); }
  private load(revision: number): void { this.api.savedItemOptions(this.id!).subscribe({ next: items => {
    if (revision !== this.revision) return;
    for (const item of items) this.items.set(item.id, item);
    this.cdr.markForCheck();
  }, error: e => { if (revision === this.revision) this.showError(e); } });
    this.api.getForEdit(this.id!).subscribe({ next: detail => {
    if (revision !== this.revision) return;
    this.version = detail.version; this.editable.set(detail.status === 'Draft' || detail.status === 'Rejected');
    this.draft = { businessDate: detail.businessDate, remark: detail.remark ?? '', lines: detail.lines.map(line =>
      ({ itemId: line.itemId, requiredDate: line.requiredDate, quantity: String(line.quantity), remark: line.remark ?? '' })) };
    this.baseline = JSON.stringify(this.draft); this.cdr.markForCheck();
  }, error: e => { if (revision === this.revision) this.showError(e); } }); }
  itemLabel(id: number | null): string { const item = id ? this.items.get(id) : null;
    return item ? `${item.code} · ${item.name}` : id ? `#${id}` : '—'; }
  itemInactive(id: number | null): boolean { return id !== null && this.items.get(id)?.isActive === false; }
  chooseItem(line: RequisitionLineDraft): void { const revision = this.revision;
    this.dialogs.select<RequisitionItemOption>({ titleKey: 'pur.requisition.chooseItem',
      columns: [{ key: 'code', labelKey: 'pur.requisition.itemCode', value: row => row.code },
        { key: 'name', labelKey: 'pur.requisition.itemName', value: row => row.name }],
      load: query => this.api.itemOptions(query), rowKey: row => row.id }).subscribe(rows => {
      if (!rows?.[0] || revision !== this.revision) return;
      line.itemId = rows[0].id; this.items.set(rows[0].id, rows[0]); this.cdr.markForCheck();
    }); }
  addLine(): void { if (this.draft.lines.length < 1000) this.draft.lines.push(newLine()); }
  removeLine(index: number): void { if (this.draft.lines.length > 1) this.draft.lines.splice(index, 1); }
  fieldError(path: string): boolean { return this.fieldErrors().includes(path); }
  serverFieldError(path: string): string | null { return this.serverFieldErrors()[path] ?? null; }
  hasUnsavedChanges(): boolean { return JSON.stringify(this.draft) !== this.baseline; }
  private validationDetails(error: unknown): { details: string[]; fields: Record<string, string> } {
    if (!(error instanceof HttpErrorResponse) || (error.status !== 400 && error.status !== 422))
      return { details: [], fields: {} };
    const fields = (error.error as { errors?: unknown } | null)?.errors;
    if (!fields || typeof fields !== 'object' || Array.isArray(fields)) return { details: [], fields: {} };
    const inline: Record<string, string> = {};
    const details = Object.entries(fields).slice(0, 20).flatMap(([path, messages]) => {
      const match = /^lines\[(\d+)\]\.(itemId|requiredDate|quantity|remark)$/i.exec(path);
      const rawField = match?.[2] ?? path;
      const field = rawField[0]?.toLowerCase() + rawField.slice(1);
      const key = ({ itemId: 'itemChoice', requiredDate: 'requiredDate', quantity: 'quantity', remark: 'remark',
        businessDate: 'date', lines: 'lines' } as Record<string, string>)[field] ?? 'invalidForm';
      const label = this.translate.instant(`pur.requisition.${key}`);
      const name = match ? this.translate.instant('pur.requisition.serverLineField', { number: Number(match[1]) + 1, field: label }) : label;
      const first = Array.isArray(messages) ? messages.find(value => typeof value === 'string') as string | undefined : undefined;
      const reason = first && /inactive/i.test(first) ? this.translate.instant('pur.requisition.inactiveItem')
        : first && /not found/i.test(first) ? this.translate.instant('pur.requisition.missingItem')
        : this.translate.instant('pur.requisition.serverFieldInvalid');
      const localPath = match ? `lines.${match[1]}.${field}` : field;
      inline[localPath] = reason;
      return [`${name}：${reason}`];
    });
    return { details, fields: inline };
  }
  private showError(error: unknown): void { this.error.set(this.api.errorKey(error));
    this.lastServerError = error;
    const validation = this.validationDetails(error);
    this.errorTrace.set(apiErrorTraceId(error)); this.errorDetails.set(validation.details);
    this.serverFieldErrors.set(validation.fields);
    this.conflicted.set(error instanceof HttpErrorResponse && error.status === 409); }
  confirmReload(): void { if (!this.id) return;
    this.dialogs.confirm({ titleKey: 'pur.requisition.reloadConfirm',
      contentKey: 'pur.requisition.reloadWarning', okKey: 'pur.requisition.reload' }).subscribe(confirmed => {
      if (!confirmed) return;
      this.error.set(null); this.errorTrace.set(null); this.errorDetails.set([]);
      this.serverFieldErrors.set({}); this.lastServerError = null; this.conflicted.set(false); this.load(this.revision);
    }); }
  save(): void { if (this.saving() || !this.editable()) return;
    const errors = requisitionFieldErrors(this.draft); this.fieldErrors.set(errors);
    if (errors.length) { this.error.set('pur.requisition.invalidForm'); this.errorTrace.set(null);
      this.errorDetails.set([]); this.serverFieldErrors.set({}); this.lastServerError = null; this.conflicted.set(false);
      const first = errors[0]; const parts = first.split('.');
      const lineField: Record<string, string> = { itemId: 'item', requiredDate: 'required', quantity: 'quantity', remark: 'line-remark' };
      const selector = first === 'businessDate' ? 'pur-form-date' : first === 'remark' ? 'pur-form-remark'
        : parts.length === 3 ? `pur-form-${lineField[parts[2]]}-${parts[1]}` : 'pur-form-add';
      document.querySelector<HTMLElement>(`[data-testid="${selector}"]`)?.focus(); return; }
    this.error.set(null); this.errorDetails.set([]); this.errorTrace.set(null); this.conflicted.set(false);
    this.serverFieldErrors.set({}); this.lastServerError = null;
    this.savedNumber.set(null); this.saving.set(true);
    const revision = this.revision;
    const action = this.id ? this.api.update(this.id, this.version, this.draft) : this.api.create(this.draft);
    action.subscribe({ next: saved => { this.saving.set(false); if (revision !== this.revision) return;
      this.savedNumber.set(saved.documentNo); this.baseline = JSON.stringify(this.draft); this.version = saved.version;
      this.dialogs.success('pur.requisition.saved', { number: saved.documentNo });
      if (this.permissions.has('PUR.REQUISITION.VIEW')) void this.router.navigateByUrl('/pur/requisitions');
      else if (!this.id && this.permissions.has('PUR.REQUISITION.EDIT'))
        void this.router.navigateByUrl(`/pur/requisitions/${saved.id}/edit`);
      else if (!this.id) void this.router.navigateByUrl('/');
    }, error: e => { this.saving.set(false); if (revision === this.revision) this.showError(e); } });
  }
}
