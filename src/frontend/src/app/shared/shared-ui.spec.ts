// WU-SYS-014 共用介面（REQ_F4118.31.1–6）：確認框、提示框、選擇視窗、列表頁／單據頁範本、日期快選、狀態標籤。
import { HttpErrorResponse } from '@angular/common/http';
import { ANIMATION_MODULE_TYPE, Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TranslateService } from '@ngx-translate/core';
import { provideNzIcons } from 'ng-zorro-antd/icon';
import { NzMessageService } from 'ng-zorro-antd/message';
import { NzTableQueryParams } from 'ng-zorro-antd/table';
import { firstValueFrom, isObservable, Observable, of, Subject, throwError } from 'rxjs';
import { provideTestTranslations } from '../../testing/i18n-fixtures';
import { APP_ICONS } from '../core/icons';
import { DateRangeFilter, isInvalidRange, QUICK_RANGES, quickRange } from './date-range/date-range';
import { ErpDialogService, TOAST_DURATION_MS } from './dialogs/dialog.service';
import { SelectDialogOptions, SelectDialogQuery } from './dialogs/select-dialog';
import { adjacentIds, DocumentPage } from './document-page/document-page';
import { HasUnsavedChanges, unsavedChangesGuard } from './document-page/unsaved-changes.guard';
import { ListPage } from './list-page/list-page';
import { nextTableState, PagedResult, sortOrderOf, tableSort } from './list-page/table-query';
import { DOCUMENT_STATUSES, STATUS_TONES, statusKey, StatusTag } from './status-tag/status-tag';

const query = (pageIndex: number, pageSize: number, sort: NzTableQueryParams['sort'] = []): NzTableQueryParams => ({
  pageIndex,
  pageSize,
  sort,
  filter: [],
});

/** overlay（對話框）在 fixture 之外，以 document 查詢。 */
const $ = (testId: string) => document.querySelector<HTMLElement>(`[data-testid="${testId}"]`);

async function settle(fixture?: ComponentFixture<unknown>): Promise<void> {
  fixture?.detectChanges();
  await new Promise((resolve) => setTimeout(resolve));
  fixture?.detectChanges();
  await fixture?.whenStable();
  fixture?.detectChanges();
}

