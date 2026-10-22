import { ChangeDetectionStrategy, ChangeDetectorRef, Component, effect, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { CompanyContextService } from '../../core/company/company-context.service';
import { PermissionService } from '../../core/auth/permission.service';
import { apiErrorTraceId } from '../../core/http/api-error';
import { ApiErrorDisplay } from '../../shared/api-error-display';
import { ErpDialogService } from '../../shared/dialogs/dialog.service';
import { HasUnsavedChanges } from '../../shared/document-page/unsaved-changes.guard';
import { RequisitionItemOption } from './requisition.service';
import { RfqDetail, RfqDraft, RfqLineDraft, RfqService, RfqSupplierOption, rfqFieldErrors } from './rfq.service';

const newLine = (): RfqLineDraft => ({ itemId: null, requisitionLineId: null, requiredDate: '', quantity: '', remark: '' });
const newDraft = (): RfqDraft => ({ businessDate: '', replyDeadline: '', remark: '', lines: [newLine()], supplierIds: [] });

export function rfqDraftFromDetail(detail: RfqDetail): RfqDraft {
  return { businessDate: detail.businessDate, replyDeadline: detail.replyDeadline,
    remark: detail.remark ?? '', supplierIds: detail.invitations.map(row => row.supplierId),
    lines: detail.lines.map(row => ({ itemId: row.itemId, requisitionLineId: row.requisitionLineId,
      requiredDate: row.requiredDate, quantity: String(row.quantity), remark: row.remark ?? '' })) };
}

@Component({
  selector: 'erp-rfq-form',
  imports: [FormsModule, RouterLink, TranslatePipe, NzButtonModule, ApiErrorDisplay],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="rfq-form" data-testid="pur-rfq-form">
      <header><h1>{{ (id ? 'pur.rfq.edit' : 'pur.rfq.create') | translate }}</h1>
        <a routerLink="/pur/rfqs">{{ 'pur.rfq.back' | translate }}</a></header>
      @if (error()) { <erp-api-error-display [messageKey]="error()!" [traceId]="trace()" [details]="details()" data-testid="pur-rfq-form-error" /> }
      @if (conflicted()) { <button nz-button type="button" (click)="confirmReload()" data-testid="pur-rfq-reload">{{ 'pur.rfq.reload' | translate }}</button> }
      @if (savedNumber()) { <p role="status" class="success" data-testid="pur-rfq-form-success">{{ 'pur.rfq.saved' | translate:{ number: savedNumber() } }}</p> }
      @if (!editable()) { <p role="status" class="field-error" data-testid="pur-rfq-not-editable">{{ 'pur.rfq.notEditable' | translate }}</p> }
      <div class="fields">
        <div><label>{{ 'pur.rfq.date' | translate }} * <input type="date" name="businessDate" autocomplete="off" [(ngModel)]="draft.businessDate" [disabled]="!editable()" [attr.aria-invalid]="fieldError('businessDate')" data-testid="pur-rfq-date" /></label>
          @if (fieldError('businessDate')) { <small role="alert">{{ 'pur.rfq.invalidDate' | translate }}</small> }
          @if (serverFieldError('businessDate'); as message) { <small role="alert">{{ message }}</small> }</div>
        <div><label>{{ 'pur.rfq.deadline' | translate }} * <input type="date" name="replyDeadline" autocomplete="off" [(ngModel)]="draft.replyDeadline" [disabled]="!editable()" [attr.aria-invalid]="fieldError('replyDeadline')" data-testid="pur-rfq-deadline" /></label>
          @if (fieldError('replyDeadline')) { <small role="alert">{{ 'pur.rfq.invalidDeadline' | translate }}</small> }
          @if (serverFieldError('replyDeadline'); as message) { <small role="alert">{{ message }}</small> }</div>
        <div><label>{{ 'pur.rfq.remark' | translate }} <input name="remark" autocomplete="off" [(ngModel)]="draft.remark" maxlength="501" [disabled]="!editable()" data-testid="pur-rfq-remark" /></label>
          @if (fieldError('remark') || serverFieldError('remark')) { <small role="alert">{{ 'pur.rfq.maxRemark' | translate }}</small> }</div>
      </div>
      <h2>{{ 'pur.rfq.lines' | translate }}</h2>
      @if (fieldError('lines')) { <small role="alert">{{ 'pur.rfq.lineRange' | translate }}</small> }
      @for (line of draft.lines; track line; let index = $index) { <div class="line" [attr.data-testid]="'pur-rfq-line-' + index">
        <div><label>{{ 'pur.rfq.itemChoice' | translate }} * <button nz-button type="button" (click)="chooseItem(line)" [disabled]="!editable() || saving()" [attr.data-testid]="'pur-rfq-item-' + index">{{ itemLabel(line.itemId) }}</button></label>
          @if (itemUnavailable(line.itemId)) { <small role="alert">{{ 'pur.rfq.inactiveItem' | translate }}</small> }
          @if (fieldError('lines.' + index + '.itemId') || serverFieldError('lines.' + index + '.itemId')) { <small role="alert">{{ 'pur.rfq.requiredItem' | translate }}</small> }</div>
        <div><label>{{ 'pur.rfq.source' | translate }} <input type="number" min="1" step="1" autocomplete="off" [name]="'source' + index" [(ngModel)]="line.requisitionLineId" [disabled]="!editable()" [attr.data-testid]="'pur-rfq-source-' + index" /></label>
          @if (fieldError('lines.' + index + '.requisitionLineId') || serverFieldError('lines.' + index + '.requisitionLineId')) { <small role="alert">{{ 'pur.rfq.invalidSource' | translate }}</small> }</div>
        <div><label>{{ 'pur.rfq.requiredDate' | translate }} * <input type="date" autocomplete="off" [name]="'requiredDate' + index" [(ngModel)]="line.requiredDate" [disabled]="!editable()" [attr.data-testid]="'pur-rfq-required-' + index" /></label>
          @if (fieldError('lines.' + index + '.requiredDate') || serverFieldError('lines.' + index + '.requiredDate')) { <small role="alert">{{ 'pur.rfq.invalidRequiredDate' | translate }}</small> }</div>
        <div><label>{{ 'pur.rfq.quantity' | translate }} * <input type="text" inputmode="decimal" autocomplete="off" [name]="'quantity' + index" [(ngModel)]="line.quantity" [disabled]="!editable()" [attr.data-testid]="'pur-rfq-quantity-' + index" /></label>
          @if (fieldError('lines.' + index + '.quantity') || serverFieldError('lines.' + index + '.quantity')) { <small role="alert">{{ 'pur.rfq.invalidQuantity' | translate }}</small> }</div>
        <div><label>{{ 'pur.rfq.remark' | translate }} <input autocomplete="off" [name]="'lineRemark' + index" [(ngModel)]="line.remark" maxlength="501" [disabled]="!editable()" /></label>
          @if (fieldError('lines.' + index + '.remark') || serverFieldError('lines.' + index + '.remark')) { <small role="alert">{{ 'pur.rfq.maxRemark' | translate }}</small> }</div>
        <button nz-button type="button" (click)="removeLine(index)" [disabled]="draft.lines.length <= 1 || !editable() || saving()" [attr.title]="draft.lines.length <= 1 ? ('pur.rfq.minLine' | translate) : !editable() ? ('pur.rfq.notEditable' | translate) : null">{{ 'pur.rfq.removeLine' | translate }}</button>
      </div> }
      <button nz-button type="button" (click)="addLine()" [disabled]="draft.lines.length >= 1000 || !editable() || saving()" [attr.title]="draft.lines.length >= 1000 ? ('pur.rfq.maxLines' | translate) : !editable() ? ('pur.rfq.notEditable' | translate) : null" data-testid="pur-rfq-add">{{ 'pur.rfq.addLine' | translate }}</button>
      <h2>{{ 'pur.rfq.suppliers' | translate }}</h2>
      @if (fieldError('supplierIds') || serverFieldError('supplierIds')) { <small role="alert">{{ 'pur.rfq.supplierRange' | translate }}</small> }
      <div class="suppliers">@for (supplierId of draft.supplierIds; track supplierId) { <span class="supplier">{{ supplierLabel(supplierId) }}
        @if (supplierUnavailable(supplierId)) { <small role="alert">{{ 'pur.rfq.inactiveSupplier' | translate }}</small> }
        <button nz-button type="button" (click)="removeSupplier(supplierId)" [disabled]="!editable() || saving()" [attr.aria-label]="'pur.rfq.removeSupplier' | translate">×</button></span> }</div>
      <div class="actions"><button nz-button type="button" (click)="chooseSupplier()" [disabled]="draft.supplierIds.length >= 50 || !editable() || saving()" [attr.title]="draft.supplierIds.length >= 50 ? ('pur.rfq.maxSuppliers' | translate) : !editable() ? ('pur.rfq.notEditable' | translate) : null" data-testid="pur-rfq-supplier">{{ 'pur.rfq.chooseSupplier' | translate }}</button>
        <button nz-button nzType="primary" type="button" (click)="save()" [disabled]="!editable() || saving()" [attr.title]="!editable() ? ('pur.rfq.notEditable' | translate) : saving() ? ('pur.rfq.busy' | translate) : null" data-testid="pur-rfq-save">{{ 'pur.rfq.save' | translate }}</button></div>
    </section>`,
  styles: `.rfq-form{display:grid;gap:16px;color:var(--erp-color-text)}header,.fields,.actions,.suppliers{display:flex;gap:12px;align-items:center;flex-wrap:wrap}header{justify-content:space-between}h1,h2{margin:0}h1{font-size:20px}h2{font-size:16px}.fields>div,.line>div{display:grid;gap:4px;min-width:0}.fields label,.line label{display:grid;gap:4px}.line{display:grid;grid-template-columns:repeat(5,minmax(130px,1fr)) auto;gap:10px;padding:12px;border:1px solid var(--erp-color-border);border-radius:4px}.rfq-form input,.line button{min-height:32px;background:var(--erp-color-bg-container);color:var(--erp-color-text);border:1px solid var(--erp-color-border);border-radius:4px}.line button{overflow:hidden;text-overflow:ellipsis}.rfq-form input:focus-visible,.rfq-form button:focus-visible{outline:2px solid var(--erp-color-primary)}small{color:var(--erp-color-error)}.success{color:var(--erp-color-success)}.supplier{display:flex;align-items:center;gap:6px;border:1px solid var(--erp-color-border);padding:4px 8px;border-radius:4px}@media(max-width:1000px){.line{grid-template-columns:repeat(2,minmax(0,1fr))}}`,
})
export class RfqForm implements HasUnsavedChanges {
  private readonly route = inject(ActivatedRoute); private readonly router = inject(Router);
  private readonly api = inject(RfqService); private readonly dialogs = inject(ErpDialogService);
  private readonly translate = inject(TranslateService); private readonly company = inject(CompanyContextService);
  private readonly permissions = inject(PermissionService); private readonly cdr = inject(ChangeDetectorRef);
  readonly id = Number(this.route.snapshot.paramMap.get('id')) || null;
  readonly error = signal<string | null>(null); readonly trace = signal<string | null>(null);
  readonly details = signal<string[]>([]); readonly fieldErrors = signal<string[]>([]);
  readonly serverFields = signal<Record<string, string>>({}); readonly savedNumber = signal<string | null>(null);
  readonly saving = signal(false); readonly editable = signal(!this.id); readonly conflicted = signal(false);
  draft = newDraft(); private version = 0; private baseline = ''; private revision = 0;
  private readonly items = new Map<number, RequisitionItemOption>();
  private readonly suppliers = new Map<number, RfqSupplierOption>();
  private lastServerError: unknown = null;
  constructor() {
    this.translate.onLangChange.pipe(takeUntilDestroyed()).subscribe(() => {
      if (this.lastServerError) this.mapServerFields(this.lastServerError);
    });
    effect(() => { this.company.current(); const revision = ++this.revision;
      this.draft = newDraft(); this.version = 0; this.items.clear(); this.suppliers.clear(); this.saving.set(false);
      this.editable.set(!this.id); this.clearFeedback(); this.savedNumber.set(null);
      this.baseline = JSON.stringify(this.draft); this.cdr.markForCheck();
      if (this.id) this.load(revision);
    });
  }
  private load(revision: number): void {
    this.api.getForEdit(this.id!).subscribe({ next: detail => {
      if (revision !== this.revision) return;
      this.version = detail.version; this.editable.set(detail.status === 'Draft');
      this.draft = rfqDraftFromDetail(detail); this.baseline = JSON.stringify(this.draft);
      this.api.savedOptions(this.id!).subscribe({ next: options => {
        if (revision !== this.revision) return;
        options.items.forEach(item => this.items.set(item.id, item));
        options.suppliers.forEach(supplier => this.suppliers.set(supplier.id, supplier));
        this.cdr.markForCheck();
      }, error: e => { if (revision === this.revision) this.showError(e); } });
      this.cdr.markForCheck();
    }, error: e => { if (revision === this.revision) this.showError(e); } });
  }
  itemLabel(id: number | null): string { const row = id ? this.items.get(id) : null;
    return row ? `${row.code} · ${row.name}` : id ? `#${id}` : '—'; }
  supplierLabel(id: number): string { const row = this.suppliers.get(id);
    return row ? `${row.code} · ${row.name}` : `#${id}`; }
  itemUnavailable(id: number | null): boolean { return id !== null && this.items.get(id)?.isActive === false; }
  supplierUnavailable(id: number): boolean { const row = this.suppliers.get(id);
    return !!row && (!row.isActive || !row.isApproved); }
  chooseItem(line: RfqLineDraft): void { const revision = this.revision;
    this.dialogs.select<RequisitionItemOption>({ titleKey: 'pur.rfq.chooseItem',
      columns: [{ key: 'code', labelKey: 'pur.rfq.itemCode', value: row => row.code },
        { key: 'name', labelKey: 'pur.rfq.itemName', value: row => row.name }],
      load: query => this.api.itemOptions(query), rowKey: row => row.id }).subscribe(rows => {
      if (!rows?.[0] || revision !== this.revision) return;
      line.itemId = rows[0].id; this.items.set(rows[0].id, rows[0]); this.cdr.markForCheck();
    }); }
  chooseSupplier(): void { const revision = this.revision;
    this.dialogs.select<RfqSupplierOption>({ titleKey: 'pur.rfq.chooseSupplier',
      columns: [{ key: 'code', labelKey: 'pur.rfq.supplierCode', value: row => row.code },
        { key: 'name', labelKey: 'pur.rfq.supplierName', value: row => row.name }],
      load: query => this.api.supplierOptions(query), rowKey: row => row.id }).subscribe(rows => {
      if (!rows?.[0] || revision !== this.revision || this.draft.supplierIds.includes(rows[0].id)) return;
      if (this.draft.supplierIds.length >= 50) return;
      this.draft.supplierIds.push(rows[0].id); this.suppliers.set(rows[0].id, rows[0]); this.cdr.markForCheck();
    }); }
  removeSupplier(id: number): void { this.draft.supplierIds = this.draft.supplierIds.filter(value => value !== id); }
  addLine(): void { if (this.draft.lines.length < 1000) this.draft.lines.push(newLine()); }
  removeLine(index: number): void { if (this.draft.lines.length > 1) this.draft.lines.splice(index, 1); }
  fieldError(path: string): boolean { return this.fieldErrors().includes(path); }
  serverFieldError(path: string): string | null { return this.serverFields()[path] ?? null; }
  hasUnsavedChanges(): boolean { return JSON.stringify(this.draft) !== this.baseline; }
  private clearFeedback(): void { this.error.set(null); this.trace.set(null); this.details.set([]);
    this.fieldErrors.set([]); this.serverFields.set({}); this.conflicted.set(false); this.lastServerError = null; }
  private mapServerFields(error: unknown): void {
    if (!(error instanceof HttpErrorResponse) || ![400, 422].includes(error.status)) return;
    const raw = (error.error as { errors?: unknown } | null)?.errors;
    if (!raw || typeof raw !== 'object' || Array.isArray(raw)) return;
    const fields: Record<string, string> = {}; const details: string[] = [];
    for (const [path] of Object.entries(raw).slice(0, 20)) {
      const match = /^lines\[(\d+)\]\.(itemId|requisitionLineId|requiredDate|quantity|remark)$/i.exec(path);
      const field = match ? match[2][0].toLowerCase() + match[2].slice(1) : path[0].toLowerCase() + path.slice(1);
      const localPath = match ? `lines.${match[1]}.${field}` : field;
      const label = this.translate.instant(`pur.rfq.${({ businessDate: 'date', replyDeadline: 'deadline', itemId: 'itemChoice', requisitionLineId: 'source', supplierIds: 'suppliers' } as Record<string, string>)[field] ?? field}`);
      const name = match ? this.translate.instant('pur.rfq.serverLineField', { number: Number(match[1]) + 1, field: label }) : label;
      const reason = this.translate.instant('pur.rfq.serverFieldInvalid');
      fields[localPath] = reason; details.push(`${name}: ${reason}`);
    }
    this.serverFields.set(fields); this.details.set(details);
  }
  private showError(error: unknown): void { this.error.set(this.api.errorKey(error)); this.trace.set(apiErrorTraceId(error));
    this.lastServerError = error; this.mapServerFields(error);
    this.conflicted.set(error instanceof HttpErrorResponse && error.status === 409); }
  confirmReload(): void { if (!this.id) return;
    this.dialogs.confirm({ titleKey: 'pur.rfq.reloadConfirm', contentKey: 'pur.rfq.reloadWarning',
      okKey: 'pur.rfq.reload' }).subscribe(confirmed => { if (!confirmed) return;
      this.clearFeedback(); this.load(this.revision); }); }
  save(): void { if (this.saving() || !this.editable()) return;
    const errors = rfqFieldErrors(this.draft); this.fieldErrors.set(errors);
    if (errors.length) { this.error.set('pur.rfq.invalidForm'); this.details.set([]); this.trace.set(null);
      const first = errors[0].split('.'); const selector = first.length === 3 ?
        `pur-rfq-${({ itemId: 'item', requisitionLineId: 'source', requiredDate: 'required', quantity: 'quantity' } as Record<string, string>)[first[2]] ?? 'remark'}-${first[1]}` :
        ({ businessDate: 'pur-rfq-date', replyDeadline: 'pur-rfq-deadline', supplierIds: 'pur-rfq-supplier', remark: 'pur-rfq-remark' } as Record<string, string>)[first[0]] ?? 'pur-rfq-add';
      document.querySelector<HTMLElement>(`[data-testid="${selector}"]`)?.focus(); return; }
    if (this.id && !this.hasUnsavedChanges()) { this.error.set('pur.rfq.noChanges'); return; }
    this.clearFeedback(); this.savedNumber.set(null); this.saving.set(true);
    const revision = this.revision;
    const action = this.id ? this.api.update(this.id, this.version, this.draft) : this.api.create(this.draft);
    action.subscribe({ next: saved => { if (revision !== this.revision) return; this.saving.set(false);
      this.version = saved.version; this.baseline = JSON.stringify(this.draft); this.savedNumber.set(saved.documentNo);
      this.dialogs.success('pur.rfq.saved', { number: saved.documentNo });
      if (this.permissions.has('PUR.RFQ.VIEW')) void this.router.navigateByUrl('/pur/rfqs');
      else if (!this.id && this.permissions.has('PUR.RFQ.EDIT')) void this.router.navigateByUrl(`/pur/rfqs/${saved.id}/edit`);
      else if (!this.id) void this.router.navigateByUrl('/');
    }, error: e => { if (revision === this.revision) { this.saving.set(false); this.showError(e); } } });
  }
}
