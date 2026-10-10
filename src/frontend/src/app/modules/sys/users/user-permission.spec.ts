// 介面 8.65（REQ_F4118.25.6、REQ_F4118.25.8）：用戶權限對話框與 API。
import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting, TestRequest } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { seedSession } from '../../../../testing/auth-fixtures';
import { provideTestTranslations } from '../../../../testing/i18n-fixtures';
import { AUTHORIZATION_API, PermissionService } from '../../../core/auth/permission.service';
import { CompanyContextService } from '../../../core/company/company-context.service';
import { ORGANIZATION_API } from '../organization/organization.service';
import { ROLE_API } from '../roles/role.service';
import { UserList } from './user-list';
import { UserPermissionDialog } from './user-permission-dialog';
import { USER_API, UserCompanyAssignment, userErrorKey, UserService, UserSummary } from './user.service';

const page = (items: object[]) => ({ items, page: 1, pageSize: 200, totalCount: items.length });

const alice: UserSummary = {
  id: 7,
  username: 'alice',
  isActive: true,
  isLocked: false,
  passwordChangedAt: '2026-10-01T09:15:00+08:00',
  isPasswordExpired: false,
  version: 3,
};

const inTaipei: UserCompanyAssignment = {
  companyId: 1,
  companyCode: 'TW01',
  companyName: 'Taipei',
  branchId: 11,
  departmentId: null,
  isDefault: true,
  isActive: true,
  roleIds: [5],
};

const roles = [
  { id: 5, code: 'CLERK', name: 'Clerk', description: null, dataScope: 'Self', isFullAccess: false, isActive: true, version: 1, permissions: [] },
  { id: 6, code: 'ADMIN', name: 'Admin', description: null, dataScope: 'Company', isFullAccess: true, isActive: true, version: 1, permissions: [] },
  { id: 9, code: 'OLD', name: 'Old', description: null, dataScope: 'Self', isFullAccess: false, isActive: false, version: 1, permissions: [] },
];

const branch = (id: number, code: string, isActive = true) => ({ id, code, name: code, isActive, version: 1 });

