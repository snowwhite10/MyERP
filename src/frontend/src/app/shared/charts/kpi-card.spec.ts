import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { FakeChartRegistry, provideFakeCharts } from '../../../testing/fake-chart';
import { provideTestTranslations } from '../../../testing/i18n-fixtures';
import { provideNzIcons } from 'ng-zorro-antd/icon';
import { LanguageService } from '../../core/i18n/language.service';
import { APP_ICONS } from '../../core/icons';
import { currencyFractionDigits, formatKpiValue, KpiCard } from './kpi-card';

@Component({
  imports: [KpiCard],
  template: `
    <erp-kpi-card
      titleKey="layout.home.kpiRevenue"
      [value]="value()"
      [previous]="previous()"
      [currency]="currency()"
      [trend]="trend()"
    />
  `,
})
class HostComponent {
  readonly value = signal(1250);
  readonly previous = signal<number | null>(1000);
  readonly currency = signal<string | undefined>(undefined);
  readonly trend = signal<readonly number[]>([]);
}

describe('KpiCard', () => {
  let registry: FakeChartRegistry;

  function render(configure?: (host: HostComponent) => void) {
    const fixture = TestBed.createComponent(HostComponent);
    configure?.(fixture.componentInstance);
    fixture.detectChanges();
    TestBed.tick();
    return fixture;
  }

  function text(fixture: { nativeElement: HTMLElement }, testId: string): string {
    return fixture.nativeElement.querySelector(`[data-testid="${testId}"]`)?.textContent?.trim() ?? '';
  }

  beforeEach(() => {
    localStorage.clear();
    window.matchMedia = undefined as unknown as typeof window.matchMedia;
    registry = new FakeChartRegistry();
    TestBed.configureTestingModule({
      providers: [...provideTestTranslations(), provideFakeCharts(registry), provideNzIcons(APP_ICONS)],
    });
  });

  afterEach(() => localStorage.clear());

  it('shows the translated title, formatted value and an upward change', () => {
    const fixture = render();
    const element = fixture.nativeElement as HTMLElement;

    expect(element.textContent).toContain('本月營收');
    expect(text(fixture, 'kpi-value')).toBe('1,250');
    expect(text(fixture, 'kpi-change')).toContain('較上期');
    expect(text(fixture, 'kpi-change')).toContain('25%');
    expect(element.querySelector('.erp-kpi-card__delta--up')).not.toBeNull();
    expect(element.querySelector('nz-icon[nztype="arrow-up"]')).not.toBeNull();
  });

  it('shows a downward change', () => {
    const fixture = render((h) => h.value.set(900));

    expect(text(fixture, 'kpi-change')).toContain('10%');
    expect(fixture.nativeElement.querySelector('.erp-kpi-card__delta--down')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('nz-icon[nztype="arrow-down"]')).not.toBeNull();
  });

  it('shows a flat change without arrow', () => {
    const fixture = render((h) => h.value.set(1000));

    expect(text(fixture, 'kpi-change')).toContain('0%');
    expect(fixture.nativeElement.querySelector('.erp-kpi-card__delta--flat')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('nz-icon')).toBeNull();
  });

  it('shows "no previous data" when the previous value is missing', () => {
    const fixture = render((h) => h.previous.set(null));

    expect(text(fixture, 'kpi-change')).toBe('無上期資料');
  });

  it('formats currency with the decimals of the currency', () => {
    const fixture = render((h) => {
      h.currency.set('TWD');
      h.value.set(1330000.4);
    });

    expect(text(fixture, 'kpi-value')).toMatch(/1,330,000$/);
  });

  it('renders a sparkline only when there are at least two trend points', () => {
    render((h) => h.trend.set([1]));
    expect(registry.charts).toHaveLength(0);

    render((h) => h.trend.set([1, 3, 2]));
    expect(registry.charts).toHaveLength(1);
    expect(registry.last?.lastSpec?.['type']).toBe('area');
  });

  it('follows the current language', async () => {
    const fixture = render();

    await firstValueFrom(TestBed.inject(LanguageService).use('en'));
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Revenue this month');
    expect(text(fixture, 'kpi-change')).toContain('vs. previous');
  });
});

describe('KPI formatting', () => {
  it.each([
    ['TWD', 0],
    ['twd', 0],
    ['JPY', 0],
    ['USD', 2],
    ['HKD', 2],
    ['CNY', 2],
  ])('%s has %d fraction digits', (currency, digits) => {
    expect(currencyFractionDigits(currency)).toBe(digits);
  });

  it('formats plain numbers with up to 2 decimals', () => {
    expect(formatKpiValue(1234.567, 'en', undefined)).toBe('1,234.57');
  });

  it('formats USD with 2 decimals', () => {
    expect(formatKpiValue(1234.5, 'en', 'USD')).toBe('$1,234.50');
  });

  it('formats JPY without decimals', () => {
    expect(formatKpiValue(1234.5, 'en', 'JPY')).toMatch(/1,235$/);
  });
});
