import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, HostListener, effect, inject, signal, untracked, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzTableModule, NzTableQueryParams } from 'ng-zorro-antd/table';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { CompanyContextService } from '../../core/company/company-context.service';
import { HasPermissionDirective } from '../../core/auth/has-permission.directive';
import { PermissionService } from '../../core/auth/permission.service';
import { apiErrorTraceId } from '../../core/http/api-error';
import { ApiErrorDisplay } from '../../shared/api-error-display';
import { ErpDialogService } from '../../shared/dialogs/dialog.service';
import { HasUnsavedChanges } from '../../shared/document-page/unsaved-changes.guard';
import { validRequisitionDate } from './requisition.service';
import { QuotationDetail, QuotationDraft, QuotationQuery, QuotationStatus, QuotationSummary, RfqDetail, RfqService, quotationFieldErrors } from './rfq.service';

/** Interface 4.14: saved quotation summaries and immutable revision history. */
@Component({
  selector: 'erp-quotation-page',
  imports: [FormsModule, RouterLink, TranslatePipe, ApiErrorDisplay, HasPermissionDirective, NzButtonModule, NzTableModule, NzTagModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="quotes">
      <header><h1>{{ 'pur.quotation.title' | translate }} · {{ 'pur.quotation.rfqId' | translate }} {{ rfqId }}</h1>
        <a nz-button routerLink="/pur/rfqs" *erpHasPermission="'PUR.RFQ.VIEW'">{{ 'pur.quotation.back' | translate }}</a></header>
      <div class="filters">
        <label>{{ 'pur.quotation.supplier' | translate }} <input #supplierInput type="number" name="quotationSupplier" min="1" step="1"
          autocomplete="off" [(ngModel)]="supplierId" data-testid="pur-quotation-supplier" />
          @if (error() === 'pur.quotation.invalidSupplier') { <small role="alert">{{ 'pur.quotation.invalidSupplier' | translate }}</small> }</label>
        <label>{{ 'pur.quotation.status' | translate }} <select name="quotationStatus" autocomplete="off"
          [(ngModel)]="status" data-testid="pur-quotation-status">
          <option value="">{{ 'pur.quotation.all' | translate }}</option>
          <option value="Active">{{ 'pur.quotation.active' | translate }}</option>
          <option value="Withdrawn">{{ 'pur.quotation.withdrawn' | translate }}</option>
          <option value="Awarded">{{ 'pur.quotation.awarded' | translate }}</option></select></label>
        <label>{{ 'pur.quotation.validOn' | translate }} <input #validOnInput type="date" name="quotationValidOn"
          autocomplete="off" [(ngModel)]="validOn" data-testid="pur-quotation-valid-on" />
          @if (error() === 'pur.quotation.invalidDate') { <small role="alert">{{ 'pur.quotation.invalidDate' | translate }}</small> }</label>
        <button nz-button (click)="search()" data-testid="pur-quotation-search">{{ 'pur.quotation.search' | translate }}</button>
        <button nz-button (click)="reset()" data-testid="pur-quotation-reset">{{ 'pur.quotation.reset' | translate }}</button>
      </div>
      @if (error()) { <erp-api-error-display [messageKey]="error()!" [traceId]="traceId()"
        data-testid="pur-quotation-error" /> }
      @if (contextError()) { <p role="status" class="context-warning" data-testid="pur-quotation-context-error">
        {{ contextError()! | translate }}</p> }
      @if (rfq()?.status === 'Sent' && canQuote() && !editor()) {
        <button nz-button nzType="primary" type="button" (click)="startCreate()" data-testid="pur-quotation-create">{{ 'pur.quotation.create' | translate }}</button>
      }
      <nz-table #table [nzData]="rows()" [nzLoading]="loading()" [nzFrontPagination]="false"
        [nzTotal]="total()" [nzPageIndex]="page()" [nzPageSize]="pageSize()" (nzQueryParams)="onQuery($event)"
        [nzNoResult]="(loadFailed() ? 'pur.quotation.loadFailed' : filtered() ? 'pur.quotation.noResults' : 'pur.quotation.empty') | translate"
        data-testid="pur-quotation-table">
        <thead><tr><th>{{ 'pur.quotation.supplier' | translate }}</th><th>{{ 'pur.quotation.status' | translate }}</th>
          <th>{{ 'pur.quotation.revision' | translate }}</th><th>{{ 'pur.quotation.quoteDate' | translate }}</th>
          <th>{{ 'pur.quotation.validUntil' | translate }}</th></tr></thead>
        <tbody>@for (row of table.data; track row.id) { <tr [attr.data-testid]="'pur-quotation-row-' + row.id">
          <td><button nz-button nzType="link" (click)="view(row)">{{ row.supplierId }}</button></td>
          <td><nz-tag [nzColor]="row.status === 'Active' && !row.isExpired ? 'processing' : 'default'">
            {{ statusKey(row.status) | translate }}</nz-tag>
            @if (row.isExpired) { <nz-tag>{{ 'pur.quotation.expired' | translate }}</nz-tag> }</td>
          <td>{{ row.currentRevisionNo }}</td><td>{{ row.quoteDate }}</td><td>{{ row.validUntil }}</td></tr> }</tbody>
      </nz-table>
      @if (detail(); as d) { <section class="detail" data-testid="pur-quotation-detail">
        <button nz-button (click)="closeDetail()" [attr.aria-label]="'pur.quotation.closeDetail' | translate">×</button>
        <h2>{{ 'pur.quotation.supplier' | translate }} {{ d.supplierId }} · {{ statusKey(d.status) | translate }}</h2>
        @if (d.isExpired) { <p>{{ 'pur.quotation.expired' | translate }}</p> }
        @if (canQuote() && rfq()?.status === 'Sent' && d.status === 'Active' && !d.isExpired) {
          <button nz-button type="button" (click)="startRevision(d)" data-testid="pur-quotation-revise">{{ 'pur.quotation.revise' | translate }}</button>
          <button nz-button nzDanger type="button" (click)="confirmWithdraw(d)" [disabled]="saving()"
            data-testid="pur-quotation-withdraw">{{ 'pur.quotation.withdraw' | translate }}</button>
        }
        <p>{{ 'pur.quotation.currentRevision' | translate }}: {{ d.currentRevisionNo }}</p>
        <h3>{{ 'pur.quotation.history' | translate }}</h3>
        @for (revision of d.revisions; track revision.id) { <section class="revision">
          <h4>{{ 'pur.quotation.revision' | translate }} {{ revision.revisionNo }}
            @if (revision.revisionNo === d.currentRevisionNo) { · {{ 'pur.quotation.current' | translate }} }</h4>
          <p>{{ 'pur.quotation.quoteDate' | translate }}: {{ revision.quoteDate }} ·
            {{ 'pur.quotation.validUntil' | translate }}: {{ revision.validUntil }}</p>
          @if (revision.remark) { <p>{{ 'pur.quotation.remark' | translate }}: {{ revision.remark }}</p> }
          <table><thead><tr><th>{{ 'pur.quotation.rfqLine' | translate }}</th>
            <th>{{ 'pur.quotation.unitPrice' | translate }}</th><th>{{ 'pur.quotation.currency' | translate }}</th>
            <th>{{ 'pur.quotation.taxRate' | translate }}</th></tr></thead><tbody>
            @for (line of revision.lines; track line.id) { <tr><td>{{ line.rfqLineId }}</td>
              <td>{{ line.unitPrice }}</td><td>{{ line.currencyCode }}</td><td>{{ line.taxRateId }}</td></tr> }
          </tbody></table>
        </section> }
      </section> }
      @if (editor(); as draft) { <section class="editor" data-testid="pur-quotation-editor">
        <h2>{{ (editId() ? 'pur.quotation.revise' : 'pur.quotation.create') | translate }}</h2>
        @if (conflict()) { <p role="alert">{{ 'pur.quotation.conflict' | translate }}</p>
          <button nz-button type="button" (click)="reloadAfterConflict()">{{ 'pur.quotation.reload' | translate }}</button> }
        <div class="fields">
          <div><label>{{ 'pur.quotation.supplier' | translate }} * <select name="quoteSupplier" autocomplete="off" [(ngModel)]="draft.supplierId" (ngModelChange)="onDraftChanged()"
            [disabled]="!!editId() || saving()" data-testid="pur-quotation-edit-supplier">
            <option [ngValue]="null">{{ 'pur.quotation.chooseSupplier' | translate }}</option>
            @for (supplier of rfq()?.invitations ?? []; track supplier.id) { <option [ngValue]="supplier.supplierId">
              {{ supplier.supplierCodeSnapshot || supplier.supplierId }} · {{ supplier.supplierNameSnapshot || supplier.supplierId }}</option> }
          </select></label>@if (invalid('supplierId')) { <small role="alert">{{ fieldErrorKey('supplierId', 'pur.quotation.invalidEditSupplier') | translate }}</small> }</div>
          <div><label>{{ 'pur.quotation.quoteDate' | translate }} * <input type="date" name="quoteDate" autocomplete="off" [(ngModel)]="draft.quoteDate" (ngModelChange)="onDraftChanged()"
            [disabled]="saving()" data-testid="pur-quotation-edit-date" /></label>@if (invalid('quoteDate')) { <small role="alert">{{ fieldErrorKey('quoteDate', 'pur.quotation.invalidDate') | translate }}</small> }</div>
          <div><label>{{ 'pur.quotation.validUntil' | translate }} * <input type="date" name="validUntil" autocomplete="off" [(ngModel)]="draft.validUntil" (ngModelChange)="onDraftChanged()"
            [disabled]="saving()" data-testid="pur-quotation-edit-until" /></label>@if (invalid('validUntil')) { <small role="alert">{{ fieldErrorKey('validUntil', 'pur.quotation.invalidUntil') | translate }}</small> }</div>
          <div><label>{{ 'pur.quotation.remark' | translate }} <input name="quoteRemark" autocomplete="off" [(ngModel)]="draft.remark" (ngModelChange)="onDraftChanged()" maxlength="501"
            [disabled]="saving()" data-testid="pur-quotation-edit-remark" /></label>@if (invalid('remark')) { <small role="alert">{{ fieldErrorKey('remark', 'pur.quotation.maxRemark') | translate }}</small> }</div>
        </div>
        <h3>{{ 'pur.quotation.lines' | translate }}</h3>
        @for (line of draft.lines; track line.rfqLineId; let index = $index) { <div class="edit-line">
          <span>{{ 'pur.quotation.rfqLine' | translate }} {{ lineNumber(line.rfqLineId) }}</span>
          <div><label>{{ 'pur.quotation.unitPrice' | translate }} * <input [name]="'price' + index" type="text" inputmode="decimal" autocomplete="off"
            [(ngModel)]="line.unitPrice" (ngModelChange)="onDraftChanged()" [disabled]="saving()" [attr.data-testid]="'pur-quotation-price-' + index" /></label>
            @if (invalid('lines.' + index + '.unitPrice')) { <small role="alert">{{ fieldErrorKey('lines.' + index + '.unitPrice', 'pur.quotation.invalidPrice') | translate }}</small> }</div>
          <div><label>{{ 'pur.quotation.currency' | translate }} * <input [name]="'currency' + index" autocomplete="off" maxlength="3"
            [(ngModel)]="line.currencyCode" (ngModelChange)="onDraftChanged()" [disabled]="saving()" [attr.data-testid]="'pur-quotation-currency-' + index" /></label>
            @if (invalid('lines.' + index + '.currencyCode')) { <small role="alert">{{ fieldErrorKey('lines.' + index + '.currencyCode', 'pur.quotation.invalidCurrency') | translate }}</small> }</div>
          <div><label>{{ 'pur.quotation.taxRate' | translate }} * <input [name]="'taxRate' + index" type="number" autocomplete="off" min="1" step="1"
            [(ngModel)]="line.taxRateId" (ngModelChange)="onDraftChanged()" [disabled]="saving()" [attr.data-testid]="'pur-quotation-tax-' + index" /></label>
            @if (invalid('lines.' + index + '.taxRateId')) { <small role="alert">{{ fieldErrorKey('lines.' + index + '.taxRateId', 'pur.quotation.invalidTax') | translate }}</small> }</div>
        </div> }
        <div class="actions"><button nz-button nzType="primary" type="button" (click)="save()" [disabled]="saving() || conflict()"
          data-testid="pur-quotation-save">{{ 'pur.quotation.save' | translate }}</button>
          <button nz-button type="button" (click)="cancelEdit()" [disabled]="saving()">{{ 'pur.quotation.cancel' | translate }}</button></div>
      </section> }
      @if (success()) { <p role="status" class="success" data-testid="pur-quotation-success">{{ success()! | translate }}</p> }
    </section>`,
  styles: `.quotes{display:grid;gap:16px;color:var(--erp-color-text)}header,.filters,.fields,.actions{display:flex;gap:8px;align-items:end;flex-wrap:wrap}h1{font-size:20px;margin:0;text-wrap:balance}.filters label,.fields label,.edit-line label{display:grid;gap:4px;font-size:12px}small{color:var(--erp-color-error,#cf1322);max-width:220px}.context-warning{padding:10px 12px;border:1px solid var(--erp-color-warning,#d48806);border-radius:4px}.filters input,.filters select,.editor input,.editor select{min-height:32px;background:var(--erp-color-bg-container);color:var(--erp-color-text);border:1px solid var(--erp-color-border);border-radius:4px}.filters input[type=number]{width:120px}.detail,.editor{padding:16px;border:1px solid var(--erp-color-border);overflow-x:auto}.detail table{width:100%}.detail th,.detail td{padding:6px;text-align:left;border-bottom:1px solid var(--erp-color-border);font-variant-numeric:tabular-nums}.detail td{overflow-wrap:anywhere}.revision{margin-top:16px;border-top:1px solid var(--erp-color-border)}.revision th:nth-child(2),.revision td:nth-child(2){text-align:right}.edit-line{display:grid;grid-template-columns:130px repeat(3,minmax(130px,1fr));gap:12px;align-items:end;padding:8px;border-bottom:1px solid var(--erp-color-border)}.success{color:var(--erp-color-success)}button:focus-visible,a:focus-visible,input:focus-visible,select:focus-visible{outline:2px solid var(--erp-color-primary);outline-offset:2px}@media(max-width:700px){.edit-line{grid-template-columns:repeat(2,minmax(0,1fr))}}`,
})
export class QuotationPage implements HasUnsavedChanges {
  private readonly api = inject(RfqService);
  private readonly company = inject(CompanyContextService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly permissions = inject(PermissionService);
  private readonly dialogs = inject(ErpDialogService);
  readonly rfqId = Number(this.route.snapshot.paramMap.get('id'));
  readonly rows = signal<QuotationSummary[]>([]);
  readonly detail = signal<QuotationDetail | null>(null);
  readonly total = signal(0); readonly page = signal(1); readonly pageSize = signal(20);
  readonly loading = signal(false); readonly loadFailed = signal(false); readonly filtered = signal(false);
  readonly error = signal<string | null>(null); readonly traceId = signal<string | null>(null);
  readonly rfq = signal<RfqDetail | null>(null);
  readonly contextError = signal<string | null>(null);
  readonly editor = signal<QuotationDraft | null>(null);
  readonly editId = signal<number | null>(null);
  private editVersion: number | null = null;
  private editBaseline = '';
  readonly saving = signal(false); readonly conflict = signal(false);
  readonly validation = signal<string[]>([]); readonly success = signal<string | null>(null);
  readonly serverValidation = signal<string[]>([]);
  private readonly supplierInput = viewChild<ElementRef<HTMLInputElement>>('supplierInput');
  private readonly validOnInput = viewChild<ElementRef<HTMLInputElement>>('validOnInput');
  supplierId: number | null = null; status = ''; validOn = '';
  private applied: Pick<QuotationQuery, 'supplierId' | 'status' | 'validOn'> = {};
  private listGeneration = 0; private detailGeneration = 0; private firstLoad = true;
  constructor() {
    const params = this.route.snapshot.queryParamMap;
    const supplier = Number(params.get('supplierId'));
    this.supplierId = Number.isSafeInteger(supplier) && supplier > 0 ? supplier : null;
    const status = params.get('status') ?? '';
    this.status = ['Active', 'Withdrawn', 'Awarded'].includes(status) ? status : '';
    this.validOn = params.get('validOn') ?? '';
    const page = Number(params.get('page'));
    if (Number.isSafeInteger(page) && page > 0) this.page.set(page);
    this.destroyRef.onDestroy(() => { ++this.listGeneration; ++this.detailGeneration; });
    effect(() => { this.company.current(); untracked(() => {
      if (this.firstLoad) { this.firstLoad = false; this.search(false); }
      else {
        ++this.listGeneration; ++this.detailGeneration;
        this.rows.set([]); this.total.set(0); this.detail.set(null); this.rfq.set(null); this.contextError.set(null);
        this.editor.set(null); this.editId.set(null); this.editVersion = null; this.editBaseline = ''; this.saving.set(false); this.success.set(null);
        this.search();
      }
    }); });
  }
  canQuote(): boolean { return this.permissions.has('PUR.QUOTATION.QUOTE'); }
  hasUnsavedChanges(): boolean { return !!this.editor() && JSON.stringify(this.editor()) !== this.editBaseline; }
  @HostListener('window:beforeunload', ['$event']) beforeUnload(event: BeforeUnloadEvent): void {
    if (this.hasUnsavedChanges()) event.preventDefault();
  }
  invalid(path: string): boolean { return this.validation().includes(path); }
  fieldErrorKey(path: string, localKey: string): string {
    return this.serverValidation().includes(path) ? 'pur.quotation.serverFieldInvalid' : localKey;
  }
  onDraftChanged(): void { this.validation.set([]); this.serverValidation.set([]); if (!this.conflict()) this.clearError(); }
  lineNumber(id: number): number { return this.rfq()?.lines.find(x => x.id === id)?.lineNo ?? id; }
  statusKey(status: QuotationStatus): string { return 'pur.quotation.' + status.toLowerCase(); }
  private clearError(): void { this.error.set(null); this.traceId.set(null); }
  private showError(error: unknown): void {
    this.error.set(this.api.errorKey(error)); this.traceId.set(apiErrorTraceId(error));
    this.conflict.set(error instanceof HttpErrorResponse && error.status === 409);
    if (error instanceof HttpErrorResponse && [400, 422].includes(error.status)) {
      const raw = (error.error as { errors?: Record<string, unknown> } | null)?.errors;
      if (raw && typeof raw === 'object') {
        const fields = Object.keys(raw).map(path => path.replace(/lines\[(\d+)\]/i, 'lines.$1')
          .replace(/\.([A-Z])/g, (_, ch: string) => `.${ch.toLowerCase()}`)
          .replace(/^([A-Z])/, (_, ch: string) => ch.toLowerCase()));
        this.serverValidation.set(fields); this.validation.set(fields);
      }
    }
  }
  private updateUrl(): void { void this.router.navigate([], { relativeTo: this.route, replaceUrl: true,
    queryParams: { supplierId: this.applied.supplierId ?? null, status: this.applied.status || null,
      validOn: this.applied.validOn || null, page: this.page() > 1 ? this.page() : null } }); }
  search(resetPage = true): void {
    if (this.supplierId !== null && (!Number.isSafeInteger(this.supplierId) || this.supplierId <= 0)) {
      this.error.set('pur.quotation.invalidSupplier'); this.supplierInput()?.nativeElement.focus(); return;
    }
    if (this.validOn && !validRequisitionDate(this.validOn)) {
      this.error.set('pur.quotation.invalidDate'); this.validOnInput()?.nativeElement.focus(); return;
    }
    this.applied = { supplierId: this.supplierId ?? undefined,
      status: this.status as QuotationStatus || undefined, validOn: this.validOn || undefined };
    this.filtered.set(!!(this.applied.supplierId || this.applied.status || this.applied.validOn));
    if (resetPage) this.page.set(1);
    this.updateUrl(); this.load();
  }
  reset(): void { this.supplierId = null; this.status = ''; this.validOn = ''; this.search(); }
  onQuery(query: NzTableQueryParams): void {
    if (query.pageIndex === this.page() && query.pageSize === this.pageSize()) return;
    this.page.set(query.pageIndex); this.pageSize.set(query.pageSize); this.updateUrl(); this.load();
  }
  load(preserveError = false): void {
    const current = ++this.listGeneration; ++this.detailGeneration;
    this.detail.set(null); this.loading.set(true); this.loadFailed.set(false);
    if (!preserveError) this.clearError();
    this.api.listQuotations(this.rfqId, { ...this.applied, page: this.page(), pageSize: this.pageSize() })
      .subscribe({ next: result => { if (current !== this.listGeneration) return;
        this.rows.set(result.items); this.total.set(result.totalCount); this.loading.set(false); },
        error: error => { if (current !== this.listGeneration) return;
          this.rows.set([]); this.total.set(0); this.loadFailed.set(true);
          this.showError(error); this.loading.set(false); } });
    if (this.permissions.has('PUR.RFQ.VIEW')) this.loadRfq(current);
    else if (this.canQuote()) this.contextError.set('pur.quotation.rfqViewRequired');
  }
  private loadRfq(current: number): void {
    this.api.get(this.rfqId).subscribe({ next: rfq => { if (current === this.listGeneration) {
      this.rfq.set(rfq); this.contextError.set(null); } },
      error: () => { if (current === this.listGeneration) { this.rfq.set(null);
        if (this.canQuote()) this.contextError.set('pur.quotation.rfqContextUnavailable'); } } });
  }
  closeDetail(): void { ++this.detailGeneration; this.detail.set(null); }
  view(row: QuotationSummary): void {
    const current = ++this.detailGeneration; this.detail.set(null); this.clearError();
    this.api.getQuotation(this.rfqId, row.id).subscribe({
      next: detail => { if (current === this.detailGeneration) this.detail.set(detail); },
      error: error => { if (current !== this.detailGeneration) return;
        this.showError(error);
        if (error instanceof HttpErrorResponse && error.status === 404) this.load(true);
      }
    });
  }
  startCreate(): void {
    const rfq = this.rfq();
    if (!this.canQuote() || !rfq || rfq.status !== 'Sent' || this.saving()) return;
    this.clearError(); this.validation.set([]); this.serverValidation.set([]); this.success.set(null); this.conflict.set(false);
    this.editId.set(null); this.detail.set(null);
    this.editVersion = null;
    const draft = { supplierId: null, quoteDate: '', validUntil: '', remark: '',
      lines: rfq.lines.map(line => ({ rfqLineId: line.id, unitPrice: '', currencyCode: '', taxRateId: null })) };
    this.editor.set(draft); this.editBaseline = JSON.stringify(draft);
  }
  startRevision(detail: QuotationDetail): void {
    if (!this.canQuote() || this.rfq()?.status !== 'Sent' || detail.status !== 'Active' || detail.isExpired || this.saving()) return;
    const current = detail.revisions.find(x => x.revisionNo === detail.currentRevisionNo);
    if (!current) return;
    this.clearError(); this.validation.set([]); this.serverValidation.set([]); this.success.set(null); this.conflict.set(false);
    this.editId.set(detail.id);
    this.editVersion = detail.version;
    const draft = { supplierId: detail.supplierId, quoteDate: current.quoteDate, validUntil: current.validUntil,
      remark: current.remark ?? '', lines: current.lines.map(line => ({ rfqLineId: line.rfqLineId,
        unitPrice: String(line.unitPrice), currencyCode: line.currencyCode, taxRateId: line.taxRateId })) };
    this.editor.set(draft); this.editBaseline = JSON.stringify(draft);
  }
  cancelEdit(): void { if (this.saving()) return;
    if (this.hasUnsavedChanges()) { this.dialogs.confirm({ titleKey: 'common.document.leaveTitle',
      contentKey: 'common.document.leaveContent', okKey: 'common.document.leave', danger: true })
      .subscribe(confirmed => { if (confirmed) this.discardEdit(); }); return; }
    this.discardEdit();
  }
  private discardEdit(): void { this.editor.set(null); this.editId.set(null); this.editVersion = null; this.editBaseline = '';
    this.validation.set([]); this.serverValidation.set([]); this.conflict.set(false); this.clearError(); }
  save(): void {
    const draft = this.editor(); const rfq = this.rfq();
    if (!draft || !rfq || rfq.status !== 'Sent' || !this.canQuote() || this.saving() || this.conflict()) return;
    const errors = quotationFieldErrors(draft);
    if (draft.lines.length !== rfq.lines.length || draft.lines.some(x => !rfq.lines.some(line => line.id === x.rfqLineId)))
      errors.push('lines');
    if (draft.supplierId && !rfq.invitations.some(x => x.supplierId === draft.supplierId)) errors.push('supplierId');
    this.serverValidation.set([]); this.validation.set(errors); this.success.set(null);
    if (errors.length) { this.error.set('pur.quotation.invalidForm'); this.traceId.set(null);
      const first = errors[0]; const index = /lines\.(\d+)/.exec(first)?.[1];
      const selector = index !== undefined ? `[data-testid="pur-quotation-${first.split('.')[2] === 'unitPrice' ? 'price' : first.split('.')[2] === 'currencyCode' ? 'currency' : 'tax'}-${index}"]` :
        `[data-testid="pur-quotation-edit-${({ supplierId: 'supplier', quoteDate: 'date', validUntil: 'until', remark: 'remark' } as Record<string, string>)[first] ?? 'supplier'}"]`;
      document.querySelector<HTMLElement>(selector)?.focus(); return; }
    this.clearError(); this.validation.set([]); this.serverValidation.set([]); this.saving.set(true);
    const generation = this.listGeneration; const id = this.editId(); const version = this.editVersion;
    if (id && !version) { this.saving.set(false); this.error.set('pur.quotation.conflict'); return; }
    const action = id ? this.api.reviseQuotation(this.rfqId, id, version!, draft) : this.api.createQuotation(this.rfqId, draft);
    action.subscribe({ next: saved => { if (generation !== this.listGeneration) return;
      this.saving.set(false); this.editor.set(null); this.editId.set(null); this.editVersion = null; this.editBaseline = '';
      this.success.set(id ? 'pur.quotation.revised' : 'pur.quotation.created');
      this.load(); this.api.getQuotation(this.rfqId, saved.id).subscribe({ next: detail => {
        if (generation + 1 === this.listGeneration) this.detail.set(detail); } });
    }, error: error => { if (generation !== this.listGeneration) return;
      this.saving.set(false); this.showError(error);
      if (error instanceof HttpErrorResponse && [404, 409, 422].includes(error.status)) this.load(true);
    } });
  }
  confirmWithdraw(detail: QuotationDetail): void {
    if (!this.canQuote() || this.rfq()?.status !== 'Sent' || detail.status !== 'Active' || detail.isExpired || this.saving()) return;
    this.dialogs.confirm({ titleKey: 'pur.quotation.confirmWithdraw', contentKey: 'pur.quotation.withdrawExplanation',
      okKey: 'pur.quotation.withdraw', danger: true }).subscribe(confirmed => {
      if (!confirmed || this.saving()) return;
      const generation = this.listGeneration; this.saving.set(true); this.clearError(); this.success.set(null);
      this.api.withdrawQuotation(this.rfqId, detail.id, detail.version).subscribe({ next: saved => {
        if (generation !== this.listGeneration) return;
        this.saving.set(false); this.success.set('pur.quotation.withdrawnSuccess'); this.load();
        this.api.getQuotation(this.rfqId, saved.id).subscribe({ next: latest => {
          if (generation + 1 === this.listGeneration) this.detail.set(latest); } });
      }, error: error => { if (generation !== this.listGeneration) return;
        this.saving.set(false); this.showError(error);
        if (error instanceof HttpErrorResponse && [404, 409, 422].includes(error.status)) this.load(true);
      } });
    });
  }
  reloadAfterConflict(): void { this.dialogs.confirm({ titleKey: 'pur.quotation.reloadConfirm',
    contentKey: 'common.document.leaveContent', okKey: 'pur.quotation.reload', danger: true })
    .subscribe(confirmed => { if (confirmed) { this.discardEdit(); this.load(); } }); }
}
