// 介面 8.66（REQ_F4118.27.6）：稽核查詢頁、API 服務、欄位差異展開、i18n 標籤與路由。
import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting, TestRequest } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { TranslateService } from '@ngx-translate/core';
import { seedSession } from '../../../../testing/auth-fixtures';
import { provideTestTranslations } from '../../../../testing/i18n-fixtures';
import { CompanyContextService } from '../../../core/company/company-context.service';
import { MAIN_MENU } from '../../../layout/menu';
import { SYS_ROUTES } from '../sys.routes';
import { AuditLogList, isoDate } from './audit-log-list';
import {
  AUDIT_LOG_API,
  auditActionKey,
  auditCategoryKey,
  auditChangeRows,
  auditErrorKey,
  AuditLogItem,
  AuditLogService,
} from './audit-log.service';

const page = (items: object[], totalCount = items.length) => ({ items, page: 1, pageSize: 20, totalCount });

const log = (overrides: Partial<AuditLogItem> = {}): AuditLogItem => ({
  id: 11,
  occurredAt: '2026-10-11T08:30:15',
  category: 'DATA',
  action: 'UPDATE',
  tableName: 'sys_user',
  recordId: '7',
  userId: 1,
  username: 'admin',
  companyId: 1,
  changes: { is_active: { old: true, new: false }, password_hash: { old: '***', new: '***' }, note: { old: null, new: 'x' } },
  remoteIp: '10.0.0.5',
  traceId: '00-abc-01',
  ...overrides,
});

