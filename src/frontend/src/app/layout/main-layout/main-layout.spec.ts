import { BreakpointObserver, BreakpointState } from '@angular/cdk/layout';
import { DOCUMENT } from '@angular/common';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { provideNzIcons } from 'ng-zorro-antd/icon';
import { Subject } from 'rxjs';
import { seedSession } from '../../../testing/auth-fixtures';
import { provideTestTranslations } from '../../../testing/i18n-fixtures';
import { AUTH_API, AuthService } from '../../core/auth/auth.service';
import { AUTHORIZATION_API, PermissionService } from '../../core/auth/permission.service';
import { CompanyContextService } from '../../core/company/company-context.service';
import { LanguageService } from '../../core/i18n/language.service';
import { APP_ICONS } from '../../core/icons';
import { DARK_CLASS, DARK_STYLESHEET_ID, ThemeService } from '../../core/theme/theme.service';
import { COLLAPSE_BREAKPOINT, MainLayout } from './main-layout';

describe('MainLayout', () => {
  let breakpoints: Subject<BreakpointState>;
  let observedQuery: string | string[] | undefined;
  let fixture: ComponentFixture<MainLayout>;
  let document: Document;

  function byTestId<T extends HTMLElement = HTMLElement>(testId: string): T | null {
    return document.querySelector<T>(`[data-testid="${testId}"]`);
  }

  async function settle(): Promise<void> {
    fixture.detectChanges();
    TestBed.tick();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  /** ng-zorro 下拉選單開啟有約 150 ms 的節流延遲。 */
  async function openDropdown(testId: string): Promise<void> {
    byTestId(testId)!.click();
    await new Promise((resolve) => setTimeout(resolve, 250));
    await settle();
  }

  beforeEach(async () => {
    localStorage.clear();
    seedSession();
    window.matchMedia = undefined as unknown as typeof window.matchMedia;
    breakpoints = new Subject<BreakpointState>();
    TestBed.configureTestingModule({
      providers: [
        ...provideTestTranslations(),
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideNzIcons(APP_ICONS),
        {
          provide: BreakpointObserver,
          useValue: {
            observe: (query: string | string[]) => {
              observedQuery = query;
              return breakpoints.asObservable();
            },
          },
        },
      ],
    });
    document = TestBed.inject(DOCUMENT);
    fixture = TestBed.createComponent(MainLayout);
    document.body.appendChild(fixture.nativeElement);
    await settle();
  });

  afterEach(() => {
    fixture.destroy();
    document.getElementById(DARK_STYLESHEET_ID)?.remove();
    document.documentElement.classList.remove(DARK_CLASS);
    localStorage.clear();
  });

  it('renders the brand and the translated menu', () => {
    expect(byTestId('layout-brand')?.textContent).toContain('MyERP');
    expect(byTestId('layout-menu-home')?.textContent).toContain('首頁');
  });

  it('toggles the side menu', async () => {
    const toggle = byTestId<HTMLButtonElement>('layout-menu-toggle')!;
    expect(toggle.getAttribute('aria-label')).toBe('收合選單');

    toggle.click();
    await settle();

    expect(byTestId('layout-sider')?.classList).toContain('ant-layout-sider-collapsed');
    expect(toggle.getAttribute('aria-label')).toBe('展開選單');
    expect(byTestId('layout-brand')?.textContent).not.toContain('MyERP');
  });

  it('collapses the menu on tablet widths', async () => {
    expect(observedQuery).toBe(COLLAPSE_BREAKPOINT);

    breakpoints.next({ matches: true, breakpoints: {} });
    await settle();
    expect(byTestId('layout-sider')?.classList).toContain('ant-layout-sider-collapsed');

    breakpoints.next({ matches: false, breakpoints: {} });
    await settle();
    expect(byTestId('layout-sider')?.classList).not.toContain('ant-layout-sider-collapsed');
  });

  it('switches language from the header dropdown', async () => {
    await openDropdown('layout-language');

    byTestId('layout-language-en')!.click();
    await settle();

    expect(TestBed.inject(LanguageService).current()).toBe('en');
    expect(byTestId('layout-menu-home')?.textContent).toContain('Home');
    expect(byTestId('layout-language')?.textContent).toContain('en');
  });

  it('switches theme from the header dropdown', async () => {
    await openDropdown('layout-theme');

    byTestId('layout-theme-dark')!.click();
    await settle();

    expect(TestBed.inject(ThemeService).mode()).toBe('dark');
    expect(document.documentElement.classList).toContain(DARK_CLASS);
  });

  it('lists theme modes with translated labels', async () => {
    await openDropdown('layout-theme');

    expect(byTestId('layout-theme-light')?.textContent).toContain('亮色');
    expect(byTestId('layout-theme-dark')?.textContent).toContain('暗色');
    expect(byTestId('layout-theme-system')?.textContent).toContain('跟隨系統');
  });

  it('disables the company switcher until companies are available', async () => {
    const select = byTestId('layout-company-select')!;
    expect(select.classList).toContain('ant-select-disabled');

    TestBed.inject(CompanyContextService).setCompanies([
      { id: 1, code: 'TW01', name: 'Taipei' },
      { id: 2, code: 'HK01', name: 'Hong Kong' },
    ]);
    await settle();

    expect(select.classList).not.toContain('ant-select-disabled');
    expect(select.textContent).toContain('TW01 Taipei');
  });

  it('switches company', async () => {
    const company = TestBed.inject(CompanyContextService);
    company.setCompanies([
      { id: 1, code: 'TW01', name: 'Taipei' },
      { id: 2, code: 'HK01', name: 'Hong Kong' },
    ]);
    await settle();

    fixture.componentInstance['changeCompany'](2);
    await settle();

    expect(company.current()?.code).toBe('HK01');
    expect(byTestId('layout-company-select')?.textContent).toContain('HK01 Hong Kong');

    // 切換公司後以新公司重新載入權限
    const reload = TestBed.inject(HttpTestingController).expectOne(AUTHORIZATION_API);
    expect(reload.request.method).toBe('GET');
    reload.flush({
      companies: [
        { id: 1, code: 'TW01', name: 'Taipei', isDefault: true },
        { id: 2, code: 'HK01', name: 'Hong Kong', isDefault: false },
      ],
      companyId: 2,
      isFullAccess: false,
      permissions: [{ code: 'SYS.ROLE.VIEW', dataScope: 'Company' }],
    });
    await settle();
    expect(TestBed.inject(PermissionService).has('SYS.ROLE.VIEW')).toBe(true);
    expect(company.current()?.code).toBe('HK01');
  });

  it('ignores switching to a company outside the list', async () => {
    fixture.componentInstance['changeCompany'](99);
    await settle();
    TestBed.inject(HttpTestingController).expectNone(AUTHORIZATION_API);
  });

  it('shows the signed-in username', () => {
    expect(byTestId('layout-username')?.textContent).toContain('admin');
    expect(byTestId('layout-logout')?.getAttribute('aria-label')).toBe('登出');
  });

  it('logs out, revokes the refresh token and returns to the login page', async () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    byTestId<HTMLButtonElement>('layout-logout')!.click();
    await settle();
    expect(byTestId<HTMLButtonElement>('layout-logout')!.disabled).toBe(true);

    const req = TestBed.inject(HttpTestingController).expectOne(`${AUTH_API}/logout`);
    expect(req.request.body).toEqual({ refreshToken: 'refresh-1' });
    req.flush(null, { status: 204, statusText: 'No Content' });
    await settle();

    expect(TestBed.inject(AuthService).isAuthenticated()).toBe(false);
    expect(TestBed.inject(PermissionService).loaded()).toBe(false);
    expect(TestBed.inject(CompanyContextService).companies()).toEqual([]);
    expect(byTestId('layout-username')).toBeNull();
    expect(navigate).toHaveBeenCalledWith('/login');
  });

  it('leaves the page before clearing the company so open lists do not reload without a session', async () => {
    const company = TestBed.inject(CompanyContextService);
    company.setCompanies([{ id: 1, code: 'TW01', name: 'Taipei' }]);
    let companiesWhenNavigating: unknown[] | null = null;
    vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockImplementation(async () => {
      companiesWhenNavigating = [...company.companies()];
      return true;
    });
    byTestId<HTMLButtonElement>('layout-logout')!.click();
    await settle();

    TestBed.inject(HttpTestingController).expectOne(`${AUTH_API}/logout`).flush(null, { status: 204, statusText: 'No Content' });
    await settle();

    expect(companiesWhenNavigating).not.toEqual([]);
    expect(company.companies()).toEqual([]);
    expect(TestBed.inject(PermissionService).loaded()).toBe(false);
  });
});
