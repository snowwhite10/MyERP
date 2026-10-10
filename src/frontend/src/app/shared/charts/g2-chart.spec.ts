import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { FakeChartRegistry, provideFakeCharts } from '../../../testing/fake-chart';
import { provideTestTranslations } from '../../../testing/i18n-fixtures';
import { createG2Chart } from './chart-factory';
import { ChartDatum, ChartSpec } from './chart-specs';
import { G2Chart } from './g2-chart';

@Component({
  imports: [G2Chart],
  template: `
    @if (visible()) {
      <erp-g2-chart [spec]="spec()" [height]="200" [showEmpty]="showEmpty()" (elementClick)="clicked.push($event)" />
    }
  `,
})
class HostComponent {
  readonly spec = signal<ChartSpec | null>({ type: 'line', data: [{ x: 1, y: 2 }] });
  readonly showEmpty = signal(true);
  readonly visible = signal(true);
  readonly clicked: ChartDatum[] = [];
}

describe('G2Chart', () => {
  let registry: FakeChartRegistry;

  function render() {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
    TestBed.tick();
    return fixture;
  }

  beforeEach(() => {
    registry = new FakeChartRegistry();
    TestBed.configureTestingModule({
      providers: [...provideTestTranslations(), provideFakeCharts(registry)],
    });
  });

  it('creates a chart in the container with the given spec and height', () => {
    const fixture = render();
    const container = fixture.nativeElement.querySelector('.erp-g2-chart') as HTMLElement;

    expect(registry.charts).toHaveLength(1);
    expect(registry.last?.container).toBe(container);
    expect(container.style.height).toBe('200px');
    expect(registry.last?.lastSpec).toEqual({ type: 'line', data: [{ x: 1, y: 2 }] });
    expect(registry.last?.renderCount).toBe(1);
  });

  it('updates the same chart instance when the spec changes', () => {
    const fixture = render();

    fixture.componentInstance.spec.set({ type: 'line', data: [{ x: 2, y: 3 }] });
    fixture.detectChanges();
    TestBed.tick();

    expect(registry.charts).toHaveLength(1);
    expect(registry.last?.specs).toHaveLength(2);
    expect(registry.last?.renderCount).toBe(2);
  });

  it('shows the empty state and destroys the chart when data becomes empty', () => {
    const fixture = render();
    const chart = registry.last!;

    fixture.componentInstance.spec.set({ type: 'line', data: [] });
    fixture.detectChanges();
    TestBed.tick();

    expect(chart.destroyed).toBe(true);
    expect(fixture.nativeElement.querySelector('nz-empty')).not.toBeNull();
    expect(fixture.nativeElement.textContent).toContain('暫無圖表資料');
  });

  it('shows the empty state for a null spec', () => {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.componentInstance.spec.set(null);
    fixture.detectChanges();
    TestBed.tick();

    expect(registry.charts).toHaveLength(0);
    expect(fixture.nativeElement.querySelector('nz-empty')).not.toBeNull();
  });

  it('can hide the empty state (sparklines)', () => {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.componentInstance.spec.set({ data: [] });
    fixture.componentInstance.showEmpty.set(false);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('nz-empty')).toBeNull();
  });

  it('recreates the chart when data returns after being empty', () => {
    const fixture = render();
    fixture.componentInstance.spec.set({ data: [] });
    fixture.detectChanges();
    TestBed.tick();

    fixture.componentInstance.spec.set({ data: [{ x: 1 }] });
    fixture.detectChanges();
    TestBed.tick();

    expect(registry.charts).toHaveLength(2);
    expect(registry.charts[0].destroyed).toBe(true);
    expect(registry.charts[1].destroyed).toBe(false);
  });

  it('emits the clicked datum for drill-down', () => {
    const fixture = render();
    const handler = registry.last!.handlers.get('element:click')!;

    handler({ data: { data: { x: 1, y: 2 } } });
    handler({});

    expect(fixture.componentInstance.clicked).toEqual([{ x: 1, y: 2 }]);
  });

  it('destroys the chart with the component', () => {
    const fixture = render();
    const chart = registry.last!;

    fixture.componentInstance.visible.set(false);
    fixture.detectChanges();

    expect(chart.destroyed).toBe(true);
  });
});

describe('createG2Chart', () => {
  it('wraps a G2 chart instance', () => {
    const container = document.createElement('div');

    const handle = createG2Chart(container);

    expect(typeof handle.options).toBe('function');
    expect(typeof handle.render).toBe('function');
    expect(typeof handle.on).toBe('function');
    handle.options({ type: 'line', data: [] });
    handle.on('element:click', () => undefined);
    handle.destroy();
  });
});
