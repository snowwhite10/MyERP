import { ChangeDetectionStrategy, ChangeDetectorRef, Component, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { forkJoin } from 'rxjs';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzInputModule } from 'ng-zorro-antd/input';
import { CompanyContextService } from '../../core/company/company-context.service';
import { HasPermissionDirective } from '../../core/auth/has-permission.directive';
import { PermissionService } from '../../core/auth/permission.service';
import { TranslateService } from '@ngx-translate/core';
import { ItemCategory, ItemCategoryService } from '../md/item-category.service';
import { InventoryOption, InventorySelectionOptionsService } from './selection-options.service';
import { StocktakeCountLine, StocktakeDetail, StocktakeService, decimalDifference, validCount } from './stocktake.service';

type CountRow = Omit<StocktakeCountLine, 'tracking'> & { tracking: (StocktakeCountLine['tracking'][number] & { bookQuantity?: number })[];
  bookQuantity: number; trackingMode: 'None' | 'Lot' | 'Serial';
  recountRequired: boolean; isAdded: boolean; itemId: number; warehouseId: number; locationId: number | null };
const emptyRow = (): CountRow => ({ itemId: 0, warehouseId: 0, locationId: null, countedQuantity: null,
  tracking: [], bookQuantity: 0, trackingMode: 'None', recountRequired: false, isAdded: true });