describe('SYS audit log', () => {
  let controller: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    seedSession();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), ...provideTestTranslations('en')],
    });
    controller = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    controller.verify();
    localStorage.clear();
    vi.useRealTimers();
  });

  function expectList(): TestRequest {
    return controller.expectOne((request) => request.method === 'GET' && request.url === AUDIT_LOG_API);
  }

  describe('AuditLogService and helpers', () => {
    it('sends paging, trimmed filters, userId and sort; omits blank filters', () => {
      const api = TestBed.inject(AuditLogService);
      api
        .list({
          page: 2,
          pageSize: 50,
          from: '2026-10-01',
          to: '2026-10-11',
          category: 'DATA',
          action: 'UPDATE',
          tableName: ' sys_user ',
          recordId: ' 7 ',
          userId: 3,
          sort: 'occurredAt',
        })
        .subscribe();
      const full = expectList();
      expect(full.request.params.toString()).toBe(
        'page=2&pageSize=50&from=2026-10-01&to=2026-10-11&category=DATA&action=UPDATE&tableName=sys_user&recordId=7&userId=3&sort=occurredAt',
      );
      full.flush(page([]));

      api.list({ page: 1, pageSize: 20, from: '', tableName: '  ', userId: null }).subscribe();
      const blank = expectList();
      expect(blank.request.params.toString()).toBe('page=1&pageSize=20');
      blank.flush(page([]));
    });

    it('turns changes into diff rows (keeping *** and null) and security details into detail rows', () => {
      expect(auditChangeRows(log().changes)).toEqual([
        { field: 'is_active', kind: 'diff', old: 'true', new: 'false' },
        { field: 'password_hash', kind: 'diff', old: '***', new: '***' },
        { field: 'note', kind: 'diff', old: null, new: 'x' },
      ]);
      expect(auditChangeRows({ reason: 'INVALID_CREDENTIALS', attempts: 3, roles: [1, 2], extra: { a: 1 } })).toEqual([
        { field: 'reason', kind: 'detail', value: 'INVALID_CREDENTIALS' },
        { field: 'attempts', kind: 'detail', value: '3' },
        { field: 'roles', kind: 'detail', value: '[1,2]' },
        { field: 'extra', kind: 'detail', value: '{"a":1}' },
      ]);
      // 只有 new（新增）或只有 old（刪除）仍是差異；空物件不是差異
      expect(auditChangeRows({ code: { new: 'A' }, name: { old: 'B' }, empty: {}, gone: null })).toEqual([
        { field: 'code', kind: 'diff', old: null, new: 'A' },
        { field: 'name', kind: 'diff', old: 'B', new: null },
        { field: 'empty', kind: 'detail', value: '{}' },
        { field: 'gone', kind: 'detail', value: null },
      ]);
      expect(auditChangeRows(null)).toEqual([]);
    });

    it('maps known actions and categories to i18n keys and leaves unknown codes unmapped', () => {
      expect(auditActionKey('LOGIN_FAILED')).toBe('sys.audit.actions.LOGIN_FAILED');
      expect(auditActionKey('SOMETHING')).toBeNull();
      expect(auditCategoryKey('SECURITY')).toBe('sys.audit.categories.SECURITY');
      expect(auditCategoryKey('OTHER')).toBeNull();
    });

    it('maps errors to i18n keys', () => {
      const error = (status: number, errorCode?: string) => new HttpErrorResponse({ status, error: errorCode ? { errorCode } : null });
      expect(auditErrorKey(error(403, 'COMMON.PERMISSION.DENIED'))).toBe('errors.COMMON.PERMISSION.DENIED');
      expect(auditErrorKey(error(400, 'COMMON.REQUEST.VALIDATION_FAILED'))).toBe('errors.COMMON.REQUEST.VALIDATION_FAILED');
      expect(auditErrorKey(error(0))).toBe('errors.http.network');
      expect(auditErrorKey(error(500))).toBe('errors.http.unknown');
    });

    it('formats local dates as yyyy-MM-dd', () => {
      expect(isoDate(new Date(2026, 0, 5))).toBe('2026-01-05');
    });
  });

  describe('AuditLogList page', () => {
    let fixture: ComponentFixture<AuditLogList>;

    async function open(inputs: Record<string, string> = {}): Promise<AuditLogList> {
      TestBed.inject(CompanyContextService).setCompanies([{ id: 1, code: 'TW01', name: 'Taipei' }, { id: 2, code: 'HK01', name: 'Hong Kong' }]);
      fixture = TestBed.createComponent(AuditLogList);
      for (const [name, value] of Object.entries(inputs)) fixture.componentRef.setInput(name, value);
      fixture.detectChanges();
      await fixture.whenStable();
      return fixture.componentInstance;
    }

    function el(testId: string): HTMLElement | null {
      return fixture.nativeElement.querySelector(`[data-testid="${testId}"]`);
    }

    async function render(): Promise<void> {
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();
    }

    it('loads newest first and shows labels, user, IP and record; expands field differences', async () => {
      await open();
      const first = expectList();
      expect(first.request.params.toString()).toBe('page=1&pageSize=20&sort=-occurredAt');
      first.flush(page([
        log(),
        log({ id: 12, category: 'SECURITY', action: 'LOGIN_FAILED', tableName: null, recordId: null, userId: null, username: null, remoteIp: null, changes: { reason: 'INVALID_CREDENTIALS' } }),
        log({ id: 13, category: 'SECURITY', action: 'CUSTOM_EVENT', tableName: null, recordId: null, username: null, userId: 9, changes: null }),
      ]));
      await render();
      const text = (testId: string) => el(testId)?.textContent ?? '';
      expect(text('sys-audit-row-11')).toContain('2026-10-11 08:30:15');
      expect(text('sys-audit-row-11')).toContain('Data change');
      expect(text('sys-audit-row-11')).toContain('Update');
      expect(text('sys-audit-row-11')).toContain('sys_user #7');
      expect(text('sys-audit-row-11')).toContain('admin');
      expect(text('sys-audit-row-11')).toContain('10.0.0.5');
      expect(text('sys-audit-row-12')).toContain('Security event');
      expect(text('sys-audit-row-12')).toContain('Sign-in failed');
      expect(text('sys-audit-row-12')).toContain('(none)');
      // 未知操作顯示原代碼；沒有帳號時顯示用戶 ID
      expect(text('sys-audit-row-13')).toContain('CUSTOM_EVENT');
      expect(text('sys-audit-row-13')).toContain('#9');
      expect(fixture.componentInstance.rows()[2].details).toEqual([]);

      fixture.componentInstance.toggle(11, true);
      fixture.componentInstance.toggle(12, true);
      await render();
      expect(text('sys-audit-change-11-is_active')).toContain('true');
      expect(text('sys-audit-change-11-is_active')).toContain('false');
      expect(text('sys-audit-change-11-password_hash')).toContain('***');
      expect(text('sys-audit-change-11-note')).toContain('(empty)');
      expect(text('sys-audit-changes-11')).toContain('Before');
      expect(fixture.nativeElement.textContent).toContain('00-abc-01');
      expect(text('sys-audit-change-12-reason')).toContain('INVALID_CREDENTIALS');

      fixture.componentInstance.toggle(11, false);
      expect([...fixture.componentInstance.expanded()]).toEqual([12]);
      // 第一個渲染 nz-table 的測試在整體並行執行時可能冷啟動較慢
    }, 15_000);

    it('searches with filters from the first page, sorts ascending and resets', async () => {
      const list = await open();
      expectList().flush(page([], 0));
      list.from.set('2026-10-01');
      list.to.set('2026-10-11');
      list.category.set('SECURITY');
      list.action.set('LOGIN');
      list.tableName.set('sys_role');
      list.recordId.set('5');
      list.page.set(3);
      list.search();
      const search = expectList();
      expect(search.request.params.toString()).toBe(
        'page=1&pageSize=20&from=2026-10-01&to=2026-10-11&category=SECURITY&action=LOGIN&tableName=sys_role&recordId=5&sort=-occurredAt',
      );
      search.flush(page([]));

      list.onQueryParams({ pageIndex: 2, pageSize: 20, sort: [{ key: 'occurredAt', value: 'ascend' }], filter: [] });
      const sorted = expectList();
      expect(sorted.request.params.get('sort')).toBe('occurredAt');
      expect(sorted.request.params.get('page')).toBe('2');
      sorted.flush(page([]));
      expect(list.sortOrder()).toBe('ascend');

      // 相同參數不重複查詢
      list.onQueryParams({ pageIndex: 2, pageSize: 20, sort: [{ key: 'occurredAt', value: 'ascend' }], filter: [] });
      controller.expectNone(AUDIT_LOG_API);

      list.reset();
      const reset = expectList();
      expect(reset.request.params.toString()).toBe('page=1&pageSize=20&sort=occurredAt');
      reset.flush(page([]));
    });

    it('rejects an end date before the start date without calling the API', async () => {
      const list = await open();
      expectList().flush(page([]));
      list.from.set('2026-10-11');
      list.to.set('2026-10-01');
      list.search();
      controller.expectNone(AUDIT_LOG_API);
      await render();
      expect(el('sys-audit-filter-error')?.textContent).toContain('The end date cannot be earlier than the start date.');

      list.to.set('2026-10-11');
      list.search();
      expectList().flush(page([]));
      expect(list.filterError()).toBeNull();
    });

    it('fills today, last 7 and last 30 days from the local date', async () => {
      vi.useFakeTimers({ toFake: ['Date'] });
      vi.setSystemTime(new Date(2026, 9, 11, 9, 0, 0));
      const list = await open();
      expectList().flush(page([]));

      list.quickRange(1);
      expect(expectList().request.params.toString()).toContain('from=2026-10-11&to=2026-10-11');
      list.quickRange(7);
      expect(expectList().request.params.toString()).toContain('from=2026-10-05&to=2026-10-11');
      list.quickRange(30);
      expect(expectList().request.params.toString()).toContain('from=2026-09-12&to=2026-10-11');
    });

    it('opens a record history from query parameters, filters by user and navigates to another history', async () => {
      const list = await open({ tableName: 'sys_role', recordId: '5', userId: '3' });
      const history = expectList();
      expect(history.request.params.toString()).toBe('page=1&pageSize=20&tableName=sys_role&recordId=5&userId=3&sort=-occurredAt');
      history.flush(page([log({ tableName: 'sys_role', recordId: '5' })]));
      await render();
      expect(list.historyMode()).toBe(true);
      expect(el('sys-audit-history')?.textContent).toContain('Change history of sys_role #5');
      expect(el('sys-audit-user-filter')?.textContent).toContain('User #3');

      list.clearUser();
      expect(expectList().request.params.has('userId')).toBe(false);

      const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
      list.openHistory(log({ tableName: 'sys_user', recordId: '7' }));
      expect(navigate).toHaveBeenCalledWith([], { queryParams: { tableName: 'sys_user', recordId: '7' } });
      // 沒有記錄 ID 的安全事件不能開歷史
      list.openHistory(log({ tableName: null, recordId: null }));
      expect(navigate).toHaveBeenCalledTimes(1);

      // 重設：清除網址上的歷史參數，不直接查詢（由輸入改變觸發）
      list.reset();
      expect(navigate).toHaveBeenLastCalledWith([], { queryParams: {} });
      controller.expectNone(AUDIT_LOG_API);

      // 不合法的 userId 忽略
      fixture.componentRef.setInput('userId', 'abc');
      await render();
      expect(expectList().request.params.has('userId')).toBe(false);
      expect(list.userId()).toBeNull();
    });

    it('reloads when the company changes and shows load errors', async () => {
      await open();
      expectList().flush(page([log()]));
      TestBed.inject(CompanyContextService).select(2);
      await render();
      expectList().flush({ errorCode: 'COMMON.PERMISSION.DENIED' }, { status: 403, statusText: 'Forbidden' });
      await render();
      expect(fixture.componentInstance.rows()).toEqual([]);
      expect(el('sys-audit-load-error')?.textContent).toContain('You do not have permission');
    });

    it('switches translated labels', async () => {
      await open();
      expectList().flush(page([log({ category: 'SECURITY', action: 'PERMISSION_CHANGE' })]));
      await render();
      expect(fixture.nativeElement.textContent).toContain('Audit log');
      TestBed.inject(TranslateService).use('zh-Hant');
      await render();
      expect(fixture.nativeElement.textContent).toContain('稽核日誌');
      expect(fixture.nativeElement.textContent).toContain('權限變更');
      TestBed.inject(TranslateService).use('zh-Hans');
      await render();
      expect(fixture.nativeElement.textContent).toContain('审计日志');
    });
  });

  it('routes /sys/audit-logs behind SYS.AUDIT.VIEW, lazy-loads the page and lists it in the menu', async () => {
    const route = SYS_ROUTES.find((r) => r.path === 'audit-logs')!;
    expect(route.canActivate).toHaveLength(1);
    expect(await route.loadComponent!()).toBe(AuditLogList);
    const sys = MAIN_MENU.find((item) => item.testId === 'layout-menu-sys')!;
    expect(sys.children!.find((item) => item.route === '/sys/audit-logs')?.permission).toBe('SYS.AUDIT.VIEW');
  });
});
