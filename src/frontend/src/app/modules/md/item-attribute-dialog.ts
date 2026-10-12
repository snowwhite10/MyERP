import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { FormArray, FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzModalModule } from 'ng-zorro-antd/modal';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { EMPTY, expand, Observable, reduce } from 'rxjs';
import { PermissionService } from '../../core/auth/permission.service';
import { latestRequest } from '../../shared/list-page/latest-request';
import { PagedResult } from '../../shared/list-page/table-query';
import {
  ItemAttribute, ItemAttributeService, ItemAttributeValue, ItemAttributeValueService,
  MAX_ATTRIBUTE_VALUE_LENGTH, MAX_ATTRIBUTES_PER_ITEM,
} from './item-attribute.service';
import { Item } from './item.service';

interface AttributeOption { id: number; code: string; name: string; sortOrder: number; isActive: boolean }
type ValueRow = FormGroup<{ attributeId: FormControl<number | null>; value: FormControl<string> }>;
/** 後端 400 的欄位鍵，例如 `attributes[1].value`。 */
const FIELD_KEY = /^attributes\[(\d+)\]\.(attributeId|value)$/;

/**
 * 物料屬性對話框（介面 8.16，REQ_F4118.45.5–6，ADR-045）：查看需 `MD.ITEM.VIEW`；有 `MD.ITEM.EDIT` 時可增刪屬性列並整批儲存。
 * 新增列只能選啟用的屬性，已有值的停用屬性保留；後端欄位錯誤按索引標在對應列。
 */
