import { ChangeDetectionStrategy, Component } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzCardModule } from 'ng-zorro-antd/card';
import { NzGridModule } from 'ng-zorro-antd/grid';
import { ChartDatum, ColumnChart, KpiCard, LineChart, PieChart } from '../../shared/charts';

const MONTHS = ['2026-05', '2026-06', '2026-07', '2026-08', '2026-09', '2026-10'];
const CURRENT = [820000, 932000, 901000, 934000, 1290000, 1330000];
const PREVIOUS = [760000, 810000, 870000, 905000, 1010000, 1120000];

/**
 * 首頁（暫時）：示範資料展示四種共用圖表，用於檢查亮 / 暗主題與三語切換。
 * 正式首頁於 WU-SYS-011、儀表板於 BI 模組取代。
 */
@Component({
  selector: 'erp-home-page',
  imports: [TranslatePipe, NzAlertModule, NzCardModule, NzGridModule, LineChart, ColumnChart, PieChart, KpiCard],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h1 class="erp-page-title" data-testid="home-title">{{ 'layout.home.welcome' | translate }}</h1>
    <nz-alert nzType="info" nzShowIcon [nzMessage]="'layout.home.sampleNotice' | translate" class="erp-home__notice" />

    <div nz-row [nzGutter]="[16, 16]">
      <div nz-col [nzXs]="24" [nzMd]="8">
        <erp-kpi-card
          data-testid="home-kpi-revenue"
          titleKey="layout.home.kpiRevenue"
          currency="TWD"
          [value]="1330000"
          [previous]="1290000"
          [trend]="current"
        />
      </div>
      <div nz-col [nzXs]="24" [nzMd]="8">
        <erp-kpi-card data-testid="home-kpi-orders" titleKey="layout.home.kpiOrders" [value]="186" [previous]="204" />
      </div>
      <div nz-col [nzXs]="24" [nzMd]="8">
        <erp-kpi-card
          data-testid="home-kpi-receivable"
          titleKey="layout.home.kpiReceivable"
          currency="TWD"
          [value]="2450000"
          [previous]="null"
        />
      </div>

      <div nz-col [nzXs]="24" [nzLg]="12">
        <nz-card [nzBordered]="false">
          <erp-line-chart
            data-testid="home-chart-trend"
            titleKey="layout.home.salesTrend"
            [data]="trend"
            xField="month"
            yField="amount"
            seriesField="series"
            [translateSeries]="true"
          />
        </nz-card>
      </div>
      <div nz-col [nzXs]="24" [nzLg]="12">
        <nz-card [nzBordered]="false">
          <erp-column-chart
            data-testid="home-chart-compare"
            titleKey="layout.home.periodCompare"
            [data]="trend"
            xField="month"
            yField="amount"
            seriesField="series"
            [translateSeries]="true"
          />
        </nz-card>
      </div>
      <div nz-col [nzXs]="24" [nzLg]="12">
        <nz-card [nzBordered]="false">
          <erp-pie-chart
            data-testid="home-chart-share"
            titleKey="layout.home.categoryShare"
            [data]="share"
            categoryField="category"
            valueField="amount"
          />
        </nz-card>
      </div>
    </div>
  `,
  styles: `
    .erp-page-title {
      margin: 0 0 var(--erp-space-2);
      font-size: 20px;
      font-weight: 600;
      color: var(--erp-color-text);
    }
    .erp-home__notice {
      margin-bottom: var(--erp-space-2);
    }
  `,
})
export class HomePage {
  protected readonly current = CURRENT;
  protected readonly trend: readonly ChartDatum[] = MONTHS.flatMap((month, i) => [
    { month, amount: CURRENT[i], series: 'layout.home.seriesCurrent' },
    { month, amount: PREVIOUS[i], series: 'layout.home.seriesPrevious' },
  ]);
  protected readonly share: readonly ChartDatum[] = [
    { category: 'A', amount: 420 },
    { category: 'B', amount: 310 },
    { category: 'C', amount: 220 },
    { category: 'D', amount: 160 },
    { category: 'E', amount: 120 },
    { category: 'F', amount: 80 },
    { category: 'G', amount: 40 },
    { category: 'H', amount: 25 },
    { category: 'I', amount: 10 },
  ];
}
