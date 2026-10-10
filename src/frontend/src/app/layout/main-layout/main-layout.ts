import { BreakpointObserver } from '@angular/cdk/layout';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
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
import { CompanyContextService } from '../../core/company/company-context.service';
import { LanguageService } from '../../core/i18n/language.service';
import { ErpLanguage } from '../../core/i18n/languages';
import { THEME_MODES, ThemeMode, ThemeService } from '../../core/theme/theme.service';
import { filterMenu, MAIN_MENU } from '../menu';

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
  private readonly router = inject(Router);

  protected readonly collapsed = signal(false);
  protected readonly loggingOut = signal(false);
  // TODO(WU-SYS-004)：改為 PermissionService.has
  protected readonly menu = filterMenu(MAIN_MENU, () => true);
  protected readonly themeModes = THEME_MODES;
  protected readonly themeIcon = computed(() => THEME_ICONS[this.theme.mode()]);
  protected readonly themeIcons = THEME_ICONS;

  constructor() {
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
    this.company.select(companyId);
  }

  protected logout(): void {
    this.loggingOut.set(true);
    this.auth.logout().subscribe(() => {
      this.loggingOut.set(false);
      void this.router.navigateByUrl('/login');
    });
  }
}
