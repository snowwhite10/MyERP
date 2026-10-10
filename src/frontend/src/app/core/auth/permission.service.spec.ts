// REQ_F4118.25.7：前端權限服務、路由權限與公司同步（後端仍以權限代碼檢查）。
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting, TestRequest } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router, UrlTree } from '@angular/router';
import { firstValueFrom, Observable } from 'rxjs';
import { seedSession } from '../../../testing/auth-fixtures';
import { provideTestTranslations } from '../../../testing/i18n-fixtures';
import { CompanyContextService, COMPANY_STORAGE_KEY } from '../company/company-context.service';
import { apiHeadersInterceptor, COMPANY_HEADER } from '../http/api-headers.interceptor';
import { PREFERENCE_KEY_PREFIX } from '../storage/preference-storage';
import { AuthService } from './auth.service';
import {
  AUTHORIZATION_API,
  authorizationGuard,
  COMPANY_ACCESS_DENIED,
  CurrentAuthorization,
  permissionGuard,
  PermissionService,
} from './permission.service';

@Component({ template: '' })
class Blank {}

const TW = { id: 1, code: 'TW01', name: 'Taipei', isDefault: true };
const HK = { id: 2, code: 'HK01', name: 'Hong Kong', isDefault: false };

function authorization(overrides: Partial<CurrentAuthorization> = {}): CurrentAuthorization {
  return {
    companies: [TW, HK],
    companyId: 1,
    isFullAccess: false,
    permissions: [
      { code: 'SYS.ROLE.VIEW', dataScope: 'Branch' },
      { code: 'SYS.USER.VIEW', dataScope: 'Self' },
    ],
    ...overrides,
  };
}

