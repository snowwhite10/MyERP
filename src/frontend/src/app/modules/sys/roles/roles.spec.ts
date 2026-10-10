// 介面 8.64（REQ_F4118.25.1–4）：角色維護頁、API 服務、權限分組、錯誤代碼與路由。
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
import { RoleList } from './role-list';
import { dataScopeKey, groupPermissions,PERMISSION_CATALOG_API, Role, ROLE_API, roleErrorKey, RoleService } from './role.service';

const page = (items: object[], totalCount = items.length) => ({ items, page: 1, pageSize: 20, totalCount });

const role = (overrides: Partial<Role> = {}): Role => ({
  id: 5,
  code: 'CLERK',
  name: 'Clerk',
  description: null,
  dataScope: 'Self',
  isFullAccess: false,
  isActive: true,
  version: 2,
  permissions: ['SYS.USER.VIEW'],
  ...overrides,
});

const CATALOG = [
  { code: 'SYS.USER.VIEW', module: 'SYS' },
  { code: 'SYS.ROLE.VIEW', module: 'SYS' },
  { code: 'MD.ITEM.VIEW', module: 'MD' },
];

describe('SYS roles', () => {
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
    return controller.expectOne((request) => request.method === 'GET' && request.url === ROLE_API);
  }

  describe('RoleService', () => {
    it('sends paging, keyword and sort; posts, puts and deletes with the version', () => {
      const api = TestBed.inject(RoleService);
      api.list({ page: 2, pageSize: 10, keyword: ' clerk ', sort: '-name' }).subscribe();
      const list = expectList();
      expect(list.request.params.toString()).toBe('page=2&pageSize=10&keyword=clerk&sort=-name');
      list.flush(page([]));

      api.list({ page: 1, pageSize: 20, keyword: '  ' }).subscribe();
      const blank = expectList();
      expect(blank.request.params.toString()).toBe('page=1&pageSize=20');
      blank.flush(page([]));

      api.permissions().subscribe();
      controller.expectOne(PERMISSION_CATALOG_API).flush(CATALOG);

      const input = { code: 'X', name: 'X', description: null, dataScope: 'Company' as const, isFullAccess: false, permissions: [] };
      api.create(input).subscribe();
      const post = controller.expectOne((r) => r.method === 'POST' && r.url === ROLE_API);
      expect(post.request.body).toEqual(input);
      post.flush(role());

      api.update(5, { name: 'X', description: null, dataScope: 'Self', isFullAccess: true, isActive: false, permissions: [], version: 2 }).subscribe();
      const put = controller.expectOne(`${ROLE_API}/5`);
      expect(put.request.method).toBe('PUT');
      expect(put.request.body.version).toBe(2);
      put.flush(role());

      api.remove(5, 3).subscribe();
      const remove = controller.expectOne((r) => r.method === 'DELETE' && r.url === `${ROLE_API}/5`);
      expect(remove.request.params.get('version')).toBe('3');
      remove.flush(null, { status: 204, statusText: 'No Content' });
    });

    it('groups the catalog by module and maps role error codes', () => {
      expect(groupPermissions(CATALOG)).toEqual([
        { module: 'MD', codes: ['MD.ITEM.VIEW'] },
        { module: 'SYS', codes: ['SYS.ROLE.VIEW', 'SYS.USER.VIEW'] },
      ]);
      expect(dataScopeKey('Department')).toBe('sys.role.scope.department');
      const error = (status: number, errorCode?: string) => new HttpErrorResponse({ status, error: errorCode ? { errorCode } : null });
      expect(roleErrorKey(error(409, 'SYS.ROLE.DUPLICATE_CODE'))).toBe('errors.SYS.ROLE.DUPLICATE_CODE');
      expect(roleErrorKey(error(422, 'SYS.ROLE.IN_USE'))).toBe('errors.SYS.ROLE.IN_USE');
      expect(roleErrorKey(error(404, 'SYS.ROLE.NOT_FOUND'))).toBe('errors.SYS.ROLE.NOT_FOUND');
      expect(roleErrorKey(error(409, 'COMMON.CONCURRENCY.CONFLICT'))).toBe('errors.COMMON.CONCURRENCY.CONFLICT');
      expect(roleErrorKey(error(400))).toBe('errors.COMMON.REQUEST.VALIDATION_FAILED');
      expect(roleErrorKey(error(0))).toBe('errors.http.network');
      expect(roleErrorKey(error(500, 'OTHER'))).toBe('errors.http.unknown');
      expect(roleErrorKey(new Error('boom'))).toBe('errors.http.unknown');
    });
  });

  describe('RoleList page', () => {
    let fixture: ComponentFixture<RoleList>;

    async function open(): Promise<RoleList> {
      TestBed.inject(CompanyContextService).setCompanies([{ id: 1, code: 'TW01', name: 'Taipei' }, { id: 2, code: 'HK01', name: 'Hong Kong' }]);
      fixture = TestBed.createComponent(RoleList);
      fixture.detectChanges();
      await fixture.whenStable();
      return fixture.componentInstance;
    }

    function shown(testId: string): boolean {
      return fixture.nativeElement.querySelector(`[data-testid="${testId}"]`) !== null;
    }

    /** 對話框內容渲染在 CDK overlay（fixture 之外）。 */
    function shownInDialog(testId: string): boolean {
      return document.querySelector(`[data-testid="${testId}"]`) !== null;
    }

    async function render(): Promise<void> {
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();
    }

    it('lists roles with scope, permission count and full-access tag; buttons follow permissions', async () => {
      await open();
      const first = expectList();
      expect(first.request.params.get('sort')).toBe('code');
      first.flush(page([role(), role({ id: 6, code: 'ADMIN', name: 'Admin', dataScope: 'Company', isFullAccess: true, permissions: [] })]));
      await render();
      expect(shown('sys-role-row-CLERK')).toBe(true);
      expect(fixture.nativeElement.textContent).toContain('1 permissions');
      expect(fixture.nativeElement.textContent).toContain('Whole company');
      expect(shown('sys-role-full-ADMIN')).toBe(true);
      expect(shown('sys-role-full-CLERK')).toBe(false);
      expect(shown('sys-role-create')).toBe(false);
      expect(shown('sys-role-edit-CLERK')).toBe(false);

      await grant(['SYS.ROLE.VIEW', 'SYS.ROLE.EDIT']);
      await render();
      expect(shown('sys-role-edit-CLERK')).toBe(true);
      expect(shown('sys-role-delete-CLERK')).toBe(false);
      expect(shown('sys-role-create')).toBe(false);
    }, 15_000);

    it('searches from the first page, sorts by name and reloads when the company changes', async () => {
      const list = await open();
      expectList().flush(page([]));
      list.keyword.set('cl');
      list.page.set(2);
      list.search();
      const search = expectList();
      expect(search.request.params.get('keyword')).toBe('cl');
      expect(search.request.params.get('page')).toBe('1');
      search.flush(page([]));

      list.onQueryParams({ pageIndex: 1, pageSize: 20, sort: [{ key: 'code', value: null }, { key: 'name', value: 'descend' }], filter: [] });
      const sorted = expectList();
      expect(sorted.request.params.get('sort')).toBe('-name');
      sorted.flush(page([]));
      expect(list.sortOrder('name')).toBe('descend');
      expect(list.sortOrder('code')).toBeNull();

      list.onQueryParams({ pageIndex: 1, pageSize: 20, sort: [{ key: 'name', value: 'descend' }], filter: [] });
      controller.expectNone(ROLE_API);

      list.onQueryParams({ pageIndex: 1, pageSize: 20, sort: [], filter: [] });
      expect(expectList().request.params.get('sort')).toBe('code');
      expect(list.sortOrder('code')).toBe('ascend');

      TestBed.inject(CompanyContextService).select(2);
      await render();
      expectList().flush(page([]));
    });

    it('shows a load error when the list request fails', async () => {
      await open();
      expectList().error(new ProgressEvent('error'));
      await render();
      expect(fixture.componentInstance.loadError()).toBe('errors.http.network');
      expect(shown('sys-role-load-error')).toBe(true);
    });

    it('creates a role with an uppercased code, grouped permissions and the catalog loaded once', async () => {
      await grant(['SYS.ROLE.VIEW', 'SYS.ROLE.CREATE']);
      const list = await open();
      expectList().flush(page([]));

      list.openCreate();
      controller.expectOne(PERMISSION_CATALOG_API).flush(CATALOG);
      await render();
      expect(list.permissionGroups().map((g) => g.module)).toEqual(['MD', 'SYS']);
      expect(shownInDialog('sys-role-permission-MD.ITEM.VIEW')).toBe(true);

      list.form.patchValue({ code: 'bad code', name: '   ' });
      list.submit();
      expect(list.form.controls.code.invalid).toBe(true);
      expect(list.form.controls.name.invalid).toBe(true);
      controller.expectNone((r) => r.method === 'POST');

      const sys = list.permissionGroups()[1];
      list.toggleGroup(sys, true);
      expect(list.groupState(sys)).toBe('all');
      list.togglePermission('SYS.USER.VIEW', false);
      expect(list.groupState(sys)).toBe('some');
      expect(list.groupState(list.permissionGroups()[0])).toBe('none');
      list.togglePermission('MD.ITEM.VIEW', true);
      list.toggleGroup(sys, false);
      list.togglePermission('SYS.ROLE.VIEW', true);

      list.form.patchValue({ code: ' sales-1 ', name: ' Sales ', description: '  ', dataScope: 'Branch' });
      list.submit();
      list.submit();
      const post = controller.expectOne((r) => r.method === 'POST' && r.url === ROLE_API);
      expect(post.request.body).toEqual({
        code: 'SALES-1',
        name: 'Sales',
        description: null,
        dataScope: 'Branch',
        isFullAccess: false,
        permissions: ['MD.ITEM.VIEW', 'SYS.ROLE.VIEW'],
      });
      post.flush({ errorCode: 'SYS.ROLE.DUPLICATE_CODE' }, { status: 409, statusText: 'Conflict' });
      expect(list.saveError()).toBe('errors.SYS.ROLE.DUPLICATE_CODE');
      expect(list.dialog()).toEqual({ mode: 'create' });

      list.submit();
      controller.expectOne((r) => r.method === 'POST').flush(role({ code: 'SALES-1' }), { status: 201, statusText: 'Created' });
      expect(list.dialog()).toBeNull();
      expect(list.notice()).toBe('sys.role.created');
      expect(list.noticeParams()).toEqual({ code: 'SALES-1' });
      expectList().flush(page([role({ code: 'SALES-1' })]));
      await render();
      expect(fixture.nativeElement.querySelector('[data-testid="sys-role-notice"]').textContent).toContain('Role SALES-1 was created.');

      // 目錄只載入一次；重新開啟時清空勾選
      list.openCreate();
      controller.expectNone(PERMISSION_CATALOG_API);
      expect(list.selected().size).toBe(0);
      list.closeDialog();
    });

    it('edits with the read version and existing permissions; full access disables the permission list', async () => {
      await grant(['SYS.ROLE.VIEW', 'SYS.ROLE.EDIT']);
      const list = await open();
      expectList().flush(page([role({ description: 'Front desk' })]));

      list.openEdit(list.rows()[0]);
      controller.expectOne(PERMISSION_CATALOG_API).error(new ProgressEvent('error'));
      expect(list.saveError()).toBe('errors.http.network');
      expect(list.form.controls.code.disabled).toBe(true);
      expect(list.dialogTitle()).toBe('sys.role.editTitle');
      expect(list.dialogTitleParams()).toEqual({ code: 'CLERK' });
      expect([...list.selected()]).toEqual(['SYS.USER.VIEW']);
      expect(list.form.controls.description.value).toBe('Front desk');

      list.form.patchValue({ isFullAccess: true, isActive: false, dataScope: 'Company' });
      await render();
      expect(shownInDialog('sys-role-full-access-hint')).toBe(true);
      list.submit();
      const put = controller.expectOne(`${ROLE_API}/5`);
      expect(put.request.body).toEqual({
        name: 'Clerk',
        description: 'Front desk',
        dataScope: 'Company',
        isFullAccess: true,
        permissions: ['SYS.USER.VIEW'],
        isActive: false,
        version: 2,
      });
      put.flush({ errorCode: 'COMMON.CONCURRENCY.CONFLICT' }, { status: 409, statusText: 'Conflict' });
      expect(list.saveError()).toBe('errors.COMMON.CONCURRENCY.CONFLICT');

      list.submit();
      controller.expectOne(`${ROLE_API}/5`).flush(role({ version: 3 }));
      expect(list.notice()).toBe('sys.role.updated');
      expectList().flush(page([]));

      // 編輯後重新開新增：代碼欄位恢復且有空白預設值
      list.openCreate();
      controller.expectOne(PERMISSION_CATALOG_API).flush(CATALOG);
      expect(list.form.controls.code.enabled).toBe(true);
      expect(list.form.getRawValue()).toEqual({ code: '', name: '', description: '', dataScope: 'Self', isFullAccess: false, isActive: true });
    });

    it('deletes with the version, shows IN_USE and ignores a second delete while busy', async () => {
      await grant(['SYS.ROLE.VIEW', 'SYS.ROLE.DELETE']);
      const list = await open();
      expectList().flush(page([role(), role({ id: 6, code: 'OLD', version: 7 })]));
      await render();
      expect(shown('sys-role-delete-CLERK')).toBe(true);

      const [clerk, old] = list.rows();
      list.remove(clerk);
      list.remove(old);
      const inUse = controller.expectOne((r) => r.method === 'DELETE');
      expect(inUse.request.url).toBe(`${ROLE_API}/5`);
      expect(inUse.request.params.get('version')).toBe('2');
      inUse.flush({ errorCode: 'SYS.ROLE.IN_USE' }, { status: 422, statusText: 'Unprocessable Entity' });
      expect(list.actionError()).toBe('errors.SYS.ROLE.IN_USE');
      expect(list.busyId()).toBeNull();
      await render();
      expect(shown('sys-role-action-error')).toBe(true);

      list.remove(old);
      expect(list.actionError()).toBeNull();
      controller.expectOne((r) => r.method === 'DELETE' && r.url === `${ROLE_API}/6`).flush(null, { status: 204, statusText: 'No Content' });
      expect(list.notice()).toBe('sys.role.deleted');
      expect(list.noticeParams()).toEqual({ code: 'OLD' });
      expectList().flush(page([role()]));
    });

    it('switches translated labels', async () => {
      await open();
      expectList().flush(page([role()]));
      await render();
      expect(fixture.nativeElement.textContent).toContain('Roles');
      TestBed.inject(TranslateService).use('zh-Hant');
      await render();
      expect(fixture.nativeElement.textContent).toContain('角色列表');
      expect(fixture.nativeElement.textContent).toContain('本人');
    });
  });

  it('routes /sys/roles behind SYS.ROLE.VIEW and lazy-loads the page', async () => {
    const route = SYS_ROUTES.find((r) => r.path === 'roles')!;
    expect(route.canActivate).toHaveLength(1);
    expect(await route.loadComponent!()).toBe(RoleList);
  });
});
