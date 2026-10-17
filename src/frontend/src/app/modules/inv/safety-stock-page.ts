import { ChangeDetectionStrategy, Component, ElementRef, effect, inject, signal, untracked, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzTableModule, NzTableQueryParams } from 'ng-zorro-antd/table';
import { apiErrorTraceId, apiValidationDetails } from '../../core/http/api-error';
import { PermissionService } from '../../core/auth/permission.service';
import { CompanyContextService } from '../../core/company/company-context.service';
import { ErpDialogService } from '../../shared/dialogs/dialog.service';
import { InventoryOption, InventorySelectionOptionsService } from './selection-options.service';
import { SafetyStockRow, SafetyStockService, validSafetyQuantity } from './safety-stock.service';

@Component({
  selector: 'erp-safety-stock-page',
  imports: [FormsModule, TranslatePipe, NzAlertModule, NzButtonModule, NzTableModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="safety" data-testid="inv-safety-page">
      <header><h1>{{ 'inv.safety.title' | translate }}</h1>
        @if (canEdit()) { <button nz-button nzType="primary" (click)="newThreshold()" [disabled]="busy()" data-testid="inv-safety-new">{{ 'inv.safety.new' | translate }}</button> }
      </header>
      <nz-alert nzType="info" nzShowIcon [nzMessage]="'inv.safety.disclaimer' | translate" />
      <div class="filters">
        <input type="search" name="keyword" autocomplete="off" [(ngModel)]="keyword" [attr.aria-label]="'inv.safety.keyword' | translate" [placeholder]="'inv.safety.keyword' | translate" (keyup.enter)="search()" data-testid="inv-safety-keyword" />
        <button nz-button (click)="chooseWarehouse()" data-testid="inv-safety-warehouse">{{ warehouseFilter() ? warehouseFilter()!.code + ' · ' + warehouseFilter()!.name : ('inv.safety.allWarehouses' | translate) }}</button>
        @if (warehouseFilter()) { <button nz-button (click)="warehouseFilter.set(null); search()" [attr.aria-label]="'inv.safety.clearWarehouse' | translate">×</button> }
        <label><input type="checkbox" [(ngModel)]="lowOnly" (ngModelChange)="search()" data-testid="inv-safety-low-only" />{{ 'inv.safety.lowOnly' | translate }}</label>
        <select [(ngModel)]="sort" (ngModelChange)="search()" [attr.aria-label]="'inv.safety.sort' | translate" data-testid="inv-safety-sort">
          <option value="itemCode">{{ 'inv.safety.itemAscending' | translate }}</option><option value="-itemCode">{{ 'inv.safety.itemDescending' | translate }}</option>
          <option value="-shortage">{{ 'inv.safety.shortageDescending' | translate }}</option><option value="shortage">{{ 'inv.safety.shortageAscending' | translate }}</option>
        </select>
        <button nz-button (click)="search()" data-testid="inv-safety-search">{{ 'inv.safety.search' | translate }}</button>
      </div>
      @if (error(); as key) { <nz-alert nzType="error" nzShowIcon [nzMessage]="key | translate" data-testid="inv-safety-error" />
        @for (detail of details(); track detail) { <p class="error-detail">{{ detail }}</p> }
        @if (traceId()) { <p class="error-detail">{{ 'inv.safety.traceId' | translate }}: {{ traceId() }}</p> }
        <button nz-button class="retry" (click)="load()">{{ 'inv.safety.retry' | translate }}</button>
      }
      @if (queriedAt() && !error()) { <p class="as-of">{{ 'inv.safety.asOf' | translate }}: {{ queriedAt().replace('T', ' ').slice(0, 19) }}</p> }
      <nz-table #table [nzData]="rows()" [nzLoading]="loading()" [nzFrontPagination]="false" [nzTotal]="total()"
        [nzPageIndex]="page()" [nzPageSize]="pageSize()" (nzQueryParams)="onQuery($event)"
        [nzNoResult]="(error() ? 'inv.safety.loadFailed' : keyword.trim() || warehouseFilter() || lowOnly ? 'inv.safety.noResults' : 'inv.safety.noThresholds') | translate" data-testid="inv-safety-table">
        <thead><tr><th>{{ 'inv.safety.item' | translate }}</th><th>{{ 'inv.safety.warehouse' | translate }}</th><th>{{ 'inv.safety.minimum' | translate }}</th>
          <th>{{ 'inv.safety.onHand' | translate }}</th><th>{{ 'inv.safety.status' | translate }}</th><th>{{ 'inv.safety.suggested' | translate }}</th><th></th></tr></thead>
        <tbody>@for (row of table.data; track row.id) { <tr [attr.data-testid]="'inv-safety-row-' + row.id">
          <td>{{ row.itemCode || row.itemId }} · {{ row.itemName || '—' }} @if (!row.itemActive) { <span class="inactive">{{ 'inv.safety.itemInactive' | translate }}</span> }</td>
          <td>{{ row.warehouseCode || row.warehouseId }} · {{ row.warehouseName || '—' }} @if (!row.warehouseActive) { <span class="inactive">{{ 'inv.safety.warehouseInactive' | translate }}</span> }</td>
          <td>{{ row.minimumQuantity }}</td><td>{{ row.onHand }}</td>
          <td><span [class.low]="row.isLow">{{ (row.isLow ? 'inv.safety.low' : 'inv.safety.normal') | translate }}</span></td>
          <td>{{ row.shortage }} @if (row.isLow) { <span class="hint">{{ 'inv.safety.hint' | translate }}</span> }</td>
          <td>@if (canEdit()) {
            @if (row.itemActive && row.warehouseActive) { <button nz-button nzType="link" (click)="edit(row)" [disabled]="busy()">{{ 'inv.safety.edit' | translate }}</button> }
            @else { <span class="inactive">{{ 'inv.safety.editInactive' | translate }}</span> }
            <button nz-button nzType="link" nzDanger (click)="remove(row)" [disabled]="busy()">{{ 'inv.safety.delete' | translate }}</button>
          }</td>
        </tr> }</tbody>
      </nz-table>
      @if (editing()) { <section class="editor" data-testid="inv-safety-editor"><h2>{{ (editing()!.id ? 'inv.safety.edit' : 'inv.safety.new') | translate }}</h2>
        @if (!editing()!.id) {
          <button nz-button (click)="chooseItem()" data-testid="inv-safety-pick-item">{{ item() ? item()!.code + ' · ' + item()!.name : ('inv.safety.chooseItem' | translate) }}</button>
          <button nz-button (click)="chooseFormWarehouse()" data-testid="inv-safety-pick-warehouse">{{ formWarehouse() ? formWarehouse()!.code + ' · ' + formWarehouse()!.name : ('inv.safety.chooseWarehouse' | translate) }}</button>
        } @else { <p>{{ editing()!.itemCode }} · {{ editing()!.warehouseCode }}</p> }
        <label>{{ 'inv.safety.minimum' | translate }} * <input #quantityInput type="text" name="minimumQuantity" autocomplete="off" inputmode="decimal" [(ngModel)]="quantity" [attr.aria-invalid]="!!formError()" [attr.aria-describedby]="formError() ? 'inv-safety-form-error' : null" data-testid="inv-safety-quantity" /></label>
        @if (formError()) { <nz-alert id="inv-safety-form-error" nzType="error" nzShowIcon [nzMessage]="formError()! | translate" data-testid="inv-safety-form-error" />
          @for (detail of details(); track detail) { <p class="error-detail">{{ detail }}</p> }
          @if (traceId()) { <p class="error-detail">{{ 'inv.safety.traceId' | translate }}: {{ traceId() }}</p> }
        }
        <div><button nz-button nzType="primary" (click)="save()" [disabled]="busy()" data-testid="inv-safety-save">{{ 'inv.safety.save' | translate }}</button>
          <button nz-button (click)="editing.set(null)" [disabled]="busy()">{{ 'inv.safety.cancel' | translate }}</button></div>
      </section> }
    </section>
  `,
  styles: `.safety{display:grid;gap:16px;color:var(--erp-color-text)}header,.filters{display:flex;align-items:center;gap:12px;flex-wrap:wrap}header{justify-content:space-between}h1{font-size:20px;margin:0}h2{font-size:18px}.filters label{display:flex;gap:6px;align-items:center}.filters input[type=search],.filters select,.editor input{padding:7px;border:1px solid var(--erp-color-border);background:var(--erp-color-bg);color:var(--erp-color-text)}.filters input[type=search]{min-width:220px}.editor{display:grid;gap:12px;max-width:620px;padding:16px;border:1px solid var(--erp-color-border);border-radius:6px}.editor>div{display:flex;gap:8px}.editor label{display:grid;gap:4px}.as-of,.hint{color:var(--erp-color-text-secondary)}.low,.inactive{color:var(--erp-color-warning)}.hint{display:block;font-size:12px}.error-detail{margin:0;color:var(--erp-color-danger);overflow-wrap:anywhere}.retry{justify-self:start}td:nth-child(3),td:nth-child(4),td:nth-child(6){font-variant-numeric:tabular-nums}`,
})
export class SafetyStockPage {
  private readonly api = inject(SafetyStockService);
  private readonly selection = inject(InventorySelectionOptionsService);
  private readonly permissions = inject(PermissionService);
  private readonly company = inject(CompanyContextService);
  private readonly dialogs = inject(ErpDialogService);
  private readonly quantityInput = viewChild<ElementRef<HTMLInputElement>>('quantityInput');
  readonly canEdit = () => this.permissions.has('INV.SAFETY_STOCK.EDIT');
  readonly rows = signal<SafetyStockRow[]>([]); readonly total = signal(0);
  readonly page = signal(1); readonly pageSize = signal(20); readonly queriedAt = signal('');
  readonly loading = signal(false); readonly busy = signal(false);
  readonly error = signal<string | null>(null); readonly formError = signal<string | null>(null);
  readonly details = signal<string[]>([]); readonly traceId = signal<string | null>(null);
  readonly editing = signal<SafetyStockRow | null>(null);
  readonly item = signal<InventoryOption | null>(null);
  readonly formWarehouse = signal<InventoryOption | null>(null);
  readonly warehouseFilter = signal<InventoryOption | null>(null);
  keyword = ''; lowOnly = false; sort: 'itemCode' | '-itemCode' | 'shortage' | '-shortage' = 'itemCode'; quantity = '';
  private generation = 0;
  constructor() { effect(() => { this.company.current(); untracked(() => {
    this.keyword = ''; this.lowOnly = false; this.warehouseFilter.set(null); this.editing.set(null);
    this.page.set(1); this.rows.set([]); this.total.set(0); this.load();
  }); }); }
  search(): void { this.page.set(1); this.load(); }
  onQuery(query: NzTableQueryParams): void { if (query.pageIndex !== this.page() || query.pageSize !== this.pageSize()) {
    this.page.set(query.pageIndex); this.pageSize.set(query.pageSize); this.load();
  } }
  load(): void { const generation = ++this.generation; this.loading.set(true); this.error.set(null); this.details.set([]); this.traceId.set(null);
    this.api.list({ page: this.page(), pageSize: this.pageSize(), keyword: this.keyword,
      warehouseId: this.warehouseFilter()?.id, lowOnly: this.lowOnly, sort: this.sort }).subscribe({
      next: result => { if (generation !== this.generation) return; this.rows.set(result.items); this.total.set(result.totalCount);
        this.queriedAt.set(result.queriedAt); this.loading.set(false); },
      error: err => { if (generation !== this.generation) return; this.rows.set([]); this.total.set(0); this.queriedAt.set('');
        this.showError(err); this.loading.set(false); },
    });
  }
  chooseWarehouse(): void { this.selection.choose('warehouse').subscribe(rows => { if (rows?.[0]) { this.warehouseFilter.set(rows[0]); this.search(); } }); }
  chooseItem(): void { this.selection.choose('item').subscribe(rows => { if (rows?.[0]) this.item.set(rows[0]); }); }
  chooseFormWarehouse(): void { this.selection.choose('warehouse').subscribe(rows => { if (rows?.[0]) this.formWarehouse.set(rows[0]); }); }
  newThreshold(): void { if (!this.canEdit() || this.busy()) return; this.item.set(null); this.formWarehouse.set(null); this.quantity = ''; this.formError.set(null);
    this.editing.set({ id: 0, itemId: 0, warehouseId: 0, itemCode: null, itemName: null, itemActive: true,
      warehouseCode: null, warehouseName: null, warehouseActive: true, minimumQuantity: 0, onHand: 0,
      isLow: false, shortage: 0, version: 0 }); }
  edit(row: SafetyStockRow): void { if (!this.canEdit() || !row.itemActive || !row.warehouseActive) return;
    this.quantity = String(row.minimumQuantity); this.formError.set(null); this.editing.set(row); }
  save(): void { const draft = this.editing(); if (!draft || !this.canEdit() || this.busy()) return;
    if (!draft.id && (!this.item() || !this.formWarehouse())) {
      this.formError.set('inv.safety.missingReference'); return;
    }
    if (!validSafetyQuantity(this.quantity)) {
      this.formError.set('inv.safety.invalid'); this.quantityInput()?.nativeElement.focus(); return;
    }
    this.busy.set(true); this.formError.set(null); this.details.set([]); this.traceId.set(null);
    const request = draft.id ? this.api.update(draft.id, draft.version, this.quantity)
      : this.api.create(this.item()!.id, this.formWarehouse()!.id, this.quantity);
    request.subscribe({ next: () => { this.busy.set(false); this.editing.set(null); this.dialogs.success('inv.safety.saved'); this.load(); },
      error: err => { this.busy.set(false); this.showError(err, true); } });
  }
  remove(row: SafetyStockRow): void { if (!this.canEdit() || this.busy()) return;
    this.dialogs.confirm({ titleKey: 'inv.safety.confirmDelete', contentKey: 'inv.safety.deleteHint', danger: true }).subscribe(confirmed => {
      if (!confirmed) return; this.busy.set(true);
      this.api.remove(row.id, row.version).subscribe({ next: () => { this.busy.set(false); this.dialogs.success('inv.safety.deleted'); this.load(); },
        error: err => { this.busy.set(false); this.showError(err); } });
    });
  }
  private showError(err: unknown, form = false): void { const key = this.api.errorKey(err);
    if (form) this.formError.set(key); else this.error.set(key);
    this.details.set(apiValidationDetails(err)); this.traceId.set(apiErrorTraceId(err));
  }
}
