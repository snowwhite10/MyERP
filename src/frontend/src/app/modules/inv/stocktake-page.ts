import { ChangeDetectionStrategy, Component, effect, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { CompanyContextService } from '../../core/company/company-context.service';
import { HasPermissionDirective } from '../../core/auth/has-permission.directive';
import { StocktakeService, StocktakeSummary } from './stocktake.service';

/** 010b1 的可返回草稿入口；完整篩選、報告及匯出在 010b2。 */
@Component({ selector: 'erp-stocktake-page', imports: [RouterLink, TranslatePipe, NzAlertModule, NzButtonModule, HasPermissionDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<section class="stocktake-page"><header><h1>{{ 'inv.stocktake.title' | translate }}</h1>
    <a nz-button nzType="primary" routerLink="new" *erpHasPermission="'INV.STOCKTAKE.CREATE'">{{ 'inv.stocktake.new' | translate }}</a></header>
    @if (error()) { <nz-alert nzType="error" [nzMessage]="error()! | translate" nzShowIcon /> }
    @for (row of rows(); track row.id) { <div class="row"><span>{{ row.documentNo }}</span><span>{{ row.businessDate }}</span>
      <span>{{ 'inv.stocktake.status.' + statusKey(row.status) | translate }}</span><span>{{ row.countedLineCount }}/{{ row.lineCount }}</span>
      @if (row.status === 'Draft' || row.status === 'Rejected') { <a [routerLink]="[row.id, 'count']" *erpHasPermission="'INV.STOCKTAKE.EDIT'">{{ 'inv.stocktake.count' | translate }}</a> }
    </div> } @empty { <p>{{ 'inv.stocktake.empty' | translate }}</p> }
    <div class="pages"><button nz-button [disabled]="page() === 1" (click)="go(page()-1)">‹</button><span>{{ page() }}</span>
      <button nz-button [disabled]="page()*20 >= total()" (click)="go(page()+1)">›</button></div></section>`,
  styles: `.stocktake-page{display:grid;gap:16px;color:var(--erp-color-text)}header,.row,.pages{display:flex;gap:16px;align-items:center}header{justify-content:space-between}h1{font-size:20px;margin:0}.row{border-bottom:1px solid var(--erp-color-border);padding:8px;flex-wrap:wrap}.pages{justify-content:center}` })
export class StocktakePage {
  private readonly api = inject(StocktakeService); private readonly company = inject(CompanyContextService);
  readonly rows = signal<StocktakeSummary[]>([]); readonly page = signal(1); readonly total = signal(0); readonly error = signal<string | null>(null);
  readonly statusKey = (status: string) => status[0].toLowerCase() + status.slice(1);
  constructor() { effect(() => { this.company.current(); this.go(1); }); }
  go(page: number): void { this.page.set(page); this.api.list({ page, pageSize: 20 }).subscribe({
    next: result => { this.rows.set(result.items); this.total.set(result.totalCount); }, error: e => this.error.set(this.api.errorKey(e)) }); }
}
