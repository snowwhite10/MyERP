import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideNzIcons } from 'ng-zorro-antd/icon';
import { FakeChartRegistry, provideFakeCharts } from '../../testing/fake-chart';
import { provideTestTranslations } from '../../testing/i18n-fixtures';
import { APP_ICONS } from '../core/icons';
import { ErrorPage } from './errors/error-page';
import { HomePage } from './home/home.page';

describe('pages', () => {
  let registry: FakeChartRegistry;

  beforeEach(() => {
    localStorage.clear();
    window.matchMedia = undefined as unknown as typeof window.matchMedia;
    registry = new FakeChartRegistry();
    TestBed.configureTestingModule({
      providers: [
        ...provideTestTranslations(),
        provideRouter([]),
        provideNzIcons(APP_ICONS),
        provideFakeCharts(registry),
      ],
    });
  });

  afterEach(() => localStorage.clear());

  describe('HomePage', () => {
    it('shows the welcome title, sample notice, KPI cards and charts', () => {
      const fixture = TestBed.createComponent(HomePage);
      fixture.detectChanges();
      TestBed.tick();
      const element = fixture.nativeElement as HTMLElement;

      expect(element.querySelector('[data-testid="home-title"]')?.textContent).toContain('歡迎使用 MyERP');
      expect(element.textContent).toContain('示範資料');
      expect(element.querySelectorAll('erp-kpi-card')).toHaveLength(3);
      expect(element.querySelector('[data-testid="home-chart-trend"]')).not.toBeNull();
      expect(element.querySelector('[data-testid="home-chart-compare"]')).not.toBeNull();
      expect(element.querySelector('[data-testid="home-chart-share"]')).not.toBeNull();
      // 3 張圖表 + 營收 KPI 的迷你趨勢
      expect(registry.charts).toHaveLength(4);
    });

    it('merges the 9 sample categories into 7 slices', () => {
      const fixture = TestBed.createComponent(HomePage);
      fixture.detectChanges();
      TestBed.tick();

      const pie = registry.charts.find((c) => c.lastSpec?.['coordinate']);
      expect(pie?.lastSpec?.['data']).toHaveLength(7);
    });
  });

  describe('ErrorPage', () => {
    it('shows 404 by default', () => {
      const fixture = TestBed.createComponent(ErrorPage);
      fixture.detectChanges();
      const element = fixture.nativeElement as HTMLElement;

      expect(element.querySelector('[data-testid="error-page-404"]')).not.toBeNull();
      expect(element.textContent).toContain('找不到頁面');
      expect(element.querySelector('[data-testid="error-page-home"]')?.getAttribute('href')).toBe('/');
    });

    it('shows 403 when the route says so', () => {
      const fixture = TestBed.createComponent(ErrorPage);
      fixture.componentRef.setInput('status', 403);
      fixture.detectChanges();

      expect(fixture.nativeElement.querySelector('[data-testid="error-page-403"]')).not.toBeNull();
      expect(fixture.nativeElement.textContent).toContain('沒有權限');
    });
  });
});
