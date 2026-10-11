import { BreakpointObserver } from '@angular/cdk/layout';
import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink, RouterOutlet } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzDropdownModule } from 'ng-zorro-antd/dropdown';
import { NzIconModule } from 'ng-zorro-antd/icon';
import { NzLayoutModule } from 'ng-zorro-antd/layout';
import { NzMenuModule } from 'ng-zorro-antd/menu';
import { NzSelectModule } from 'ng-zorro-antd/select';
import { NzTooltipModule } from 'ng-zorro-antd/tooltip';
import { AuthService } from '../../core/auth/auth.service';
import { PermissionService } from '../../core/auth/permission.service';
import { CompanyContextService } from '../../core/company/company-context.service';
import { LanguageService } from '../../core/i18n/language.service';
import { ErpLanguage } from '../../core/i18n/languages';
import { THEME_MODES, ThemeMode, ThemeService } from '../../core/theme/theme.service';
import { filterMenu, MAIN_MENU } from '../menu';
import { NotificationService } from '../../modules/sys/notifications/notification.service';

/** 平板以下（< 1200 px）選單預設收合（ui-conventions.md 第 2 節）。 */
export const COLLAPSE_BREAKPOINT = '(max-width: 1199.98px)';

const THEME_ICONS: Record<ThemeMode, string> = {
  light: 'sun',
  dark: 'moon',
  system: 'desktop',
};

/** 主框架：左側選單（可收合）＋頂列（公司切換、語言、主題）＋內容區。 */
@Component({
  selector: 'erp-main-layout',
  imports: [
    FormsModule,
    RouterOutlet,
    RouterLink,
    TranslatePipe,
    NzLayoutModule,
    NzMenuModule,
    NzIconModule,
    NzButtonModule,
    NzDropdownModule,
    NzSelectModule,
    NzTooltipModule,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './main-layout.html',
  styleUrl: './main-layout.scss',
})
export class MainLayout {
  protected readonly language = inject(LanguageService);
  protected readonly theme = inject(ThemeService);
  protected readonly company = inject(CompanyContextService);
  protected readonly auth = inject(AuthService);
  protected readonly notifications = inject(NotificationService);
  private readonly router = inject(Router);

  protected readonly collapsed = signal(false);
  protected readonly loggingOut = signal(false);
  private readonly permissions = inject(PermissionService);
  /** 無權限的選單項目不顯示；切換公司重新載入權限後自動更新。 */
  protected readonly menu = computed(() => filterMenu(MAIN_MENU, (code) => this.permissions.has(code)));
  protected readonly canViewNotifications = computed(() => this.permissions.has('SYS.NOTIFICATION.VIEW'));
  protected readonly themeModes = THEME_MODES;
  protected readonly themeIcon = computed(() => THEME_ICONS[this.theme.mode()]);
  protected readonly themeIcons = THEME_ICONS;

  constructor() {
    effect(() => {
      this.company.current();
      if (this.canViewNotifications()) untracked(() => this.notifications.refreshCount());
      else this.notifications.unreadCount.set(0);
    });
    inject(BreakpointObserver)
      .observe(COLLAPSE_BREAKPOINT)
      .pipe(takeUntilDestroyed())
      .subscribe((state) => this.collapsed.set(state.matches));
  }

  protected toggleCollapsed(): void {
    this.collapsed.update((value) => !value);
  }

  protected changeLanguage(code: ErpLanguage): void {
    this.language.use(code).subscribe();
  }

  protected changeTheme(mode: ThemeMode): void {
    this.theme.setMode(mode);
  }

  protected changeCompany(companyId: number): void {
    if (this.company.select(companyId)) {
      this.permissions.load().subscribe();
    }
  }

  protected logout(): void {
    this.loggingOut.set(true);
    this.notifications.unreadCount.set(0);
    this.auth.logout().subscribe(() => {
      this.loggingOut.set(false);
      // 先離開主框架再清除公司：清除公司會令仍開啟的列表頁（依公司重新查詢）在沒有 Session 時送出請求而得到 401
      void this.router.navigateByUrl('/login').finally(() => {
        this.permissions.clear();
        this.company.clear();
      });
    });
  }
}
