import { ChangeDetectionStrategy, ChangeDetectorRef, Component, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { forkJoin } from 'rxjs';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzInputModule } from 'ng-zorro-antd/input';
import { CompanyContextService } from '../../core/company/company-context.service';
import { Item, ItemService } from '../md/item.service';
import { StorageLocation, Warehouse, WarehouseService } from '../md/warehouse.service';
import { fetchAllPages, OPTION_PAGE_SIZE } from '../../shared/list-page/fetch-all-pages';
import { sameBalanceKey, TransferDraft, TransferLineDraft, TransferService, validTransferLine } from './transfer.service';
import { TrackingInput } from './tracking-input';
import { duplicateSerialAcrossLines, trackingError } from './tracking';

const blankLine = (): TransferLineDraft => ({ itemId: null, sourceWarehouseId: null, sourceLocationId: null,
  destinationWarehouseId: null, destinationLocationId: null, quantity: '' });

/** 介面 5.15 建立、5.17 編輯（草稿或已駁回）；批序號可於草稿暫缺，送審前由後端要求完整（ADR-061）。 */
@Component({
  selector: 'erp-transfer-form',
  imports: [FormsModule, RouterLink, TranslatePipe, NzAlertModule, NzButtonModule, NzInputModule, TrackingInput],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="transfer-form"><header><h1>{{ (id ? 'inv.transfer.edit' : 'inv.transfer.new') | translate }}</h1>
      <a routerLink="/inv/transfers">{{ 'inv.transfer.back' | translate }}</a></header>
      @if (error()) { <nz-alert nzType="error" nzShowIcon [nzMessage]="error()! | translate" data-testid="inv-transfer-error" /> }
      <div class="head"><label>{{ 'inv.transfer.date' | translate }} <input nz-input type="date" [(ngModel)]="draft.businessDate" data-testid="inv-transfer-date" /></label>
        <label>{{ 'inv.transfer.remark' | translate }} <input nz-input [(ngModel)]="draft.remark" maxlength="500" data-testid="inv-transfer-remark" /></label></div>
      <h2>{{ 'inv.transfer.lines' | translate }}</h2>
      <div class="line-head"><span>{{ 'inv.transfer.item' | translate }}</span><span>{{ 'inv.transfer.sourceWarehouse' | translate }}</span>
        <span>{{ 'inv.transfer.sourceLocation' | translate }}</span><span>{{ 'inv.transfer.destinationWarehouse' | translate }}</span>
        <span>{{ 'inv.transfer.destinationLocation' | translate }}</span><span>{{ 'inv.transfer.quantity' | translate }}</span><span></span></div>
      @for (line of draft.lines; track $index; let index = $index) {
        <div class="line" [attr.data-testid]="'inv-transfer-line-' + index">
          <select [(ngModel)]="line.itemId" (ngModelChange)="line.tracking = []" [attr.aria-label]="'inv.transfer.item' | translate"><option [ngValue]="null">—</option>
            @for (item of items(); track item.id) { <option [ngValue]="item.id">{{ item.code }} · {{ item.name }}</option> }</select>
          <select [(ngModel)]="line.sourceWarehouseId" (ngModelChange)="line.sourceLocationId = null; loadLocations($event)" [attr.aria-label]="'inv.transfer.sourceWarehouse' | translate"><option [ngValue]="null">—</option>
            @for (wh of warehouses(); track wh.id) { <option [ngValue]="wh.id">{{ wh.code }} · {{ wh.name }}</option> }</select>
          <select [(ngModel)]="line.sourceLocationId" [attr.aria-label]="'inv.transfer.sourceLocation' | translate"><option [ngValue]="null">—</option>
            @for (loc of locations(line.sourceWarehouseId); track loc.id) { <option [ngValue]="loc.id">{{ loc.code }} · {{ loc.name }}</option> }</select>
          <select [(ngModel)]="line.destinationWarehouseId" (ngModelChange)="line.destinationLocationId = null; loadLocations($event)" [attr.aria-label]="'inv.transfer.destinationWarehouse' | translate"><option [ngValue]="null">—</option>
            @for (wh of warehouses(); track wh.id) { <option [ngValue]="wh.id">{{ wh.code }} · {{ wh.name }}</option> }</select>
          <select [(ngModel)]="line.destinationLocationId" [attr.aria-label]="'inv.transfer.destinationLocation' | translate"><option [ngValue]="null">—</option>
            @for (loc of locations(line.destinationWarehouseId); track loc.id) { <option [ngValue]="loc.id">{{ loc.code }} · {{ loc.name }}</option> }</select>
          <input nz-input type="text" inputmode="decimal" [(ngModel)]="line.quantity" [attr.aria-label]="'inv.transfer.quantity' | translate" />
          <button nz-button type="button" [attr.aria-label]="'inv.transfer.removeLine' | translate" (click)="removeLine(index)">×</button>
          @if (line.sourceWarehouseId && sameKey(line)) { <p class="warn">{{ 'inv.transfer.sameKey' | translate }}</p> }
          @if (mode(line) !== 'None') { <erp-tracking-input class="tracking" [mode]="mode(line)" [quantity]="line.quantity"
            [rows]="line.tracking ?? []" [incoming]="false" (rowsChange)="line.tracking = $event" /> }
        </div>
      }
      <p class="hint">{{ 'inv.transfer.hint' | translate }}</p>
      <div class="actions"><button nz-button type="button" (click)="addLine()" data-testid="inv-transfer-add-line">{{ 'inv.transfer.addLine' | translate }}</button>
        <button nz-button nzType="primary" type="button" data-testid="inv-transfer-save" [disabled]="saving()" (click)="save()">{{ 'inv.transfer.save' | translate }}</button></div>
    </section>
  `,
  styles: `.transfer-form{display:grid;gap:16px;color:var(--erp-color-text)}header,.head,.actions{display:flex;gap:16px;align-items:center;flex-wrap:wrap}header{justify-content:space-between}h1{margin:0;font-size:20px}h2{font-size:16px}.head label{display:grid;gap:4px}.line,.line-head{display:grid;grid-template-columns:2fr 2fr 2fr 2fr 2fr 1fr 40px;gap:8px;align-items:center}.line select,.line input{min-width:0;width:100%}.tracking,.warn{grid-column:1/-1}.warn{color:var(--erp-color-error,#ff4d4f);margin:0}.hint{color:var(--erp-color-text-secondary);margin:0}@media(max-width:1100px){.line,.line-head{grid-template-columns:repeat(2,minmax(0,1fr))}}`,
})
export class TransferForm {
  private readonly route = inject(ActivatedRoute); private readonly router = inject(Router);
  private readonly api = inject(TransferService); private readonly itemsApi = inject(ItemService);
  private readonly warehouseApi = inject(WarehouseService); private readonly company = inject(CompanyContextService);
  private readonly cdr = inject(ChangeDetectorRef);
  readonly id = Number(this.route.snapshot.paramMap.get('id')) || null;
  readonly error = signal<string | null>(null); readonly saving = signal(false);
  readonly items = signal<Item[]>([]); readonly warehouses = signal<Warehouse[]>([]);
  private readonly locationMap = signal<Record<number, StorageLocation[]>>({});
  private version = 0;
  draft: TransferDraft = { businessDate: '', remark: '', lines: [blankLine()] };
  readonly sameKey = sameBalanceKey;
  constructor() { effect(() => { this.company.current(); this.loadOptions(); if (this.id) this.load(); }); }
  private load(): void { this.api.get(this.id!).subscribe({ next: d => {
    this.version = d.version; this.draft = { businessDate: d.businessDate, remark: d.remark ?? '', lines: d.lines.map(l => ({
      itemId: l.itemId, sourceWarehouseId: l.sourceWarehouseId, sourceLocationId: l.sourceLocationId,
      destinationWarehouseId: l.destinationWarehouseId, destinationLocationId: l.destinationLocationId,
      quantity: String(l.quantity), tracking: l.tracking?.map(t => ({ lotNo: t.lotNo, serialNo: t.serialNo, quantity: String(t.quantity) })) ?? [],
    })) };
    this.cdr.markForCheck(); // draft 不是 signal；詳情可能晚於 MD 選項回應
    for (const line of this.draft.lines) { this.loadLocations(line.sourceWarehouseId); this.loadLocations(line.destinationWarehouseId); }
  }, error: e => this.error.set(this.api.errorKey(e)) }); }
  private loadOptions(): void {
    forkJoin({ items: fetchAllPages(page => this.itemsApi.list({ page, pageSize: OPTION_PAGE_SIZE })),
      warehouses: fetchAllPages(page => this.warehouseApi.list({ page, pageSize: OPTION_PAGE_SIZE })) }).subscribe({
      next: data => { this.items.set(data.items.filter(x => x.isActive)); this.warehouses.set(data.warehouses.filter(x => x.isActive)); },
      error: e => this.error.set(this.api.errorKey(e)),
    });
  }
  locations(warehouseId: number | null): StorageLocation[] { return warehouseId ? this.locationMap()[warehouseId] ?? [] : []; }
  loadLocations(id: number | null): void {
    if (!id || this.locationMap()[id]) return;
    this.locationMap.update(current => ({ ...current, [id]: [] }));
    fetchAllPages(page => this.warehouseApi.listLocations(id, { page, pageSize: OPTION_PAGE_SIZE })).subscribe({
      next: list => this.locationMap.update(current => ({ ...current, [id]: list.filter(x => x.isActive) })),
      error: e => this.error.set(this.api.errorKey(e)),
    });
  }
  addLine(): void { this.draft.lines.push(blankLine()); }
  removeLine(index: number): void { this.draft.lines.splice(index, 1); }
  mode(line: TransferLineDraft): Item['trackingMode'] { return this.items().find(x => x.id === line.itemId)?.trackingMode ?? 'None'; }
  save(): void {
    if (this.saving()) return;
    if (!this.draft.businessDate || !this.draft.lines.length || !this.draft.lines.every(validTransferLine)) {
      this.error.set(this.draft.lines.some(line => line.sourceWarehouseId && sameBalanceKey(line)) ? 'inv.transfer.sameKey' : 'inv.transfer.invalid'); return;
    }
    // 草稿可暫缺批序號；已輸入的必須與數量及追蹤方式一致。
    const trackingIssue = this.draft.lines.filter(line => line.tracking?.length || this.mode(line) === 'None')
      .map(line => trackingError(this.mode(line), line.quantity, line.tracking ?? [], false)).find(Boolean);
    if (trackingIssue) { this.error.set(trackingIssue); return; }
    if (duplicateSerialAcrossLines(this.draft.lines)) { this.error.set('inv.tracking.duplicate'); return; }
    this.error.set(null); this.saving.set(true);
    const action = this.id ? this.api.update(this.id, this.version, this.draft) : this.api.create(this.draft);
    action.subscribe({ next: () => { this.saving.set(false); void this.router.navigateByUrl('/inv/transfers'); },
      error: e => { this.saving.set(false); this.error.set(this.api.errorKey(e)); } });
  }
}
