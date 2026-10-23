import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, effect, inject, signal, untracked, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzTableModule, NzTableQueryParams } from 'ng-zorro-antd/table';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { CompanyContextService } from '../../core/company/company-context.service';
import { HasPermissionDirective } from '../../core/auth/has-permission.directive';
import { apiErrorTraceId } from '../../core/http/api-error';
import { ApiErrorDisplay } from '../../shared/api-error-display';
import { validRequisitionDate } from './requisition.service';
import { QuotationDetail, QuotationQuery, QuotationStatus, QuotationSummary, RfqService } from './rfq.service';

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
    </section>`,
  styles: `.quotes{display:grid;gap:16px;color:var(--erp-color-text)}header,.filters{display:flex;gap:8px;align-items:end;flex-wrap:wrap}h1{font-size:20px;margin:0;text-wrap:balance}.filters label{display:grid;gap:4px;font-size:12px}.filters small{color:var(--erp-color-error,#cf1322);max-width:180px}.filters input,.filters select{min-height:32px;background:var(--erp-color-bg-container);color:var(--erp-color-text);border:1px solid var(--erp-color-border);border-radius:4px}.filters input[type=number]{width:120px}.detail{padding:16px;border:1px solid var(--erp-color-border);overflow-x:auto}.detail table{width:100%}.detail th,.detail td{padding:6px;text-align:left;border-bottom:1px solid var(--erp-color-border);font-variant-numeric:tabular-nums}.detail td{overflow-wrap:anywhere}.revision{margin-top:16px;border-top:1px solid var(--erp-color-border)}.revision th:nth-child(2),.revision td:nth-child(2){text-align:right}button:focus-visible,a:focus-visible,input:focus-visible,select:focus-visible{outline:2px solid var(--erp-color-primary);outline-offset:2px}`,
})
export class QuotationPage {
  private readonly api = inject(RfqService);
  private readonly company = inject(CompanyContextService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  readonly rfqId = Number(this.route.snapshot.paramMap.get('id'));
  readonly rows = signal<QuotationSummary[]>([]);
  readonly detail = signal<QuotationDetail | null>(null);
  readonly total = signal(0); readonly page = signal(1); readonly pageSize = signal(20);
  readonly loading = signal(false); readonly loadFailed = signal(false); readonly filtered = signal(false);
  readonly error = signal<string | null>(null); readonly traceId = signal<string | null>(null);
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
        this.rows.set([]); this.total.set(0); this.detail.set(null);
        this.search();
      }
    }); });
  }
  statusKey(status: QuotationStatus): string { return 'pur.quotation.' + status.toLowerCase(); }
  private clearError(): void { this.error.set(null); this.traceId.set(null); }
  private showError(error: unknown): void {
    this.error.set(this.api.errorKey(error)); this.traceId.set(apiErrorTraceId(error));
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
}
