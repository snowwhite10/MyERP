import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzInputModule } from 'ng-zorro-antd/input';
import { CompanyContextService } from '../../core/company/company-context.service';
import { InventoryOption, InventorySelectionOptionsService } from './selection-options.service';
import { newRequestKey, OpeningDraft, OpeningLineDraft, OpeningLineError, openingLineErrorKey, OpeningPreview, OpeningStockService,
  validOpeningLine } from './opening-stock.service';
import { TrackingInput } from './tracking-input';
import { duplicateSerialAcrossLines, trackingError } from './tracking';

const blankLine = (): OpeningLineDraft => ({ itemId: null, warehouseId: null, locationId: null, quantity: '', unitCost: '0' });

/** 期初庫存批次輸入：先預覽逐行錯誤，無錯且內容未再修改才可提交；同一批次重試沿用同一 requestKey（ADR-059）。 */
@Component({
  selector: 'erp-opening-stock-form',
  imports: [FormsModule, RouterLink, TranslatePipe, NzAlertModule, NzButtonModule, NzInputModule, TrackingInput],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="opening-form"><header><h1>{{ 'inv.opening.new' | translate }}</h1>
      <a routerLink="/inv/opening-stocks">{{ 'inv.opening.back' | translate }}</a></header>
      <p class="hint">{{ 'inv.opening.hint' | translate }}</p>
      @if (error()) { <nz-alert nzType="error" nzShowIcon [nzMessage]="error()! | translate" data-testid="inv-opening-error" /> }
      @if (preview(); as p) {
        <nz-alert [nzType]="p.errorCount ? 'warning' : 'success'" nzShowIcon data-testid="inv-opening-preview"
          [nzMessage]="(p.errorCount ? 'inv.opening.previewErrors' : 'inv.opening.previewOk') | translate: { count: p.errorCount, quantity: p.totalQuantity, amount: p.totalAmount }" />
      }
      <div class="head"><label>{{ 'inv.opening.date' | translate }} <input nz-input type="date" [(ngModel)]="draft.openingDate" (ngModelChange)="changed()" data-testid="inv-opening-date" /></label>
        <label>{{ 'inv.opening.remark' | translate }} <input nz-input [(ngModel)]="draft.remark" (ngModelChange)="changed()" maxlength="500" data-testid="inv-opening-remark" /></label></div>
      <h2>{{ 'inv.opening.lines' | translate }}</h2>
      <div class="line-head"><span>{{ 'inv.opening.item' | translate }}</span><span>{{ 'inv.opening.warehouse' | translate }}</span>
        <span>{{ 'inv.opening.location' | translate }}</span><span>{{ 'inv.opening.quantity' | translate }}</span><span>{{ 'inv.opening.unitCost' | translate }}</span><span></span></div>
      @for (line of draft.lines; track $index; let index = $index) {
        <div class="line" [class.has-error]="lineErrors(index).length" [attr.data-testid]="'inv-opening-line-' + index">
          <button nz-button type="button" [attr.title]="label(items(), line.itemId)" [attr.aria-label]="'inv.opening.item' | translate" (click)="chooseItem(line)">{{ label(items(), line.itemId) }}</button>
          <button nz-button type="button" [attr.title]="label(warehouses(), line.warehouseId)" [attr.aria-label]="'inv.opening.warehouse' | translate" (click)="chooseWarehouse(line)">{{ label(warehouses(), line.warehouseId) }}</button>
          <button nz-button type="button" [disabled]="!line.warehouseId" [attr.title]="line.warehouseId ? label(locations(), line.locationId) : ('inv.selection.chooseWarehouseFirst' | translate)" [attr.aria-label]="'inv.opening.location' | translate" (click)="chooseLocation(line)">{{ label(locations(), line.locationId) }}</button>
          <input nz-input type="text" inputmode="decimal" [(ngModel)]="line.quantity" (ngModelChange)="changed()" [attr.aria-label]="'inv.opening.quantity' | translate" />
          <input nz-input type="text" inputmode="decimal" [(ngModel)]="line.unitCost" (ngModelChange)="changed()" [attr.aria-label]="'inv.opening.unitCost' | translate" />
          <button nz-button type="button" [attr.aria-label]="'inv.opening.removeLine' | translate" (click)="removeLine(index)">×</button>
          @if (mode(line) !== 'None') { <erp-tracking-input class="tracking" [mode]="mode(line)" [quantity]="line.quantity"
            [rows]="line.tracking ?? []" (rowsChange)="line.tracking = $event; changed()" /> }
          @if (lineErrors(index).length) { <ul class="line-errors" [attr.data-testid]="'inv-opening-line-errors-' + index">
            @for (e of lineErrors(index); track $index) { <li>{{ 'inv.opening.lineNo' | translate: { no: index + 1 } }} · {{ errorKey(e.code) | translate }}</li> }</ul> }
        </div>
      }
      <div class="actions"><button nz-button type="button" (click)="addLine()">{{ 'inv.opening.addLine' | translate }}</button>
        <button nz-button type="button" data-testid="inv-opening-preview-button" [disabled]="busy()" (click)="runPreview()">{{ 'inv.opening.preview' | translate }}</button>
        <button nz-button nzType="primary" type="button" data-testid="inv-opening-submit" [disabled]="busy() || !canSubmit()" (click)="submit()">{{ 'inv.opening.submit' | translate }}</button></div>
    </section>
  `,
  styles: `.opening-form{display:grid;gap:16px;color:var(--erp-color-text)}header,.head,.actions{display:flex;gap:16px;align-items:center;flex-wrap:wrap}header{justify-content:space-between}h1{margin:0;font-size:20px}h2{font-size:16px}.hint{margin:0;color:var(--erp-color-text-secondary)}.head label{display:grid;gap:4px}.line,.line-head{display:grid;grid-template-columns:2fr 2fr 2fr 1fr 1fr 40px;gap:8px;align-items:center}.line button,.line input{min-width:0;width:100%;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.tracking{grid-column:1/-1}.line.has-error{outline:1px solid var(--erp-color-error,#ff4d4f);outline-offset:4px}.line-errors{grid-column:1/-1;margin:0;padding-left:20px;color:var(--erp-color-error,#ff4d4f)}@media(max-width:900px){.line,.line-head{grid-template-columns:repeat(2,minmax(0,1fr))}}`,
})
export class OpeningStockForm {
  private readonly router = inject(Router);
  private readonly api = inject(OpeningStockService); private readonly selection = inject(InventorySelectionOptionsService);
  private readonly company = inject(CompanyContextService); private readonly translate = inject(TranslateService);
  readonly error = signal<string | null>(null); readonly busy = signal(false);
  readonly preview = signal<OpeningPreview | null>(null);
  readonly items = signal<InventoryOption[]>([]); readonly warehouses = signal<InventoryOption[]>([]);
  readonly locations = signal<InventoryOption[]>([]);
  private contextRevision = 0;
  /** 最近一次成功預覽時的請求內容；內容改變後必須重新預覽。 */
  private previewed: string | null = null;
  draft: OpeningDraft = this.blankDraft();
  /** 切換公司時物料／倉庫選項不同，整批重新開始。 */
  constructor() { effect(() => { this.company.current(); untracked(() => {
    ++this.contextRevision; this.items.set([]); this.warehouses.set([]); this.locations.set([]);
    this.draft = this.blankDraft(); this.changed(); this.error.set(null); }); }); }
  private blankDraft(): OpeningDraft { return { requestKey: newRequestKey(), openingDate: '', remark: '', lines: [blankLine()] }; }
  label(options: InventoryOption[], id: number | null): string { const row = options.find(x => x.id === id);
    return row ? `${row.code} · ${row.name}${row.isActive === false ? ` (${this.translate.instant('inv.selection.inactive')})` : ''}` : '—'; }
  chooseItem(line: OpeningLineDraft): void { const revision = this.contextRevision; this.selection.choose('item').subscribe(rows => {
    if (!rows?.[0] || revision !== this.contextRevision) return;
    if (line.itemId !== rows[0].id) { line.tracking = []; this.changed(); }
    line.itemId = rows[0].id; this.items.update(old => [...old.filter(x => x.id !== rows[0].id), rows[0]]);
  }); }
  chooseWarehouse(line: OpeningLineDraft): void { const revision = this.contextRevision; this.selection.choose('warehouse').subscribe(rows => {
    if (!rows?.[0] || revision !== this.contextRevision) return;
    if (line.warehouseId !== rows[0].id) { line.locationId = null; this.changed(); }
    line.warehouseId = rows[0].id; this.warehouses.update(old => [...old.filter(x => x.id !== rows[0].id), rows[0]]);
  }); }
  chooseLocation(line: OpeningLineDraft): void { if (!line.warehouseId) return; const warehouseId = line.warehouseId;
    const revision = this.contextRevision; this.selection.choose('location', warehouseId).subscribe(rows => {
      if (!rows?.[0] || revision !== this.contextRevision || line.warehouseId !== warehouseId) return;
      if (line.locationId !== rows[0].id) this.changed(); line.locationId = rows[0].id;
      this.locations.update(old => [...old.filter(x => x.id !== rows[0].id), rows[0]]);
    }); }
  addLine(): void { this.draft.lines.push(blankLine()); this.changed(); }
  removeLine(index: number): void { this.draft.lines.splice(index, 1); this.changed(); }
  mode(line: OpeningLineDraft): 'None' | 'Lot' | 'Serial' { return this.items().find(x => x.id === line.itemId)?.trackingMode ?? 'None'; }
  /** 任何修改都使上一次預覽失效（逐行錯誤以行序對應，行增刪後不可再沿用）。 */
  changed(): void { this.previewed = null; this.preview.set(null); }
  /** 依行號索引預覽錯誤；最多 1000 行，避免範本每行線性搜尋。 */
  private readonly errorsByLine = computed(() => new Map((this.preview()?.lines ?? []).map(x => [x.lineNo, x.errors])));
  lineErrors(index: number): OpeningLineError[] { return this.errorsByLine().get(index + 1) ?? []; }
  errorKey(code: string): string { return openingLineErrorKey(code); }
  canSubmit(): boolean { const p = this.preview(); return !!p && p.errorCount === 0 && this.previewed === this.signature(); }
  private signature(): string { return JSON.stringify(this.draft); }
  private valid(): boolean {
    if (!this.draft.openingDate || !this.draft.lines.length || !this.draft.lines.every(validOpeningLine)) { this.error.set('inv.opening.invalid'); return false; }
    const trackingIssue = this.draft.lines.map(line => trackingError(this.mode(line), line.quantity, line.tracking ?? [], true)).find(Boolean);
    if (trackingIssue) { this.error.set(trackingIssue); return false; }
    if (duplicateSerialAcrossLines(this.draft.lines)) { this.error.set('inv.tracking.duplicate'); return false; }
    return true;
  }
  runPreview(): void {
    if (this.busy() || !this.valid()) return;
    const signature = this.signature(); this.error.set(null); this.busy.set(true);
    this.api.preview(this.draft).subscribe({
      next: result => { this.busy.set(false); this.preview.set(result); this.previewed = signature; },
      error: e => { this.busy.set(false); this.error.set(this.api.errorKey(e)); },
    });
  }
  submit(): void {
    if (this.busy() || !this.canSubmit()) return;
    this.error.set(null); this.busy.set(true);
    this.api.submit(this.draft).subscribe({
      next: result => { this.busy.set(false); void this.router.navigate(['/inv/opening-stocks'], { queryParams: { posted: result.id, replayed: result.replayed ? 1 : null } }); },
      error: e => {
        this.busy.set(false); const key = this.api.errorKey(e); this.error.set(key);
        if (key === 'errors.INV.OPENING_STOCK.REQUEST_CONFLICT') {
          // 同一鍵已以其他內容提交：換新批次鍵並要求重新預覽，預覽會揭示已入帳的重複期初。
          this.draft.requestKey = newRequestKey(); this.changed();
        } else if (key === 'errors.INV.OPENING_STOCK.HAS_ERRORS') {
          this.changed(); this.runPreviewKeepingError(key);
        }
        // 其他失敗（網絡、逾時）保留同一 requestKey 與預覽，可直接重試。
      },
    });
  }
  private runPreviewKeepingError(key: string): void { this.runPreview(); this.error.set(key); }
}
