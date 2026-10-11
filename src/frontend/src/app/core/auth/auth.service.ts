import { HttpClient } from '@angular/common/http';
import { computed, inject, Injectable, InjectionToken, signal } from '@angular/core';
import { catchError, finalize, map, Observable, of, shareReplay, tap, throwError } from 'rxjs';
import { PreferenceStorage } from '../storage/preference-storage';

export const AUTH_API = '/api/v1/sys/auth';

/** 不需（也不可）帶 Access Token 的匿名端點；401 時不觸發刷新。 */
export const ANONYMOUS_AUTH_PATHS = ['/captcha', '/login', '/refresh', '/change-expired-password'].map((p) => AUTH_API + p);

/** 登入狀態存放鍵（PreferenceStorage 會加上 `myerp.` 前綴）。 */
export const AUTH_SESSION_KEY = 'auth.session';

/** 目前時間（毫秒）。測試可替換；前端不受後端 IClock 規則約束，但集中一處方便控制。 */
export const AUTH_NOW = new InjectionToken<() => number>('AUTH_NOW', {
  providedIn: 'root',
  factory: () => () => Date.now(),
});

export interface CaptchaChallenge {
  captchaId: string;
  /** SVG data URI */
  image: string;
  length: number;
  expiresInSeconds: number;
}

export interface LoginCredentials {
  username: string;
  password: string;
  captchaId: string;
  captchaCode: string;
}

export interface ExpiredPasswordCredentials {
  username: string;
  currentPassword: string;
  newPassword: string;
  captchaId: string;
  captchaCode: string;
}

/** 後端 AuthTokens（時間為 ISO 8601，含 +08:00）。 */
export interface AuthTokens {
  accessToken: string;
  accessTokenExpiresAt: string;
  refreshToken: string;
  refreshTokenExpiresAt: string;
  tokenType: string;
}

export interface AuthSession extends AuthTokens {
  username: string;
}

function isSession(value: unknown): value is AuthSession {
  const v = value as Partial<AuthSession> | null;
  return (
    !!v &&
    typeof v.accessToken === 'string' &&
    typeof v.refreshToken === 'string' &&
    typeof v.username === 'string' &&
    typeof v.refreshTokenExpiresAt === 'string' &&
    !Number.isNaN(Date.parse(v.refreshTokenExpiresAt))
  );
}

/**
 * 登入狀態（REQ_F4118.24.1–3）：保存 Token、刷新（同時只發一個 refresh 請求）、登出。
 * Token 存 localStorage，讓多分頁共用登入；決策見 ADR-013。
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly storage = inject(PreferenceStorage);
  private readonly now = inject(AUTH_NOW);

  private readonly state = signal<AuthSession | null>(this.restore());
  private refreshing: Observable<AuthSession> | null = null;

  readonly session = this.state.asReadonly();
  readonly username = computed(() => this.state()?.username ?? null);

  /** 有 Session 且 Refresh Token 未過期；Access Token 過期由攔截器以刷新處理。 */
  isAuthenticated(): boolean {
    const session = this.state();
    if (!session) return false;
    if (Date.parse(session.refreshTokenExpiresAt) <= this.now()) {
      this.clear();
      return false;
    }
    return true;
  }

  accessToken(): string | null {
    return this.state()?.accessToken ?? null;
  }

  captcha(): Observable<CaptchaChallenge> {
    return this.http.get<CaptchaChallenge>(`${AUTH_API}/captcha`);
  }

  login(credentials: LoginCredentials): Observable<AuthSession> {
    const username = credentials.username.trim();
    return this.http
      .post<AuthTokens>(`${AUTH_API}/login`, { ...credentials, username })
      .pipe(map((tokens) => this.store(tokens, username)));
  }

  changePassword(currentPassword: string, newPassword: string): Observable<AuthSession> {
    const session = this.state();
    if (!session) return throwError(() => new Error('No session'));
    return this.http.post<AuthTokens>(`${AUTH_API}/change-password`, {
      currentPassword, newPassword, refreshToken: session.refreshToken,
    }).pipe(map((tokens) => this.store(tokens, session.username)));
  }

  changeExpiredPassword(credentials: ExpiredPasswordCredentials): Observable<AuthSession> {
    const username = credentials.username.trim();
    return this.http.post<AuthTokens>(`${AUTH_API}/change-expired-password`, { ...credentials, username })
      .pipe(map((tokens) => this.store(tokens, username)));
  }

  /** 以 Refresh Token 換新 Token；並發呼叫共用同一請求（後端輪替後舊 Token 即失效）。失敗即清除 Session。 */
  refresh(): Observable<AuthSession> {
    const session = this.state();
    if (!session) {
      return throwError(() => new Error('No session to refresh'));
    }
    this.refreshing ??= this.http
      .post<AuthTokens>(`${AUTH_API}/refresh`, { refreshToken: session.refreshToken })
      .pipe(
        map((tokens) => this.store(tokens, session.username)),
        catchError((error: unknown) => {
          this.clear();
          return throwError(() => error);
        }),
        finalize(() => (this.refreshing = null)),
        shareReplay({ bufferSize: 1, refCount: false }),
      );
    return this.refreshing;
  }

  /** 撤銷 Refresh Token 後清除本機 Session；伺服器失敗（例如已過期）也照樣登出。 */
  logout(): Observable<void> {
    const session = this.state();
    if (!session) return of(undefined);
    return this.http.post<void>(`${AUTH_API}/logout`, { refreshToken: session.refreshToken }).pipe(
      catchError(() => of(undefined)),
      tap(() => this.clear()),
      map(() => undefined),
    );
  }

  clear(): void {
    this.state.set(null);
    this.storage.remove(AUTH_SESSION_KEY);
  }

  private store(tokens: AuthTokens, username: string): AuthSession {
    const session: AuthSession = { ...tokens, username };
    this.state.set(session);
    this.storage.set(AUTH_SESSION_KEY, JSON.stringify(session));
    return session;
  }

  private restore(): AuthSession | null {
    const raw = this.storage.get(AUTH_SESSION_KEY);
    if (!raw) return null;
    try {
      const parsed: unknown = JSON.parse(raw);
      if (isSession(parsed)) return parsed;
    } catch {
      // 壞資料視同未登入
    }
    this.storage.remove(AUTH_SESSION_KEY);
    return null;
  }
}
