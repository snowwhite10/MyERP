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
import { OtherIssueService, IssueDraft, IssueLineDraft, validIssueLine } from './other-issue.service';
import { TrackingInput } from './tracking-input';
import { duplicateSerialAcrossLines, trackingError } from './tracking';

const blankLine = (): IssueLineDraft => ({ itemId: null, warehouseId: null, locationId: null, quantity: '', unitCost: '0' });

@Component({
  selector: 'erp-other-issue-form',
  imports: [FormsModule, RouterLink, TranslatePipe, NzAlertModule, NzButtonModule, NzInputModule, TrackingInput],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="issue-form"><header><h1>{{ (id ? 'inv.issue.edit' : 'inv.issue.new') | translate }}</h1>
      <a routerLink="/inv/other-issues">{{ 'inv.issue.back' | translate }}</a></header>
      @if (error()) { <nz-alert nzType="error" nzShowIcon [nzMessage]="error()! | translate" /> }
      <div class="head"><label>{{ 'inv.issue.date' | translate }} <input nz-input type="date" [(ngModel)]="draft.businessDate" data-testid="inv-issue-date" /></label>
        <label>{{ 'inv.issue.remark' | translate }} <input nz-input [(ngModel)]="draft.remark" maxlength="500" data-testid="inv-issue-remark" /></label></div>
      <h2>{{ 'inv.issue.lines' | translate }}</h2>
      <div class="line-head"><span>{{ 'inv.issue.item' | translate }}</span><span>{{ 'inv.issue.warehouse' | translate }}</span>
        <span>{{ 'inv.issue.location' | translate }}</span><span>{{ 'inv.issue.quantity' | translate }}</span><span>{{ 'inv.issue.estimatedUnitCost' | translate }}</span><span></span></div>
      @for (line of draft.lines; track $index; let index = $index) {
        <div class="line" [attr.data-testid]="'inv-issue-line-' + index">
          <select [(ngModel)]="line.itemId" (ngModelChange)="line.tracking = []" [attr.aria-label]="'inv.issue.item' | translate"><option [ngValue]="null">—</option>
            @for (item of items(); track item.id) { <option [ngValue]="item.id">{{ item.code }} · {{ item.name }}</option> }</select>
          <select [(ngModel)]="line.warehouseId" (ngModelChange)="warehouseChanged(line)" [attr.aria-label]="'inv.issue.warehouse' | translate"><option [ngValue]="null">—</option>
            @for (wh of warehouses(); track wh.id) { <option [ngValue]="wh.id">{{ wh.code }} · {{ wh.name }}</option> }</select>
          <select [(ngModel)]="line.locationId" [attr.aria-label]="'inv.issue.location' | translate"><option [ngValue]="null">—</option>
            @for (loc of locations(line.warehouseId); track loc.id) { <option [ngValue]="loc.id">{{ loc.code }} · {{ loc.name }}</option> }</select>
          <input nz-input type="text" inputmode="decimal" [(ngModel)]="line.quantity" [attr.aria-label]="'inv.issue.quantity' | translate" />
          <input nz-input type="text" inputmode="decimal" [(ngModel)]="line.unitCost" [attr.aria-label]="'inv.issue.estimatedUnitCost' | translate" />
          <button nz-button type="button" [attr.aria-label]="'inv.issue.removeLine' | translate" (click)="removeLine(index)">×</button>
          @if (mode(line) !== 'None') { <erp-tracking-input class="tracking" [mode]="mode(line)" [quantity]="line.quantity"
            [rows]="line.tracking ?? []" [incoming]="false" (rowsChange)="line.tracking = $event" /> }
        </div>
      }
      <div class="actions"><button nz-button type="button" (click)="addLine()">{{ 'inv.issue.addLine' | translate }}</button>
        <button nz-button nzType="primary" type="button" data-testid="inv-issue-save" [disabled]="saving()" (click)="save()">{{ 'inv.issue.save' | translate }}</button></div>
    </section>
  `,
  styles: `.issue-form{display:grid;gap:16px;color:var(--erp-color-text)}header,.head,.actions{display:flex;gap:16px;align-items:center;flex-wrap:wrap}header{justify-content:space-between}h1{margin:0;font-size:20px}h2{font-size:16px}.head label{display:grid;gap:4px}.line,.line-head{display:grid;grid-template-columns:2fr 2fr 2fr 1fr 1fr 40px;gap:8px;align-items:center}.line select,.line input{min-width:0;width:100%}.tracking{grid-column:1/-1}@media(max-width:900px){.line,.line-head{grid-template-columns:repeat(2,minmax(0,1fr))}}`,
})
export class OtherIssueForm {
  private readonly route = inject(ActivatedRoute); private readonly router = inject(Router);
  private readonly api = inject(OtherIssueService); private readonly itemsApi = inject(ItemService);
  private readonly warehouseApi = inject(WarehouseService); private readonly company = inject(CompanyContextService);
  private readonly cdr = inject(ChangeDetectorRef);
  readonly id = Number(this.route.snapshot.paramMap.get('id')) || null;
  readonly error = signal<string | null>(null); readonly saving = signal(false);
  readonly items = signal<Item[]>([]); readonly warehouses = signal<Warehouse[]>([]);
  private readonly locationMap = signal<Record<number, StorageLocation[]>>({});
  private version = 0;
  draft: IssueDraft = { businessDate: '', remark: '', lines: [blankLine()] };
  constructor() { effect(() => { this.company.current(); this.loadOptions(); if (this.id) this.load(); }); }
  private load(): void { this.api.get(this.id!).subscribe({ next: d => {
    this.version = d.version; this.draft = { businessDate: d.businessDate, remark: d.remark ?? '', lines: d.lines.map(l => ({
      itemId: l.itemId, warehouseId: l.warehouseId, locationId: l.locationId,
      quantity: String(l.quantity), unitCost: String(l.unitCost), tracking: l.tracking?.map(t => ({
        lotNo: t.lotNo, serialNo: t.serialNo, quantity: String(t.quantity) })) ?? [],
    })) }; this.cdr.markForCheck(); // draft 不是 signal；詳情可能晚於 MD 選項回應
    for (const line of this.draft.lines) if (line.warehouseId) this.loadLocations(line.warehouseId);
  }, error: e => this.error.set(this.api.errorKey(e)) }); }
  private loadOptions(): void {
    forkJoin({ items: fetchAllPages(page => this.itemsApi.list({ page, pageSize: OPTION_PAGE_SIZE })),
      warehouses: fetchAllPages(page => this.warehouseApi.list({ page, pageSize: OPTION_PAGE_SIZE })) }).subscribe({
      next: data => { this.items.set(data.items.filter(x => x.isActive)); this.warehouses.set(data.warehouses.filter(x => x.isActive)); },
      error: e => this.error.set(this.api.errorKey(e)),
    });
  }
  locations(warehouseId: number | null): StorageLocation[] { return warehouseId ? this.locationMap()[warehouseId] ?? [] : []; }
  warehouseChanged(line: IssueLineDraft): void { line.locationId = null; if (line.warehouseId) this.loadLocations(line.warehouseId); }
  private loadLocations(id: number): void {
    if (this.locationMap()[id]) return;
    fetchAllPages(page => this.warehouseApi.listLocations(id, { page, pageSize: OPTION_PAGE_SIZE })).subscribe({
      next: list => this.locationMap.update(current => ({ ...current, [id]: list.filter(x => x.isActive) })),
      error: e => this.error.set(this.api.errorKey(e)),
    });
  }
  addLine(): void { this.draft.lines.push(blankLine()); }
  removeLine(index: number): void { this.draft.lines.splice(index, 1); }
  mode(line: IssueLineDraft): Item['trackingMode'] { return this.items().find(x => x.id === line.itemId)?.trackingMode ?? 'None'; }
  save(): void {
    if (this.saving()) return;
    if (!this.draft.businessDate || !this.draft.lines.length || !this.draft.lines.every(validIssueLine)) {
      this.error.set('inv.issue.invalid'); return;
    }
    const trackingIssue = this.draft.lines.map(line => trackingError(this.mode(line), line.quantity, line.tracking ?? [], false)).find(Boolean);
    if (trackingIssue) { this.error.set(trackingIssue); return; }
    if (duplicateSerialAcrossLines(this.draft.lines)) { this.error.set('inv.tracking.duplicate'); return; }
    this.error.set(null); this.saving.set(true);
    const action = this.id ? this.api.update(this.id, this.version, this.draft) : this.api.create(this.draft);
    action.subscribe({ next: () => { this.saving.set(false); void this.router.navigateByUrl('/inv/other-issues'); },
      error: e => { this.saving.set(false); this.error.set(this.api.errorKey(e)); } });
  }
}
