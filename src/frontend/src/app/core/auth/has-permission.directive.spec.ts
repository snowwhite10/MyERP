// REQ_F4118.25.7：無權限的按鈕不顯示；權限改變（例如切換公司）時畫面同步。
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { seedSession } from '../../../testing/auth-fixtures';
import { HasPermissionDirective } from './has-permission.directive';
import { AUTHORIZATION_API, PermissionService } from './permission.service';

@Component({
  imports: [HasPermissionDirective],
  template: `
    <button *erpHasPermission="'SYS.ROLE.CREATE'" data-testid="create">Create</button>
    <button *erpHasPermission="anyOf()" data-testid="any">Any</button>
  `,
})
class Host {
  readonly anyOf = signal<readonly string[]>(['SYS.ROLE.EDIT', 'SYS.ROLE.DELETE']);
}

describe('HasPermissionDirective', () => {
  let fixture: ComponentFixture<Host>;
  let controller: HttpTestingController;
  let permissions: PermissionService;

  function shown(testId: string): boolean {
    return fixture.nativeElement.querySelector(`[data-testid="${testId}"]`) !== null;
  }

  async function grant(codes: string[]): Promise<void> {
    const done = firstValueFrom(permissions.load());
    controller.expectOne(AUTHORIZATION_API).flush({
      companies: [],
      companyId: 1,
      isFullAccess: false,
      permissions: codes.map((code) => ({ code, dataScope: 'Company' })),
    });
    await done;
    fixture.detectChanges();
    await fixture.whenStable();
  }

  beforeEach(async () => {
    localStorage.clear();
    seedSession();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    controller = TestBed.inject(HttpTestingController);
    permissions = TestBed.inject(PermissionService);
    fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    await fixture.whenStable();
  });

  afterEach(() => {
    controller.verify();
    localStorage.clear();
  });

  it('hides content until a matching permission is granted', async () => {
    expect(shown('create')).toBe(false);
    expect(shown('any')).toBe(false);

    await grant(['SYS.ROLE.CREATE', 'SYS.ROLE.DELETE']);
    expect(shown('create')).toBe(true);
    expect(shown('any')).toBe(true);
  });

  it('removes content when permissions are withdrawn and reacts to input changes', async () => {
    await grant(['SYS.ROLE.CREATE']);
    expect(shown('create')).toBe(true);
    expect(shown('any')).toBe(false);

    fixture.componentInstance.anyOf.set(['SYS.ROLE.CREATE']);
    fixture.detectChanges();
    await fixture.whenStable();
    expect(shown('any')).toBe(true);

    await grant([]);
    expect(shown('create')).toBe(false);
    expect(shown('any')).toBe(false);
    // 再授權不會重複渲染
    await grant(['SYS.ROLE.CREATE']);
    expect(fixture.nativeElement.querySelectorAll('[data-testid="create"]').length).toBe(1);
  });
});
