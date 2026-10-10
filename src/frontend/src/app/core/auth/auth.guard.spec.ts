// REQ_F4118.24.1：未登入不可進入主框架；登入後的 returnUrl 只接受站內路徑。
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { seedSession } from '../../../testing/auth-fixtures';
import { authGuard, guestGuard, safeReturnUrl } from './auth.guard';

@Component({ template: '' })
class Blank {}

describe('auth guards', () => {
  let router: Router;

  function setup(): void {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([
          { path: 'login', component: Blank, canActivate: [guestGuard] },
          { path: '', component: Blank, canActivate: [authGuard], children: [{ path: '**', component: Blank }] },
        ]),
      ],
    });
    router = TestBed.inject(Router);
  }

  beforeEach(() => localStorage.clear());
  afterEach(() => localStorage.clear());

  it('redirects signed-out users to login with the requested URL', async () => {
    setup();
    await router.navigateByUrl('/sys/users?page=2');
    expect(router.url).toBe('/login?returnUrl=%2Fsys%2Fusers%3Fpage%3D2');
  });

  it('redirects signed-out users from home to login without returnUrl', async () => {
    setup();
    await router.navigateByUrl('/');
    expect(router.url).toBe('/login');
  });

  it('lets signed-in users through', async () => {
    seedSession();
    setup();
    await router.navigateByUrl('/sys/users');
    expect(router.url).toBe('/sys/users');
  });

  it('sends signed-in users away from the login page', async () => {
    seedSession();
    setup();
    await router.navigateByUrl('/login');
    expect(router.url).toBe('/');
  });

  it('shows the login page to signed-out users', async () => {
    setup();
    await router.navigateByUrl('/login');
    expect(router.url).toBe('/login');
  });

  it.each([
    [undefined, '/'],
    [null, '/'],
    ['', '/'],
    ['/sys/users?page=2', '/sys/users?page=2'],
    ['https://evil.example', '/'],
    ['//evil.example', '/'],
    ['/\\evil.example', '/'],
    ['javascript:alert(1)', '/'],
    ['/login?returnUrl=%2F', '/'],
  ])('safeReturnUrl(%s) = %s', (value, expected) => {
    expect(safeReturnUrl(value)).toBe(expected);
  });
});
