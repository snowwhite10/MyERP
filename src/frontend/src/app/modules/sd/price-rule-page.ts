import { ChangeDetectionStrategy, Component, DestroyRef, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { TimeoutError } from 'rxjs';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzTableModule, NzTableQueryParams } from 'ng-zorro-antd/table';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { HasPermissionDirective } from '../../core/auth/has-permission.directive';
import { CompanyContextService } from '../../core/company/company-context.service';
import { apiErrorKey, apiErrorTraceId } from '../../core/http/api-error';
import { ApiErrorDisplay } from '../../shared/api-error-display';
import { formatDecimalText } from '../../shared/decimal-text';
import { ErpDialogService } from '../../shared/dialogs/dialog.service';
import { nextTableState, PAGE_SIZE_OPTIONS, sortOrderOf } from '../../shared/list-page/table-query';
import { Customer, CustomerService } from '../md/customer.service';
import { Item, ItemService } from '../md/item.service';
import { PRICE_RULE_DEFAULT_SORT, PRICE_RULE_SORTS, priceRuleDeactivateFailureKey, SalesPriceRule,
  SalesPriceRuleQuery, SalesPriceRuleService } from './price-rule.service';

type Scope = '' | 'default' | 'customer';
type Status = '' | 'active' | 'inactive';
interface Notice { type: 'success' | 'warning' | 'error'; key: string; params: Record<string, unknown>; traceId: string | null }

function validDate(value: string): boolean {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) return false;
  const [year, month, day] = value.split('-').map(Number);
  return year >= 1 && month >= 1 && month <= 12 && day >= 1 &&
    day <= new Date(Date.UTC(year, month, 0)).getUTCDate();
}
function positiveId(value: string | null): number | null {
  const id = Number(value);
  return value && Number.isSafeInteger(id) && id > 0 ? id : null;
}

/**
 * WU-SD-005b1a interface 2.11 (REQ_F4112.4.1, ADR-161): company price rules with item/customer/currency/date/status
 * filters, paging and sorting kept in the URL, current MD labels, and versioned deactivation. Create/edit is 005b1b.
 */
