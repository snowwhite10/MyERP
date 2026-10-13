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
import { fetchAllPages, OPTION_PAGE_SIZE } from '../../shared/list-page/fetch-all-pages';
import { Item, ItemService } from '../md/item.service';
import { ItemCategory, ItemCategoryService } from '../md/item-category.service';
import { StorageLocation, Warehouse, WarehouseService } from '../md/warehouse.service';
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
          @for (warehouse of warehouses(); track warehouse.id) { <label><input type="checkbox" [checked]="warehouseIds.includes(warehouse.id)" (change)="toggle(warehouseIds, warehouse.id)" />{{ warehouse.code }} · {{ warehouse.name }}</label> }
        </fieldset><fieldset><legend>{{ 'inv.stocktake.categories' | translate }}</legend>
          @for (category of categories(); track category.id) { <label><input type="checkbox" [checked]="categoryIds.includes(category.id)" (change)="toggle(categoryIds, category.id)" />{{ category.code }} · {{ category.name }}</label> }
        </fieldset><fieldset><legend>{{ 'inv.stocktake.items' | translate }}</legend>
          @for (item of items(); track item.id) { <label><input type="checkbox" [checked]="itemIds.includes(item.id)" (change)="toggle(itemIds, item.id)" />{{ item.code }} · {{ item.name }}</label> }
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
              <select [(ngModel)]="row.itemId" (ngModelChange)="row.trackingMode = mode(row.itemId); row.tracking = []" [attr.aria-label]="'inv.stocktake.item' | translate"><option [ngValue]="0">—</option>
                @for (item of items(); track item.id) { <option [ngValue]="item.id">{{ item.code }} · {{ item.name }}</option> }</select>
              <select [(ngModel)]="row.warehouseId" (ngModelChange)="row.locationId = null; loadLocations($event)" [attr.aria-label]="'inv.stocktake.warehouse' | translate"><option [ngValue]="0">—</option>
                @for (warehouse of scopeWarehouses(); track warehouse.id) { <option [ngValue]="warehouse.id">{{ warehouse.code }} · {{ warehouse.name }}</option> }</select>
              <select [(ngModel)]="row.locationId" [attr.aria-label]="'inv.stocktake.location' | translate"><option [ngValue]="null">—</option>
                @for (loc of locations(row.warehouseId); track loc.id) { <option [ngValue]="loc.id">{{ loc.code }} · {{ loc.name }}</option> }</select>
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
  styles: `.stocktake-count{display:grid;gap:16px;color:var(--erp-color-text)}header,.head,.actions{display:flex;gap:12px;align-items:center;flex-wrap:wrap}header{justify-content:space-between}h1{font-size:20px;margin:0}.head label{display:grid;gap:4px}.scope{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:12px}.scope fieldset{max-height:240px;overflow:auto;border:1px solid var(--erp-color-border);border-radius:8px}.scope label{display:block}.line,.line-head{display:grid;grid-template-columns:repeat(6,minmax(0,1fr));gap:8px;align-items:center}.line{padding:8px;border-bottom:1px solid var(--erp-color-border)}.line input,.line select{width:100%;min-width:0}.tracking,.warning,.line-action{grid-column:1/-1}.tracking-row{display:flex;gap:12px;margin:6px 0}.tracking-row>*{max-width:180px}.warning{color:var(--erp-color-error)}@media(max-width:900px){.scope{grid-template-columns:1fr}.line,.line-head{grid-template-columns:repeat(2,minmax(0,1fr))}}`,
})
export class StocktakeCount {
  private readonly route = inject(ActivatedRoute); private readonly router = inject(Router);
  private readonly api = inject(StocktakeService); private readonly itemApi = inject(ItemService);
  private readonly categoryApi = inject(ItemCategoryService); private readonly warehouseApi = inject(WarehouseService);
  private readonly company = inject(CompanyContextService); private readonly cdr = inject(ChangeDetectorRef);
  readonly id = Number(this.route.snapshot.paramMap.get('id')) || null;
  readonly busy = signal(false); readonly error = signal<string | null>(null);
  readonly changedKeys = signal<{ itemId: number; warehouseId: number; locationId: number | null }[]>([]);
  readonly detail = signal<StocktakeDetail | null>(null);
  readonly items = signal<Item[]>([]); readonly categories = signal<ItemCategory[]>([]); readonly warehouses = signal<Warehouse[]>([]);
  private readonly locationMap = signal<Record<number, StorageLocation[]>>({});
  readonly validCount = validCount; readonly difference = (counted: string, book: number) => decimalDifference(counted, String(book));
  readonly statusKey = (status: string) => status[0].toLowerCase() + status.slice(1);
  businessDate = ''; remark = ''; warehouseIds: number[] = []; categoryIds: number[] = []; itemIds: number[] = []; rows: CountRow[] = [];
  constructor() { effect(() => { this.company.current(); untracked(() => { this.loadOptions(); if (this.id) this.load(); }); }); }
  private loadOptions(): void {
    forkJoin({ items: fetchAllPages(page => this.itemApi.list({ page, pageSize: OPTION_PAGE_SIZE })),
      warehouses: fetchAllPages(page => this.warehouseApi.list({ page, pageSize: OPTION_PAGE_SIZE })), categories: this.categoryApi.list() }).subscribe({
      next: data => { this.items.set(data.items.filter(x => x.isActive)); this.warehouses.set(data.warehouses.filter(x => x.isActive));
        this.categories.set(data.categories.filter(x => x.isActive)); }, error: e => this.error.set(this.api.errorKey(e)), });
  }
  private load(): void { this.api.get(this.id!).subscribe({ next: detail => this.accept(detail), error: e => this.fail(e) }); }
  private accept(detail: StocktakeDetail): void {
    this.detail.set(detail); this.businessDate = detail.businessDate; this.remark = detail.remark ?? '';
    this.rows = detail.lines.map(line => ({ lineId: line.id, itemId: line.itemId, warehouseId: line.warehouseId,
      locationId: line.locationId, bookQuantity: line.bookQuantity, countedQuantity: line.countedQuantity === null ? null : String(line.countedQuantity),
      trackingMode: line.trackingMode, isAdded: line.isAdded, recountRequired: line.recountRequired,
      tracking: line.tracking.map(t => ({ lotNo: t.lotNo, serialNo: t.serialNo,
        bookQuantity: t.bookQuantity, countedQuantity: t.countedQuantity === null ? null : String(t.countedQuantity) })) }));
    for (const line of this.rows) this.loadLocations(line.warehouseId);
    this.cdr.markForCheck();
  }
  toggle(ids: number[], id: number): void { const index = ids.indexOf(id); if (index < 0) ids.push(id); else ids.splice(index, 1); }
  scopeWarehouses(): Warehouse[] { return this.warehouses().filter(w => this.detail()?.scope.warehouseIds.includes(w.id)); }
  itemName(id: number): string { const x = this.items().find(i => i.id === id); return x ? `${x.code} · ${x.name}` : String(id); }
  warehouseName(id: number): string { const x = this.warehouses().find(w => w.id === id); return x ? `${x.code} · ${x.name}` : String(id); }
  locationName(id: number | null): string { if (id === null) return '—'; const x = Object.values(this.locationMap()).flat().find(l => l.id === id); return x ? `${x.code} · ${x.name}` : String(id); }
  locations(id: number): StorageLocation[] { return this.locationMap()[id] ?? []; }
  loadLocations(id: number): void { if (!id || this.locationMap()[id]) return; this.locationMap.update(map => ({ ...map, [id]: [] }));
    fetchAllPages(page => this.warehouseApi.listLocations(id, { page, pageSize: OPTION_PAGE_SIZE })).subscribe({
      next: list => this.locationMap.update(map => ({ ...map, [id]: list.filter(x => x.isActive) })), error: e => this.fail(e) }); }
  mode(id: number): CountRow['trackingMode'] { return this.items().find(i => i.id === id)?.trackingMode ?? 'None'; }
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
