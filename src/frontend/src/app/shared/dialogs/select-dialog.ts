import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NZ_MODAL_DATA, NzModalRef } from 'ng-zorro-antd/modal';
import { NzTableModule, NzTableQueryParams } from 'ng-zorro-antd/table';
import { Observable, Subscription } from 'rxjs';
import { apiErrorKey, apiErrorTraceId } from '../../core/http/api-error';
import { PagedResult } from '../list-page/table-query';

/** 選擇視窗的查詢條件：第幾頁、每頁筆數、關鍵字（已去除前後空白）。 */
export interface SelectDialogQuery {
  page: number;
  pageSize: number;
  keyword: string;
}

export interface SelectDialogColumn<T> {
  /** 欄位代碼（用於 `data-testid`）。 */
  key: string;
  labelKey: string;
  value: (row: T) => string | number | null | undefined;
  width?: string;
}

/** `ErpDialogService.select()` 的選項。 */
export interface SelectDialogOptions<T> {
  titleKey: string;
  titleParams?: Record<string, unknown>;
  columns: readonly SelectDialogColumn<T>[];
  /** 伺服器端查詢；頁面傳入 `*.api.ts` 的方法（例如 `q => api.list({ ...q, isActive: true })`）。 */
  load: (query: SelectDialogQuery) => Observable<PagedResult<T>>;
  rowKey: (row: T) => string | number;
  /** 多選（預設單選）。 */
  multiple?: boolean;
  /** 開啟時已選取的資料（多選時跨頁保留）。 */
  selected?: readonly T[];
  searchPlaceholderKey?: string;
  pageSize?: number;
  /** 載入失敗時，頁面認得的錯誤代碼（見 `apiErrorKey`）。 */
  errorCodes?: readonly string[];
}

/**
 * 選擇視窗（`erp-select-dialog`，BlueWhale 4.4.1.9 Select Client / Supplier / Item Interface 等）：
 * 關鍵字搜尋、伺服器端分頁；單選時按列即選取，按兩下直接確定；多選時以勾選框跨頁累積。
 * 確定回傳選取的資料陣列，取消或關閉回傳 null（由 `ErpDialogService.select()` 處理）。
 */
