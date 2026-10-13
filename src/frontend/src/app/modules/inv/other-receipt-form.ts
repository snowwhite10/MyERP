import { ChangeDetectionStrategy, Component, effect, inject, signal } from '@angular/core';
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
import { OtherReceiptService, ReceiptDraft, ReceiptLineDraft, validReceiptLine } from './other-receipt.service';

const blankLine = (): ReceiptLineDraft => ({ itemId: null, warehouseId: null, locationId: null, quantity: '', unitCost: '0' });

@Component({
  selector: 'erp-other-receipt-form',
  imports: [FormsModule, RouterLink, TranslatePipe, NzAlertModule, NzButtonModule, NzInputModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="receipt-form"><header><h1>{{ (id ? 'inv.receipt.edit' : 'inv.receipt.new') | translate }}</h1>
      <a routerLink="/inv/other-receipts">{{ 'inv.receipt.back' | translate }}</a></header>
      @if (error()) { <nz-alert nzType="error" nzShowIcon [nzMessage]="error()! | translate" /> }
      <div class="head"><label>{{ 'inv.receipt.date' | translate }} <input nz-input type="date" [(ngModel)]="draft.businessDate" data-testid="inv-receipt-date" /></label>
        <label>{{ 'inv.receipt.remark' | translate }} <input nz-input [(ngModel)]="draft.remark" maxlength="500" data-testid="inv-receipt-remark" /></label></div>
      <h2>{{ 'inv.receipt.lines' | translate }}</h2>
      <div class="line-head"><span>{{ 'inv.receipt.item' | translate }}</span><span>{{ 'inv.receipt.warehouse' | translate }}</span>
        <span>{{ 'inv.receipt.location' | translate }}</span><span>{{ 'inv.receipt.quantity' | translate }}</span><span>{{ 'inv.receipt.unitCost' | translate }}</span><span></span></div>
      @for (line of draft.lines; track $index; let index = $index) {
        <div class="line" [attr.data-testid]="'inv-receipt-line-' + index">
          <select [(ngModel)]="line.itemId" [attr.aria-label]="'inv.receipt.item' | translate"><option [ngValue]="null">—</option>
            @for (item of items(); track item.id) { <option [ngValue]="item.id">{{ item.code }} · {{ item.name }}</option> }</select>
          <select [(ngModel)]="line.warehouseId" (ngModelChange)="warehouseChanged(line)" [attr.aria-label]="'inv.receipt.warehouse' | translate"><option [ngValue]="null">—</option>
            @for (wh of warehouses(); track wh.id) { <option [ngValue]="wh.id">{{ wh.code }} · {{ wh.name }}</option> }</select>
          <select [(ngModel)]="line.locationId" [attr.aria-label]="'inv.receipt.location' | translate"><option [ngValue]="null">—</option>
            @for (loc of locations(line.warehouseId); track loc.id) { <option [ngValue]="loc.id">{{ loc.code }} · {{ loc.name }}</option> }</select>
          <input nz-input type="text" inputmode="decimal" [(ngModel)]="line.quantity" [attr.aria-label]="'inv.receipt.quantity' | translate" />
          <input nz-input type="text" inputmode="decimal" [(ngModel)]="line.unitCost" [attr.aria-label]="'inv.receipt.unitCost' | translate" />
          <button nz-button type="button" [attr.aria-label]="'inv.receipt.removeLine' | translate" (click)="removeLine(index)">×</button>
        </div>
      }
      <div class="actions"><button nz-button type="button" (click)="addLine()">{{ 'inv.receipt.addLine' | translate }}</button>
        <button nz-button nzType="primary" type="button" data-testid="inv-receipt-save" [disabled]="saving()" (click)="save()">{{ 'inv.receipt.save' | translate }}</button></div>
    </section>
  `,
  styles: `.receipt-form{display:grid;gap:16px;color:var(--erp-color-text)}header,.head,.actions{display:flex;gap:16px;align-items:center;flex-wrap:wrap}header{justify-content:space-between}h1{margin:0;font-size:20px}h2{font-size:16px}.head label{display:grid;gap:4px}.line,.line-head{display:grid;grid-template-columns:2fr 2fr 2fr 1fr 1fr 40px;gap:8px;align-items:center}.line select,.line input{min-width:0;width:100%}@media(max-width:900px){.line,.line-head{grid-template-columns:repeat(2,minmax(0,1fr))}}`,
})
export class OtherReceiptForm {
  private readonly route = inject(ActivatedRoute); private readonly router = inject(Router);
  private readonly api = inject(OtherReceiptService); private readonly itemsApi = inject(ItemService);
  private readonly warehouseApi = inject(WarehouseService); private readonly company = inject(CompanyContextService);
  readonly id = Number(this.route.snapshot.paramMap.get('id')) || null;
  readonly error = signal<string | null>(null); readonly saving = signal(false);
  readonly items = signal<Item[]>([]); readonly warehouses = signal<Warehouse[]>([]);
  private readonly locationMap = signal<Record<number, StorageLocation[]>>({});
  private version = 0;
  draft: ReceiptDraft = { businessDate: '', remark: '', lines: [blankLine()] };
  constructor() { effect(() => { this.company.current(); this.loadOptions(); if (this.id) this.load(); }); }
  private load(): void { this.api.get(this.id!).subscribe({ next: d => {
    this.version = d.version; this.draft = { businessDate: d.businessDate, remark: d.remark ?? '', lines: d.lines.map(l => ({
      itemId: l.itemId, warehouseId: l.warehouseId, locationId: l.locationId,
      quantity: String(l.quantity), unitCost: String(l.unitCost),
    })) }; for (const line of this.draft.lines) if (line.warehouseId) this.loadLocations(line.warehouseId);
  }, error: e => this.error.set(this.api.errorKey(e)) }); }
  private loadOptions(): void {
    forkJoin({ items: fetchAllPages(page => this.itemsApi.list({ page, pageSize: OPTION_PAGE_SIZE })),
      warehouses: fetchAllPages(page => this.warehouseApi.list({ page, pageSize: OPTION_PAGE_SIZE })) }).subscribe({
      next: data => { this.items.set(data.items.filter(x => x.isActive)); this.warehouses.set(data.warehouses.filter(x => x.isActive)); },
      error: e => this.error.set(this.api.errorKey(e)),
    });
  }
  locations(warehouseId: number | null): StorageLocation[] { return warehouseId ? this.locationMap()[warehouseId] ?? [] : []; }
  warehouseChanged(line: ReceiptLineDraft): void { line.locationId = null; if (line.warehouseId) this.loadLocations(line.warehouseId); }
  private loadLocations(id: number): void {
    if (this.locationMap()[id]) return;
    fetchAllPages(page => this.warehouseApi.listLocations(id, { page, pageSize: OPTION_PAGE_SIZE })).subscribe({
      next: list => this.locationMap.update(current => ({ ...current, [id]: list.filter(x => x.isActive) })),
      error: e => this.error.set(this.api.errorKey(e)),
    });
  }
  addLine(): void { this.draft.lines.push(blankLine()); }
  removeLine(index: number): void { this.draft.lines.splice(index, 1); }
  save(): void {
    if (this.saving()) return;
    if (!this.draft.businessDate || !this.draft.lines.length || !this.draft.lines.every(validReceiptLine)) {
      this.error.set('inv.receipt.invalid'); return;
    }
    this.error.set(null); this.saving.set(true);
    const action = this.id ? this.api.update(this.id, this.version, this.draft) : this.api.create(this.draft);
    action.subscribe({ next: () => { this.saving.set(false); void this.router.navigateByUrl('/inv/other-receipts'); },
      error: e => { this.saving.set(false); this.error.set(this.api.errorKey(e)); } });
  }
}
