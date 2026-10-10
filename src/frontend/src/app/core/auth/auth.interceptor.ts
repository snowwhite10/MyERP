import { HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, switchMap, throwError } from 'rxjs';
import { isApiRequest } from '../http/api-headers.interceptor';
import { ANONYMOUS_AUTH_PATHS, AuthService } from './auth.service';

function pathOf(url: string): string {
  const path = url.replace(/^https?:\/\/[^/]+/i, '').split('?')[0];
  return path.startsWith('/') ? path : `/${path}`;
}

export function isAnonymousAuthRequest(url: string): boolean {
  return ANONYMOUS_AUTH_PATHS.includes(pathOf(url));
}

function withBearer(req: HttpRequest<unknown>, token: string): HttpRequest<unknown> {
  return req.clone({ setHeaders: { Authorization: `Bearer ${token}` } });
}

/**
 * API 請求加 `Authorization: Bearer`；收到 401 時以 Refresh Token 刷新一次並重送，
 * 刷新失敗則清除 Session 並導向登入頁（保留 returnUrl）。匿名端點（captcha / login / refresh）不處理。
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  if (!isApiRequest(req.url) || isAnonymousAuthRequest(req.url)) {
    return next(req);
  }
  const auth = inject(AuthService);
  const router = inject(Router);
  const token = auth.accessToken();
  if (!token) {
    return next(req);
  }

  return next(withBearer(req, token)).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse) || error.status !== 401) {
        return throwError(() => error);
      }
      return auth.refresh().pipe(
        catchError((refreshError: unknown) => {
          const returnUrl = router.url;
          void router.navigate(['/login'], {
            queryParams: returnUrl && returnUrl !== '/' && !returnUrl.startsWith('/login') ? { returnUrl } : {},
          });
          return throwError(() => refreshError);
        }),
        // 重送的錯誤（包括再次 401）直接傳出，不會再進入外層 catchError，因此最多刷新一次
        switchMap((session) => next(withBearer(req, session.accessToken))),
      );
    }),
  );
};