describe('PermissionService', () => {
  let service: PermissionService;
  let company: CompanyContextService;
  let controller: HttpTestingController;
  let router: Router;

  beforeEach(() => {
    localStorage.clear();
    seedSession();
    TestBed.configureTestingModule({
      providers: [
        ...provideTestTranslations(),
        provideHttpClient(withInterceptors([apiHeadersInterceptor])),
        provideHttpClientTesting(),
        provideRouter([
          { path: 'forbidden', component: Blank },
          {
            path: '',
            component: Blank,
            canActivate: [authorizationGuard],
            children: [
              { path: 'roles', component: Blank, canActivate: [permissionGuard('SYS.ROLE.VIEW')] },
              { path: 'secret', component: Blank, canActivate: [permissionGuard('SYS.ROLE.DELETE', 'SYS.USER.EDIT')] },
            ],
          },
        ]),
      ],
    });
    service = TestBed.inject(PermissionService);
    company = TestBed.inject(CompanyContextService);
    controller = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
  });

  afterEach(() => {
    controller.verify();
    localStorage.clear();
  });

  function expectAuthorization(): TestRequest {
    return controller.expectOne(AUTHORIZATION_API);
  }

  it('denies everything before loading', () => {
    expect(service.loaded()).toBe(false);
    expect(service.has('SYS.ROLE.VIEW')).toBe(false);
    expect(service.scopeOf('SYS.ROLE.VIEW')).toBeNull();
  });

  it('loads permissions and companies, selecting the default company first', async () => {
    const done = firstValueFrom(service.load());
    const req = expectAuthorization();
    expect(req.request.headers.has(COMPANY_HEADER)).toBe(false);
    req.flush(authorization());
    await done;

    expect(service.loaded()).toBe(true);
    expect(service.has('SYS.ROLE.VIEW')).toBe(true);
    expect(service.has('SYS.ROLE.EDIT')).toBe(false);
    expect(service.hasAny(['SYS.ROLE.EDIT', 'SYS.USER.VIEW'])).toBe(true);
    expect(service.hasAny([])).toBe(false);
    expect(service.scopeOf('SYS.ROLE.VIEW')).toBe('Branch');
    expect(company.companies().map((c) => c.id)).toEqual([1, 2]);
    expect(company.current()?.id).toBe(1);
  });

  it('treats full access as every permission with company scope', async () => {
    const done = firstValueFrom(service.load());
    expectAuthorization().flush(authorization({ isFullAccess: true, permissions: [] }));
    await done;
    expect(service.has('ANY.THING.VIEW')).toBe(true);
    expect(service.scopeOf('ANY.THING.VIEW')).toBe('Company');
  });

  it('reloads for the remembered company when the server answered for another one', async () => {
    localStorage.setItem(PREFERENCE_KEY_PREFIX + COMPANY_STORAGE_KEY, '2');
    // 尚未取得清單時不會帶出本機偏好的公司
    const done = firstValueFrom(service.load());
    expectAuthorization().flush(authorization());
    const second = expectAuthorization();
    expect(second.request.headers.get(COMPANY_HEADER)).toBe('2');
    second.flush(authorization({ companyId: 2, permissions: [{ code: 'SYS.USER.EDIT', dataScope: 'Company' }] }));
    await done;

    expect(company.current()?.id).toBe(2);
    expect(service.has('SYS.USER.EDIT')).toBe(true);
    expect(service.has('SYS.ROLE.VIEW')).toBe(false);
  });

  it('drops an inaccessible company and falls back to the default one', async () => {
    company.setCompanies([{ id: 9, code: 'OLD', name: 'Old' }]);
    const done = firstValueFrom(service.load());
    const first = expectAuthorization();
    expect(first.request.headers.get(COMPANY_HEADER)).toBe('9');
    first.flush({ errorCode: COMPANY_ACCESS_DENIED }, { status: 403, statusText: 'Forbidden' });
    const retry = expectAuthorization();
    expect(retry.request.headers.has(COMPANY_HEADER)).toBe(false);
    retry.flush(authorization());
    await done;

    expect(company.current()?.id).toBe(1);
    expect(service.has('SYS.ROLE.VIEW')).toBe(true);
  });

  it('continues with no permissions when loading fails', async () => {
    const done = firstValueFrom(service.load());
    expectAuthorization().flush({ errorCode: 'COMMON.SERVER.INTERNAL_ERROR' }, { status: 500, statusText: 'Error' });
    const result = await done;

    expect(result.permissions).toEqual([]);
    expect(service.loaded()).toBe(true);
    expect(service.has('SYS.ROLE.VIEW')).toBe(false);
  });

  it('does not retry other 403 responses', async () => {
    company.setCompanies([{ id: 1, code: 'TW01', name: 'Taipei' }]);
    const done = firstValueFrom(service.load());
    expectAuthorization().flush({ errorCode: 'COMMON.PERMISSION.DENIED' }, { status: 403, statusText: 'Forbidden' });
    await done;
    expect(company.current()?.id).toBe(1);
    expect(service.has('SYS.ROLE.VIEW')).toBe(false);
  });

  it('is not considered loaded for a different account and clears on demand', async () => {
    const done = firstValueFrom(service.load());
    expectAuthorization().flush(authorization());
    await done;

    TestBed.inject(AuthService).clear();
    expect(service.loaded()).toBe(false);

    service.clear();
    expect(service.has('SYS.ROLE.VIEW')).toBe(false);
  });

  it('guards load once and route permissions redirect to the forbidden page', async () => {
    const navigation = router.navigateByUrl('/roles');
    // 主框架守衛與路由守衛共用同一次載入
    await new Promise((resolve) => setTimeout(resolve));
    expectAuthorization().flush(authorization());
    expect(await navigation).toBe(true);
    expect(router.url).toBe('/roles');

    await router.navigateByUrl('/secret');
    controller.expectNone(AUTHORIZATION_API);
    expect(router.url).toBe('/forbidden');
  });

  it('route guard loads permissions itself when used without the layout guard', async () => {
    const guard = permissionGuard('SYS.ROLE.VIEW');
    const result = TestBed.runInInjectionContext(() => guard({} as never, {} as never));
    const decision = firstValueFrom(result as Observable<boolean | UrlTree>);
    expectAuthorization().flush(authorization({ permissions: [] }));
    expect(String(await decision)).toBe('/forbidden');
  });
});
