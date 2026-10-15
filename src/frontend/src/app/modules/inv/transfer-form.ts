import { ChangeDetectionStrategy, ChangeDetectorRef, Component, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzInputModule } from 'ng-zorro-antd/input';
import { CompanyContextService } from '../../core/company/company-context.service';
import { InventoryOption, InventorySavedReferences, InventorySelectionOptionsService } from './selection-options.service';
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
          <button nz-button type="button" [attr.title]="label(items(), line.itemId)" [attr.aria-label]="'inv.transfer.item' | translate" (click)="chooseItem(line)">{{ label(items(), line.itemId) }}</button>
          <button nz-button type="button" [attr.title]="label(warehouses(), line.sourceWarehouseId)" [attr.aria-label]="'inv.transfer.sourceWarehouse' | translate" (click)="chooseSourceWarehouse(line)">{{ label(warehouses(), line.sourceWarehouseId) }}</button>
          <button nz-button type="button" [disabled]="!line.sourceWarehouseId" [attr.title]="line.sourceWarehouseId ? label(locations(), line.sourceLocationId) : ('inv.selection.chooseWarehouseFirst' | translate)" [attr.aria-label]="'inv.transfer.sourceLocation' | translate" (click)="chooseSourceLocation(line)">{{ label(locations(), line.sourceLocationId) }}</button>
          <button nz-button type="button" [attr.title]="label(warehouses(), line.destinationWarehouseId)" [attr.aria-label]="'inv.transfer.destinationWarehouse' | translate" (click)="chooseDestinationWarehouse(line)">{{ label(warehouses(), line.destinationWarehouseId) }}</button>
          <button nz-button type="button" [disabled]="!line.destinationWarehouseId" [attr.title]="line.destinationWarehouseId ? label(locations(), line.destinationLocationId) : ('inv.selection.chooseWarehouseFirst' | translate)" [attr.aria-label]="'inv.transfer.destinationLocation' | translate" (click)="chooseDestinationLocation(line)">{{ label(locations(), line.destinationLocationId) }}</button>
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
  styles: `.transfer-form{display:grid;gap:16px;color:var(--erp-color-text)}header,.head,.actions{display:flex;gap:16px;align-items:center;flex-wrap:wrap}header{justify-content:space-between}h1{margin:0;font-size:20px}h2{font-size:16px}.head label{display:grid;gap:4px}.line,.line-head{display:grid;grid-template-columns:2fr 2fr 2fr 2fr 2fr 1fr 40px;gap:8px;align-items:center}.line button,.line input{min-width:0;width:100%;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.tracking,.warn{grid-column:1/-1}.warn{color:var(--erp-color-error,#ff4d4f);margin:0}.hint{color:var(--erp-color-text-secondary);margin:0}@media(max-width:1100px){.line,.line-head{grid-template-columns:repeat(2,minmax(0,1fr))}}`,
})
export class TransferForm {
  private readonly route = inject(ActivatedRoute); private readonly router = inject(Router);
  private readonly api = inject(TransferService); private readonly selection = inject(InventorySelectionOptionsService);
  private readonly company = inject(CompanyContextService); private readonly translate = inject(TranslateService);
  private readonly cdr = inject(ChangeDetectorRef);
  readonly id = Number(this.route.snapshot.paramMap.get('id')) || null;
  readonly error = signal<string | null>(null); readonly saving = signal(false);
  readonly items = signal<InventoryOption[]>([]); readonly warehouses = signal<InventoryOption[]>([]);
  readonly locations = signal<InventoryOption[]>([]);
  private contextRevision = 0;
  private version = 0;
  draft: TransferDraft = { businessDate: '', remark: '', lines: [blankLine()] };
  readonly sameKey = sameBalanceKey;
  constructor() { effect(() => { this.company.current(); const revision = ++this.contextRevision;
    this.items.set([]); this.warehouses.set([]); this.locations.set([]); this.version = 0;
    this.draft = { businessDate: '', remark: '', lines: [blankLine()] }; this.error.set(null);
    this.cdr.markForCheck(); if (this.id) this.load(revision); }); }
  private load(revision: number): void { this.loadSaved(revision); this.api.get(this.id!).subscribe({ next: d => { if (revision !== this.contextRevision) return;
    this.version = d.version; this.draft = { businessDate: d.businessDate, remark: d.remark ?? '', lines: d.lines.map(l => ({
      itemId: l.itemId, sourceWarehouseId: l.sourceWarehouseId, sourceLocationId: l.sourceLocationId,
      destinationWarehouseId: l.destinationWarehouseId, destinationLocationId: l.destinationLocationId,
      quantity: String(l.quantity), tracking: l.tracking?.map(t => ({ lotNo: t.lotNo, serialNo: t.serialNo, quantity: String(t.quantity) })) ?? [],
    })) };
    this.cdr.markForCheck();
  }, error: e => { if (revision === this.contextRevision) this.error.set(this.api.errorKey(e)); } }); }
  private loadSaved(revision: number): void { this.selection.saved('transfers', this.id!).subscribe({
    next: (refs: InventorySavedReferences) => { if (revision !== this.contextRevision) return;
      this.items.update(old => [...refs.items.filter(x => !old.some(y => y.id === x.id)), ...old]);
      this.warehouses.update(old => [...refs.warehouses.filter(x => !old.some(y => y.id === x.id)), ...old]);
      this.locations.update(old => [...refs.locations.filter(x => !old.some(y => y.id === x.id)), ...old]); },
    error: e => { if (revision === this.contextRevision) this.error.set(this.api.errorKey(e)); },
  }); }
  label(options: InventoryOption[], id: number | null): string { const row = options.find(x => x.id === id);
    return row ? `${row.code} · ${row.name}${row.isActive === false ? ` (${this.translate.instant('inv.selection.inactive')})` : ''}` : '—'; }
  chooseItem(line: TransferLineDraft): void { const revision = this.contextRevision; this.selection.choose('item').subscribe(rows => {
    if (!rows?.[0] || revision !== this.contextRevision) return;
    if (line.itemId !== rows[0].id) line.tracking = [];
    line.itemId = rows[0].id; this.items.update(old => [...old.filter(x => x.id !== rows[0].id), rows[0]]); this.cdr.markForCheck();
  }); }
  chooseSourceWarehouse(line: TransferLineDraft): void { this.chooseWarehouse(line, 'source'); }
  chooseDestinationWarehouse(line: TransferLineDraft): void { this.chooseWarehouse(line, 'destination'); }
  private chooseWarehouse(line: TransferLineDraft, side: 'source' | 'destination'): void { const revision = this.contextRevision;
    this.selection.choose('warehouse').subscribe(rows => { if (!rows?.[0] || revision !== this.contextRevision) return;
      if (side === 'source') { if (line.sourceWarehouseId !== rows[0].id) line.sourceLocationId = null; line.sourceWarehouseId = rows[0].id; }
      else { if (line.destinationWarehouseId !== rows[0].id) line.destinationLocationId = null; line.destinationWarehouseId = rows[0].id; }
      this.warehouses.update(old => [...old.filter(x => x.id !== rows[0].id), rows[0]]); this.cdr.markForCheck();
    }); }
  chooseSourceLocation(line: TransferLineDraft): void { this.chooseLocation(line, 'source'); }
  chooseDestinationLocation(line: TransferLineDraft): void { this.chooseLocation(line, 'destination'); }
  private chooseLocation(line: TransferLineDraft, side: 'source' | 'destination'): void {
    const warehouseId = side === 'source' ? line.sourceWarehouseId : line.destinationWarehouseId; if (!warehouseId) return;
    const revision = this.contextRevision; this.selection.choose('location', warehouseId).subscribe(rows => {
      if (!rows?.[0] || revision !== this.contextRevision) return;
      if (side === 'source') { if (line.sourceWarehouseId !== warehouseId) return; line.sourceLocationId = rows[0].id; }
      else { if (line.destinationWarehouseId !== warehouseId) return; line.destinationLocationId = rows[0].id; }
      this.locations.update(old => [...old.filter(x => x.id !== rows[0].id), rows[0]]); this.cdr.markForCheck();
    }); }
  addLine(): void { this.draft.lines.push(blankLine()); }
  removeLine(index: number): void { this.draft.lines.splice(index, 1); }
  mode(line: TransferLineDraft): 'None' | 'Lot' | 'Serial' { return this.items().find(x => x.id === line.itemId)?.trackingMode ?? 'None'; }
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
