import { ChangeDetectionStrategy, Component, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { CompanyContextService } from '../../core/company/company-context.service';
import { fetchAllPages, OPTION_PAGE_SIZE } from '../../shared/list-page/fetch-all-pages';
import { InventoryValuation, ValuationItemOption, ValuationService } from './valuation.service';

@Component({
  selector: 'erp-valuation-page',
  imports: [FormsModule, TranslatePipe, NzAlertModule, NzButtonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="valuation" data-testid="inv-valuation-page">
      <h1>{{ 'inv.valuation.title' | translate }}</h1>
      <nz-alert nzType="info" nzShowIcon [nzMessage]="'inv.valuation.provisional' | translate" />
      <div class="filters">
        <label>{{ 'inv.valuation.item' | translate }}
          <select [(ngModel)]="itemId" data-testid="inv-valuation-item">
            <option [ngValue]="null">{{ 'inv.valuation.chooseItem' | translate }}</option>
            @for (item of items(); track item.id) { <option [ngValue]="item.id">{{ item.code }} · {{ item.name }}</option> }
          </select>
        </label>
        <label>{{ 'inv.valuation.cutoff' | translate }}
          <input type="datetime-local" [(ngModel)]="cutoff" data-testid="inv-valuation-cutoff" />
        </label>
        <button nz-button nzType="primary" [disabled]="!itemId || loading()" (click)="search()" data-testid="inv-valuation-search">{{ 'inv.valuation.search' | translate }}</button>
      </div>
      @if (error()) { <nz-alert nzType="error" nzShowIcon [nzMessage]="error()! | translate" /> }
      @if (result(); as row) {
        <div class="result" data-testid="inv-valuation-result">
          <p>{{ 'inv.valuation.status' | translate }}: {{ (row.status === 'Unpriced' ? 'inv.valuation.unpricedStatus' : 'inv.valuation.valuedStatus') | translate }}</p>
          <p>{{ 'inv.valuation.onHand' | translate }}: {{ fixed(row.onHand, 6) }}</p>
          <p>{{ 'inv.valuation.value' | translate }}: {{ row.value === null ? '—' : fixed(row.value, 4) }}</p>
          <p>{{ 'inv.valuation.average' | translate }}: {{ row.averageUnitCost === null ? '—' : fixed(row.averageUnitCost, 6) }}</p>
          <p>{{ 'inv.valuation.movements' | translate }}: {{ row.movementCount }}</p>
          <p>{{ 'inv.valuation.asOf' | translate }}: {{ row.cutoffPostedAt || ('inv.valuation.current' | translate) }}</p>
        </div>
        @if (row.status === 'Unpriced') {
          <nz-alert nzType="warning" nzShowIcon [nzMessage]="'inv.valuation.unpriced' | translate: { quantity: fixed(row.unpricedQuantity, 6) }" data-testid="inv-valuation-unpriced" />
        }
      }
    </section>
  `,
  styles: `.valuation{display:grid;gap:16px;color:var(--erp-color-text)}h1{font-size:20px;margin:0}.filters{display:flex;gap:12px;align-items:end;flex-wrap:wrap}.filters label{display:grid;gap:4px}select,input{min-width:190px;padding:6px;border:1px solid var(--erp-color-border);background:var(--erp-color-bg);color:var(--erp-color-text)}.result{padding:16px;border:1px solid var(--erp-color-border);border-radius:6px}.result p{margin:4px 0}`,
})
export class ValuationPage {
  private readonly api = inject(ValuationService);
  private readonly company = inject(CompanyContextService);
  readonly items = signal<ValuationItemOption[]>([]);
  readonly result = signal<InventoryValuation | null>(null);
  readonly error = signal<string | null>(null);
  readonly loading = signal(false);
  itemId: number | null = null;
  cutoff = '';
  private generation = 0;
  constructor() { effect(() => { this.company.current(); untracked(() => {
    const generation = ++this.generation;
    this.itemId = null; this.cutoff = ''; this.result.set(null); this.error.set(null); this.loading.set(false); this.items.set([]);
    fetchAllPages(page => this.api.items(page, OPTION_PAGE_SIZE)).subscribe({
      next: items => { if (generation === this.generation) this.items.set(items); },
      error: error => { if (generation === this.generation) this.error.set(this.api.errorKey(error)); },
    });
  }); }); }
  fixed(value: number, digits: number): string { return value.toFixed(digits); }
  search(): void {
    if (!this.itemId || this.loading()) return;
    const generation = ++this.generation;
    this.loading.set(true); this.error.set(null); this.result.set(null);
    this.api.item(this.itemId, this.cutoff || undefined).subscribe({
      next: result => { if (generation !== this.generation) return; this.result.set(result); this.loading.set(false); },
      error: error => { if (generation !== this.generation) return; this.error.set(this.api.errorKey(error)); this.loading.set(false); },
    });
  }
}