describe('SYS user permissions', () => {
  let controller: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    seedSession();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), ...provideTestTranslations('en')],
    });
    controller = TestBed.inject(HttpTestingController);
    TestBed.inject(CompanyContextService).setCompanies([{ id: 1, code: 'TW01', name: 'Taipei' }, { id: 2, code: 'HK01', name: 'Hong Kong' }]);
  });

  afterEach(() => {
    controller.verify();
    localStorage.clear();
  });

  function expectOrganization(kind: 'branch' | 'department', company: number): TestRequest {
    const request = controller.expectOne((r) => r.method === 'GET' && r.url === ORGANIZATION_API[kind] && r.headers.get('X-Company-Id') === String(company));
    expect(request.request.params.toString()).toBe('page=1&pageSize=200&sort=code');
    return request;
  }

  it('UserService reads and replaces assignments and loads active options with the chosen company header', async () => {
    const api = TestBed.inject(UserService);
    api.assignments(7).subscribe();
    controller.expectOne(`${USER_API}/7/companies`).flush([inTaipei]);

    api.assign(7, 2, { branchId: null, departmentId: 3, isDefault: false, isActive: true, roleIds: [5] }).subscribe();
    const put = controller.expectOne(`${USER_API}/7/companies/2`);
    expect(put.request.method).toBe('PUT');
    expect(put.request.body).toEqual({ branchId: null, departmentId: 3, isDefault: false, isActive: true, roleIds: [5] });
    put.flush(null, { status: 204, statusText: 'No Content' });

    const options = firstValueFrom(api.organizationOptions('branch', 2));
    expectOrganization('branch', 2).flush(page([branch(21, 'KLN'), branch(22, 'OLD', false)]));
    expect(await options).toEqual([{ id: 21, code: 'KLN', name: 'KLN' }]);

    const error = (status: number, errorCode: string) => new HttpErrorResponse({ status, error: { errorCode } });
    expect(userErrorKey(error(404, 'SYS.COMPANY.NOT_FOUND'))).toBe('errors.SYS.COMPANY.NOT_FOUND');
  });

  describe('UserPermissionDialog', () => {
    let fixture: ComponentFixture<UserPermissionDialog>;
    let closed: boolean[];

    async function open(user: UserSummary | null = alice): Promise<UserPermissionDialog> {
      fixture = TestBed.createComponent(UserPermissionDialog);
      closed = [];
      fixture.componentInstance.closed.subscribe((saved) => closed.push(saved));
      fixture.componentRef.setInput('user', user);
      fixture.detectChanges();
      await fixture.whenStable();
      return fixture.componentInstance;
    }

    function load(assignments: UserCompanyAssignment[]): void {
      controller.expectOne(`${USER_API}/7/companies`).flush(assignments);
      const list = controller.expectOne((r) => r.method === 'GET' && r.url === ROLE_API);
      expect(list.request.params.get('pageSize')).toBe('200');
      list.flush(page(roles));
    }

    it('loads assignments and active roles, then shows the current company settings', async () => {
      const dialog = await open();
      expect(dialog.loading()).toBe(true);
      load([inTaipei]);
      expect(dialog.loading()).toBe(false);
      expect(dialog.roleOptions().map((r) => r.code)).toEqual(['CLERK', 'ADMIN']);
      expect(dialog.companyId()).toBe(1);
      expect(dialog.current()).toEqual(inTaipei);
      expect(dialog.form.getRawValue()).toEqual({ branchId: 11, departmentId: null, isDefault: true, isActive: true, roleIds: [5] });
      expectOrganization('branch', 1).flush(page([branch(11, 'TPE')]));
      expectOrganization('department', 1).flush(page([]));
      expect(dialog.branches()).toEqual([{ id: 11, code: 'TPE', name: 'TPE' }]);
      expect(dialog.organizationUnavailable()).toBe(false);
    });

    it('adds a company without access using defaults, tolerates missing organization permissions and saves sorted roles', async () => {
      const dialog = await open();
      load([inTaipei]);
      expectOrganization('branch', 1).flush(page([]));
      expectOrganization('department', 1).flush(page([]));

      dialog.company.setValue(2);
      expect(dialog.current()).toBeNull();
      expect(dialog.form.getRawValue()).toEqual({ branchId: null, departmentId: null, isDefault: false, isActive: true, roleIds: [] });
      expectOrganization('branch', 2).flush({ errorCode: 'COMMON.PERMISSION.DENIED' }, { status: 403, statusText: 'Forbidden' });
      expectOrganization('department', 2).flush(page([branch(31, 'HR')]));
      expect(dialog.organizationUnavailable()).toBe(true);
      expect(dialog.departments()).toEqual([{ id: 31, code: 'HR', name: 'HR' }]);

      dialog.form.patchValue({ departmentId: 31, isDefault: true, roleIds: [6, 5] });
      dialog.save();
      dialog.save();
      const put = controller.expectOne(`${USER_API}/7/companies/2`);
      expect(put.request.body).toEqual({ branchId: null, departmentId: 31, isDefault: true, isActive: true, roleIds: [5, 6] });
      put.flush({ errorCode: 'SYS.COMPANY.NOT_FOUND' }, { status: 404, statusText: 'Not Found' });
      expect(dialog.error()).toBe('errors.SYS.COMPANY.NOT_FOUND');
      expect(closed).toEqual([]);

      dialog.save();
      controller.expectOne(`${USER_API}/7/companies/2`).flush(null, { status: 204, statusText: 'No Content' });
      expect(closed).toEqual([true]);
    });

    it('ignores organization responses for a company that is no longer selected', async () => {
      const dialog = await open();
      load([]);
      const staleBranch = expectOrganization('branch', 1);
      const staleDepartment = expectOrganization('department', 1);
      dialog.company.setValue(2);
      staleBranch.flush(page([branch(11, 'TPE')]));
      staleDepartment.flush(page([branch(12, 'ACC')]));
      expect(dialog.branches()).toEqual([]);
      expect(dialog.departments()).toEqual([]);
      expectOrganization('branch', 2).flush(page([]));
      expectOrganization('department', 2).flush(page([]));
    });

    it('shows load errors, does not save while nothing is selected, and resets when closed', async () => {
      const dialog = await open();
      controller.expectOne(`${USER_API}/7/companies`).flush({ errorCode: 'SYS.USER.NOT_FOUND' }, { status: 404, statusText: 'Not Found' });
      controller.expectOne((r) => r.url === ROLE_API).flush(page(roles));
      expect(dialog.error()).toBe('errors.SYS.USER.NOT_FOUND');
      expectOrganization('branch', 1).flush(page([]));
      expectOrganization('department', 1).flush(page([]));

      fixture.componentRef.setInput('user', null);
      fixture.detectChanges();
      await fixture.whenStable();
      expect(dialog.companyId()).toBeNull();
      expect(dialog.error()).toBeNull();
      dialog.save();
      controller.expectNone((r) => r.method === 'PUT');
    });

    it('reports a roles load failure and skips applying results after the dialog is closed', async () => {
      const dialog = await open();
      controller.expectOne((r) => r.url === ROLE_API).flush({ errorCode: 'COMMON.PERMISSION.DENIED' }, { status: 403, statusText: 'Forbidden' });
      expect(dialog.error()).toBe('errors.COMMON.PERMISSION.DENIED');
      fixture.componentRef.setInput('user', null);
      fixture.detectChanges();
      await fixture.whenStable();
      controller.expectOne(`${USER_API}/7/companies`).flush([inTaipei]);
      expect(dialog.companyId()).toBeNull();
      expect(dialog.assignments()).toEqual([]);
      expect(dialog.loading()).toBe(false);
    });
  });

  it('UserList opens the dialog from a row with SYS.USER.EDIT and shows a notice after saving', async () => {
    const done = firstValueFrom(TestBed.inject(PermissionService).load());
    controller.expectOne(AUTHORIZATION_API).flush({
      companies: [{ id: 1, code: 'TW01', name: 'Taipei', isDefault: true }],
      companyId: 1,
      isFullAccess: false,
      permissions: [{ code: 'SYS.USER.VIEW', dataScope: 'Company' }, { code: 'SYS.USER.EDIT', dataScope: 'Company' }],
    });
    await done;
    const fixture = TestBed.createComponent(UserList);
    fixture.detectChanges();
    await fixture.whenStable();
    controller.expectOne((r) => r.method === 'GET' && r.url === USER_API).flush({ items: [alice], page: 1, pageSize: 20, totalCount: 1 });
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const button = fixture.nativeElement.querySelector('[data-testid="sys-user-permissions-alice"]') as HTMLButtonElement;
    expect(button.textContent).toContain('Permissions');
    button.click();
    fixture.detectChanges();
    await fixture.whenStable();
    const list = fixture.componentInstance;
    expect(list.permissionUser()).toEqual(alice);
    controller.expectOne(`${USER_API}/7/companies`).flush([]);
    controller.expectOne((r) => r.url === ROLE_API).flush(page([]));
    expectOrganization('branch', 1).flush(page([]));
    expectOrganization('department', 1).flush(page([]));

    list.onPermissionsClosed(false);
    expect(list.permissionUser()).toBeNull();
    expect(list.notice()).toBeNull();
    list.permissionUser.set(alice);
    list.onPermissionsClosed(true);
    expect(list.notice()).toBe('sys.user.permissionsSaved');
    expect(list.noticeParams()).toEqual({ username: 'alice' });
  }, 15_000);
});
