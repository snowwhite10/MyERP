import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';

export type OrganizationKind = 'company' | 'branch' | 'department';

export interface OrganizationRow {
  id: number;
  companyId: number;
  code: string;
  name: string;
}

/** Local list shell. An authorized API adapter will supply only permitted rows. */
@Component({
  selector: 'erp-organization-list',
  imports: [TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="erp-organization">
      <h1>{{ ('sys.organization.' + kind() + '.title') | translate }}</h1>
      <label for="organization-search">{{ 'sys.organization.search' | translate }}</label>
      <input id="organization-search" data-testid="sys-organization-search" type="search"
        [value]="search()" (input)="search.set($any($event.target).value); page.set(1)" />
      <div class="erp-organization__scroll">
        <table>
          <thead><tr><th scope="col">{{ 'sys.organization.code' | translate }}</th>
            <th scope="col">{{ 'sys.organization.name' | translate }}</th></tr></thead>
          <tbody>
            @for (row of visibleRows(); track row.id) {
              <tr><td>{{ row.code }}</td><td>{{ row.name }}</td></tr>
            } @empty {
              <tr><td colspan="2">{{ 'sys.organization.empty' | translate }}</td></tr>
            }
          </tbody>
        </table>
      </div>
      <nav [attr.aria-label]="'sys.organization.pagination' | translate">
        <button type="button" [disabled]="page() <= 1" (click)="page.set(page() - 1)">
          {{ 'sys.organization.previous' | translate }}
        </button>
        <span>{{ page() }} / {{ pageCount() }}</span>
        <button type="button" [disabled]="page() >= pageCount()" (click)="page.set(page() + 1)">
          {{ 'sys.organization.next' | translate }}
        </button>
      </nav>
    </section>
  `,
  styles: `
    .erp-organization { display: grid; gap: var(--erp-space-2, 16px); color: var(--erp-color-text); }
    .erp-organization h1 { margin: 0; font-size: 20px; }
    .erp-organization__scroll { overflow-x: auto; }
    table { width: 100%; border-collapse: collapse; }
    th, td { padding: 8px 16px; text-align: left; border-bottom: 1px solid var(--erp-color-border, #ddd); }
    nav { display: flex; align-items: center; justify-content: flex-end; gap: 16px; }
  `,
})
export class OrganizationList {
  readonly kind = input.required<OrganizationKind>();
  readonly companyId = input<number | null>(null);
  readonly rows = input<readonly OrganizationRow[]>([]);
  readonly search = signal('');
  readonly page = signal(1);
  readonly pageSize = 10;

  private readonly filtered = computed(() => {
    const companyId = this.companyId();
    const keyword = this.search().trim().toLocaleLowerCase();
    return this.rows()
      .filter((row) => this.kind() === 'company' || (companyId !== null && row.companyId === companyId))
      .filter((row) => !keyword || `${row.code} ${row.name}`.toLocaleLowerCase().includes(keyword))
      .sort((a, b) => a.code.localeCompare(b.code));
  });

  readonly pageCount = computed(() => Math.max(1, Math.ceil(this.filtered().length / this.pageSize)));
  readonly visibleRows = computed(() => {
    const page = Math.min(this.page(), this.pageCount());
    return this.filtered().slice((page - 1) * this.pageSize, page * this.pageSize);
  });
}
