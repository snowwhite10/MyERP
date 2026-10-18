import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, ChangeDetectorRef, Component, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzInputModule } from 'ng-zorro-antd/input';
import { CompanyContextService } from '../../core/company/company-context.service';
import { apiErrorTraceId } from '../../core/http/api-error';
import { ApiErrorDisplay } from '../../shared/api-error-display';
import { HasUnsavedChanges } from '../../shared/document-page/unsaved-changes.guard';
import { DisassemblyDraft, DisassemblyLineDraft, DisassemblyService, validDisassemblyDraft } from './disassembly.service';
import { disassemblyValidationDetails } from './disassembly-validation';
import { InventoryOption, InventorySavedReferences, InventorySelectionOptionsService } from './selection-options.service';
import { duplicateSerialAcrossLines, trackingError } from './tracking';
import { TrackingInput } from './tracking-input';

const blankLine = (): DisassemblyLineDraft => ({ itemId: null, warehouseId: null, locationId: null, quantity: '' });
const blankComponent = (): DisassemblyLineDraft => ({ ...blankLine(), allocationWeight: '1' });

/** 介面 5.13／5.14：恰一成品來源與多個組件產出，保存整組草稿；成本由核准過帳時決定。 */
@Component({
  selector: 'erp-disassembly-form',
  imports: [NgTemplateOutlet, FormsModule, RouterLink, TranslatePipe, ApiErrorDisplay, NzButtonModule, NzInputModule, TrackingInput],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="disassembly-form"><header><h1>{{ (id ? 'inv.disassembly.edit' : 'inv.disassembly.new') | translate }}</h1>
      <a routerLink="/inv/disassemblies">{{ 'inv.disassembly.back' | translate }}</a></header>
      @if (error()) { <erp-api-error-display [messageKey]="error()!" [traceId]="errorTrace()" [details]="errorDetails()" data-testid="inv-disassembly-error" /> }
      <div class="head"><label>{{ 'inv.disassembly.date' | translate }} <input nz-input type="date" name="disassemblyDate" autocomplete="off" [(ngModel)]="draft.businessDate" data-testid="inv-disassembly-date" /></label>
        <label>{{ 'inv.disassembly.remark' | translate }} <input nz-input name="disassemblyRemark" autocomplete="off" [(ngModel)]="draft.remark" maxlength="1000" data-testid="inv-disassembly-remark" /></label></div>
      <p class="hint">{{ 'inv.disassembly.hint' | translate }}</p>
      <h2>{{ 'inv.disassembly.finished' | translate }}</h2>
      <ng-container [ngTemplateOutlet]="lineFields" [ngTemplateOutletContext]="{ $implicit: draft.finished, incoming: false }" />
      <h2>{{ 'inv.disassembly.components' | translate }}</h2>
      @for (line of draft.components; track $index; let index = $index) {
        <div [attr.data-testid]="'inv-disassembly-component-' + index">
          <ng-container [ngTemplateOutlet]="lineFields" [ngTemplateOutletContext]="{ $implicit: line, incoming: true }" />
          <label>{{ 'inv.disassembly.allocationWeight' | translate }} <input nz-input type="text" inputmode="decimal" name="disassemblyWeight" autocomplete="off" [(ngModel)]="line.allocationWeight" [attr.aria-label]="'inv.disassembly.allocationWeight' | translate" /></label>
          <button nz-button type="button" (click)="removeComponent(index)" [attr.aria-label]="'inv.disassembly.removeLine' | translate">{{ 'inv.disassembly.removeLine' | translate }}</button>
        </div>
      }
      <ng-template #lineFields let-line let-incoming="incoming">
        <div class="line-head"><span>{{ 'inv.disassembly.item' | translate }}</span><span>{{ 'inv.disassembly.warehouse' | translate }}</span>
          <span>{{ 'inv.disassembly.location' | translate }}</span><span>{{ 'inv.disassembly.quantity' | translate }}</span></div>
        <div class="line">
          <button nz-button type="button" [attr.aria-label]="'inv.disassembly.item' | translate" [attr.title]="label(items(), line.itemId)" (click)="chooseItem(line)">{{ label(items(), line.itemId) }}</button>
          <button nz-button type="button" [attr.aria-label]="'inv.disassembly.warehouse' | translate" [attr.title]="label(warehouses(), line.warehouseId)" (click)="chooseWarehouse(line)">{{ label(warehouses(), line.warehouseId) }}</button>
          <button nz-button type="button" [disabled]="!line.warehouseId" [attr.aria-label]="'inv.disassembly.location' | translate"
            [attr.title]="line.warehouseId ? label(locations(), line.locationId) : ('inv.selection.chooseWarehouseFirst' | translate)"
            (click)="chooseLocation(line)">{{ label(locations(), line.locationId) }}</button>
          <input nz-input type="text" inputmode="decimal" name="disassemblyQuantity" autocomplete="off" [(ngModel)]="line.quantity" [attr.aria-label]="'inv.disassembly.quantity' | translate" />
          @if (mode(line) !== 'None') { <erp-tracking-input class="tracking" [mode]="mode(line)" [quantity]="line.quantity"
            [rows]="line.tracking ?? []" [incoming]="incoming" (rowsChange)="line.tracking = $event" /> }
        </div>
      </ng-template>
      <div class="actions"><button nz-button type="button" (click)="addComponent()" [disabled]="draft.components.length >= 999"
        [attr.title]="draft.components.length >= 999 ? ('inv.disassembly.maxComponents' | translate) : null"
        data-testid="inv-disassembly-add">{{ 'inv.disassembly.addLine' | translate }}</button>
        <button nz-button nzType="primary" type="button" [disabled]="saving()" [attr.title]="saving() ? ('inv.disassembly.busy' | translate) : null"
          (click)="save()" data-testid="inv-disassembly-save">{{ 'inv.disassembly.save' | translate }}</button></div>
    </section>
  `,
  styles: `.disassembly-form{display:grid;gap:16px;color:var(--erp-color-text)}header,.head,.actions{display:flex;gap:16px;align-items:center;flex-wrap:wrap}header{justify-content:space-between}h1{margin:0;font-size:20px;text-wrap:balance}h2{font-size:16px;margin:0}.head label{display:grid;gap:4px}.line,.line-head{display:grid;grid-template-columns:repeat(3,minmax(0,2fr)) minmax(120px,1fr);gap:8px;align-items:center;margin-bottom:8px}.line-head{color:var(--erp-color-text-secondary);font-size:12px}.line button,.line input{min-width:0;width:100%;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.line input{font-variant-numeric:tabular-nums}.tracking{grid-column:1/-1}.hint{color:var(--erp-color-text-secondary);margin:0}@media(max-width:900px){.line,.line-head{grid-template-columns:repeat(2,minmax(0,1fr))}}`,
})
export class DisassemblyForm implements HasUnsavedChanges {
  private readonly route = inject(ActivatedRoute); private readonly router = inject(Router);
  private readonly api = inject(DisassemblyService); private readonly selection = inject(InventorySelectionOptionsService);
  private readonly company = inject(CompanyContextService); private readonly translate = inject(TranslateService);
  private readonly cdr = inject(ChangeDetectorRef);
  readonly id = Number(this.route.snapshot.paramMap.get('id')) || null;
  readonly error = signal<string | null>(null); readonly errorTrace = signal<string | null>(null);
  readonly errorDetails = signal<string[]>([]); readonly saving = signal(false);
  readonly items = signal<InventoryOption[]>([]); readonly warehouses = signal<InventoryOption[]>([]);
  readonly locations = signal<InventoryOption[]>([]);
  private contextRevision = 0; private version = 0; private baseline = '';
  draft: DisassemblyDraft = { businessDate: '', remark: '', finished: blankLine(), components: [] };
  constructor() { effect(() => { this.company.current(); const revision = ++this.contextRevision;
    this.items.set([]); this.warehouses.set([]); this.locations.set([]); this.version = 0;
    this.draft = { businessDate: '', remark: '', finished: blankLine(), components: [] };
    this.error.set(null); this.errorTrace.set(null); this.errorDetails.set([]);
    this.baseline = JSON.stringify(this.draft);
    this.cdr.markForCheck(); if (this.id) this.load(revision); }); }
  private load(revision: number): void {
    this.api.savedReferences(this.id!).subscribe({ next: (refs: InventorySavedReferences) => {
      if (revision !== this.contextRevision) return;
      this.items.set(refs.items); this.warehouses.set(refs.warehouses); this.locations.set(refs.locations);
    }, error: e => { if (revision === this.contextRevision) this.showError(e); } });
    this.api.get(this.id!).subscribe({ next: detail => { if (revision !== this.contextRevision) return;
      this.version = detail.version;
      const line = (value: typeof detail.lines[number]): DisassemblyLineDraft => ({ itemId: value.itemId,
        warehouseId: value.warehouseId, locationId: value.locationId, quantity: String(value.quantity),
        allocationWeight: value.allocationWeight == null ? undefined : String(value.allocationWeight),
        tracking: value.tracking?.map(t => ({ lotNo: t.lotNo, serialNo: t.serialNo, quantity: String(t.quantity) })) ?? [] });
      const finished = detail.lines.find(x => x.role === 'Finished');
      if (!finished) { this.error.set('inv.disassembly.invalid'); return; }
      this.draft = { businessDate: detail.businessDate, remark: detail.remark ?? '', finished: line(finished),
        components: detail.lines.filter(x => x.role === 'Component').map(line) };
      this.baseline = JSON.stringify(this.draft); this.cdr.markForCheck();
    }, error: e => { if (revision === this.contextRevision) this.showError(e); } });
  }
  label(options: InventoryOption[], id: number | null): string { const row = options.find(x => x.id === id);
    return row ? `${row.code} · ${row.name}${row.isActive === false ? ` (${this.translate.instant('inv.selection.inactive')})` : ''}` : '—'; }
  mode(line: DisassemblyLineDraft): 'None' | 'Lot' | 'Serial' { return this.items().find(x => x.id === line.itemId)?.trackingMode ?? 'None'; }
  chooseItem(line: DisassemblyLineDraft): void { const revision = this.contextRevision; this.selection.choose('item').subscribe(rows => {
    if (!rows?.[0] || revision !== this.contextRevision) return;
    if (line.itemId !== rows[0].id) line.tracking = [];
    line.itemId = rows[0].id; this.items.update(old => [...old.filter(x => x.id !== rows[0].id), rows[0]]); this.cdr.markForCheck();
  }); }
  chooseWarehouse(line: DisassemblyLineDraft): void { const revision = this.contextRevision; this.selection.choose('warehouse').subscribe(rows => {
    if (!rows?.[0] || revision !== this.contextRevision) return;
    if (line.warehouseId !== rows[0].id) line.locationId = null;
    line.warehouseId = rows[0].id; this.warehouses.update(old => [...old.filter(x => x.id !== rows[0].id), rows[0]]); this.cdr.markForCheck();
  }); }
  chooseLocation(line: DisassemblyLineDraft): void { if (!line.warehouseId) return;
    const warehouseId = line.warehouseId; const revision = this.contextRevision;
    this.selection.choose('location', warehouseId).subscribe(rows => {
      if (!rows?.[0] || revision !== this.contextRevision || line.warehouseId !== warehouseId) return;
      line.locationId = rows[0].id; this.locations.update(old => [...old.filter(x => x.id !== rows[0].id), rows[0]]); this.cdr.markForCheck();
    }); }
  addComponent(): void { if (this.draft.components.length < 999) this.draft.components.push(blankComponent()); }
  removeComponent(index: number): void { this.draft.components.splice(index, 1); }
  hasUnsavedChanges(): boolean { return JSON.stringify(this.draft) !== this.baseline; }
  private showError(error: unknown): void { this.error.set(this.api.errorKey(error));
    this.errorTrace.set(apiErrorTraceId(error));
    this.errorDetails.set(disassemblyValidationDetails(error, (key, params) => this.translate.instant(key, params))); }
  save(): void {
    if (this.saving()) return;
    if (!validDisassemblyDraft(this.draft)) { this.error.set('inv.disassembly.invalid'); return; }
    const allLines = [this.draft.finished, ...this.draft.components];
    const issue = allLines.filter(line => line.tracking?.length || this.mode(line) === 'None')
      .map(line => trackingError(this.mode(line), line.quantity, line.tracking ?? [], line !== this.draft.finished)).find(Boolean);
    if (issue) { this.error.set(issue); return; }
    if (duplicateSerialAcrossLines(allLines)) { this.error.set('inv.tracking.duplicate'); return; }
    this.error.set(null); this.errorTrace.set(null); this.errorDetails.set([]); this.saving.set(true);
    const revision = this.contextRevision;
    const action = this.id ? this.api.update(this.id, this.version, this.draft) : this.api.create(this.draft);
    action.subscribe({ next: () => { this.saving.set(false); if (revision !== this.contextRevision) return;
      this.baseline = JSON.stringify(this.draft);
      void this.router.navigateByUrl('/inv/disassemblies'); },
      error: e => { this.saving.set(false); if (revision === this.contextRevision) this.showError(e); } });
  }
}
