// 介面 8.76（REQ_F4118.33.2–3）：通知 API 服務、未讀數與收件箱頁。
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting, TestRequest } from '@angular/common/http/testing';
import { ANIMATION_MODULE_TYPE } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideNzIcons } from 'ng-zorro-antd/icon';
import { firstValueFrom } from 'rxjs';
import { seedSession } from '../../../../testing/auth-fixtures';
import { provideTestTranslations } from '../../../../testing/i18n-fixtures';
import { AUTHORIZATION_API, PermissionService } from '../../../core/auth/permission.service';
import { CompanyContextService } from '../../../core/company/company-context.service';
import { APP_ICONS } from '../../../core/icons';
import { NotificationInbox } from './notification-inbox';
import { NotificationItem, NotificationService } from './notification.service';

const API = '/api/v1/sys/notifications';
const item = (overrides: Partial<NotificationItem> = {}): NotificationItem => ({
  id: 1, category: 'approval', title: 'Review order', body: 'Order 42 needs approval', targetPath: '/sys/approvals',
  createdAt: '2026-10-11T19:00:00', readAt: null, ...overrides,
});

describe('SYS notifications', () => {
  let controller: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    seedSession();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), ...provideTestTranslations('en'),
        provideNzIcons(APP_ICONS), { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' }],
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
      companyId: 1, isFullAccess: false, permissions: codes.map((code) => ({ code, dataScope: 'Company' })),
    });
    await done;
  }
  const expectList = (): TestRequest => controller.expectOne((r) => r.method === 'GET' && r.url === API);
  const expectCount = (): TestRequest => controller.expectOne(`${API}/unread-count`);

  describe('service', () => {
    it('sends paging and unread filter and posts read actions', () => {
      const api = TestBed.inject(NotificationService);
      api.list(2, 50, true).subscribe();
      const list = expectList();
      expect(list.request.params.get('page')).toBe('2');
      expect(list.request.params.get('pageSize')).toBe('50');
      expect(list.request.params.get('unreadOnly')).toBe('true');
      list.flush({ items: [], page: 2, pageSize: 50, totalCount: 0 });
      api.markRead(7).subscribe();
      controller.expectOne((r) => r.method === 'POST' && r.url === `${API}/7/read`).flush(null);
      let updated = -1;
      api.markAllRead().subscribe((r) => (updated = r.updatedCount));
      controller.expectOne((r) => r.method === 'POST' && r.url === `${API}/read-all`).flush({ updatedCount: 3 });
      expect(updated).toBe(3);
    });

    it('keeps only the latest unread count for the current company', async () => {
      const api = TestBed.inject(NotificationService);
      api.refreshCount();
      expect(api.unreadCount()).toBe(0);
      controller.expectNone(`${API}/unread-count`);

      await grant(['SYS.NOTIFICATION.VIEW']);
      api.refreshCount();
      const slow = expectCount();
      api.refreshCount();
      expectCount().flush({ count: 4 });
      slow.flush({ count: 9 });
      expect(api.unreadCount()).toBe(4);

      api.refreshCount();
      TestBed.inject(CompanyContextService).select(2);
      expectCount().flush({ count: 5 });
      expect(api.unreadCount()).toBe(4);

      api.refreshCount();
      expectCount().flush({}, { status: 500, statusText: 'Error' });
      expect(api.unreadCount()).toBe(0);
    });
  });

  describe('inbox', () => {
    let fixture: ComponentFixture<NotificationInbox>;
    const byTestId = (id: string): HTMLElement | null => fixture.nativeElement.querySelector(`[data-testid="${id}"]`);

    async function settle(): Promise<void> {
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();
    }

    async function open(codes: string[], items: NotificationItem[], unread: number): Promise<void> {
      await grant(codes);
      fixture = TestBed.createComponent(NotificationInbox);
      await settle();
      respond(items, unread);
      await settle();
    }

    function respond(items: NotificationItem[], unread: number): void {
      expectList().flush({ items, page: 1, pageSize: 20, totalCount: items.length });
      expectCount().flush({ count: unread });
    }

    it('lists notifications and marks one or all as read', async () => {
      await open(['SYS.NOTIFICATION.VIEW', 'SYS.NOTIFICATION.READ'], [item(), item({ id: 2, title: 'Done', targetPath: null, readAt: '2026-10-11T19:05:00' })], 1);
      expect(byTestId('sys-notifications-title')?.textContent).toContain('Notifications');
      const first = byTestId('sys-notification-1')!;
      expect(first.textContent).toContain('Review order');
      expect(first.textContent).toContain('2026-10-11 19:00:00');
      expect(first.textContent).toContain('Unread');
      expect(first.querySelector('a')?.getAttribute('href')).toBe('/sys/approvals');
      const second = byTestId('sys-notification-2')!;
      expect(second.textContent).toContain('Read');
      expect(second.querySelector('a, button')).toBeNull();

      (first.querySelector('button') as HTMLButtonElement).click();
      controller.expectOne(`${API}/1/read`).flush(null);
      respond([item({ readAt: '2026-10-11T19:06:00' })], 0);
      await settle();
      expect(TestBed.inject(NotificationService).unreadCount()).toBe(0);
      expect((byTestId('sys-notifications-read-all') as HTMLButtonElement).disabled).toBe(true);
    });

    it('marks all as read, filters unread and shows errors', async () => {
      await open(['SYS.NOTIFICATION.VIEW', 'SYS.NOTIFICATION.READ'], [item()], 1);
      byTestId('sys-notifications-read-all')!.click();
      controller.expectOne(`${API}/read-all`).flush({ updatedCount: 1 });
      respond([], 0);
      await settle();

      byTestId('sys-notifications-filter')!.click();
      const filtered = expectList();
      expect(filtered.request.params.get('unreadOnly')).toBe('true');
      filtered.flush({}, { status: 500, statusText: 'Error' });
      expectCount().flush({ count: 0 });
      await settle();
      expect(byTestId('sys-notifications-error')).not.toBeNull();
      expect(byTestId('sys-notifications-filter')?.textContent).toContain('Show all');
    });

    it('hides read actions without READ and reloads after switching company', async () => {
      await open(['SYS.NOTIFICATION.VIEW'], [item()], 1);
      expect(byTestId('sys-notifications-read-all')).toBeNull();
      expect(byTestId('sys-notification-1')!.querySelector('button')).toBeNull();

      TestBed.inject(CompanyContextService).select(2);
      await settle();
      const reload = expectList();
      expect(reload.request.params.get('page')).toBe('1');
      reload.flush({ items: [], page: 1, pageSize: 20, totalCount: 0 });
      expectCount().flush({ count: 0 });
      await settle();
      expect(byTestId('sys-notifications-table')?.textContent).toContain('No notifications');
    });
  });
});
