// 介面 8.63（REQ_F4118.23.5–8）：用戶維護頁、API 服務、錯誤代碼與路由。
import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting, TestRequest } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TranslateService } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { seedSession } from '../../../../testing/auth-fixtures';
import { provideTestTranslations } from '../../../../testing/i18n-fixtures';
import { AUTHORIZATION_API, PermissionService } from '../../../core/auth/permission.service';
import { CompanyContextService } from '../../../core/company/company-context.service';
import { SYS_ROUTES } from '../sys.routes';
import { UserList } from './user-list';
import { USER_API, userErrorKey, UserService, UserSummary } from './user.service';

const page = (items: object[], totalCount = items.length) => ({ items, page: 1, pageSize: 20, totalCount });

const user = (overrides: Partial<UserSummary> = {}): UserSummary => ({
  id: 7,
  username: 'alice',
  isActive: true,
  isLocked: false,
  passwordChangedAt: '2026-10-01T09:15:00+08:00',
  isPasswordExpired: false,
  version: 3,
  ...overrides,
});

const STRONG = 'Str0ng!Passw0rd';

describe('SYS users', () => {
  let controller: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    seedSession();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), ...provideTestTranslations('en')],
    });
    controller = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    controller.verify();
    localStorage.clear();
  });

  async function grant(codes: string[]): Promise<void> {
    const done = firstValueFrom(TestBed.inject(PermissionService).load());
    controller.expectOne(AUTHORIZATION_API).flush({
      companies: [{ id: 1, code: 'TW01', name: 'Taipei', isDefault: true }, { id: 2, code: 'HK01', name: 'Hong Kong', isDefault: false }],
      companyId: 1,
      isFullAccess: false,
      permissions: codes.map((code) => ({ code, dataScope: 'Company' })),
    });
    await done;
  }

  function expectList(): TestRequest {
    return controller.expectOne((request) => request.method === 'GET' && request.url === USER_API);
  }

  describe('UserService', () => {
    it('sends paging, trimmed keyword and sort; posts create, toggle, reset and unlock bodies', () => {
      const api = TestBed.inject(UserService);
      api.list({ page: 2, pageSize: 10, keyword: '  Ali ', sort: '-username' }).subscribe();
      const list = expectList();
      expect(list.request.params.toString()).toBe('page=2&pageSize=10&keyword=Ali&sort=-username');
      list.flush(page([]));

      api.list({ page: 1, pageSize: 20, keyword: '   ' }).subscribe();
      const blank = expectList();
      expect(blank.request.params.toString()).toBe('page=1&pageSize=20');
      blank.flush(page([]));

      api.create('bob', STRONG).subscribe();
      const post = controller.expectOne((r) => r.method === 'POST' && r.url === USER_API);
      expect(post.request.body).toEqual({ username: 'bob', password: STRONG });
      post.flush(user({ username: 'bob' }));

      api.setActive(7, false, 3).subscribe();
      const put = controller.expectOne(`${USER_API}/7`);
      expect(put.request.method).toBe('PUT');
      expect(put.request.body).toEqual({ isActive: false, version: 3 });
      put.flush(user({ isActive: false, version: 4 }));

      api.resetPassword(7, STRONG).subscribe();
      const reset = controller.expectOne(`${USER_API}/7/reset-password`);
      expect(reset.request.method).toBe('POST');
      expect(reset.request.body).toEqual({ password: STRONG });
      reset.flush(null, { status: 204, statusText: 'No Content' });

      api.unlock(7).subscribe();
      const unlock = controller.expectOne(`${USER_API}/7/unlock`);
      expect(unlock.request.method).toBe('POST');
      unlock.flush(null, { status: 204, statusText: 'No Content' });
    });

    it('maps user error codes, password validation, network and other failures to i18n keys', () => {
      const error = (status: number, errorCode?: string) => new HttpErrorResponse({ status, error: errorCode ? { errorCode } : null });
      expect(userErrorKey(error(409, 'SYS.USER.DUPLICATE'))).toBe('errors.SYS.USER.DUPLICATE');
      expect(userErrorKey(error(422, 'SYS.USER.SELF_DEACTIVATION'))).toBe('errors.SYS.USER.SELF_DEACTIVATION');
      expect(userErrorKey(error(404, 'SYS.USER.NOT_FOUND'))).toBe('errors.SYS.USER.NOT_FOUND');
      expect(userErrorKey(error(409, 'COMMON.CONCURRENCY.CONFLICT'))).toBe('errors.COMMON.CONCURRENCY.CONFLICT');
      expect(userErrorKey(error(403, 'COMMON.PERMISSION.DENIED'))).toBe('errors.COMMON.PERMISSION.DENIED');
      expect(userErrorKey(error(400, 'COMMON.REQUEST.VALIDATION_FAILED'))).toBe('errors.COMMON.REQUEST.VALIDATION_FAILED');
      expect(userErrorKey(error(400, 'COMMON.REQUEST.VALIDATION_FAILED'), true)).toBe('sys.user.passwordRejected');
      expect(userErrorKey(error(0))).toBe('errors.http.network');
      expect(userErrorKey(error(500, 'SOMETHING.ELSE'))).toBe('errors.http.unknown');
      expect(userErrorKey(new Error('boom'))).toBe('errors.http.unknown');
    });
  });

  describe('UserList page', () => {
    let fixture: ComponentFixture<UserList>;

    async function open(): Promise<UserList> {
      TestBed.inject(CompanyContextService).setCompanies([{ id: 1, code: 'TW01', name: 'Taipei' }, { id: 2, code: 'HK01', name: 'Hong Kong' }]);
      fixture = TestBed.createComponent(UserList);
      fixture.detectChanges();
      await fixture.whenStable();
      return fixture.componentInstance;
    }

    function shown(testId: string): boolean {
      return fixture.nativeElement.querySelector(`[data-testid="${testId}"]`) !== null;
    }

    async function render(): Promise<void> {
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();
    }

    it('loads users sorted by username with status, lock and expiry tags; buttons follow permissions', async () => {
      await open();
      const first = expectList();
      expect(first.request.params.get('sort')).toBe('username');
      expect(first.request.params.get('page')).toBe('1');
      first.flush(page([
        user(),
        user({ id: 8, username: 'bob', isActive: false, isLocked: true, isPasswordExpired: true, version: 1 }),
      ]));
      await render();
      expect(shown('sys-user-row-alice')).toBe(true);
      expect(fixture.nativeElement.textContent).toContain('2026-10-01 09:15');
      expect(shown('sys-user-locked-bob')).toBe(true);
      expect(shown('sys-user-expired-bob')).toBe(true);
      expect(shown('sys-user-locked-alice')).toBe(false);
      expect(fixture.nativeElement.textContent).toContain('Inactive');
      expect(shown('sys-user-create')).toBe(false);
      expect(shown('sys-user-toggle-alice')).toBe(false);
      expect(shown('sys-user-reset-alice')).toBe(false);

      // EDIT：可停用／解鎖（只有已鎖定的列有解鎖），但沒有重設密碼
      await grant(['SYS.USER.VIEW', 'SYS.USER.EDIT']);
      await render();
      expect(shown('sys-user-toggle-alice')).toBe(true);
      expect(shown('sys-user-unlock-bob')).toBe(true);
      expect(shown('sys-user-unlock-alice')).toBe(false);
      expect(shown('sys-user-reset-alice')).toBe(false);
      expect(shown('sys-user-create')).toBe(false);
      expect(fixture.nativeElement.querySelector('[data-testid="sys-user-toggle-bob"]').textContent).toContain('Activate');
      // 第一個渲染 nz-table 的測試在整體並行執行時曾因冷啟動超過預設 5 秒
    }, 15_000);

    it('searches from the first page, sorts descending and reloads when the company changes', async () => {
      const list = await open();
      expectList().flush(page([], 0));
      list.keyword.set(' AL ');
      list.page.set(3);
      list.search();
      const search = expectList();
      expect(search.request.params.get('keyword')).toBe('AL');
      expect(search.request.params.get('page')).toBe('1');
      search.flush(page([]));

      list.onQueryParams({ pageIndex: 1, pageSize: 20, sort: [{ key: 'username', value: 'descend' }], filter: [] });
      const sorted = expectList();
      expect(sorted.request.params.get('sort')).toBe('-username');
      sorted.flush(page([]));
      expect(list.sortOrder()).toBe('descend');

      // 相同參數不重複查詢
      list.onQueryParams({ pageIndex: 1, pageSize: 20, sort: [{ key: 'username', value: 'descend' }], filter: [] });
      controller.expectNone(USER_API);

      TestBed.inject(CompanyContextService).select(2);
      await render();
      expectList().flush(page([]));
    });

    it('shows a load error when the list request fails', async () => {
      await open();
      expectList().error(new ProgressEvent('error'));
      await render();
      expect(fixture.componentInstance.loadError()).toBe('errors.http.network');
      expect(shown('sys-user-load-error')).toBe(true);
    });

    it('validates the create form, creates a trimmed username, and clears passwords after an API error', async () => {
      await grant(['SYS.USER.VIEW', 'SYS.USER.CREATE']);
      const list = await open();
      expectList().flush(page([user()]));

      list.openCreate();
      list.form.setValue({ username: 'a b', password: STRONG, confirm: 'different' });
      list.submit();
      expect(list.form.controls.username.invalid).toBe(true);
      expect(list.form.hasError('mismatch')).toBe(true);
      controller.expectNone((r) => r.method === 'POST');

      list.form.setValue({ username: ' Alice ', password: STRONG, confirm: STRONG });
      list.submit();
      const duplicate = controller.expectOne((r) => r.method === 'POST' && r.url === USER_API);
      expect(duplicate.request.body).toEqual({ username: 'Alice', password: STRONG });
      duplicate.flush({ errorCode: 'SYS.USER.DUPLICATE' }, { status: 409, statusText: 'Conflict' });
      expect(list.saveError()).toBe('errors.SYS.USER.DUPLICATE');
      expect(list.dialog()).toEqual({ mode: 'create' });
      expect(list.form.getRawValue()).toEqual({ username: ' Alice ', password: '', confirm: '' });

      list.form.patchValue({ username: 'carol', password: 'weak', confirm: 'weak' });
      list.submit();
      controller.expectOne((r) => r.method === 'POST').flush({ errorCode: 'COMMON.REQUEST.VALIDATION_FAILED' }, { status: 400, statusText: 'Bad Request' });
      expect(list.saveError()).toBe('sys.user.passwordRejected');

      list.form.patchValue({ password: STRONG, confirm: STRONG });
      list.submit();
      controller.expectOne((r) => r.method === 'POST').flush(user({ id: 9, username: 'carol' }), { status: 201, statusText: 'Created' });
      expect(list.dialog()).toBeNull();
      expect(list.notice()).toBe('sys.user.created');
      expect(list.noticeParams()).toEqual({ username: 'carol' });
      expectList().flush(page([user(), user({ id: 9, username: 'carol' })]));
      await render();
      expect(fixture.nativeElement.querySelector('[data-testid="sys-user-notice"]').textContent).toContain('User carol was created.');
    });

    it('resets a password without a username field and ignores a second submit while saving', async () => {
      await grant(['SYS.USER.VIEW', 'SYS.USER.RESET_PASSWORD']);
      const list = await open();
      expectList().flush(page([user()]));
      await render();

      list.openReset(list.rows()[0]);
      expect(list.form.controls.username.disabled).toBe(true);
      expect(list.dialogTitle()).toBe('sys.user.resetTitle');
      expect(list.dialogTitleParams()).toEqual({ username: 'alice' });
      list.form.patchValue({ password: STRONG, confirm: STRONG });
      list.submit();
      list.submit();
      const reset = controller.expectOne(`${USER_API}/7/reset-password`);
      expect(reset.request.body).toEqual({ password: STRONG });
      reset.flush(null, { status: 204, statusText: 'No Content' });
      expect(list.notice()).toBe('sys.user.passwordReset');
      expect(list.dialog()).toBeNull();
      expectList().flush(page([user()]));

      // 重新開新增對話框時帳號欄位恢復
      list.openCreate();
      expect(list.form.controls.username.enabled).toBe(true);
      expect(list.form.getRawValue()).toEqual({ username: '', password: '', confirm: '' });
      list.closeDialog();
      expect(list.dialog()).toBeNull();
    });

    it('deactivates with the read version, unlocks, and shows row action errors', async () => {
      await grant(['SYS.USER.VIEW', 'SYS.USER.EDIT']);
      const list = await open();
      expectList().flush(page([user(), user({ id: 8, username: 'bob', isLocked: true, version: 5 })]));

      const [alice, bob] = list.rows();
      list.toggleActive(alice);
      // 進行中再按其他列不送出
      list.unlock(bob);
      const put = controller.expectOne(`${USER_API}/7`);
      expect(put.request.body).toEqual({ isActive: false, version: 3 });
      expect(list.busyId()).toBe(7);
      put.flush({ errorCode: 'SYS.USER.SELF_DEACTIVATION' }, { status: 422, statusText: 'Unprocessable Entity' });
      expect(list.actionError()).toBe('errors.SYS.USER.SELF_DEACTIVATION');
      expect(list.busyId()).toBeNull();
      await render();
      expect(shown('sys-user-action-error')).toBe(true);

      list.unlock(bob);
      expect(list.actionError()).toBeNull();
      controller.expectOne(`${USER_API}/8/unlock`).flush(null, { status: 204, statusText: 'No Content' });
      expect(list.notice()).toBe('sys.user.unlocked');
      expectList().flush(page([user(), user({ id: 8, username: 'bob', version: 5 })]));

      list.toggleActive(user({ isActive: false, version: 4 }));
      const activate = controller.expectOne(`${USER_API}/7`);
      expect(activate.request.body).toEqual({ isActive: true, version: 4 });
      activate.flush(user({ version: 5 }));
      expect(list.notice()).toBe('sys.user.activated');
      expectList().flush(page([]));
    });

    it('switches translated labels', async () => {
      await open();
      expectList().flush(page([user({ isLocked: true })]));
      await render();
      expect(fixture.nativeElement.textContent).toContain('Users');
      TestBed.inject(TranslateService).use('zh-Hant');
      await render();
      expect(fixture.nativeElement.textContent).toContain('用戶列表');
      expect(fixture.nativeElement.textContent).toContain('已鎖定');
    });
  });

  it('routes /sys/users behind SYS.USER.VIEW and lazy-loads the page', async () => {
    const route = SYS_ROUTES.find((r) => r.path === 'users')!;
    expect(route.canActivate).toHaveLength(1);
    expect(await route.loadComponent!()).toBe(UserList);
  });
});
