import { ChangeDetectionStrategy, ChangeDetectorRef, Component, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzInputModule } from 'ng-zorro-antd/input';
import { CompanyContextService } from '../../core/company/company-context.service';
import { InventoryOption, InventorySavedReferences, InventorySelectionOptionsService } from './selection-options.service';
import { OtherReceiptService, ReceiptDraft, ReceiptLineDraft, validReceiptLine } from './other-receipt.service';
import { TrackingInput } from './tracking-input';
import { duplicateSerialAcrossLines, trackingError } from './tracking';

const blankLine = (): ReceiptLineDraft => ({ itemId: null, warehouseId: null, locationId: null, quantity: '', unitCost: '0' });

@Component({
  selector: 'erp-other-receipt-form',
  imports: [FormsModule, RouterLink, TranslatePipe, NzAlertModule, NzButtonModule, NzInputModule, TrackingInput],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="receipt-form"><header><h1>{{ (id ? 'inv.receipt.edit' : 'inv.receipt.new') | translate }}</h1>
      <a routerLink="/inv/other-receipts">{{ 'inv.receipt.back' | translate }}</a></header>
      @if (error()) { <nz-alert nzType="error" nzShowIcon data-testid="inv-receipt-error" [nzMessage]="error()! | translate" /> }
      <div class="head"><label>{{ 'inv.receipt.date' | translate }} <input nz-input type="date" [(ngModel)]="draft.businessDate" data-testid="inv-receipt-date" /></label>
        <label>{{ 'inv.receipt.remark' | translate }} <input nz-input [(ngModel)]="draft.remark" maxlength="500" data-testid="inv-receipt-remark" /></label></div>
      <h2>{{ 'inv.receipt.lines' | translate }}</h2>
      <div class="line-head"><span>{{ 'inv.receipt.item' | translate }}</span><span>{{ 'inv.receipt.warehouse' | translate }}</span>
        <span>{{ 'inv.receipt.location' | translate }}</span><span>{{ 'inv.receipt.quantity' | translate }}</span><span>{{ 'inv.receipt.unitCost' | translate }}</span><span></span></div>
      @for (line of draft.lines; track $index; let index = $index) {
        <div class="line" [attr.data-testid]="'inv-receipt-line-' + index">
          <button nz-button type="button" [attr.title]="label(items(), line.itemId)" [attr.aria-label]="'inv.receipt.item' | translate" (click)="chooseItem(line)">{{ label(items(), line.itemId) }}</button>
          <button nz-button type="button" [attr.title]="label(warehouses(), line.warehouseId)" [attr.aria-label]="'inv.receipt.warehouse' | translate" (click)="chooseWarehouse(line)">{{ label(warehouses(), line.warehouseId) }}</button>
          <button nz-button type="button" [disabled]="!line.warehouseId" [attr.title]="line.warehouseId ? label(locations(), line.locationId) : ('inv.selection.chooseWarehouseFirst' | translate)" [attr.aria-label]="'inv.receipt.location' | translate" (click)="chooseLocation(line)">{{ label(locations(), line.locationId) }}</button>
          <input nz-input type="text" inputmode="decimal" [(ngModel)]="line.quantity" [attr.aria-label]="'inv.receipt.quantity' | translate" />
          <input nz-input type="text" inputmode="decimal" [(ngModel)]="line.unitCost" [attr.aria-label]="'inv.receipt.unitCost' | translate" />
          <button nz-button type="button" [attr.aria-label]="'inv.receipt.removeLine' | translate" (click)="removeLine(index)">×</button>
          @if (mode(line) !== 'None') { <erp-tracking-input class="tracking" [mode]="mode(line)" [quantity]="line.quantity"
            [rows]="line.tracking ?? []" (rowsChange)="line.tracking = $event" /> }
        </div>
      }
      <div class="actions"><button nz-button type="button" (click)="addLine()">{{ 'inv.receipt.addLine' | translate }}</button>
        <button nz-button nzType="primary" type="button" data-testid="inv-receipt-save" [disabled]="saving()" (click)="save()">{{ 'inv.receipt.save' | translate }}</button></div>
    </section>
  `,
  styles: `.receipt-form{display:grid;gap:16px;color:var(--erp-color-text)}header,.head,.actions{display:flex;gap:16px;align-items:center;flex-wrap:wrap}header{justify-content:space-between}h1{margin:0;font-size:20px}h2{font-size:16px}.head label{display:grid;gap:4px}.line,.line-head{display:grid;grid-template-columns:2fr 2fr 2fr 1fr 1fr 40px;gap:8px;align-items:center}.line button,.line input{min-width:0;width:100%;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.tracking{grid-column:1/-1}@media(max-width:900px){.line,.line-head{grid-template-columns:repeat(2,minmax(0,1fr))}}`,
})
export class OtherReceiptForm {
  private readonly route = inject(ActivatedRoute); private readonly router = inject(Router);
  private readonly api = inject(OtherReceiptService); private readonly selection = inject(InventorySelectionOptionsService);
  private readonly company = inject(CompanyContextService);
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly translate = inject(TranslateService);
  readonly id = Number(this.route.snapshot.paramMap.get('id')) || null;
  readonly error = signal<string | null>(null); readonly saving = signal(false);
  readonly items = signal<InventoryOption[]>([]); readonly warehouses = signal<InventoryOption[]>([]);
  readonly locations = signal<InventoryOption[]>([]);
  private contextRevision = 0;
  private version = 0;
  draft: ReceiptDraft = { businessDate: '', remark: '', lines: [blankLine()] };
  constructor() { effect(() => { this.company.current(); const revision = ++this.contextRevision; this.items.set([]); this.warehouses.set([]); this.locations.set([]); if (this.id) this.load(revision); }); }
  private load(revision: number): void { this.loadSaved(revision); this.api.get(this.id!).subscribe({ next: d => { if (revision !== this.contextRevision) return;
    this.version = d.version; this.draft = { businessDate: d.businessDate, remark: d.remark ?? '', lines: d.lines.map(l => ({
      itemId: l.itemId, warehouseId: l.warehouseId, locationId: l.locationId,
      quantity: String(l.quantity), unitCost: String(l.unitCost), tracking: l.tracking?.map(t => ({
        lotNo: t.lotNo, serialNo: t.serialNo, quantity: String(t.quantity), manufactureDate: t.manufactureDate, expiryDate: t.expiryDate })) ?? [],
    })) }; this.cdr.markForCheck();
  }, error: e => { if (revision === this.contextRevision) this.error.set(this.api.errorKey(e)); } }); }
  private loadSaved(revision: number): void { this.selection.saved('other-receipts', this.id!).subscribe({
    next: (refs: InventorySavedReferences) => { if (revision !== this.contextRevision) return;
      this.items.update(current => [...refs.items.filter(x => !current.some(y => y.id === x.id)), ...current]);
      this.warehouses.update(current => [...refs.warehouses.filter(x => !current.some(y => y.id === x.id)), ...current]);
      this.locations.update(current => [...refs.locations.filter(x => !current.some(y => y.id === x.id)), ...current]); },
    error: e => { if (revision === this.contextRevision) this.error.set(this.api.errorKey(e)); },
  }); }
  label(options: InventoryOption[], id: number | null): string {
    const option = options.find(x => x.id === id);
    return option ? `${option.code} · ${option.name}${option.isActive === false ? ` (${this.translate.instant('inv.selection.inactive')})` : ''}` : '—';
  }
  chooseItem(line: ReceiptLineDraft): void { const revision = this.contextRevision; this.selection.choose('item').subscribe(rows => {
    if (!rows?.[0] || revision !== this.contextRevision) return;
    if (line.itemId !== rows[0].id) line.tracking = [];
    line.itemId = rows[0].id; this.items.update(old => [...old.filter(x => x.id !== rows[0].id), rows[0]]); this.cdr.markForCheck();
  }); }
  chooseWarehouse(line: ReceiptLineDraft): void { const revision = this.contextRevision; this.selection.choose('warehouse').subscribe(rows => {
    if (!rows?.[0] || revision !== this.contextRevision) return;
    if (line.warehouseId !== rows[0].id) line.locationId = null;
    line.warehouseId = rows[0].id; this.warehouses.update(old => [...old.filter(x => x.id !== rows[0].id), rows[0]]); this.cdr.markForCheck();
  }); }
  chooseLocation(line: ReceiptLineDraft): void { if (!line.warehouseId) return; const warehouseId = line.warehouseId; const revision = this.contextRevision; this.selection.choose('location', warehouseId).subscribe(rows => {
    if (!rows?.[0] || line.warehouseId !== warehouseId || revision !== this.contextRevision) return;
    line.locationId = rows[0].id; this.locations.update(old => [...old.filter(x => x.id !== rows[0].id), rows[0]]); this.cdr.markForCheck();
  }); }
  addLine(): void { this.draft.lines.push(blankLine()); }
  removeLine(index: number): void { this.draft.lines.splice(index, 1); }
  mode(line: ReceiptLineDraft): 'None' | 'Lot' | 'Serial' { return this.items().find(x => x.id === line.itemId)?.trackingMode ?? 'None'; }
  save(): void {
    if (this.saving()) return;
    if (!this.draft.businessDate || !this.draft.lines.length || !this.draft.lines.every(validReceiptLine)) {
      this.error.set('inv.receipt.invalid'); return;
    }
    const trackingIssue = this.draft.lines.map(line => trackingError(this.mode(line), line.quantity, line.tracking ?? [], true)).find(Boolean);
    if (trackingIssue) { this.error.set(trackingIssue); return; }
    if (duplicateSerialAcrossLines(this.draft.lines)) { this.error.set('inv.tracking.duplicate'); return; }
    this.error.set(null); this.saving.set(true);
    const action = this.id ? this.api.update(this.id, this.version, this.draft) : this.api.create(this.draft);
    action.subscribe({ next: () => { this.saving.set(false); void this.router.navigateByUrl('/inv/other-receipts'); },
      error: e => { this.saving.set(false); this.error.set(this.api.errorKey(e)); } });
  }
}
