import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzTableModule, NzTableQueryParams } from 'ng-zorro-antd/table';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { CompanyContextService } from '../../../core/company/company-context.service';
import { PermissionService } from '../../../core/auth/permission.service';
import { displayTime } from '../../../shared/date';
import { NotificationItem, notificationCategoryKey, NotificationService } from './notification.service';

/** 列表的一列：載入時先算好類別翻譯 key 與顯示時間，範本不必每次變更偵測都重算。 */
type NotificationRow = NotificationItem & { categoryKey: string | null; time: string };

function toRow(item: NotificationItem): NotificationRow {
  return { ...item, categoryKey: notificationCategoryKey(item.category), time: displayTime(item.createdAt) };
}

@Component({
  selector: 'erp-notification-inbox',
  imports: [RouterLink, TranslatePipe, NzButtonModule, NzTableModule, NzTagModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="erp-notifications">
      <h1 data-testid="sys-notifications-title">{{ 'sys.notifications.title' | translate }}</h1>
      <div class="erp-notifications__actions">
        <button nz-button data-testid="sys-notifications-filter" (click)="toggleUnread()">
          {{ (unreadOnly() ? 'sys.notifications.showAll' : 'sys.notifications.unreadOnly') | translate }}
        </button>
        @if (canRead()) {
          <button nz-button data-testid="sys-notifications-read-all" [disabled]="busy() || notifications.unreadCount() === 0" (click)="markAllRead()">
            {{ 'sys.notifications.readAll' | translate }}
          </button>
        }
      </div>
      @if (error()) { <p role="alert" data-testid="sys-notifications-error">{{ 'sys.notifications.error' | translate }}</p> }
      <nz-table #table data-testid="sys-notifications-table" [nzData]="items()" [nzLoading]="loading()"
        [nzFrontPagination]="false" [nzTotal]="total()" [nzPageIndex]="page()" [nzPageSize]="pageSize()"
        [nzNoResult]="'sys.notifications.empty' | translate" (nzQueryParams)="query($event)">
        <thead><tr><th>{{ 'sys.notifications.category' | translate }}</th><th>{{ 'sys.notifications.message' | translate }}</th>
          <th>{{ 'sys.notifications.createdAt' | translate }}</th><th>{{ 'sys.notifications.status' | translate }}</th>
          <th>{{ 'sys.notifications.actions' | translate }}</th></tr></thead>
        <tbody>
          @for (item of table.data; track item.id) {
            <tr [attr.data-testid]="'sys-notification-' + item.id">
              <td>{{ item.categoryKey ? (item.categoryKey | translate) : item.category }}</td>
              <td><strong>{{ item.title }}</strong><br>{{ item.body }}</td>
              <td>{{ item.time }}</td>
              <td><nz-tag [nzColor]="item.readAt ? 'default' : 'blue'">{{ (item.readAt ? 'sys.notifications.read' : 'sys.notifications.unread') | translate }}</nz-tag></td>
              <td>
                @if (item.targetPath) { <a [routerLink]="item.targetPath">{{ 'sys.notifications.open' | translate }}</a> }
                @if (!item.readAt && canRead()) { <button nz-button nzType="link" [disabled]="busy()" (click)="markRead(item)">{{ 'sys.notifications.markRead' | translate }}</button> }
              </td>
            </tr>
          }
        </tbody>
      </nz-table>
    </section>
  `,
  styles: [`.erp-notifications__actions { display: flex; gap: 8px; margin: 12px 0; }`],
})
export class NotificationInbox {
  protected readonly notifications = inject(NotificationService);
  private readonly company = inject(CompanyContextService);
  private readonly permissions = inject(PermissionService);
  protected readonly canRead = computed(() => this.permissions.has('SYS.NOTIFICATION.READ'));
  protected readonly items = signal<NotificationRow[]>([]);
  protected readonly loading = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal(false);
  protected readonly page = signal(1);
  protected readonly pageSize = signal(20);
  protected readonly total = signal(0);
  protected readonly unreadOnly = signal(false);
  private requestNumber = 0;
  private readonly companyId = computed(() => this.company.current()?.id ?? null);

  constructor() {
    // 只在公司 ID 改變時回到第一頁並重算未讀數（同一公司的物件更新不重新查詢）
    effect(() => {
      this.companyId();
      untracked(() => {
        this.page.set(1);
        this.load(true);
      });
    });
  }

  protected query(params: NzTableQueryParams): void {
    if (params.pageIndex === this.page() && params.pageSize === this.pageSize()) return;
    this.page.set(params.pageIndex);
    this.pageSize.set(params.pageSize);
    this.load();
  }

  protected toggleUnread(): void {
    this.unreadOnly.update((value) => !value);
    this.page.set(1);
    this.load();
  }

  /** 翻頁與篩選不影響未讀數；只有切換公司或標記已讀後（`refreshCount`）才重算。 */
  protected load(refreshCount = false): void {
    const request = ++this.requestNumber;
    this.loading.set(true);
    this.error.set(false);
    this.notifications.list(this.page(), this.pageSize(), this.unreadOnly()).subscribe({
      next: (result) => {
        if (request !== this.requestNumber) return;
        this.items.set(result.items.map(toRow));
        this.total.set(result.totalCount);
        this.loading.set(false);
      },
      error: () => {
        if (request !== this.requestNumber) return;
        this.error.set(true);
        this.loading.set(false);
      },
    });
    if (refreshCount) this.notifications.refreshCount();
  }

  protected markRead(item: NotificationItem): void {
    this.busy.set(true);
    this.notifications.markRead(item.id).subscribe({
      next: () => {
        this.busy.set(false);
        this.load(true);
      },
      error: () => {
        this.busy.set(false);
        this.error.set(true);
      },
    });
  }

  protected markAllRead(): void {
    this.busy.set(true);
    this.notifications.markAllRead().subscribe({
      next: () => {
        this.busy.set(false);
        this.load(true);
      },
      error: () => {
        this.busy.set(false);
        this.error.set(true);
      },
    });
  }
}