@Component({
  selector: 'erp-item-attribute-dialog',
  imports: [ReactiveFormsModule, TranslatePipe, NzAlertModule, NzButtonModule, NzInputModule, NzModalModule, NzTagModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <nz-modal [nzVisible]="item() !== null" [nzTitle]="'md.itemAttributeValue.title' | translate: { code: item()?.code }"
      nzWidth="720px" [nzOkText]="canEdit() ? ('common.action.save' | translate) : null"
      [nzCancelText]="(canEdit() ? 'common.action.cancel' : 'common.action.confirm') | translate"
      [nzOkLoading]="saving()" [nzOkDisabled]="loading()" (nzOnOk)="save()" (nzOnCancel)="closed.emit()">
      <ng-container *nzModalContent>
        <div class="item-attributes" data-testid="md-item-attribute-dialog">
          @if (error()) { <nz-alert nzType="error" nzShowIcon data-testid="md-item-attribute-error" [nzMessage]="error()! | translate" /> }
          @if (loading()) { <p class="muted">{{ 'common.state.loading' | translate }}</p> }
          @else if (!canEdit()) {
            @if (values().length === 0) { <p class="muted" data-testid="md-item-attribute-empty">{{ 'md.itemAttributeValue.empty' | translate }}</p> }
            <dl>@for (row of values(); track row.attributeId) {
              <dt [attr.data-testid]="'md-item-attribute-name-' + row.attributeCode">{{ row.attributeName }}
                @if (!row.isAttributeActive) { <nz-tag>{{ 'md.itemAttribute.inactive' | translate }}</nz-tag> }</dt>
              <dd [attr.data-testid]="'md-item-attribute-value-' + row.attributeCode">{{ row.value }}</dd>
            }</dl>
          } @else {
            <form [formGroup]="form" (ngSubmit)="save()" data-testid="md-item-attribute-form">
              @if (rows.length === 0) { <p class="muted" data-testid="md-item-attribute-empty">{{ 'md.itemAttributeValue.empty' | translate }}</p> }
              <div formArrayName="rows" class="rows">
                @for (row of rows.controls; track row; let i = $index) {
                  <div class="row" [formGroupName]="i" [attr.data-testid]="'md-item-attribute-row-' + i">
                    <select formControlName="attributeId" [attr.data-testid]="'md-item-attribute-select-' + i"
                      [attr.aria-label]="'md.itemAttributeValue.attribute' | translate">
                      <option [ngValue]="null">{{ 'md.itemAttributeValue.selectAttribute' | translate }}</option>
                      @for (option of optionsFor(i); track option.id) {
                        <option [ngValue]="option.id">{{ option.code }} — {{ option.name }}{{ option.isActive ? '' : ' (' + ('md.itemAttribute.inactive' | translate) + ')' }}</option>
                      }
                    </select>
                    <input nz-input formControlName="value" [maxlength]="maxValueLength" [attr.data-testid]="'md-item-attribute-input-' + i"
                      [attr.aria-label]="'md.itemAttributeValue.value' | translate" [placeholder]="'md.itemAttributeValue.value' | translate" />
                    <button nz-button nzType="link" nzDanger type="button" [attr.data-testid]="'md-item-attribute-remove-' + i"
                      (click)="removeRow(i)">{{ 'common.action.delete' | translate }}</button>
                    @if (rowError(i); as key) { <p role="alert" [attr.data-testid]="'md-item-attribute-row-error-' + i">{{ key | translate }}</p> }
                  </div>
                }
              </div>
              <button nz-button type="button" data-testid="md-item-attribute-add" [disabled]="rows.length >= maxAttributes"
                (click)="addRow()">{{ 'md.itemAttributeValue.add' | translate }}</button>
              @if (!canViewDefinitions()) { <p class="muted" data-testid="md-item-attribute-no-definitions">{{ 'md.itemAttributeValue.noDefinitions' | translate }}</p> }
            </form>
          }
        </div>
      </ng-container>
    </nz-modal>
  `,
  styles: `
    .item-attributes { display: grid; gap: 12px; color: var(--erp-color-text); }
    .muted { margin: 0; color: var(--erp-color-text-secondary); }
    dl { display: grid; grid-template-columns: auto 1fr; gap: 4px 16px; margin: 0; } dd { margin: 0; word-break: break-all; }
    form, .rows { display: grid; gap: 8px; } form > button { justify-self: start; }
    .row { display: grid; grid-template-columns: minmax(160px, 1fr) 2fr auto; gap: 8px; align-items: center; }
    .row p { grid-column: 1 / -1; color: var(--erp-status-error); margin: 0; }
  `,
})
export class ItemAttributeDialog {
  readonly item = input<Item | null>(null);
  readonly closed = output<void>();
  readonly maxAttributes = MAX_ATTRIBUTES_PER_ITEM;
  readonly maxValueLength = MAX_ATTRIBUTE_VALUE_LENGTH;
  private readonly api = inject(ItemAttributeValueService);
  private readonly definitionApi = inject(ItemAttributeService);
  private readonly permissions = inject(PermissionService);
  private readonly valueRequest = latestRequest();
  private readonly definitionRequest = latestRequest();
  readonly values = signal<ItemAttributeValue[]>([]);
  readonly definitions = signal<ItemAttribute[]>([]);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly error = signal<string | null>(null);
  /** 伺服器回傳的列錯誤（索引 → i18n key）；修改任一列後清除。 */
  readonly serverRowErrors = signal<Record<number, string>>({});
  readonly canEdit = computed(() => this.permissions.has('MD.ITEM.EDIT'));
  readonly canViewDefinitions = computed(() => this.permissions.has('MD.ITEM_ATTRIBUTE.VIEW'));
  readonly rows = new FormArray<ValueRow>([]);
  readonly form = new FormGroup({ rows: this.rows });
  /** 表單變動計數，讓 optionsFor 隨各列選擇更新。 */
  private readonly formRevision = signal(0);
  private generation = 0;
  /** 啟用的屬性定義加上物料已有值的屬性（可能已停用）。 */
  readonly options = computed<AttributeOption[]>(() => {
    const map = new Map<number, AttributeOption>();
    for (const x of this.values()) map.set(x.attributeId, { id: x.attributeId, code: x.attributeCode, name: x.attributeName,
      sortOrder: x.sortOrder, isActive: x.isAttributeActive });
    for (const x of this.definitions()) if (x.isActive || map.has(x.id)) map.set(x.id, x);
    return [...map.values()].sort((a, b) => a.sortOrder - b.sortOrder || a.code.localeCompare(b.code));
  });

  constructor() {
    this.rows.valueChanges.subscribe(() => { this.formRevision.update(x => x + 1); this.serverRowErrors.set({}); });
    effect(() => {
      const item = this.item();
      untracked(() => {
        this.generation++;
        this.values.set([]); this.definitions.set([]); this.error.set(null); this.saving.set(false); this.resetRows([]);
        this.valueRequest.cancel(); this.definitionRequest.cancel();
        if (item) this.load(item.id);
      });
    });
  }

  /** 第 i 列可選：排除其他列已選的屬性。 */
  optionsFor(index: number): AttributeOption[] {
    this.formRevision();
    const taken = new Set(this.rows.controls.filter((_, i) => i !== index).map(x => x.controls.attributeId.value));
    return this.options().filter(x => !taken.has(x.id));
  }

  rowError(index: number): string | null {
    this.formRevision();
    const server = this.serverRowErrors()[index];
    if (server) return server;
    const row = this.rows.at(index);
    if (!row) return null;
    if (row.controls.attributeId.touched && row.controls.attributeId.invalid) return 'md.itemAttributeValue.invalidAttribute';
    if (row.controls.value.touched && row.controls.value.invalid) return 'md.itemAttributeValue.invalidValue';
    return null;
  }

  addRow(): void {
    if (this.rows.length >= MAX_ATTRIBUTES_PER_ITEM) return;
    this.rows.push(this.newRow(null, ''));
  }

  removeRow(index: number): void { this.rows.removeAt(index); }

  save(): void {
    const item = this.item();
    if (!item || !this.canEdit() || this.saving() || this.loading()) return;
    this.rows.markAllAsTouched(); this.formRevision.update(x => x + 1);
    if (this.rows.invalid) return;
    const body = this.rows.getRawValue().map(x => ({ attributeId: x.attributeId!, value: x.value }));
    const generation = this.generation;
    this.saving.set(true); this.error.set(null);
    this.api.replace(item.id, body).subscribe({
      next: (values) => {
        if (generation !== this.generation) return;
        this.saving.set(false); this.values.set(values); this.resetRows(values); this.closed.emit();
      },
      error: (error: unknown) => { if (generation !== this.generation) return; this.saving.set(false); this.showError(error); },
    });
  }

  private load(itemId: number): void {
    this.loading.set(true);
    this.valueRequest.run(this.api.list(itemId), {
      next: (values) => { this.values.set(values); this.resetRows(values); this.loading.set(false); },
      error: (error: unknown) => { this.error.set(this.api.errorKey(error)); this.loading.set(false); },
    });
    if (this.canEdit() && this.canViewDefinitions()) {
      this.definitionRequest.run(this.allPages(page => this.definitionApi.list({ page, pageSize: 200, sort: 'sortOrder' })), {
        next: (rows) => this.definitions.set(rows), error: () => this.definitions.set([]),
      });
    }
  }

  /** 欄位錯誤標在對應列；無法對應列時顯示整體錯誤。 */
  private showError(error: unknown): void {
    const fields = error instanceof HttpErrorResponse && error.status === 400
      ? Object.keys((error.error as { errors?: Record<string, unknown> } | null)?.errors ?? {}) : [];
    const rowErrors: Record<number, string> = {};
    for (const field of fields) {
      const match = FIELD_KEY.exec(field);
      if (match) rowErrors[Number(match[1])] = match[2] === 'value' ? 'md.itemAttributeValue.invalidValue' : 'md.itemAttributeValue.invalidAttribute';
    }
    this.serverRowErrors.set(rowErrors);
    this.error.set(Object.keys(rowErrors).length > 0 ? null : this.api.errorKey(error));
  }

  private resetRows(values: readonly ItemAttributeValue[]): void {
    this.rows.clear({ emitEvent: false });
    for (const x of values) this.rows.push(this.newRow(x.attributeId, x.value), { emitEvent: false });
    this.rows.markAsUntouched();
    this.formRevision.update(x => x + 1); this.serverRowErrors.set({});
  }

  private newRow(attributeId: number | null, value: string): ValueRow {
    return new FormGroup({
      attributeId: new FormControl<number | null>(attributeId, Validators.required),
      value: new FormControl(value, { nonNullable: true, validators: [Validators.required, Validators.pattern(/.*\S.*/),
        Validators.maxLength(MAX_ATTRIBUTE_VALUE_LENGTH)] }),
    });
  }

  private allPages<T>(load: (page: number) => Observable<PagedResult<T>>): Observable<T[]> {
    return load(1).pipe(
      expand(result => result.page * result.pageSize < result.totalCount ? load(result.page + 1) : EMPTY),
      reduce((rows, result) => [...rows, ...result.items], [] as T[]));
  }
}