@Component({
  selector: 'erp-select-dialog',
  imports: [FormsModule, TranslatePipe, NzAlertModule, NzButtonModule, NzInputModule, NzTableModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="erp-select" data-testid="erp-select-dialog">
      <h2 class="erp-select__title" data-testid="erp-select-title">{{ options.titleKey | translate: options.titleParams }}</h2>
      <div class="erp-select__search">
        <input nz-input type="search" data-testid="erp-select-keyword" [attr.aria-label]="searchKey | translate"
          [placeholder]="searchKey | translate" [ngModel]="keyword()" (ngModelChange)="keyword.set($event)"
          (keyup.enter)="search()" />
        <button nz-button type="button" data-testid="erp-select-search" (click)="search()">
          {{ 'common.action.search' | translate }}
        </button>
      </div>
      @if (loadError()) {
        <nz-alert nzType="error" nzShowIcon data-testid="erp-select-error" [nzMessage]="loadError()! | translate" />
        @if (traceId()) { <span data-testid="erp-select-trace">Trace ID: {{ traceId() }}</span> }
      }
      <nz-table #table data-testid="erp-select-table" nzSize="small" [nzData]="rows()" [nzLoading]="loading()"
        [nzFrontPagination]="false" [nzTotal]="total()" [nzPageIndex]="page()" [nzPageSize]="pageSize()"
        [nzNoResult]="'common.state.empty' | translate" (nzQueryParams)="onQueryParams($event)">
        <thead>
          <tr>
            <th nzWidth="48px">
              @if (options.multiple) {
                <span class="erp-select__sr">{{ 'common.select.select' | translate }}</span>
              }
            </th>
            @for (column of options.columns; track column.key) {
              <th [nzWidth]="column.width ?? null">{{ column.labelKey | translate }}</th>
            }
          </tr>
        </thead>
        <tbody>
          @for (row of table.data; track options.rowKey(row)) {
            <tr class="erp-select__row" [class.erp-select__row--selected]="isSelected(row)"
              [attr.data-testid]="'erp-select-row-' + options.rowKey(row)" [attr.aria-selected]="isSelected(row)"
              tabindex="0" (click)="toggle(row)" (dblclick)="pick(row)"
              (keydown.enter)="onRowKey(row, $event)" (keydown.space)="onRowKey(row, $event)">
              <td [nzChecked]="isSelected(row)" [nzLabel]="'common.select.select' | translate"
                (nzCheckedChange)="setChecked(row, $event)" (click)="$event.stopPropagation()"></td>
              @for (column of options.columns; track column.key) {
                <td [attr.data-testid]="'erp-select-cell-' + options.rowKey(row) + '-' + column.key">{{ column.value(row) }}</td>
              }
            </tr>
          }
        </tbody>
      </nz-table>
      <div class="erp-select__footer">
        <span data-testid="erp-select-count">{{ 'common.select.selectedCount' | translate: { count: selected().size } }}</span>
        <button nz-button type="button" data-testid="erp-select-cancel" (click)="cancel()">
          {{ 'common.action.cancel' | translate }}
        </button>
        <button nz-button nzType="primary" type="button" data-testid="erp-select-confirm" [disabled]="!canConfirm()"
          (click)="confirm()">
          {{ 'common.action.confirm' | translate }}
        </button>
      </div>
    </div>
  `,
  styles: `
    .erp-select { display: grid; gap: 12px; color: var(--erp-color-text); }
    .erp-select__title { margin: 0; font-size: 16px; font-weight: 600; }
    .erp-select__search { display: flex; gap: 8px; }
    .erp-select__search input { max-width: 320px; }
    .erp-select__row { cursor: pointer; }
    .erp-select__row:focus-visible { outline: 2px solid var(--erp-color-primary); outline-offset: -2px; }
    .erp-select__footer { display: flex; align-items: center; justify-content: flex-end; gap: 8px; }
    .erp-select__footer span { margin-inline-end: auto; color: var(--erp-color-text-secondary); }
    .erp-select__sr { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0); }
  `,
})
export class SelectDialog<T> implements OnInit {
  readonly options = inject<SelectDialogOptions<T>>(NZ_MODAL_DATA);
  private readonly ref = inject<NzModalRef<SelectDialog<T>, T[]>>(NzModalRef);
  private readonly destroyRef = inject(DestroyRef);

  readonly searchKey = this.options.searchPlaceholderKey ?? 'common.select.searchPlaceholder';

  readonly keyword = signal('');
  readonly rows = signal<T[]>([]);
  readonly total = signal(0);
  readonly page = signal(1);
  readonly pageSize = signal(this.options.pageSize ?? 10);
  readonly loading = signal(false);
  readonly loadError = signal<string | null>(null);
  readonly traceId = signal<string | null>(null);
  /** 已選取（以 rowKey 為鍵，多選時跨頁保留）。 */
  readonly selected = signal<ReadonlyMap<string | number, T>>(
    new Map((this.options.selected ?? []).map((row) => [this.options.rowKey(row), row])),
  );
  readonly selectedRows = computed(() => [...this.selected().values()]);
  canConfirm(): boolean { return this.selected().size > 0 || !!(this.options.multiple && this.options.selected?.length); }

  private request?: Subscription;

  ngOnInit(): void {
    this.reload();
  }

  isSelected(row: T): boolean {
    return this.selected().has(this.options.rowKey(row));
  }

  onRowKey(row: T, event: Event): void {
    if (event.target !== event.currentTarget) return;
    event.preventDefault();
    this.toggle(row);
  }

  /** 單選：選取此列（再按一次不取消，避免誤觸）；多選：切換此列。 */
  toggle(row: T): void {
    const key = this.options.rowKey(row);
    if (!this.options.multiple) {
      this.selected.set(new Map([[key, row]]));
      return;
    }
    const next = new Map(this.selected());
    if (next.has(key)) next.delete(key);
    else next.set(key, row);
    this.selected.set(next);
  }

  /** Checkbox also emits when the row updates its checked state; apply the requested state idempotently. */
  setChecked(row: T, checked: boolean): void {
    if (!this.options.multiple) { if (checked) this.toggle(row); return; }
    const next = new Map(this.selected());
    if (checked) next.set(this.options.rowKey(row), row);
    else next.delete(this.options.rowKey(row));
    this.selected.set(next);
  }

  /** 按兩下：單選直接確定；多選時等同勾選。 */
  pick(row: T): void {
    if (this.options.multiple) return;
    this.selected.set(new Map([[this.options.rowKey(row), row]]));
    this.confirm();
  }

  search(): void {
    this.page.set(1);
    this.reload();
  }

  onQueryParams(params: NzTableQueryParams): void {
    if (params.pageIndex === this.page() && params.pageSize === this.pageSize()) return;
    this.page.set(params.pageIndex);
    this.pageSize.set(params.pageSize);
    this.reload();
  }

  reload(): void {
    // 只保留最後一次查詢的結果（快速換頁或連按搜尋時）
    this.request?.unsubscribe();
    this.loading.set(true);
    this.loadError.set(null);
    this.traceId.set(null);
    this.request = this.options
      .load({ page: this.page(), pageSize: this.pageSize(), keyword: this.keyword().trim() })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (result) => {
          this.rows.set(result.items);
          this.total.set(result.totalCount);
          this.loading.set(false);
        },
        error: (error: unknown) => {
          this.rows.set([]);
          this.total.set(0);
          this.loadError.set(apiErrorKey(error, this.options.errorCodes ?? []));
          this.traceId.set(apiErrorTraceId(error));
          this.loading.set(false);
        },
      });
  }

  confirm(): void {
    if (!this.canConfirm()) return;
    this.ref.close(this.selectedRows());
  }

  cancel(): void {
    this.ref.close();
  }
}