describe('shared UI', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      // jsdom 不會發出 animationend：以 NoopAnimations 讓 nz-modal 立即關閉並發出結果
      providers: [...provideTestTranslations('en'), provideNzIcons(APP_ICONS), { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' }],
    });
  });

  afterEach(async () => {
    TestBed.inject(ErpDialogService).closeAll();
    // 等關閉動畫結束、對話框移出 DOM，避免下一個測試查到舊的對話框
    await vi.waitFor(() => expect(document.querySelector('[data-testid="erp-message-dialog"], [data-testid="erp-select-dialog"]')).toBeNull());
  });

  describe('table query helpers', () => {
    it('maps nz-table sorting to the API sort parameter with a whitelist and fallback', () => {
      expect(tableSort(query(1, 20, [{ key: 'name', value: 'descend' }]), ['code', 'name'], 'code')).toBe('-name');
      expect(tableSort(query(1, 20, [{ key: 'code', value: 'ascend' }]), ['code'], '-code')).toBe('code');
      expect(tableSort(query(1, 20, [{ key: 'secret', value: 'ascend' }]), ['code'], 'code')).toBe('code');
      expect(tableSort(query(1, 20, [{ key: 'code', value: null }]), ['code'], '-createdAt')).toBe('-createdAt');
      expect(sortOrderOf('name', 'name')).toBe('ascend');
      expect(sortOrderOf('-name', 'name')).toBe('descend');
      expect(sortOrderOf('code', 'name')).toBeNull();
    });

    it('returns the next table state only when paging or sorting changed', () => {
      const current = { page: 1, pageSize: 20, sort: 'code' };
      expect(nextTableState(query(1, 20), current, ['code'], 'code')).toBeNull();
      expect(nextTableState(query(2, 20), current, ['code'], 'code')).toEqual({ page: 2, pageSize: 20, sort: 'code' });
      expect(nextTableState(query(1, 50, [{ key: 'code', value: 'descend' }]), current, ['code'], 'code')).toEqual({
        page: 1,
        pageSize: 50,
        sort: '-code',
      });
    });
  });

  describe('date range quick select', () => {
    // 2026-10-14 是星期三
    const wednesday = new Date(2026, 9, 14);

    it('computes inclusive quick ranges in local dates', () => {
      expect(quickRange('today', wednesday)).toEqual({ from: '2026-10-14', to: '2026-10-14' });
      expect(quickRange('thisWeek', wednesday)).toEqual({ from: '2026-10-12', to: '2026-10-18' });
      expect(quickRange('thisMonth', wednesday)).toEqual({ from: '2026-10-01', to: '2026-10-31' });
      expect(quickRange('lastMonth', wednesday)).toEqual({ from: '2026-09-01', to: '2026-09-30' });
      expect(quickRange('thisQuarter', wednesday)).toEqual({ from: '2026-10-01', to: '2026-12-31' });
      expect(quickRange('thisYear', wednesday)).toEqual({ from: '2026-01-01', to: '2026-12-31' });
    });

    it('handles week, month and year boundaries', () => {
      // 星期日屬於前一個星期一開始的那週
      expect(quickRange('thisWeek', new Date(2026, 10, 1))).toEqual({ from: '2026-10-26', to: '2026-11-01' });
      // 週跨年
      expect(quickRange('thisWeek', new Date(2026, 11, 31))).toEqual({ from: '2026-12-28', to: '2027-01-03' });
      // 一月的上月是去年十二月；閏年二月
      expect(quickRange('lastMonth', new Date(2026, 0, 15))).toEqual({ from: '2025-12-01', to: '2025-12-31' });
      expect(quickRange('lastMonth', new Date(2028, 2, 31))).toEqual({ from: '2028-02-01', to: '2028-02-29' });
      expect(quickRange('thisQuarter', new Date(2026, 1, 28))).toEqual({ from: '2026-01-01', to: '2026-03-31' });
      expect(isInvalidRange({ from: '2026-10-02', to: '2026-10-01' })).toBe(true);
      expect(isInvalidRange({ from: '2026-10-01', to: '2026-10-01' })).toBe(false);
      expect(isInvalidRange({ from: '', to: '2026-10-01' })).toBe(false);
    });

    @Component({
      imports: [DateRangeFilter],
      template: `<erp-date-range testId="t-date" [today]="today" [(from)]="from" [(to)]="to" (quickSelected)="picked.push($event)" />`,
    })
    class Host {
      readonly today = new Date(2026, 9, 14);
      from = '';
      to = '';
      picked: { from: string; to: string }[] = [];
    }

    it('fills both dates from a quick button, emits the range and flags an inverted range', async () => {
      const fixture = TestBed.createComponent(Host);
      await settle(fixture);
      const el = fixture.nativeElement as HTMLElement;
      expect(QUICK_RANGES.every((kind) => el.querySelector(`[data-testid="t-date-${kind}"]`))).toBe(true);
      expect(el.querySelector('[data-testid="t-date-thisMonth"]')?.textContent).toContain('This month');

      (el.querySelector('[data-testid="t-date-lastMonth"]') as HTMLButtonElement).click();
      await settle(fixture);
      expect(fixture.componentInstance.from).toBe('2026-09-01');
      expect(fixture.componentInstance.to).toBe('2026-09-30');
      expect(fixture.componentInstance.picked).toEqual([{ from: '2026-09-01', to: '2026-09-30' }]);
      expect(el.querySelector('[data-testid="t-date-invalid"]')).toBeNull();

      const from = el.querySelector('[data-testid="t-date-from"]') as HTMLInputElement;
      from.value = '2026-10-05';
      from.dispatchEvent(new Event('input'));
      await settle(fixture);
      expect(fixture.componentInstance.from).toBe('2026-10-05');
      expect(el.querySelector('[data-testid="t-date-invalid"]')?.textContent).toContain('cannot be later');
    });
  });

  describe('status tag', () => {
    @Component({
      imports: [StatusTag],
      template: `<erp-status-tag [status]="status()" [labelKey]="labelKey()" testId="t-status" />`,
    })
    class Host {
      readonly status = signal<(typeof DOCUMENT_STATUSES)[number]>('Draft');
      readonly labelKey = signal<string | null>(null);
    }

    it('maps every status to a tone and a trilingual label', async () => {
      expect(DOCUMENT_STATUSES.map((s) => STATUS_TONES[s])).toEqual(['draft', 'pending', 'success', 'success', 'error', 'muted', 'muted']);
      expect(statusKey('Submitted')).toBe('common.status.submitted');
      const translate = TestBed.inject(TranslateService);
      for (const status of DOCUMENT_STATUSES) {
        expect(translate.instant(statusKey(status))).not.toBe(statusKey(status));
      }

      const fixture = TestBed.createComponent(Host);
      await settle(fixture);
      const tag = () => fixture.nativeElement.querySelector('[data-testid="t-status"]') as HTMLElement;
      expect(tag().textContent).toContain('Draft');
      expect(tag().classList).toContain('erp-status-tag--draft');

      fixture.componentInstance.status.set('Voided');
      await settle(fixture);
      expect(tag().classList).toContain('erp-status-tag--muted');
      expect(tag().classList).toContain('erp-status-tag');
      expect(tag().dataset['status']).toBe('Voided');

      fixture.componentInstance.labelKey.set('common.action.delete');
      await settle(fixture);
      expect(tag().textContent).toContain('Delete');
    });
  });

  describe('list page template', () => {
    @Component({
      imports: [ListPage],
      template: `
        <erp-list-page titleKey="common.app.name" testId="t-list" [selectedCount]="selected()" [errorKey]="error()"
          [resettable]="resettable()" (querySubmit)="events.push('search')" (filterReset)="events.push('reset')"
          (retryLoad)="events.push('retry')" (selectionClear)="events.push('clear')">
          <button erpListActions data-testid="t-list-add">add</button>
          <input erpListFilters data-testid="t-list-keyword" type="search" />
          <button erpListBatch data-testid="t-list-batch-delete">batch</button>
          <table data-testid="t-list-table"></table>
        </erp-list-page>
      `,
    })
    class Host {
      readonly selected = signal(0);
      readonly error = signal<string | null>(null);
      readonly resettable = signal(true);
      events: string[] = [];
    }

    it('projects actions, filters and content; emits search and reset', async () => {
      const fixture = TestBed.createComponent(Host);
      await settle(fixture);
      const el = fixture.nativeElement as HTMLElement;
      const q = (id: string) => el.querySelector<HTMLElement>(`[data-testid="${id}"]`);
      expect(q('t-list-title')?.textContent).toContain('MyERP');
      expect(q('t-list-add')?.closest('.erp-list-page__actions')).not.toBeNull();
      expect(q('t-list-keyword')?.closest('.erp-list-page__filters')).not.toBeNull();
      expect(q('t-list-table')).not.toBeNull();
      expect(q('t-list-batch')).toBeNull();
      expect(q('t-list-batch-delete')).toBeNull();
      expect(q('t-list-load-error')).toBeNull();

      q('t-list-search-button')!.click();
      q('t-list-reset')!.click();
      // 投影內容的原生 search 事件冒泡到範本，不得觸發查詢
      q('t-list-keyword')!.dispatchEvent(new Event('search', { bubbles: true }));
      expect(fixture.componentInstance.events).toEqual(['search', 'reset']);

      fixture.componentInstance.resettable.set(false);
      await settle(fixture);
      expect(q('t-list-reset')).toBeNull();
    });

    it('shows the batch bar with the selected count and a retryable load error', async () => {
      const fixture = TestBed.createComponent(Host);
      fixture.componentInstance.selected.set(3);
      fixture.componentInstance.error.set('errors.http.network');
      await settle(fixture);
      const el = fixture.nativeElement as HTMLElement;
      const q = (id: string) => el.querySelector<HTMLElement>(`[data-testid="${id}"]`);
      expect(q('t-list-selected-count')?.textContent).toContain('3 selected');
      expect(q('t-list-batch-delete')?.closest('[data-testid="t-list-batch"]')).not.toBeNull();
      expect(q('t-list-load-error')?.textContent).toContain('Unable to reach the server');
      q('t-list-retry')!.click();
      q('t-list-clear-selection')!.click();
      expect(fixture.componentInstance.events).toEqual(['retry', 'clear']);
    });
  });

  describe('document page template', () => {
    @Component({
      imports: [DocumentPage],
      template: `
        <erp-document-page titleKey="common.app.name" testId="t-doc" [docNo]="docNo()" [status]="status()"
          [navigable]="true" [hasPrevious]="nav().previous !== null" [hasNext]="nav().next !== null" [dirty]="dirty()"
          [errorKey]="error()" [conflict]="conflict()" (back)="events.push('back')" (previous)="events.push('previous')"
          (next)="events.push('next')" (reload)="events.push('reload')" (errorClose)="error.set(null)">
          <button erpDocumentActions data-testid="t-doc-save">save</button>
          <form data-testid="t-doc-form"></form>
          <div erpDocumentTabs data-testid="t-doc-tabs"></div>
        </erp-document-page>
      `,
    })
    class Host {
      readonly docNo = signal<string | null>(null);
      readonly status = signal<'Draft' | 'Approved' | null>(null);
      readonly nav = signal(adjacentIds([1, 2, 3], 1));
      readonly dirty = signal(false);
      readonly error = signal<string | null>(null);
      readonly conflict = signal(false);
      events: string[] = [];
    }

    it('computes previous / next ids from the list order', () => {
      expect(adjacentIds([10, 20, 30], 20)).toEqual({ previous: 10, next: 30 });
      expect(adjacentIds([10, 20, 30], 10)).toEqual({ previous: null, next: 20 });
      expect(adjacentIds([10, 20, 30], 30)).toEqual({ previous: 20, next: null });
      expect(adjacentIds([10], 99)).toEqual({ previous: null, next: null });
    });

    it('renders the header, status, navigation, projected slots, conflict and error', async () => {
      const fixture = TestBed.createComponent(Host);
      await settle(fixture);
      const el = fixture.nativeElement as HTMLElement;
      const q = (id: string) => el.querySelector<HTMLElement>(`[data-testid="${id}"]`);
      expect(q('t-doc-title')?.textContent).toContain('(New)');
      expect(q('t-doc-status')).toBeNull();
      expect((q('t-doc-previous') as HTMLButtonElement).disabled).toBe(true);
      expect((q('t-doc-next') as HTMLButtonElement).disabled).toBe(false);
      expect(q('t-doc-previous')?.getAttribute('aria-label')).toBe('Previous');
      expect(q('t-doc-save')?.closest('.erp-document__actions')).not.toBeNull();
      expect(q('t-doc-form')?.closest('.erp-document__body')).not.toBeNull();
      expect(q('t-doc-tabs')?.closest('.erp-document__tabs')).not.toBeNull();

      fixture.componentInstance.docNo.set('SO202610140001');
      fixture.componentInstance.status.set('Approved');
      fixture.componentInstance.dirty.set(true);
      fixture.componentInstance.conflict.set(true);
      fixture.componentInstance.error.set('errors.COMMON.CONCURRENCY.CONFLICT');
      fixture.componentInstance.nav.set(adjacentIds([1, 2, 3], 3));
      await settle(fixture);
      expect(q('t-doc-doc-no')?.textContent).toBe('SO202610140001');
      expect(q('t-doc-status')?.textContent).toContain('Approved');
      expect(q('t-doc-dirty')?.textContent).toContain('Unsaved');
      expect(q('t-doc-conflict')?.textContent).toContain('changed by someone else');
      expect(q('t-doc-error')).not.toBeNull();
      expect((q('t-doc-next') as HTMLButtonElement).disabled).toBe(true);

      q('t-doc-back')!.click();
      q('t-doc-previous')!.click();
      q('t-doc-reload')!.click();
      expect(fixture.componentInstance.events).toEqual(['back', 'previous', 'reload']);
    });
  });

  describe('ErpDialogService', () => {
    it('confirm emits true on OK and uses the danger style', async () => {
      const dialogs = TestBed.inject(ErpDialogService);
      const result = firstValueFrom(
        dialogs.confirm({ titleKey: 'sys.role.deleteConfirm', contentKey: 'common.confirm.irreversible', params: { code: 'X' }, danger: true }),
      );
      await settle();
      expect($('erp-message-dialog')?.dataset['kind']).toBe('confirm');
      expect($('erp-message-content')?.textContent).toContain('cannot be undone');
      expect($('erp-message-ok')?.textContent).toContain('OK');
      expect($('erp-message-ok')?.classList).toContain('ant-btn-dangerous');
      $('erp-message-ok')!.click();
      expect(await result).toBe(true);
    });

    it('confirm emits false on cancel and on closeAll (Esc / close)', async () => {
      const dialogs = TestBed.inject(ErpDialogService);
      const cancelled = firstValueFrom(dialogs.confirm({ titleKey: 'common.document.leaveTitle', okKey: 'common.document.leave' }));
      await settle();
      expect($('erp-message-ok')?.textContent).toContain('Leave');
      expect($('erp-message-ok')?.classList).not.toContain('ant-btn-dangerous');
      expect($('erp-message-content')).toBeNull();
      $('erp-message-cancel')!.click();
      expect(await cancelled).toBe(false);

      const dismissed = firstValueFrom(dialogs.confirm({ titleKey: 'common.document.leaveTitle' }));
      await settle();
      dialogs.closeAll();
      expect(await dismissed).toBe(false);
    });

    it('alert shows a prompt box with a single OK button and the kind icon', async () => {
      const dialogs = TestBed.inject(ErpDialogService);
      const closed = firstValueFrom(dialogs.alert('success', { titleKey: 'common.status.posted' }));
      await settle();
      expect($('erp-message-dialog')?.dataset['kind']).toBe('success');
      expect($('erp-message-title')?.textContent).toContain('Posted');
      expect($('erp-message-cancel')).toBeNull();
      $('erp-message-ok')!.click();
      await expect(closed).resolves.toBeUndefined();
    });

    it('success shows a translated toast for 3 seconds', () => {
      const message = TestBed.inject(NzMessageService);
      const spy = vi.spyOn(message, 'success');
      TestBed.inject(ErpDialogService).success('common.list.selectedCount', { count: 2 });
      expect(spy).toHaveBeenCalledWith('2 selected', { nzDuration: TOAST_DURATION_MS });
    });
  });

  describe('select dialog', () => {
    interface Customer {
      id: number;
      code: string;
      name: string;
    }
    const customers: Customer[] = Array.from({ length: 12 }, (_, i) => ({ id: i + 1, code: `C${i + 1}`, name: `Customer ${i + 1}` }));
    const result = (items: Customer[], totalCount = items.length): PagedResult<Customer> => ({ items, page: 1, pageSize: 10, totalCount });

    function options(load: (q: SelectDialogQuery) => Observable<PagedResult<Customer>>, extra: Partial<SelectDialogOptions<Customer>> = {}) {
      return {
        titleKey: 'common.select.select',
        columns: [
          { key: 'code', labelKey: 'common.app.name', value: (c: Customer) => c.code },
          { key: 'name', labelKey: 'common.app.title', value: (c: Customer) => c.name },
        ],
        load,
        rowKey: (c: Customer) => c.id,
        ...extra,
      } satisfies SelectDialogOptions<Customer>;
    }

    it('single select: searches with a trimmed keyword, picks by row click and confirms', async () => {
      const queries: SelectDialogQuery[] = [];
      const load = (q: SelectDialogQuery) => {
        queries.push(q);
        return of(result(customers.slice(0, 10), 12));
      };
      const picked = firstValueFrom(TestBed.inject(ErpDialogService).select(options(load)));
      await settle();
      expect(queries).toEqual([{ page: 1, pageSize: 10, keyword: '' }]);
      expect($('erp-select-title')?.textContent).toContain('Select');
      expect($('erp-select-cell-1-name')?.textContent).toBe('Customer 1');
      expect(($('erp-select-confirm') as HTMLButtonElement).disabled).toBe(true);

      const keyword = $('erp-select-keyword') as HTMLInputElement;
      keyword.value = '  cus ';
      keyword.dispatchEvent(new Event('input'));
      $('erp-select-search')!.click();
      await settle();
      expect(queries.at(-1)).toEqual({ page: 1, pageSize: 10, keyword: 'cus' });

      $('erp-select-row-2')!.click();
      $('erp-select-row-3')!.click();
      await settle();
      expect($('erp-select-row-3')?.getAttribute('aria-selected')).toBe('true');
      expect($('erp-select-row-2')?.getAttribute('aria-selected')).toBe('false');
      expect($('erp-select-count')?.textContent).toContain('1 selected');
      $('erp-select-confirm')!.click();
      expect(await picked).toEqual([customers[2]]);
    });

    it('single select: double click confirms immediately; cancel returns null', async () => {
      const dialogs = TestBed.inject(ErpDialogService);
      const picked = firstValueFrom(dialogs.select(options(() => of(result(customers.slice(0, 3))))));
      await settle();
      $('erp-select-row-2')!.dispatchEvent(new MouseEvent('dblclick', { bubbles: true }));
      expect(await picked).toEqual([customers[1]]);

      const cancelled = firstValueFrom(dialogs.select(options(() => of(result(customers.slice(0, 3))))));
      await settle();
      $('erp-select-cancel')!.click();
      expect(await cancelled).toBeNull();
    });

    it('multi select keeps the preselected rows and the selection across pages', async () => {
      const load = (q: SelectDialogQuery) => of(result(customers.slice((q.page - 1) * 10, q.page * 10), 12));
      const dialogs = TestBed.inject(ErpDialogService);
      const picked = firstValueFrom(dialogs.select(options(load, { multiple: true, selected: [customers[0]] })));
      await settle();
      expect($('erp-select-count')?.textContent).toContain('1 selected');
      $('erp-select-row-2')!.click();
      $('erp-select-row-1')!.click();
      // 按兩下在多選時不會直接確定
      $('erp-select-row-4')!.dispatchEvent(new MouseEvent('dblclick', { bubbles: true }));
      await settle();
      expect($('erp-select-count')?.textContent).toContain('1 selected');

      const nextPage = document.querySelector<HTMLButtonElement>('[data-testid="erp-select-dialog"] .ant-pagination-next button');
      nextPage!.click();
      await settle();
      expect($('erp-select-row-11')).not.toBeNull();
      $('erp-select-row-12')!.click();
      await settle();
      expect($('erp-select-count')?.textContent).toContain('2 selected');
      $('erp-select-confirm')!.click();
      expect(await picked).toEqual([customers[1], customers[11]]);
    });

    it('shows a translated load error and keeps only the latest response', async () => {
      const pending = new Subject<PagedResult<Customer>>();
      let calls = 0;
      const load = (): Observable<PagedResult<Customer>> => {
        calls += 1;
        if (calls === 1) return pending;
        return throwError(() => new HttpErrorResponse({ status: 0 }));
      };
      const dialogs = TestBed.inject(ErpDialogService);
      const closed = firstValueFrom(dialogs.select(options(load)));
      await settle();
      $('erp-select-search')!.click();
      await settle();
      // 第一次查詢已被取消：較晚到的舊結果不會覆蓋
      expect(pending.observed).toBe(false);
      expect($('erp-select-error')?.textContent).toContain('Unable to reach the server');
      expect($('erp-select-row-1')).toBeNull();
      dialogs.closeAll();
      expect(await closed).toBeNull();
    });
  });

  describe('unsavedChangesGuard', () => {
    const run = (component: HasUnsavedChanges) =>
      TestBed.runInInjectionContext(() => unsavedChangesGuard(component, null!, null!, null!)) as Observable<boolean>;

    it('allows leaving without changes and asks before discarding changes', async () => {
      expect(await firstValueFrom(run({ hasUnsavedChanges: () => false }))).toBe(true);

      const leave = run({ hasUnsavedChanges: () => true });
      expect(isObservable(leave)).toBe(true);
      const left = firstValueFrom(leave);
      await settle();
      expect($('erp-message-title')?.textContent).toContain('Leave without saving?');
      expect($('erp-message-cancel')?.textContent).toContain('Keep editing');
      $('erp-message-ok')!.click();
      expect(await left).toBe(true);

      const stay = firstValueFrom(run({ hasUnsavedChanges: () => true }));
      await settle();
      $('erp-message-cancel')!.click();
      expect(await stay).toBe(false);
    });
  });
});