/** 介面 5.18：建立範圍、保存逐行及逐號實盤；送審前先保存最新版本（ADR-062）。 */
@Component({
  selector: 'erp-stocktake-count',
  imports: [FormsModule, RouterLink, TranslatePipe, NzAlertModule, NzButtonModule, NzInputModule, HasPermissionDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="stocktake-count"><header><h1>{{ (id ? 'inv.stocktake.count' : 'inv.stocktake.new') | translate }}</h1>
      <a routerLink="/inv/stocktakes">{{ 'inv.stocktake.back' | translate }}</a></header>
      @if (error()) { <nz-alert nzType="error" nzShowIcon [nzMessage]="error()! | translate" data-testid="inv-stocktake-error" /> }
      @if (changedKeys().length) { <nz-alert nzType="warning" nzShowIcon [nzMessage]="'inv.stocktake.staleKeys' | translate" />
        <ul>@for (key of changedKeys(); track $index) { <li>{{ itemName(key.itemId) }} · {{ warehouseName(key.warehouseId) }} · {{ locationName(key.locationId) }}</li> }</ul> }
      <div class="head"><label>{{ 'inv.stocktake.date' | translate }} <input nz-input type="date" [(ngModel)]="businessDate" data-testid="inv-stocktake-date" /></label>
        <label>{{ 'inv.stocktake.remark' | translate }} <input nz-input [(ngModel)]="remark" maxlength="1000" /></label></div>
      @if (!id) {
        <p>{{ 'inv.stocktake.scopeHint' | translate }}</p>
        <div class="scope"><fieldset><legend>{{ 'inv.stocktake.warehouses' | translate }}</legend>
          <button nz-button type="button" (click)="chooseScopeWarehouses()">{{ 'inv.selection.warehouse' | translate }}</button>
          @for (id of warehouseIds; track id) { <p>{{ warehouseName(id) }} <button nz-button type="button" [attr.aria-label]="('inv.stocktake.removeScope' | translate) + ' ' + warehouseName(id)" (click)="toggle(warehouseIds,id)">×</button></p> }
        </fieldset><fieldset><legend>{{ 'inv.stocktake.categories' | translate }}</legend>
          @if (!canViewCategories()) { <p>{{ 'inv.stocktake.categoryPermission' | translate }}</p> }
          @else if (!categoriesLoading() && !categories().length) { <p>{{ 'inv.stocktake.noCategories' | translate }}</p> }
          @for (category of categories(); track category.id) { <label><input type="checkbox" [checked]="categoryIds.includes(category.id)" (change)="toggle(categoryIds, category.id)" />{{ category.code }} · {{ category.name }}</label> }
        </fieldset><fieldset><legend>{{ 'inv.stocktake.items' | translate }}</legend>
          <button nz-button type="button" (click)="chooseScopeItems()">{{ 'inv.selection.item' | translate }}</button>
          @for (id of itemIds; track id) { <p>{{ itemName(id) }} <button nz-button type="button" [attr.aria-label]="('inv.stocktake.removeScope' | translate) + ' ' + itemName(id)" (click)="toggle(itemIds,id)">×</button></p> }
        </fieldset></div>
        <button nz-button nzType="primary" [disabled]="busy()" (click)="create()" data-testid="inv-stocktake-create">{{ 'inv.stocktake.search' | translate }}</button>
      } @else {
        @if (detail(); as d) { <p>{{ d.documentNo }} · {{ 'inv.stocktake.snapshotAt' | translate }} {{ d.snapshotAt }} · {{ 'inv.stocktake.status.' + statusKey(d.status) | translate }}</p> }
        <div class="line-head"><span>{{ 'inv.stocktake.item' | translate }}</span><span>{{ 'inv.stocktake.warehouse' | translate }}</span>
          <span>{{ 'inv.stocktake.location' | translate }}</span><span>{{ 'inv.stocktake.book' | translate }}</span>
          <span>{{ 'inv.stocktake.counted' | translate }}</span><span>{{ 'inv.stocktake.difference' | translate }}</span></div>
        @for (row of rows; track $index; let index = $index) {
          <div class="line" [attr.data-testid]="'inv-stocktake-line-' + index">
            @if (row.isAdded && !row.lineId) {
              <button nz-button type="button" [attr.aria-label]="'inv.stocktake.item' | translate" [attr.title]="itemName(row.itemId)" (click)="chooseItem(row)">{{ itemName(row.itemId) }}</button>
              <button nz-button type="button" [attr.aria-label]="'inv.stocktake.warehouse' | translate" [attr.title]="warehouseName(row.warehouseId)" (click)="chooseWarehouse(row)">{{ warehouseName(row.warehouseId) }}</button>
              <button nz-button type="button" [disabled]="!row.warehouseId" [attr.aria-label]="'inv.stocktake.location' | translate" [attr.title]="row.warehouseId ? locationName(row.locationId) : ('inv.selection.chooseWarehouseFirst' | translate)" (click)="chooseLocation(row)">{{ locationName(row.locationId) }}</button>
            } @else { <span>{{ itemName(row.itemId) }}</span><span>{{ warehouseName(row.warehouseId) }}</span><span>{{ locationName(row.locationId) }}</span> }
            <span>{{ row.bookQuantity }}</span>
            <input nz-input inputmode="decimal" [ngModel]="row.countedQuantity" (ngModelChange)="row.countedQuantity = $event" [attr.aria-label]="'inv.stocktake.counted' | translate" />
            <span>{{ row.countedQuantity !== null && validCount(row.countedQuantity) ? difference(row.countedQuantity, row.bookQuantity) : '—' }}</span>
          @if (row.isAdded && !row.lineId) { <button nz-button type="button" class="line-action" (click)="removeLine(index)">{{ 'inv.stocktake.removeLine' | translate }}</button> }
          @if (row.recountRequired) { <p class="warning">{{ 'inv.stocktake.recount' | translate }}</p> }
            @if (row.trackingMode !== 'None') { <div class="tracking"><strong>{{ 'inv.stocktake.tracking' | translate }}</strong>
              @for (entry of row.tracking; track $index; let number = $index) { <div class="tracking-row">
                @if (entry.bookQuantity === undefined || entry.bookQuantity === 0) { <input nz-input [ngModel]="entry.lotNo ?? entry.serialNo" (ngModelChange)="setNumber(row, number, $event)" [attr.aria-label]="'inv.stocktake.number' | translate" /> }
                @else { <span>{{ entry.lotNo ?? entry.serialNo }}</span><span>{{ entry.bookQuantity }}</span> }
                <input nz-input inputmode="decimal" [ngModel]="entry.countedQuantity" (ngModelChange)="entry.countedQuantity = $event" [attr.aria-label]="'inv.stocktake.counted' | translate" />
                @if (entry.bookQuantity === undefined || entry.bookQuantity === 0) { <button nz-button type="button" (click)="removeNumber(row, number)">{{ 'inv.stocktake.removeNumber' | translate }}</button> }
              </div> }
              <button nz-button type="button" (click)="addNumber(row)">{{ 'inv.stocktake.addNumber' | translate }}</button>
            </div> }
          </div>
        }
        <div class="actions"><button nz-button (click)="fillUncounted()" [disabled]="busy()">{{ 'inv.stocktake.fillUncounted' | translate }}</button>
          <button nz-button (click)="addLine()" [disabled]="busy()">{{ 'inv.stocktake.addLine' | translate }}</button>
          <button nz-button (click)="save()" [disabled]="busy()" data-testid="inv-stocktake-save">{{ 'inv.stocktake.save' | translate }}</button>
          <button nz-button (click)="refresh()" [disabled]="busy()" data-testid="inv-stocktake-refresh">{{ 'inv.stocktake.refresh' | translate }}</button>
          <button nz-button nzType="primary" *erpHasPermission="'INV.STOCKTAKE.SUBMIT'" (click)="submit()" [disabled]="busy()" data-testid="inv-stocktake-submit">{{ 'inv.stocktake.submit' | translate }}</button></div>
      }
    </section>
  `,
  styles: `.stocktake-count{display:grid;gap:16px;color:var(--erp-color-text)}header,.head,.actions{display:flex;gap:12px;align-items:center;flex-wrap:wrap}header{justify-content:space-between}h1{font-size:20px;margin:0}.head label{display:grid;gap:4px}.scope{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:12px}.scope fieldset{max-height:240px;overflow:auto;border:1px solid var(--erp-color-border);border-radius:8px}.scope label{display:block}.scope p{overflow-wrap:anywhere}.line,.line-head{display:grid;grid-template-columns:repeat(6,minmax(0,1fr));gap:8px;align-items:center}.line{padding:8px;border-bottom:1px solid var(--erp-color-border)}.line input,.line button{width:100%;min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.tracking,.warning,.line-action{grid-column:1/-1}.tracking-row{display:flex;gap:12px;margin:6px 0}.tracking-row>*{max-width:180px}.warning{color:var(--erp-color-error)}@media(max-width:900px){.scope{grid-template-columns:1fr}.line,.line-head{grid-template-columns:repeat(2,minmax(0,1fr))}}`,
})
export class StocktakeCount {
  private readonly route = inject(ActivatedRoute); private readonly router = inject(Router);
  private readonly api = inject(StocktakeService); private readonly selection = inject(InventorySelectionOptionsService);
  private readonly categoryApi = inject(ItemCategoryService); private readonly permissions = inject(PermissionService);
  private readonly translate = inject(TranslateService);
  private readonly company = inject(CompanyContextService); private readonly cdr = inject(ChangeDetectorRef);
  readonly id = Number(this.route.snapshot.paramMap.get('id')) || null;
  readonly busy = signal(false); readonly error = signal<string | null>(null);
  readonly changedKeys = signal<{ itemId: number; warehouseId: number; locationId: number | null }[]>([]);
  readonly detail = signal<StocktakeDetail | null>(null);
  readonly items = signal<InventoryOption[]>([]); readonly categories = signal<ItemCategory[]>([]);
  readonly warehouses = signal<InventoryOption[]>([]); readonly locations = signal<InventoryOption[]>([]);
  readonly categoriesLoading = signal(false);
  private contextRevision = 0;
  readonly validCount = validCount; readonly difference = (counted: string, book: number) => decimalDifference(counted, String(book));
  readonly statusKey = (status: string) => status[0].toLowerCase() + status.slice(1);
  businessDate = ''; remark = ''; warehouseIds: number[] = []; categoryIds: number[] = []; itemIds: number[] = []; rows: CountRow[] = [];
  constructor() { effect(() => { this.company.current(); untracked(() => {
    ++this.contextRevision; this.items.set([]); this.warehouses.set([]); this.locations.set([]); this.categories.set([]);
    this.warehouseIds = []; this.itemIds = []; this.categoryIds = []; this.rows = []; this.detail.set(null);
    this.error.set(null); this.loadOptions(); if (this.id) this.load(); }); }); }
  private loadOptions(): void {
    if (!this.canViewCategories()) return;
    this.categoriesLoading.set(true);
    const revision = this.contextRevision;
    this.categoryApi.list().subscribe({ next: data => { if (revision === this.contextRevision) {
      this.categories.set(data.filter(x => x.isActive)); this.categoriesLoading.set(false); } },
      error: e => { if (revision === this.contextRevision) { this.categoriesLoading.set(false); this.error.set(this.api.errorKey(e)); } } });
  }
  canViewCategories(): boolean { return this.permissions.has('MD.ITEM_CATEGORY.VIEW'); }
  private load(): void { const revision = this.contextRevision;
    forkJoin({ detail: this.api.get(this.id!), references: this.selection.saved('stocktakes', this.id!) }).subscribe({
      next: ({ detail, references }) => { if (revision !== this.contextRevision) return;
        this.items.set(references.items); this.warehouses.set(references.warehouses); this.locations.set(references.locations);
        this.accept(detail); }, error: e => { if (revision === this.contextRevision) this.fail(e); } }); }
  private accept(detail: StocktakeDetail): void {
    this.detail.set(detail); this.businessDate = detail.businessDate; this.remark = detail.remark ?? '';
    this.rows = detail.lines.map(line => ({ lineId: line.id, itemId: line.itemId, warehouseId: line.warehouseId,
      locationId: line.locationId, bookQuantity: line.bookQuantity, countedQuantity: line.countedQuantity === null ? null : String(line.countedQuantity),
      trackingMode: line.trackingMode, isAdded: line.isAdded, recountRequired: line.recountRequired,
      tracking: line.tracking.map(t => ({ lotNo: t.lotNo, serialNo: t.serialNo,
        bookQuantity: t.bookQuantity, countedQuantity: t.countedQuantity === null ? null : String(t.countedQuantity) })) }));
    if (detail.lines.some(line => !this.items().some(x => x.id === line.itemId)
      || !this.warehouses().some(x => x.id === line.warehouseId)
      || (line.locationId !== null && !this.locations().some(x => x.id === line.locationId)))) {
      const revision = this.contextRevision;
      this.selection.saved('stocktakes', detail.id).subscribe({ next: refs => {
        if (revision !== this.contextRevision || this.detail()?.id !== detail.id) return;
        this.items.set(this.merge(this.items(), refs.items));
        this.warehouses.set(this.merge(this.warehouses(), refs.warehouses));
        this.locations.set(this.merge(this.locations(), refs.locations));
        this.cdr.markForCheck();
      }, error: e => { if (revision === this.contextRevision) this.error.set(this.api.errorKey(e)); } });
    }
    this.cdr.markForCheck();
  }
  private merge(old: InventoryOption[], incoming: InventoryOption[]): InventoryOption[] {
    const ids = new Set(incoming.map(x => x.id)); return [...old.filter(x => !ids.has(x.id)), ...incoming];
  }
  toggle(ids: number[], id: number): void { const index = ids.indexOf(id); if (index < 0) ids.push(id); else ids.splice(index, 1); }
  private label(options: InventoryOption[], id: number | null): string { if (!id) return '—'; const x = options.find(i => i.id === id);
    return x ? `${x.code} · ${x.name}${x.isActive === false ? ` (${this.translate.instant('inv.selection.inactive')})` : ''}` : String(id); }
  itemName(id: number): string { return this.label(this.items(), id); }
  warehouseName(id: number): string { return this.label(this.warehouses(), id); }
  locationName(id: number | null): string { return this.label(this.locations(), id); }
  private remember(kind: 'item' | 'warehouse' | 'location', row: InventoryOption): void {
    const values = kind === 'item' ? this.items : kind === 'warehouse' ? this.warehouses : this.locations;
    values.update(old => [...old.filter(x => x.id !== row.id), row]);
  }
  chooseScopeWarehouses(): void { const revision = this.contextRevision;
    this.selection.chooseMany('warehouse', this.warehouses().filter(x => this.warehouseIds.includes(x.id))).subscribe(rows => {
      if (!rows || revision !== this.contextRevision) return;
      this.warehouseIds = rows.map(x => x.id); rows.forEach(x => this.remember('warehouse', x)); this.cdr.markForCheck();
    }); }
  chooseScopeItems(): void { const revision = this.contextRevision;
    this.selection.chooseMany('item', this.items().filter(x => this.itemIds.includes(x.id))).subscribe(rows => {
      if (!rows || revision !== this.contextRevision) return;
      this.itemIds = rows.map(x => x.id); rows.forEach(x => this.remember('item', x)); this.cdr.markForCheck();
    }); }
  chooseItem(row: CountRow): void { const revision = this.contextRevision;
    this.selection.choose('item').subscribe(rows => { if (!rows?.[0] || revision !== this.contextRevision || !this.rows.includes(row)) return;
      if (row.itemId !== rows[0].id) row.tracking = [];
      row.itemId = rows[0].id; row.trackingMode = rows[0].trackingMode ?? 'None'; this.remember('item', rows[0]); this.cdr.markForCheck(); }); }
  chooseWarehouse(row: CountRow): void { const revision = this.contextRevision;
    this.selection.choose('warehouse').subscribe(rows => { if (!rows?.[0] || revision !== this.contextRevision || !this.rows.includes(row)) return;
      if (!this.detail()?.scope.warehouseIds.includes(rows[0].id)) { this.error.set('inv.stocktake.warehouseOutOfScope'); return; }
      if (row.warehouseId !== rows[0].id) row.locationId = null;
      row.warehouseId = rows[0].id; this.remember('warehouse', rows[0]); this.cdr.markForCheck(); }); }
  chooseLocation(row: CountRow): void { if (!row.warehouseId) return;
    const revision = this.contextRevision, warehouseId = row.warehouseId;
    this.selection.choose('location', warehouseId).subscribe(rows => {
      if (!rows?.[0] || revision !== this.contextRevision || row.warehouseId !== warehouseId || !this.rows.includes(row)) return;
      row.locationId = rows[0].id; this.remember('location', rows[0]); this.cdr.markForCheck(); }); }
  addLine(): void { this.rows.push(emptyRow()); this.cdr.markForCheck(); }
  removeLine(index: number): void { this.rows.splice(index, 1); this.cdr.markForCheck(); }
  addNumber(row: CountRow): void { row.tracking.push({ lotNo: row.trackingMode === 'Lot' ? '' : null,
    serialNo: row.trackingMode === 'Serial' ? '' : null, countedQuantity: null }); this.cdr.markForCheck(); }
  setNumber(row: CountRow, index: number, value: string): void { row.tracking[index] = { ...row.tracking[index],
    lotNo: row.trackingMode === 'Lot' ? value : null, serialNo: row.trackingMode === 'Serial' ? value : null }; }
  removeNumber(row: CountRow, index: number): void { row.tracking.splice(index, 1); this.cdr.markForCheck(); }
  fillUncounted(): void { for (const row of this.rows) { if (row.countedQuantity === null) row.countedQuantity = String(row.bookQuantity);
    for (const entry of row.tracking) if (entry.countedQuantity === null && entry.bookQuantity !== undefined) entry.countedQuantity = String(entry.bookQuantity); } }
  create(): void {
    if (this.busy()) return;
    if (!this.businessDate || !this.warehouseIds.length) { this.error.set('inv.stocktake.invalidScope'); return; }
    this.busy.set(true); this.error.set(null);
    this.api.create({ businessDate: this.businessDate, remark: this.remark, scope: { warehouseIds: this.warehouseIds,
      categoryIds: this.categoryIds, itemIds: this.itemIds } }).subscribe({
      next: d => { this.busy.set(false); void this.router.navigate(['/inv/stocktakes', d.id, 'count']); }, error: e => this.fail(e) });
  }
  private validRows(): boolean { return this.rows.every(row => (row.countedQuantity === null || validCount(row.countedQuantity))
    && (!row.isAdded || !!row.lineId || (!!row.itemId && !!row.warehouseId && this.detail()?.scope.warehouseIds.includes(row.warehouseId)))
    && row.tracking.every(entry => (entry.countedQuantity === null || validCount(entry.countedQuantity))
      && !!(entry.lotNo?.trim() || entry.serialNo?.trim()))); }
  private draft() { return { businessDate: this.businessDate, remark: this.remark,
    lines: this.rows.map(row => ({ lineId: row.lineId, itemId: row.lineId ? undefined : row.itemId,
      warehouseId: row.lineId ? undefined : row.warehouseId, locationId: row.lineId ? undefined : row.locationId,
      countedQuantity: row.countedQuantity, tracking: row.tracking.map(t => ({ lotNo: t.lotNo, serialNo: t.serialNo,
        countedQuantity: t.countedQuantity })) })) }; }
  save(): void {
    if (this.busy() || !this.detail()) return;
    if (!this.businessDate || !this.validRows()) { this.error.set('inv.stocktake.invalidCount'); return; }
    this.busy.set(true); this.error.set(null);
    this.api.update(this.id!, this.detail()!.version, this.draft()).subscribe({
      next: d => { this.busy.set(false); this.changedKeys.set([]); this.accept(d); }, error: e => this.fail(e) });
  }
  refresh(): void { if (this.busy() || !this.detail()) return;
    if (!this.businessDate || !this.validRows()) { this.error.set('inv.stocktake.invalidCount'); return; }
    // Persist local counts before re-reading the book; refresh preserves stored counts only.
    this.busy.set(true); this.api.update(this.id!, this.detail()!.version, this.draft()).subscribe({ next: saved => {
      this.accept(saved); this.api.refresh(this.id!, saved.version).subscribe({
        next: d => { this.busy.set(false); this.changedKeys.set([]); this.accept(d); }, error: e => this.fail(e) });
    }, error: e => this.fail(e) }); }
  submit(): void { if (this.busy() || !this.detail()) return;
    if (!this.businessDate || !this.validRows() || this.rows.some(row => row.countedQuantity === null || row.tracking.some(t => t.countedQuantity === null))) {
      this.error.set('inv.stocktake.missing'); return; }
    this.busy.set(true); this.error.set(null);
    this.api.update(this.id!, this.detail()!.version, this.draft()).subscribe({ next: saved => {
      this.accept(saved); this.api.submit(this.id!, saved.version).subscribe({ next: () => { this.busy.set(false); void this.router.navigateByUrl('/inv/stocktakes'); },
        error: e => this.fail(e) }); }, error: e => this.fail(e) }); }
  private fail(error: unknown): void { this.busy.set(false); this.error.set(this.api.errorKey(error));
    const body = (error as { error?: { changedKeys?: { itemId: number; warehouseId: number; locationId: number | null }[] } })?.error;
    this.changedKeys.set(body?.changedKeys ?? []); }
}
