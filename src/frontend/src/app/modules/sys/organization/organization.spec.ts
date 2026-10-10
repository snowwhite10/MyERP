// 介面 8.58–8.60（REQ_F4118.20–22）：公司／分店／部門維護頁、表單、API 服務、路由與選單權限。
import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting, TestRequest } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Route } from '@angular/router';
import { TranslateService } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { seedSession } from '../../../../testing/auth-fixtures';
import { provideTestTranslations } from '../../../../testing/i18n-fixtures';
import { routes } from '../../../app.routes';
import { AUTHORIZATION_API, PermissionService } from '../../../core/auth/permission.service';
import { CompanyContextService } from '../../../core/company/company-context.service';
import { MAIN_MENU } from '../../../layout/menu';
import { SYS_ROUTES } from '../sys.routes';
import { OrganizationForm } from './organization-form';
import { OrganizationList } from './organization-list';
import { ORGANIZATION_API, organizationErrorKey, OrganizationService } from './organization.service';

const page = (items: object[], totalCount = items.length) => ({ items, page: 1, pageSize: 20, totalCount });

describe('SYS organization', () => {
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

  /** 列表請求（不含部門頁一次取 200 筆的下拉選項請求）。 */
  function expectList(url: string): TestRequest {
    return controller.expectOne((request) => request.method === 'GET' && request.url === url && request.params.get('pageSize') !== '200');
  }

  describe('OrganizationService', () => {
    it('sends paging, trimmed keyword and includeInactive only for companies', () => {
      const api = TestBed.inject(OrganizationService);
      api.list('company', { page: 2, pageSize: 10, keyword: '  acme ', sort: '-name', includeInactive: true }).subscribe();
      const company = expectList(ORGANIZATION_API.company);
      expect(company.request.params.toString()).toBe('page=2&pageSize=10&keyword=acme&sort=-name&includeInactive=true');
      company.flush(page([]));

      api.list('branch', { page: 1, pageSize: 20, keyword: ' ', includeInactive: true }).subscribe();
      const branch = expectList(ORGANIZATION_API.branch);
      expect(branch.request.params.toString()).toBe('page=1&pageSize=20');
      branch.flush(page([]));

      api.update('department', 7, { name: 'HR', isActive: false, version: 3, branchId: null, parentDepartmentId: 5 }).subscribe();
      const put = controller.expectOne(`${ORGANIZATION_API.department}/7`);
      expect(put.request.method).toBe('PUT');
      expect(put.request.body).toEqual({ name: 'HR', isActive: false, version: 3, branchId: null, parentDepartmentId: 5 });
      put.flush({});
    });

    it('maps known error codes, validation, network and other failures to i18n keys', () => {
      const error = (status: number, errorCode?: string) => new HttpErrorResponse({ status, error: errorCode ? { errorCode } : null });
      expect(organizationErrorKey(error(409, 'SYS.BRANCH.DUPLICATE_CODE'))).toBe('errors.SYS.BRANCH.DUPLICATE_CODE');
      expect(organizationErrorKey(error(409, 'COMMON.CONCURRENCY.CONFLICT'))).toBe('errors.COMMON.CONCURRENCY.CONFLICT');
      expect(organizationErrorKey(error(400, 'COMMON.REQUEST.VALIDATION_FAILED'))).toBe('errors.COMMON.REQUEST.VALIDATION_FAILED');
      expect(organizationErrorKey(error(0))).toBe('errors.http.network');
      expect(organizationErrorKey(error(500, 'SOMETHING.ELSE'))).toBe('errors.http.unknown');
      expect(organizationErrorKey(new Error('boom'))).toBe('errors.http.unknown');
    });
  });

  describe('OrganizationForm', () => {
    it('validates company code and name before emitting a normalized draft', () => {
      const fixture = TestBed.createComponent(OrganizationForm);
      fixture.componentRef.setInput('kind', 'company');
      fixture.detectChanges();
      const saved: unknown[] = [];
      fixture.componentInstance.save.subscribe((value) => saved.push(value));
      fixture.componentInstance.form.patchValue({ code: 'bad code', name: 'Acme' });
      fixture.componentInstance.submit();
      expect(saved).toEqual([]);
      fixture.componentInstance.form.patchValue({ code: 'acme_1' });
      fixture.componentInstance.submit();
      expect(saved).toEqual([{ code: 'ACME_1', name: 'Acme', branchId: null, parentDepartmentId: null, isActive: true }]);
      fixture.componentInstance.form.patchValue({ name: '   ' });
      fixture.componentInstance.submit();
      expect(saved).toHaveLength(1);
    });

    it('locks the code when editing and emits the active flag', () => {
      const fixture = TestBed.createComponent(OrganizationForm);
      fixture.componentRef.setInput('kind', 'branch');
      fixture.componentRef.setInput('value', { code: 'NORTH', name: 'North', isActive: true });
      fixture.detectChanges();
      const form = fixture.componentInstance.form;
      expect(form.controls.code.disabled).toBe(true);
      expect(fixture.nativeElement.querySelector('[data-testid="sys-active"]')).not.toBeNull();
      const saved: unknown[] = [];
      fixture.componentInstance.save.subscribe((value) => saved.push(value));
      form.patchValue({ name: ' North HQ ', isActive: false });
      fixture.componentInstance.submit();
      expect(saved).toEqual([{ code: 'NORTH', name: 'North HQ', branchId: null, parentDepartmentId: null, isActive: false }]);

      fixture.componentRef.setInput('value', null);
      fixture.detectChanges();
      expect(form.controls.code.enabled).toBe(true);
      expect(form.getRawValue().code).toBe('');
    });

    it('renders branch and department references and switches translated labels', () => {
      const fixture = TestBed.createComponent(OrganizationForm);
      fixture.componentRef.setInput('kind', 'department');
      fixture.componentRef.setInput('branches', [{ id: 10, code: 'MAIN', name: 'Main' }]);
      fixture.componentRef.setInput('departments', [{ id: 20, code: 'HR', name: 'Human Resources' }]);
      fixture.detectChanges();
      expect(fixture.nativeElement.querySelector('[data-testid="sys-branch-id"]')).not.toBeNull();
      expect(fixture.nativeElement.querySelector('[data-testid="sys-parent-id"]')).not.toBeNull();
      expect(fixture.nativeElement.textContent).toContain('Parent department');
      TestBed.inject(TranslateService).use('zh-Hant');
      fixture.detectChanges();
      expect(fixture.nativeElement.textContent).toContain('上級部門');
    });
  });

  describe('OrganizationList page', () => {
    let fixture: ComponentFixture<OrganizationList>;

    async function open(kind: 'company' | 'branch' | 'department'): Promise<void> {
      TestBed.inject(CompanyContextService).setCompanies([{ id: 1, code: 'TW01', name: 'Taipei' }, { id: 2, code: 'HK01', name: 'Hong Kong' }]);
      fixture = TestBed.createComponent(OrganizationList);
      fixture.componentRef.setInput('kind', kind);
      fixture.detectChanges();
      await fixture.whenStable();
    }

    function shown(testId: string): boolean {
      return fixture.nativeElement.querySelector(`[data-testid="${testId}"]`) !== null;
    }

    async function render(): Promise<void> {
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();
    }

    it('loads the first page sorted by code and shows create/edit only with permissions', async () => {
      await open('branch');
      const first = expectList(ORGANIZATION_API.branch);
      expect(first.request.params.get('sort')).toBe('code');
      expect(first.request.params.get('page')).toBe('1');
      first.flush(page([{ id: 1, code: 'NORTH', name: 'North', isActive: true, version: 1 }, { id: 2, code: 'OLD', name: 'Old', isActive: false, version: 4 }]));
      await render();
      expect(shown('sys-organization-row-NORTH')).toBe(true);
      expect(fixture.nativeElement.textContent).toContain('Inactive');
      expect(shown('sys-organization-create')).toBe(false);
      expect(shown('sys-organization-edit-NORTH')).toBe(false);

      await grant(['SYS.BRANCH.VIEW', 'SYS.BRANCH.CREATE']);
      await render();
      expect(shown('sys-organization-create')).toBe(true);
      expect(shown('sys-organization-edit-NORTH')).toBe(false);
    });

    it('searches by keyword from the first page and reloads when the company changes', async () => {
      await open('company');
      expectList(ORGANIZATION_API.company).flush(page([], 0));
      fixture.componentInstance.keyword.set(' tw ');
      fixture.componentInstance.page.set(3);
      fixture.componentInstance.search();
      const search = expectList(ORGANIZATION_API.company);
      expect(search.request.params.get('keyword')).toBe('tw');
      expect(search.request.params.get('page')).toBe('1');
      search.flush(page([]));

      fixture.componentInstance.onQueryParams({ pageIndex: 1, pageSize: 20, sort: [{ key: 'name', value: 'descend' }], filter: [] });
      const sorted = expectList(ORGANIZATION_API.company);
      expect(sorted.request.params.get('sort')).toBe('-name');
      sorted.flush(page([]));

      TestBed.inject(CompanyContextService).select(2);
      await render();
      expectList(ORGANIZATION_API.company).flush(page([]));
    });

    it('creates a record, reloads the list, and shows API errors in the dialog', async () => {
      await grant(['SYS.BRANCH.CREATE', 'SYS.BRANCH.EDIT']);
      await open('branch');
      expectList(ORGANIZATION_API.branch).flush(page([{ id: 1, code: 'NORTH', name: 'North', isActive: true, version: 2 }]));
      const list = fixture.componentInstance;

      list.openCreate();
      list.persist({ code: 'NORTH', name: 'Dup', branchId: null, parentDepartmentId: null, isActive: true });
      const duplicate = controller.expectOne((r) => r.method === 'POST' && r.url === ORGANIZATION_API.branch);
      expect(duplicate.request.body).toEqual({ code: 'NORTH', name: 'Dup' });
      duplicate.flush({ errorCode: 'SYS.BRANCH.DUPLICATE_CODE' }, { status: 409, statusText: 'Conflict' });
      expect(list.saveError()).toBe('errors.SYS.BRANCH.DUPLICATE_CODE');
      expect(list.dialogOpen()).toBe(true);

      list.persist({ code: 'SOUTH', name: 'South', branchId: null, parentDepartmentId: null, isActive: true });
      controller.expectOne((r) => r.method === 'POST').flush({ id: 2, code: 'SOUTH', name: 'South', isActive: true, version: 1 });
      expect(list.dialogOpen()).toBe(false);
      expectList(ORGANIZATION_API.branch).flush(page([]));

      // 編輯帶回讀取時的 version
      list.openEdit({ id: 1, code: 'NORTH', name: 'North', isActive: true, version: 2 });
      list.persist({ code: 'NORTH', name: 'North HQ', branchId: null, parentDepartmentId: null, isActive: false });
      const put = controller.expectOne(`${ORGANIZATION_API.branch}/1`);
      expect(put.request.body).toEqual({ name: 'North HQ', isActive: false, version: 2 });
      put.flush({ errorCode: 'COMMON.CONCURRENCY.CONFLICT' }, { status: 409, statusText: 'Conflict' });
      expect(list.saveError()).toBe('errors.COMMON.CONCURRENCY.CONFLICT');
    });

    it('loads branch and parent options for departments and excludes the edited department', async () => {
      await open('department');
      expectList(ORGANIZATION_API.department).flush(page([{ id: 5, code: 'FIN', name: 'Finance', branchId: 9, parentDepartmentId: null, isActive: true, version: 1 }]));
      const options = controller.match((r) => r.params.get('pageSize') === '200');
      expect(options.map((r) => r.request.url).sort()).toEqual([ORGANIZATION_API.branch, ORGANIZATION_API.department].sort());
      options.find((r) => r.request.url === ORGANIZATION_API.branch)!.flush(page([
        { id: 9, code: 'MAIN', name: 'Main', isActive: true, version: 1 },
        { id: 8, code: 'GONE', name: 'Closed', isActive: false, version: 1 },
      ]));
      options.find((r) => r.request.url === ORGANIZATION_API.department)!
        .flush({ errorCode: 'COMMON.PERMISSION.DENIED' }, { status: 403, statusText: 'Forbidden' });
      await render();
      const list = fixture.componentInstance;
      expect(fixture.nativeElement.textContent).toContain('MAIN — Main');
      expect(list.activeBranches().map((b) => b.id)).toEqual([9]);
      expect(list.departmentOptions()).toEqual([]);

      list.departmentOptions.set([
        { id: 5, code: 'FIN', name: 'Finance', isActive: true, version: 1 },
        { id: 6, code: 'AP', name: 'Payables', isActive: true, version: 1 },
      ]);
      list.openEdit(list.rows()[0]);
      expect(list.parentChoices().map((d) => d.id)).toEqual([6]);
    });

    it('shows a load error when the list request fails', async () => {
      await open('company');
      expectList(ORGANIZATION_API.company).error(new ProgressEvent('error'));
      await render();
      expect(fixture.componentInstance.loadError()).toBe('errors.http.network');
      expect(shown('sys-organization-load-error')).toBe(true);
    });
  });

  it('routes /sys pages behind VIEW permissions and lists them in the menu', () => {
    const shell = routes.find((route) => route.path === '')!;
    expect(shell.children!.some((route) => route.path === 'sys' && typeof route.loadChildren === 'function')).toBe(true);
    expect(SYS_ROUTES.map((route: Route) => [route.path, route.data?.['kind']])).toEqual([
      ['companies', 'company'],
      ['branches', 'branch'],
      ['departments', 'department'],
      ['users', undefined],
      ['roles', undefined],
    ]);
    expect(SYS_ROUTES.every((route) => route.canActivate?.length === 1)).toBe(true);
    const sys = MAIN_MENU.find((item) => item.testId === 'layout-menu-sys')!;
    expect(sys.children!.map((item) => [item.route, item.permission])).toEqual([
      ['/sys/companies', 'SYS.COMPANY.VIEW'],
      ['/sys/branches', 'SYS.BRANCH.VIEW'],
      ['/sys/departments', 'SYS.DEPARTMENT.VIEW'],
      ['/sys/users', 'SYS.USER.VIEW'],
      ['/sys/roles', 'SYS.ROLE.VIEW'],
    ]);
  });
});
