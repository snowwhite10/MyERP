import { ChangeDetectionStrategy, Component } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';

@Component({
  selector: 'erp-master-data-home',
  imports: [TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<main class="md-home"><h1>{{ 'md.home.title' | translate }}</h1><p>{{ 'md.home.description' | translate }}</p></main>`,
  styles: [`.md-home { padding: 24px; }`],
})
export class MasterDataHome {}
