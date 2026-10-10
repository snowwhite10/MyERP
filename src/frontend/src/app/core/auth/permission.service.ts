import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { computed, inject, Injectable, signal } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { catchError, map, Observable, of, switchMap } from 'rxjs';
import { CompanyContextService, CompanyOption } from '../company/company-context.service';
import { AUTH_API, AuthService } from './auth.service';

/** 資料範圍（後端 DataScope，由窄至寬）。 */
export type DataScope = 'Self' | 'Department' | 'Branch' | 'Company';

export interface PermissionGrant {
  code: string;
  dataScope: DataScope;
}

export interface AuthorizedCompany extends CompanyOption {
  isDefault: boolean;
}

/** GET /api/v1/sys/auth/authorization */
export interface CurrentAuthorization {
  companies: AuthorizedCompany[];
  companyId: number | null;
  isFullAccess: boolean;
  permissions: PermissionGrant[];
}

export const AUTHORIZATION_API = `${AUTH_API}/authorization`;
export const COMPANY_ACCESS_DENIED = 'COMMON.COMPANY.ACCESS_DENIED';

const EMPTY: CurrentAuthorization = { companies: [], companyId: null, isFullAccess: false, permissions: [] };

/**
 * 目前用戶在目前公司的權限（REQ_F4118.25.7）。只用於顯示控制（無權限的按鈕與選單不顯示）；
 * 後端每個 API 仍以權限代碼檢查，以後端為準。
 */
@Injectable({ providedIn: 'root' })
export class PermissionService {
  private readonly http = inject(HttpClient);
  private readonly company = inject(CompanyContextService);

  private readonly auth = inject(AuthService);

  private readonly state = signal<CurrentAuthorization | null>(null);
  /** 載入時的帳號：Session 失效後換另一帳號登入，不沿用上一位用戶的權限。 */
  private readonly owner = signal<string | null>(null);
  private readonly codes = computed(() => new Set((this.state()?.permissions ?? []).map((p) => p.code)));

  /** 目前帳號是否已載入（含載入失敗後的空權限）。 */
  readonly loaded = computed(() => this.state() !== null && this.owner() === this.auth.username());
  readonly isFullAccess = computed(() => this.state()?.isFullAccess ?? false);

  has(code: string): boolean {
    return this.isFullAccess() || this.codes().has(code);
  }

  hasAny(codes: readonly string[]): boolean {
    return codes.some((code) => this.has(code));
  }

  scopeOf(code: string): DataScope | null {
    if (this.isFullAccess()) return 'Company';
    return this.state()?.permissions.find((p) => p.code === code)?.dataScope ?? null;
  }

  /**
   * 載入授權並同步公司清單。本機記住的公司已不可存取（403）時改取預設公司；
   * 後端回傳的公司與目前選擇不同時（例如首次登入），以目前選擇重新取一次，令權限與 X-Company-Id 一致。
   */
  load(): Observable<CurrentAuthorization> {
    const username = this.auth.username();
    return this.fetch(true).pipe(
      switchMap((result) => {
        this.company.setCompanies(result.companies.map(({ id, code, name }) => ({ id, code, name })));
        const selected = this.company.current()?.id ?? null;
        return selected !== null && selected !== result.companyId ? this.fetch(false) : of(result);
      }),
      catchError(() => of(EMPTY)),
      map((result) => {
        this.owner.set(username);
        this.state.set(result);
        return result;
      }),
    );
  }

  clear(): void {
    this.state.set(null);
    this.owner.set(null);
  }

  private fetch(retryWithoutCompany: boolean): Observable<CurrentAuthorization> {
    return this.http.get<CurrentAuthorization>(AUTHORIZATION_API).pipe(
      catchError((error: unknown) => {
        if (retryWithoutCompany && error instanceof HttpErrorResponse && error.status === 403 && error.error?.errorCode === COMPANY_ACCESS_DENIED) {
          // 清除選擇後攔截器不再帶 X-Company-Id，後端改取預設公司
          this.company.clear();
          return this.http.get<CurrentAuthorization>(AUTHORIZATION_API);
        }
        throw error;
      }),
    );
  }
}

/** 進入主框架前載入權限（只載入一次；失敗時以空權限繼續，畫面只顯示不需權限的項目）。 */
export const authorizationGuard: CanActivateFn = () => {
  const permissions = inject(PermissionService);
  return permissions.loaded() ? true : permissions.load().pipe(map(() => true));
};

/** 路由權限：沒有指定權限時導向 403 頁（後端仍會檢查）。用法：`canActivate: [permissionGuard('SYS.ROLE.VIEW')]`。 */
export function permissionGuard(...codes: string[]): CanActivateFn {
  return () => {
    const permissions = inject(PermissionService);
    const router = inject(Router);
    const decide = () => (permissions.hasAny(codes) ? true : router.createUrlTree(['/forbidden']));
    return permissions.loaded() ? decide() : permissions.load().pipe(map(decide));
  };
}
