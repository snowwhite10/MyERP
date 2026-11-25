import { ChangeDetectionStrategy, Component, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzTableModule, NzTableQueryParams } from 'ng-zorro-antd/table';
import { apiErrorTraceId } from '../../core/http/api-error';
import { CompanyContextService } from '../../core/company/company-context.service';
import { ApiErrorDisplay } from '../../shared/api-error-display';
import { formatDecimalText } from '../../shared/decimal-text';
import { SalesSourceDetail, SalesSourceService, SalesSourceSummary, SalesSourceType } from './sales-source.service';

/** Read-only FI posting trace. No SD draft or pending row can appear in this committed source list. */
@Component({
  selector: 'erp-sales-source-page',
  imports: [FormsModule, TranslatePipe, NzButtonModule, NzInputModule, NzTableModule, ApiErrorDisplay],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="trace">
      <h1>{{ 'fi.salesSource.title' | translate }}</h1>
      <p class="hint">{{ 'fi.salesSource.intro' | translate }}</p>
      <form class="filters" (ngSubmit)="search()" data-testid="fi-source-filters">
        <label>{{ 'fi.salesSource.type' | translate }}
          <select name="type" [(ngModel)]="type" data-testid="fi-source-type">
            <option value="">{{ 'fi.salesSource.allTypes' | translate }}</option>
            <option value="Invoice">{{ 'fi.salesSource.invoice' | translate }}</option>
            <option value="Return">{{ 'fi.salesSource.return' | translate }}</option>
          </select></label>
        <label>{{ 'fi.salesSource.customerId' | translate }}
          <input nz-input name="customerId" type="text" inputmode="numeric" autocomplete="off" spellcheck="false"
            [(ngModel)]="customerId" data-testid="fi-source-customer" /></label>
        <label>{{ 'fi.salesSource.from' | translate }} <input name="from" type="date" [(ngModel)]="from" data-testid="fi-source-from" /></label>
        <label>{{ 'fi.salesSource.to' | translate }} <input name="to" type="date" [(ngModel)]="to" data-testid="fi-source-to" /></label>
        <button nz-button nzType="primary" type="submit" data-testid="fi-source-search">{{ 'fi.salesSource.search' | translate }}</button>
        <button nz-button type="button" (click)="reset()" data-testid="fi-source-reset">{{ 'fi.salesSource.reset' | translate }}</button>
      </form>
      <form class="lookup" (ngSubmit)="lookup()" data-testid="fi-source-lookup">
        <span>{{ 'fi.salesSource.keyLookup' | translate }}</span>
        <select name="lookupType" [(ngModel)]="lookupType" [attr.aria-label]="'fi.salesSource.type' | translate">
          <option value="Invoice">{{ 'fi.salesSource.invoice' | translate }}</option>
          <option value="Return">{{ 'fi.salesSource.return' | translate }}</option>
        </select>
        <input nz-input name="lookupId" type="text" inputmode="numeric" autocomplete="off" spellcheck="false"
          [(ngModel)]="lookupId" [attr.aria-label]="'fi.salesSource.sourceId' | translate" data-testid="fi-source-lookup-id" />
        <button nz-button type="submit" data-testid="fi-source-lookup-button">{{ 'fi.salesSource.findKey' | translate }}</button>
      </form>
      @if (validation()) { <p role="alert" class="validation" data-testid="fi-source-validation">{{ validation()! | translate }}</p> }
      @if (error()) {
        <erp-api-error-display [messageKey]="error()!" [traceId]="traceId()" data-testid="fi-source-error" />
        <button nz-button type="button" (click)="retry()" data-testid="fi-source-retry">{{ 'fi.salesSource.retry' | translate }}</button>
      }
      <div class="table-wrap">
        <nz-table #table [nzData]="rows()" [nzLoading]="loading()" [nzFrontPagination]="false" [nzTotal]="total()"
          [nzPageIndex]="page()" [nzPageSize]="pageSize()" [nzShowSizeChanger]="true" (nzQueryParams)="onQuery($event)"
          [nzNoResult]="(failed() ? 'fi.salesSource.loadFailed' : filtered() ? 'fi.salesSource.noResults' : 'fi.salesSource.empty') | translate"
          data-testid="fi-source-table">
          <thead><tr>
            <th nzColumnKey="id" [nzSortFn]="true">{{ 'fi.salesSource.fiId' | translate }}</th>
            <th>{{ 'fi.salesSource.type' | translate }}</th><th>{{ 'fi.salesSource.sourceId' | translate }}</th>
            <th>{{ 'fi.salesSource.customerId' | translate }}</th>
            <th nzColumnKey="businessDate" [nzSortFn]="true" nzSortOrder="descend">{{ 'fi.salesSource.date' | translate }}</th>
            <th class="num">{{ 'fi.salesSource.gross' | translate }}</th>
            <th class="num">{{ 'fi.salesSource.baseGross' | translate }}</th>
            <th class="num">{{ 'fi.salesSource.open' | translate }}</th>
            <th>{{ 'fi.salesSource.journal' | translate }}</th>
          </tr></thead>
          <tbody>@for (row of table.data; track row.id) {
            <tr [attr.data-testid]="'fi-source-row-' + row.id">
              <td><button nz-button nzType="link" type="button" (click)="view(row.id)" [attr.data-testid]="'fi-source-view-' + row.id">#{{ row.id }}</button></td>
              <td>{{ typeKey(row.sourceType) | translate }}</td><td>#{{ row.sourceId }}</td><td>#{{ row.customerId }}</td>
              <td>{{ row.businessDate }}</td><td class="num">{{ money(row.grossAmount) }}&nbsp;{{ row.currency }}</td>
              <td class="num">{{ money(row.baseGrossAmount) }}&nbsp;{{ row.baseCurrency }}</td>
              <td class="num">{{ row.openBaseAmount === null ? '—' : money(row.openBaseAmount) + ' ' + row.baseCurrency }}</td>
              <td>#{{ row.glJournalId }}</td>
            </tr>
          }</tbody>
        </nz-table>
      </div>
      @if (detail(); as d) {
        <section class="detail" data-testid="fi-source-detail">
          <header><h2>{{ typeKey(d.source.sourceType) | translate }} · #{{ d.source.sourceId }}</h2>
            <button nz-button type="button" (click)="detail.set(null)">{{ 'fi.salesSource.close' | translate }}</button></header>
          <dl>
            <dt>{{ 'fi.salesSource.fiId' | translate }}</dt><dd>#{{ d.source.id }}</dd>
            <dt>{{ 'fi.salesSource.version' | translate }}</dt><dd>{{ d.source.sourceVersion }}</dd>
            <dt>{{ 'fi.salesSource.customerId' | translate }}</dt><dd>#{{ d.source.customerId }}</dd>
            <dt>{{ 'fi.salesSource.date' | translate }}</dt><dd>{{ d.source.businessDate }}</dd>
            <dt>{{ 'fi.salesSource.net' | translate }}</dt><dd>{{ money(d.netAmount) }}&nbsp;{{ d.source.currency }}</dd>
            <dt>{{ 'fi.salesSource.tax' | translate }}</dt><dd>{{ money(d.taxAmount) }}&nbsp;{{ d.source.currency }}</dd>
            <dt>{{ 'fi.salesSource.gross' | translate }}</dt><dd>{{ money(d.source.grossAmount) }}&nbsp;{{ d.source.currency }}</dd>
            <dt>{{ 'fi.salesSource.baseNet' | translate }}</dt><dd>{{ money(d.baseNetAmount) }}&nbsp;{{ d.source.baseCurrency }}</dd>
            <dt>{{ 'fi.salesSource.baseTax' | translate }}</dt><dd>{{ money(d.baseTaxAmount) }}&nbsp;{{ d.source.baseCurrency }}</dd>
            <dt>{{ 'fi.salesSource.baseGross' | translate }}</dt><dd>{{ money(d.source.baseGrossAmount) }}&nbsp;{{ d.source.baseCurrency }}</dd>
            <dt>{{ 'fi.salesSource.exchangeRate' | translate }}</dt><dd>{{ d.exchangeRate }}</dd>
            <dt>{{ 'fi.salesSource.receivable' | translate }}</dt><dd>{{ d.source.receivableId === null ? '—' : '#' + d.source.receivableId }}</dd>
            <dt>{{ 'fi.salesSource.open' | translate }}</dt><dd data-testid="fi-source-open">{{ d.source.openBaseAmount === null ? '—' : money(d.source.openBaseAmount) + ' ' + d.source.baseCurrency }}</dd>
            <dt>{{ 'fi.salesSource.journal' | translate }}</dt><dd>#{{ d.source.glJournalId }}</dd>
          </dl>
          <h3>{{ 'fi.salesSource.allocations' | translate }}</h3>
          @if (!d.allocations.length) { <p>{{ 'fi.salesSource.noAllocations' | translate }}</p> }
          @else { <div class="table-wrap"><table class="allocations" data-testid="fi-source-allocations">
            <thead><tr><th>{{ 'fi.salesSource.originalInvoice' | translate }}</th><th>{{ 'fi.salesSource.creditSource' | translate }}</th>
              <th>{{ 'fi.salesSource.invoiceLine' | translate }}</th><th class="num">{{ 'fi.salesSource.quantity' | translate }}</th>
              <th class="num">{{ 'fi.salesSource.gross' | translate }}</th><th class="num">{{ 'fi.salesSource.baseGross' | translate }}</th></tr></thead>
            <tbody>@for (a of d.allocations; track a.id) { <tr>
              <td><button nz-button nzType="link" type="button" (click)="openKey('Invoice', a.originalSourceId)">#{{ a.originalSourceId }}</button></td>
              <td><button nz-button nzType="link" type="button" (click)="view(a.creditSourceId)">#{{ a.creditSourceId }}</button></td>
              <td>#{{ a.invoiceLineId }}</td><td class="num">{{ a.quantity }}</td>
              <td class="num">{{ money(a.grossAmount) }}</td><td class="num">{{ money(a.baseGrossAmount) }}</td>
            </tr> }</tbody>
          </table></div> }
        </section>
      }
    </section>`,
  styles: `:host{display:block;min-width:0}.trace{display:grid;gap:16px;min-width:0;color:var(--erp-color-text)}h1,h2,h3,p{margin:0}h1{font-size:20px}h2{font-size:17px}h3{font-size:15px}.hint{color:var(--erp-color-text-secondary)}.filters,.lookup,header{display:flex;gap:8px;align-items:center;flex-wrap:wrap}.filters label{display:flex;gap:4px;align-items:center}.filters input[type=text],.lookup input{width:130px}.filters input[type=date]{width:145px}select,input[type=date]{background:var(--erp-color-bg-container);color:var(--erp-color-text);border:1px solid var(--erp-color-border);border-radius:4px;padding:4px}.validation{color:var(--erp-color-error-text)}.table-wrap{overflow-x:auto;min-width:0}.num{text-align:right;white-space:nowrap;font-variant-numeric:tabular-nums}.detail{display:grid;gap:12px;padding:16px;border:1px solid var(--erp-color-border);border-radius:8px;background:var(--erp-color-bg-container)}.detail header{justify-content:space-between}.detail dl{display:grid;grid-template-columns:minmax(125px,180px) 1fr;gap:7px;margin:0}.detail dt{font-weight:600}.detail dd{margin:0;overflow-wrap:anywhere}.allocations{width:100%;border-collapse:collapse}.allocations th,.allocations td{padding:6px 8px;border-bottom:1px solid var(--erp-color-border)}button:focus-visible,input:focus-visible,select:focus-visible{outline:2px solid var(--erp-color-primary);outline-offset:2px}@media(max-width:600px){.detail dl{grid-template-columns:1fr}}`,
})
export class SalesSourcePage {
  private readonly api = inject(SalesSourceService);
  private readonly company = inject(CompanyContextService);
  readonly rows = signal<SalesSourceSummary[]>([]);
  readonly detail = signal<SalesSourceDetail | null>(null);
  readonly page = signal(1); readonly pageSize = signal(20); readonly total = signal(0);
  readonly loading = signal(false); readonly failed = signal(false); readonly filtered = signal(false);
  readonly error = signal<string | null>(null); readonly traceId = signal<string | null>(null);
  readonly validation = signal<string | null>(null);
  type = ''; customerId = ''; from = ''; to = ''; lookupType: SalesSourceType = 'Invoice'; lookupId = '';
  private sort = '-businessDate'; private generation = 0; private detailGeneration = 0;
  private lastRead: (() => void) | null = null;

  constructor() {
    effect(() => { this.company.current(); untracked(() => { this.lookupId = ''; this.detail.set(null); this.reset(); }); });
  }
  typeKey(type: SalesSourceType): string { return `fi.salesSource.${type.toLowerCase()}`; }
  money(value: string): string { return formatDecimalText(value, 'en-US'); }
  reset(): void { this.type = ''; this.customerId = ''; this.from = ''; this.to = ''; this.search(); }
  search(): void {
    this.validation.set(null);
    if (this.customerId.trim() && (!/^[1-9]\d*$/.test(this.customerId.trim()) ||
      !Number.isSafeInteger(Number(this.customerId.trim())))) this.validation.set('fi.salesSource.customerInvalid');
    else if (this.from && this.to && this.from > this.to) this.validation.set('fi.salesSource.rangeInvalid');
    if (this.validation()) return;
    this.filtered.set(!!(this.type || this.customerId.trim() || this.from || this.to));
    this.page.set(1); ++this.detailGeneration; this.detail.set(null); this.load();
  }
  onQuery(query: NzTableQueryParams): void {
    const sorted = query.sort.find(x => x.value);
    const sort = sorted ? (sorted.value === 'descend' ? '-' : '') + sorted.key : '-businessDate';
    if (query.pageIndex === this.page() && query.pageSize === this.pageSize() && sort === this.sort) return;
    this.page.set(query.pageIndex); this.pageSize.set(query.pageSize); this.sort = sort;
    ++this.detailGeneration; this.detail.set(null); this.load();
  }
  load(): void {
    const current = ++this.generation; this.loading.set(true); this.failed.set(false); this.error.set(null);
    this.lastRead = () => this.load();
    this.api.list({ page: this.page(), pageSize: this.pageSize(), customerId: this.customerId.trim() ? Number(this.customerId) : undefined,
      sourceType: this.type ? this.type as SalesSourceType : undefined, from: this.from, to: this.to, sort: this.sort }).subscribe({
      next: result => { if (current !== this.generation) return; this.rows.set(result.items); this.total.set(result.totalCount); this.loading.set(false); },
      error: e => { if (current !== this.generation) return; this.rows.set([]); this.total.set(0); this.loading.set(false);
        this.failed.set(true); this.fail(e); },
    });
  }
  view(id: number): void {
    const current = ++this.detailGeneration; this.detail.set(null); this.error.set(null); this.lastRead = () => this.view(id);
    this.api.get(id).subscribe({ next: result => { if (current === this.detailGeneration) this.detail.set(result); },
      error: e => { if (current === this.detailGeneration) { this.detail.set(null); this.fail(e); } } });
  }
  lookup(): void {
    this.validation.set(null);
    if (!/^[1-9]\d*$/.test(this.lookupId.trim()) || !Number.isSafeInteger(Number(this.lookupId.trim()))) {
      this.validation.set('fi.salesSource.keyInvalid'); return;
    }
    this.openKey(this.lookupType, Number(this.lookupId));
  }
  openKey(type: SalesSourceType, id: number): void {
    const current = ++this.detailGeneration; this.detail.set(null); this.error.set(null); this.lastRead = () => this.openKey(type, id);
    this.api.byKey(type, id).subscribe({ next: result => { if (current === this.detailGeneration) this.detail.set(result); },
      error: e => { if (current === this.detailGeneration) { this.detail.set(null); this.fail(e); } } });
  }
  retry(): void { this.lastRead?.(); }
  private fail(error: unknown): void { this.error.set(this.api.errorKey(error)); this.traceId.set(apiErrorTraceId(error)); }
}
