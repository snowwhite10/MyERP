import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable, signal } from '@angular/core';
import { Observable } from 'rxjs';
import { CompanyContextService } from '../../../core/company/company-context.service';
import { PagedResult } from '../../../shared/list-page/table-query';

export interface NotificationItem {
  id: number;
  category: string;
  title: string;
  body: string;
  targetPath: string | null;
  createdAt: string;
  readAt: string | null;
}

const API = '/api/v1/sys/notifications';

/** 已有三語名稱的通知類別；其他類別（新模組尚未加入翻譯）顯示原始代碼。 */
export const NOTIFICATION_CATEGORIES = ['approval'] as const;

export function notificationCategoryKey(category: string): string | null {
  return (NOTIFICATION_CATEGORIES as readonly string[]).includes(category) ? `sys.notifications.categories.${category}` : null;
}

@Injectable({ providedIn: 'root' })
export class NotificationService {
  private readonly http = inject(HttpClient);
  private readonly company = inject(CompanyContextService);
  readonly unreadCount = signal(0);
  private countRequest = 0;

  list(page: number, pageSize: number, unreadOnly: boolean): Observable<PagedResult<NotificationItem>> {
    return this.http.get<PagedResult<NotificationItem>>(API, {
      params: new HttpParams().set('page', page).set('pageSize', pageSize).set('unreadOnly', unreadOnly),
    });
  }

  refreshCount(): void {
    const request = ++this.countRequest;
    const companyId = this.company.current()?.id;
    if (!companyId) { this.unreadCount.set(0); return; }
    this.http.get<{ count: number }>(`${API}/unread-count`).subscribe({
      next: ({ count }) => { if (request === this.countRequest && this.company.current()?.id === companyId) this.unreadCount.set(count); },
      error: () => { if (request === this.countRequest && this.company.current()?.id === companyId) this.unreadCount.set(0); },
    });
  }

  markRead(id: number): Observable<void> { return this.http.post<void>(`${API}/${id}/read`, {}); }
  markAllRead(): Observable<{ updatedCount: number }> { return this.http.post<{ updatedCount: number }>(`${API}/read-all`, {}); }
}
