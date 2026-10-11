import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzCardModule } from 'ng-zorro-antd/card';
import { NzSelectModule } from 'ng-zorro-antd/select';
import { LanguageService } from '../../core/i18n/language.service';
import { ErpLanguage } from '../../core/i18n/languages';
import { THEME_MODES, ThemeMode, ThemeService } from '../../core/theme/theme.service';

/** 個人設定：語言與主題由各自服務保存於瀏覽器，重開後沿用。 */
@Component({
  selector: 'erp-personal-settings-page',
  imports: [FormsModule, RouterLink, TranslatePipe, NzAlertModule, NzButtonModule, NzCardModule, NzSelectModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <main class="erp-personal" data-testid="personal-settings-page">
      <h1>{{ 'sys.personal.title' | translate }}</h1>
      <nz-card [nzBordered]="false">
        <p>{{ 'sys.personal.description' | translate }}</p>
        <label for="personal-language">{{ 'sys.personal.language' | translate }}</label>
        <nz-select id="personal-language" data-testid="personal-language" [ngModel]="language.current()" (ngModelChange)="changeLanguage($event)">
          @for (option of language.languages; track option.code) { <nz-option [nzValue]="option.code" [nzLabel]="option.nativeName" /> }
        </nz-select>
        <label for="personal-theme">{{ 'sys.personal.theme' | translate }}</label>
        <nz-select id="personal-theme" data-testid="personal-theme" [ngModel]="theme.mode()" (ngModelChange)="changeTheme($event)">
          @for (mode of modes; track mode) { <nz-option [nzValue]="mode" [nzLabel]="('common.theme.' + mode) | translate" /> }
        </nz-select>
        @if (message()) { <nz-alert nzType="success" nzShowIcon [nzMessage]="'sys.personal.saved' | translate" data-testid="personal-saved" /> }
        <a routerLink="/">{{ 'sys.personal.back' | translate }}</a>
      </nz-card>
    </main>
  `,
  styles: `
    .erp-personal { max-width: 640px; margin: 0 auto; }
    h1 { color: var(--erp-color-text); }
    label { display: block; margin: 18px 0 6px; }
    nz-select { display: block; width: 100%; }
    nz-alert { display: block; margin: 20px 0; }
    a { display: inline-block; margin-top: 24px; }
  `,
})
export class PersonalSettingsPage {
  readonly language = inject(LanguageService);
  readonly theme = inject(ThemeService);
  readonly modes = THEME_MODES;
  readonly message = signal(false);

  changeLanguage(code: ErpLanguage): void {
    this.message.set(false);
    this.language.use(code).subscribe({ next: () => this.message.set(true) });
  }

  changeTheme(mode: ThemeMode): void {
    this.theme.setMode(mode);
    this.message.set(true);
  }
}