@Component({
  selector: 'erp-sales-price-rule-page',
  imports: [FormsModule, HasPermissionDirective, TranslatePipe, NzAlertModule, NzButtonModule, NzTableModule, NzTagModule, ApiErrorDisplay],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="rule-page">
      <header><h1>{{ 'sd.priceRule.title' | translate }}</h1>
        <p class="hint">{{ 'sd.priceRule.intro' | translate }}</p></header>
      <form class="filters" (ngSubmit)="search()" data-testid="sd-price-rule-filters">
        <div class="field"><span id="price-rule-item-label">{{ 'sd.priceRule.item' | translate }}</span>
          <div class="picker" role="group" aria-labelledby="price-rule-item-label">
            <span data-testid="sd-price-rule-item">{{ itemFilterLabel() || ('sd.priceRule.anyItem' | translate) }}</span>
            <button nz-button type="button" (click)="pickItem()" data-testid="sd-price-rule-item-pick">{{ 'sd.priceRule.chooseItem' | translate }}</button>
            @if (itemId !== null) { <button nz-button nzType="link" type="button" (click)="clearItem()" data-testid="sd-price-rule-item-clear">{{ 'sd.priceRule.clear' | translate }}</button> }
          </div></div>
        <label class="field">{{ 'sd.priceRule.scope' | translate }}
          <select name="scope" [(ngModel)]="scope" data-testid="sd-price-rule-scope">
            <option value="">{{ 'sd.priceRule.scopeAll' | translate }}</option>
            <option value="default">{{ 'sd.priceRule.scopeDefault' | translate }}</option>
            <option value="customer">{{ 'sd.priceRule.scopeCustomer' | translate }}</option>
          </select></label>
        @if (scope === 'customer') { <div class="field"><span id="price-rule-customer-label">{{ 'sd.priceRule.customer' | translate }}</span>
          <div class="picker" role="group" aria-labelledby="price-rule-customer-label">
            <span data-testid="sd-price-rule-customer">{{ customerFilterLabel() || ('sd.priceRule.noCustomer' | translate) }}</span>
            <button nz-button type="button" (click)="pickCustomer()" data-testid="sd-price-rule-customer-pick">{{ 'sd.priceRule.chooseCustomer' | translate }}</button>
          </div></div> }
        <label class="field">{{ 'sd.priceRule.currency' | translate }}
          <input name="currency" autocomplete="off" maxlength="3" [(ngModel)]="currency" data-testid="sd-price-rule-currency" /></label>
        <label class="field">{{ 'sd.priceRule.asOf' | translate }}
          <input type="date" name="asOf" [(ngModel)]="asOf" data-testid="sd-price-rule-as-of" /></label>
        <label class="field">{{ 'sd.priceRule.status' | translate }}
          <select name="status" [(ngModel)]="status" data-testid="sd-price-rule-status">
            <option value="">{{ 'sd.priceRule.statusAll' | translate }}</option>
            <option value="active">{{ 'sd.priceRule.active' | translate }}</option>
            <option value="inactive">{{ 'sd.priceRule.inactive' | translate }}</option>
          </select></label>
        <div class="actions">
          <button nz-button nzType="primary" type="submit" data-testid="sd-price-rule-search">{{ 'sd.priceRule.search' | translate }}</button>
          <button nz-button type="button" (click)="reset()" data-testid="sd-price-rule-reset">{{ 'sd.priceRule.reset' | translate }}</button>
        </div>
      </form>
      @if (validation()) { <p class="error" role="alert" data-testid="sd-price-rule-validation">{{ validation()! | translate }}</p> }
      <div aria-live="polite" class="notice">
        @if (notice(); as shown) { <nz-alert [nzType]="shown.type" nzShowIcon data-testid="sd-price-rule-notice"
          [nzMessage]="shown.key | translate: shown.params" [nzDescription]="shown.traceId ? 'Trace ID: ' + shown.traceId : null" /> }
      </div>
      @if (error()) { <erp-api-error-display [messageKey]="error()!" [traceId]="traceId()" data-testid="sd-price-rule-error" />
        <button nz-button type="button" (click)="load()" data-testid="sd-price-rule-retry">{{ 'sd.priceRule.retry' | translate }}</button> }
      <div class="scroll"><nz-table #table [nzData]="rows()" [nzLoading]="loading()" [nzFrontPagination]="false"
        [nzTotal]="total()" [nzPageIndex]="page()" [nzPageSize]="pageSize()" nzShowSizeChanger
        [nzPageSizeOptions]="pageSizeOptions" (nzQueryParams)="onQuery($event)"
        [nzNoResult]="(error() ? 'sd.priceRule.loadFailed' : filtered() ? 'sd.priceRule.noResults' : 'sd.priceRule.empty') | translate"
        data-testid="sd-price-rule-table">
        <thead><tr>
          <th nzColumnKey="id" [nzSortFn]="true" [nzSortOrder]="sortOrderOf(sort(), 'id')">{{ 'sd.priceRule.id' | translate }}</th>
          <th>{{ 'sd.priceRule.item' | translate }}</th><th>{{ 'sd.priceRule.customer' | translate }}</th>
          <th>{{ 'sd.priceRule.currency' | translate }}</th>
          <th nzColumnKey="validFrom" [nzSortFn]="true" [nzSortOrder]="sortOrderOf(sort(), 'validFrom')">{{ 'sd.priceRule.validFrom' | translate }}</th>
          <th>{{ 'sd.priceRule.validTo' | translate }}</th>
          <th class="number">{{ 'sd.priceRule.unitPrice' | translate }}</th>
          <th class="number">{{ 'sd.priceRule.discountCeiling' | translate }}</th>
          <th>{{ 'sd.priceRule.status' | translate }}</th><th class="number">{{ 'sd.priceRule.version' | translate }}</th>
          <th>{{ 'sd.priceRule.actions' | translate }}</th></tr></thead>
        <tbody>@for (row of table.data; track row.id) { <tr [attr.data-testid]="'sd-price-rule-row-' + row.id">
          <td class="number">{{ row.id }}</td>
          <td>{{ itemLabel(row) || ('sd.priceRule.deletedItem' | translate: { id: row.itemId }) }}</td>
          <td>@if (row.customerId === null) { <nz-tag nzColor="blue">{{ 'sd.priceRule.companyDefault' | translate }}</nz-tag> }
            @else { {{ customerLabel(row) || ('sd.priceRule.unknownCustomer' | translate: { id: row.customerId }) }} }</td>
          <td>{{ row.currencyCode }}</td><td class="date">{{ row.validFrom }}</td><td class="date">{{ row.validTo }}</td>
          <td class="number">{{ decimal(row.unitPrice) }}</td><td class="number">{{ decimal(row.discountCeiling) }}%</td>
          <td><nz-tag [nzColor]="row.isActive ? 'green' : 'default'">{{ (row.isActive ? 'sd.priceRule.active' : 'sd.priceRule.inactive') | translate }}</nz-tag></td>
          <td class="number">{{ row.version }}</td>
          <td>@if (row.isActive) { <button nz-button nzType="link" nzDanger type="button" *erpHasPermission="'SAL.PRICING.DEACTIVATE'"
              [disabled]="busyId() !== null" [nzLoading]="busyId() === row.id" (click)="deactivate(row)"
              [attr.data-testid]="'sd-price-rule-deactivate-' + row.id">{{ 'sd.priceRule.deactivate' | translate }}</button> }</td>
        </tr> }</tbody>
      </nz-table></div>
    </section>`,
  styles: `.rule-page{display:grid;gap:16px;color:var(--erp-color-text);min-width:0}h1{margin:0}.hint{color:var(--erp-color-text-secondary);margin:4px 0 0}.filters{display:flex;gap:12px;flex-wrap:wrap;align-items:end}.field{display:grid;gap:4px;min-width:0}.picker{display:flex;gap:8px;align-items:center;flex-wrap:wrap;min-height:32px}.picker span{overflow-wrap:anywhere}.actions{display:flex;gap:8px}.filters input,.filters select{min-height:32px;padding:4px;background:var(--erp-color-bg-container);color:var(--erp-color-text);border:1px solid var(--erp-color-border);border-radius:4px}.filters input[name=currency]{width:6em;text-transform:uppercase}.notice:empty{display:none}.error{color:var(--erp-color-error-text);margin:0}.scroll{overflow-x:auto;min-width:0}.scroll nz-table{display:block;min-width:1000px}td{overflow-wrap:anywhere}th,td.date,td.number{white-space:nowrap}.rule-page>button{justify-self:start}.number{text-align:right!important;font-variant-numeric:tabular-nums}button:focus-visible,input:focus-visible,select:focus-visible{outline:2px solid var(--erp-color-primary);outline-offset:2px}`,
})
export class SalesPriceRulePage {
  private readonly api = inject(SalesPriceRuleService);
  private readonly items = inject(ItemService);
  private readonly customers = inject(CustomerService);
  private readonly dialogs = inject(ErpDialogService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly company = inject(CompanyContextService);
  readonly pageSizeOptions = [...PAGE_SIZE_OPTIONS];
  readonly sortOrderOf = sortOrderOf;
  readonly rows = signal<SalesPriceRule[]>([]);
  readonly loading = signal(false); readonly total = signal(0);
  readonly page = signal(1); readonly pageSize = signal(20); readonly sort = signal(PRICE_RULE_DEFAULT_SORT);
  readonly filtered = signal(false);
  readonly validation = signal<string | null>(null);
  readonly error = signal<string | null>(null); readonly traceId = signal<string | null>(null);
  readonly notice = signal<Notice | null>(null);
  readonly busyId = signal<number | null>(null);
  readonly itemFilterLabel = signal(''); readonly customerFilterLabel = signal('');
  itemId: number | null = null; customerId: number | null = null;
  scope: Scope = ''; currency = ''; asOf = ''; status: Status = '';
  private applied: Omit<SalesPriceRuleQuery, 'page' | 'pageSize' | 'sort'> = {};
  private listEpoch = 0; private actionEpoch = 0; private first = true;

  constructor() {
    const params = this.route.snapshot.queryParamMap;
    this.itemId = positiveId(params.get('itemId'));
    this.customerId = positiveId(params.get('customerId'));
    this.scope = this.customerId !== null ? 'customer' : params.get('defaultOnly') === 'true' ? 'default' : '';
    this.currency = params.get('currency') ?? ''; this.asOf = params.get('asOf') ?? '';
    const active = params.get('active');
    this.status = active === 'true' ? 'active' : active === 'false' ? 'inactive' : '';
    if (this.itemId !== null) this.itemFilterLabel.set(`ID ${this.itemId}`);
    if (this.customerId !== null) this.customerFilterLabel.set(`ID ${this.customerId}`);
    const page = Number(params.get('page'));
    if (Number.isSafeInteger(page) && page > 0) this.page.set(page);
    const pageSize = Number(params.get('pageSize'));
    if (PAGE_SIZE_OPTIONS.some(value => value === pageSize)) this.pageSize.set(pageSize);
    const sort = params.get('sort') ?? '';
    if ((PRICE_RULE_SORTS as readonly string[]).includes(sort.replace(/^-/, ''))) this.sort.set(sort);
    inject(DestroyRef).onDestroy(() => { this.listEpoch++; this.actionEpoch++; });
    effect(() => { this.company.current(); untracked(() => {
      this.listEpoch++; this.actionEpoch++; this.busyId.set(null); this.notice.set(null);
      this.rows.set([]); this.total.set(0);
      if (!this.first) { this.page.set(1); this.clearItem(); this.customerId = null; this.customerFilterLabel.set('');
        if (this.scope === 'customer') this.scope = ''; }
      this.first = false; this.search(false);
    }); });
  }

  itemLabel(row: SalesPriceRule): string { return row.itemCode ? `${row.itemCode} · ${row.itemName ?? ''}` : ''; }
  customerLabel(row: SalesPriceRule): string { return row.customerCode ? `${row.customerCode} · ${row.customerName ?? ''}` : ''; }
  /** Server digits with grouping, trailing zeros dropped, never rounded. */
  decimal(value: string): string { return formatDecimalText(String(value)); }

  search(resetPage = true): void {
    const currency = this.currency.trim().toUpperCase();
    if (currency && !/^[A-Z]{3}$/.test(currency)) { this.validation.set('sd.priceRule.invalidCurrency'); return; }
    if (this.asOf && !validDate(this.asOf)) { this.validation.set('sd.priceRule.invalidDate'); return; }
    if (this.scope === 'customer' && this.customerId === null) { this.validation.set('sd.priceRule.customerRequired'); return; }
    this.validation.set(null); this.currency = currency;
    this.applied = { itemId: this.itemId ?? undefined,
      customerId: this.scope === 'customer' ? this.customerId ?? undefined : undefined,
      defaultOnly: this.scope === 'default' ? true : undefined, currencyCode: currency || undefined,
      asOf: this.asOf || undefined, active: this.status === '' ? undefined : this.status === 'active' };
    this.filtered.set(Object.values(this.applied).some(value => value !== undefined));
    if (resetPage) this.page.set(1);
    this.updateUrl(); this.load();
  }

  reset(): void {
    this.clearItem(); this.customerId = null; this.customerFilterLabel.set('');
    this.scope = ''; this.currency = ''; this.asOf = ''; this.status = ''; this.notice.set(null); this.search();
  }

  onQuery(params: NzTableQueryParams): void {
    const next = nextTableState(params, { page: this.page(), pageSize: this.pageSize(), sort: this.sort() },
      PRICE_RULE_SORTS, PRICE_RULE_DEFAULT_SORT);
    if (!next) return;
    this.page.set(next.page); this.pageSize.set(next.pageSize); this.sort.set(next.sort);
    this.updateUrl(); this.load();
  }

  load(): void {
    const epoch = ++this.listEpoch;
    this.loading.set(true); this.error.set(null); this.traceId.set(null); this.rows.set([]); this.total.set(0);
    this.api.list({ ...this.applied, page: this.page(), pageSize: this.pageSize(), sort: this.sort() }).subscribe({
      next: result => { if (epoch !== this.listEpoch) return;
        this.rows.set(result.items); this.total.set(result.totalCount); this.loading.set(false);
        this.adoptFilterLabels(result.items); },
      error: failure => { if (epoch !== this.listEpoch) return;
        this.error.set(failure instanceof TimeoutError ? 'sd.priceRule.timeout' :
          apiErrorKey(failure, ['COMMON.REQUEST.VALIDATION_FAILED']));
        this.traceId.set(apiErrorTraceId(failure)); this.loading.set(false); },
    });
  }

  pickItem(): void {
    const epoch = this.listEpoch;
    this.dialogs.select<Item>({ titleKey: 'sd.priceRule.chooseItem',
      columns: [{ key: 'code', labelKey: 'sd.priceRule.code', value: row => row.code },
        { key: 'name', labelKey: 'sd.priceRule.name', value: row => row.name }],
      load: query => this.items.list(query), rowKey: row => row.id }).subscribe(rows => {
      if (epoch !== this.listEpoch || !rows?.[0]) return;
      this.itemId = rows[0].id; this.itemFilterLabel.set(`${rows[0].code} · ${rows[0].name}`);
    });
  }

  clearItem(): void { this.itemId = null; this.itemFilterLabel.set(''); }

  pickCustomer(): void {
    const epoch = this.listEpoch;
    this.dialogs.select<Customer>({ titleKey: 'sd.priceRule.chooseCustomer',
      columns: [{ key: 'code', labelKey: 'sd.priceRule.code', value: row => row.code },
        { key: 'name', labelKey: 'sd.priceRule.name', value: row => row.name }],
      load: query => this.customers.list(query), rowKey: row => row.id }).subscribe(rows => {
      if (epoch !== this.listEpoch || !rows?.[0]) return;
      this.customerId = rows[0].id; this.customerFilterLabel.set(`${rows[0].code} · ${rows[0].name}`);
    });
  }

  /** Versioned retirement (ADR-158): confirm, send the version shown, then reload so the list matches the server. */
  deactivate(row: SalesPriceRule): void {
    if (this.busyId() !== null || !row.isActive) return;
    const epoch = ++this.actionEpoch;
    const label = this.itemLabel(row) || `ID ${row.itemId}`;
    this.dialogs.confirm({ titleKey: 'sd.priceRule.deactivateTitle', contentKey: 'sd.priceRule.deactivateConfirm',
      params: { id: row.id, item: label, from: row.validFrom, to: row.validTo }, okKey: 'sd.priceRule.deactivate',
      danger: true }).subscribe(ok => {
      if (!ok || epoch !== this.actionEpoch) return;
      this.busyId.set(row.id); this.notice.set(null);
      this.api.deactivate(row.id, row.version).subscribe({
        next: saved => { if (epoch !== this.actionEpoch) return;
          this.busyId.set(null);
          this.notice.set({ type: 'success', key: 'sd.priceRule.deactivated', traceId: null,
            params: { id: saved.id, version: saved.version } });
          this.load(); },
        error: failure => { if (epoch !== this.actionEpoch) return;
          this.busyId.set(null);
          const named = priceRuleDeactivateFailureKey(failure);
          this.notice.set({ type: named ? 'warning' : 'error', params: { id: row.id }, traceId: apiErrorTraceId(failure),
            key: named ?? (failure instanceof TimeoutError ? 'sd.priceRule.deactivateUnknown' : apiErrorKey(failure, [])) });
          if (named) this.load(); },
      });
    });
  }

  /** A filter restored from the URL shows its ID until a returned row names it. */
  private adoptFilterLabels(rows: SalesPriceRule[]): void {
    const item = this.applied.itemId !== undefined && rows.find(row => row.itemId === this.applied.itemId && row.itemCode);
    if (item && this.itemId === item.itemId) this.itemFilterLabel.set(this.itemLabel(item));
    const customer = this.applied.customerId !== undefined &&
      rows.find(row => row.customerId === this.applied.customerId && row.customerCode);
    if (customer && this.customerId === customer.customerId) this.customerFilterLabel.set(this.customerLabel(customer));
  }

  private updateUrl(): void {
    void this.router.navigate([], { relativeTo: this.route, replaceUrl: true, queryParams: {
      itemId: this.applied.itemId ?? null, customerId: this.applied.customerId ?? null,
      defaultOnly: this.applied.defaultOnly ? true : null, currency: this.applied.currencyCode ?? null,
      asOf: this.applied.asOf ?? null, active: this.applied.active ?? null,
      page: this.page() > 1 ? this.page() : null, pageSize: this.pageSize() !== 20 ? this.pageSize() : null,
      sort: this.sort() !== PRICE_RULE_DEFAULT_SORT ? this.sort() : null,
    } });
  }
}
