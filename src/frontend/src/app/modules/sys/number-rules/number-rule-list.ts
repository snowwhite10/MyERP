import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { AbstractControl, FormControl, FormGroup, FormsModule, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzCheckboxModule } from 'ng-zorro-antd/checkbox';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzModalModule } from 'ng-zorro-antd/modal';
import { NzSelectModule } from 'ng-zorro-antd/select';
import { NzTableModule, NzTableQueryParams } from 'ng-zorro-antd/table';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { Observable } from 'rxjs';
import { HasPermissionDirective } from '../../../core/auth/has-permission.directive';
import { CompanyContextService } from '../../../core/company/company-context.service';
import { isoDate } from '../../../shared/date';
import {
  DATE_FORMATS,
  dateFormatKey,
  DOCUMENT_TYPE_MAX_LENGTH,
  DOCUMENT_TYPE_PATTERN,
  formatNumber,
  MAX_SEQUENCE_LENGTH,
  MIN_SEQUENCE_LENGTH,
  NUMBER_RULE_NAME_MAX_LENGTH,
  NumberDateFormat,
  NumberPreview,
  NumberResetPeriod,
  NumberRule,
  numberRuleErrorKey,
  NumberRuleService,
  periodCovered,
  PREFIX_MAX_LENGTH,
  PREFIX_PATTERN,
  RESET_PERIODS,
  resetPeriodKey,
} from './number-rule.service';

/** 對話框：新增規則，或編輯某規則。 */
export type NumberRuleDialog = { mode: 'create' } | { mode: 'edit'; rule: NumberRule };

/** 單據類型去除空白並轉大寫後須符合 `{PREFIX}.{DOCUMENT}`。 */
function documentTypeFormat(control: AbstractControl<string>): ValidationErrors | null {
  const value = control.value.trim().toUpperCase();
  return value.length <= DOCUMENT_TYPE_MAX_LENGTH && DOCUMENT_TYPE_PATTERN.test(value) ? null : { documentType: true };
}

function prefixFormat(control: AbstractControl<string>): ValidationErrors | null {
  return PREFIX_PATTERN.test(control.value.trim().toUpperCase()) ? null : { prefix: true };
}

function notBlank(control: AbstractControl<string>): ValidationErrors | null {
  return control.value.trim() ? null : { required: true };
}

/** 重設週期必須包含在日期格式內（表單層級錯誤 `period`）。 */
function periodWithinFormat(group: AbstractControl): ValidationErrors | null {
  const { dateFormat, resetPeriod } = group.getRawValue() as { dateFormat: NumberDateFormat; resetPeriod: NumberResetPeriod };
  return periodCovered(dateFormat, resetPeriod) ? null : { period: true };
}

const SORTABLE = ['documentType', 'name'] as const;

/**
 * 單號規則維護頁（介面 8.67，REQ_F4118.28.1–4、28.6）：伺服器端分頁、單據類型／名稱排序與搜尋；
 * 新增／編輯對話框即時顯示樣式，已發出單號的規則鎖定日期格式與重設週期、流水號長度不可縮短；
 * 「預覽」對話框依業務日期查詢下一個單號（不佔號）。規則不可刪除，以停用代替。
 */
@Component({
  selector: 'erp-number-rule-list',
  imports: [
    FormsModule,
    ReactiveFormsModule,
    TranslatePipe,
    NzAlertModule,
    NzButtonModule,
    NzCheckboxModule,
    NzInputModule,
    NzModalModule,
    NzSelectModule,
    NzTableModule,
    NzTagModule,
    HasPermissionDirective,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="erp-number-rule">
      <header class="erp-number-rule__header">
        <h1 data-testid="sys-number-rule-title">{{ 'sys.numberRule.title' | translate }}</h1>
        <button nz-button nzType="primary" data-testid="sys-number-rule-create" *erpHasPermission="'SYS.NUMBER_RULE.CREATE'"
          (click)="openCreate()">
          {{ 'common.action.add' | translate }}
        </button>
      </header>
      <div class="erp-number-rule__filters">
        <input nz-input id="number-rule-search" data-testid="sys-number-rule-search" type="search"
          [attr.aria-label]="'sys.numberRule.search' | translate" [placeholder]="'sys.numberRule.search' | translate"
          [ngModel]="keyword()" (ngModelChange)="keyword.set($event)" (keyup.enter)="search()" />
        <button nz-button data-testid="sys-number-rule-search-button" (click)="search()">
          {{ 'common.action.search' | translate }}
        </button>
      </div>
      @if (loadError()) {
        <nz-alert nzType="error" nzShowIcon data-testid="sys-number-rule-load-error" [nzMessage]="loadError()! | translate" />
      }
      @if (notice()) {
        <nz-alert nzType="success" nzShowIcon nzCloseable data-testid="sys-number-rule-notice"
          [nzMessage]="notice()! | translate: noticeParams()" (nzOnClose)="notice.set(null)" />
      }
      <nz-table #table data-testid="sys-number-rule-table" nzSize="middle" [nzData]="rows()" [nzLoading]="loading()"
        [nzFrontPagination]="false" [nzTotal]="total()" [nzPageIndex]="page()" [nzPageSize]="pageSize()"
        nzShowSizeChanger [nzNoResult]="'sys.numberRule.empty' | translate" (nzQueryParams)="onQueryParams($event)">
        <thead>
          <tr>
            <th nzColumnKey="documentType" [nzSortFn]="true" [nzSortOrder]="sortOrder('documentType')">
              {{ 'sys.numberRule.documentType' | translate }}
            </th>
            <th nzColumnKey="name" [nzSortFn]="true" [nzSortOrder]="sortOrder('name')">{{ 'sys.numberRule.name' | translate }}</th>
            <th>{{ 'sys.numberRule.sample' | translate }}</th>
            <th>{{ 'sys.numberRule.dateFormat' | translate }}</th>
            <th>{{ 'sys.numberRule.resetPeriod' | translate }}</th>
            <th>{{ 'sys.numberRule.status' | translate }}</th>
            <th>{{ 'sys.numberRule.actions' | translate }}</th>
          </tr>
        </thead>
        <tbody>
          @for (row of table.data; track row.id) {
            <tr [attr.data-testid]="'sys-number-rule-row-' + row.documentType">
              <td>{{ row.documentType }}</td>
              <td>{{ row.name }}</td>
              <td><code [attr.data-testid]="'sys-number-rule-sample-' + row.documentType">{{ row.sample }}</code></td>
              <td>{{ dateFormatKey(row.dateFormat) | translate }}</td>
              <td>{{ resetPeriodKey(row.resetPeriod) | translate }}</td>
              <td>
                <nz-tag [nzColor]="row.isActive ? 'success' : 'default'">
                  {{ (row.isActive ? 'sys.numberRule.active' : 'sys.numberRule.inactive') | translate }}
                </nz-tag>
                @if (row.isInUse) {
                  <nz-tag nzColor="blue" [attr.data-testid]="'sys-number-rule-in-use-' + row.documentType">
                    {{ 'sys.numberRule.inUse' | translate }}
                  </nz-tag>
                }
              </td>
              <td>
                <button nz-button nzType="link" nzSize="small" [attr.data-testid]="'sys-number-rule-preview-' + row.documentType"
                  (click)="openPreview(row)">
                  {{ 'sys.numberRule.preview' | translate }}
                </button>
                <button nz-button nzType="link" nzSize="small" *erpHasPermission="'SYS.NUMBER_RULE.EDIT'"
                  [attr.data-testid]="'sys-number-rule-edit-' + row.documentType" (click)="openEdit(row)">
                  {{ 'common.action.edit' | translate }}
                </button>
              </td>
            </tr>
          }
        </tbody>
      </nz-table>
    </section>

    <nz-modal [nzVisible]="dialog() !== null" [nzTitle]="dialogTitle() | translate: dialogTitleParams()" nzWidth="600px"
      [nzOkText]="'common.action.save' | translate" [nzCancelText]="'common.action.cancel' | translate"
      [nzOkLoading]="saving()" (nzOnOk)="submit()" (nzOnCancel)="closeDialog()">
      <ng-container *nzModalContent>
        @if (saveError()) {
          <nz-alert nzType="error" nzShowIcon data-testid="sys-number-rule-save-error" [nzMessage]="saveError()! | translate" />
        }
        @if (dialogRule()?.isInUse) {
          <nz-alert nzType="info" nzShowIcon data-testid="sys-number-rule-locked" [nzMessage]="'sys.numberRule.lockedHint' | translate" />
        }
        <form class="erp-number-rule-form" [formGroup]="form" (ngSubmit)="submit()" data-testid="sys-number-rule-form">
          <label for="number-rule-type">{{ 'sys.numberRule.documentType' | translate }}</label>
          <input id="number-rule-type" data-testid="sys-number-rule-type" formControlName="documentType"
            [attr.maxlength]="documentTypeMaxLength" autocomplete="off" placeholder="SD.ORDER" required />
          @if (form.controls.documentType.touched && form.controls.documentType.invalid) {
            <p role="alert">{{ 'sys.numberRule.invalidDocumentType' | translate }}</p>
          }
          <label for="number-rule-name">{{ 'sys.numberRule.name' | translate }}</label>
          <input id="number-rule-name" data-testid="sys-number-rule-name" formControlName="name" [attr.maxlength]="nameMaxLength" required />
          @if (form.controls.name.touched && form.controls.name.invalid) {
            <p role="alert">{{ 'sys.numberRule.invalidName' | translate }}</p>
          }
          <label for="number-rule-prefix">{{ 'sys.numberRule.prefix' | translate }}</label>
          <input id="number-rule-prefix" data-testid="sys-number-rule-prefix" formControlName="prefix"
            [attr.maxlength]="prefixMaxLength" autocomplete="off" />
          @if (form.controls.prefix.touched && form.controls.prefix.invalid) {
            <p role="alert">{{ 'sys.numberRule.invalidPrefix' | translate }}</p>
          }
          <label for="number-rule-date-format">{{ 'sys.numberRule.dateFormat' | translate }}</label>
          <nz-select nzId="number-rule-date-format" data-testid="sys-number-rule-date-format" formControlName="dateFormat">
            @for (format of dateFormats; track format) {
              <nz-option [nzValue]="format" [nzLabel]="dateFormatKey(format) | translate" />
            }
          </nz-select>
          <label for="number-rule-length">{{ 'sys.numberRule.sequenceLength' | translate }}</label>
          <input id="number-rule-length" data-testid="sys-number-rule-length" type="number" formControlName="sequenceLength"
            [min]="minLength()" [max]="maxLength" required />
          @if (form.controls.sequenceLength.touched && form.controls.sequenceLength.invalid) {
            <p role="alert">{{ 'sys.numberRule.invalidLength' | translate: { min: minLength(), max: maxLength } }}</p>
          }
          <label for="number-rule-reset">{{ 'sys.numberRule.resetPeriod' | translate }}</label>
          <nz-select nzId="number-rule-reset" data-testid="sys-number-rule-reset" formControlName="resetPeriod">
            @for (period of resetPeriods; track period) {
              <nz-option [nzValue]="period" [nzLabel]="resetPeriodKey(period) | translate" />
            }
          </nz-select>
          @if (form.hasError('period')) {
            <p role="alert" data-testid="sys-number-rule-period-error">{{ 'sys.numberRule.periodNotCovered' | translate }}</p>
          }
          @if (dialog()?.mode === 'edit') {
            <label nz-checkbox data-testid="sys-number-rule-active" formControlName="isActive">
              {{ 'sys.numberRule.active' | translate }}
            </label>
          }
          <p class="erp-number-rule-form__sample">
            {{ 'sys.numberRule.samplePreview' | translate }}
            <code data-testid="sys-number-rule-live-sample">{{ liveSample() }}</code>
          </p>
          <button type="submit" hidden></button>
        </form>
      </ng-container>
    </nz-modal>

    <nz-modal [nzVisible]="previewRule() !== null" [nzTitle]="'sys.numberRule.previewTitle' | translate: previewTitleParams()"
      [nzFooter]="null" (nzOnCancel)="closePreview()">
      <ng-container *nzModalContent>
        <div class="erp-number-rule-preview">
          <label for="number-rule-preview-date">{{ 'sys.numberRule.documentDate' | translate }}</label>
          <input id="number-rule-preview-date" type="date" data-testid="sys-number-rule-preview-date"
            [ngModel]="previewDate()" (ngModelChange)="previewDate.set($event ?? '')" />
          <button nz-button nzType="primary" data-testid="sys-number-rule-preview-run" [nzLoading]="previewLoading()"
            (click)="runPreview()">
            {{ 'sys.numberRule.preview' | translate }}
          </button>
        </div>
        @if (previewError()) {
          <nz-alert nzType="error" nzShowIcon data-testid="sys-number-rule-preview-error" [nzMessage]="previewError()! | translate" />
        }
        @if (previewResult(); as result) {
          <p data-testid="sys-number-rule-preview-result">
            {{ 'sys.numberRule.nextNumber' | translate: { date: result.documentDate } }}
            <code>{{ result.nextNumber }}</code>
          </p>
        }
        <small class="erp-number-rule-form__hint">{{ 'sys.numberRule.previewHint' | translate }}</small>
      </ng-container>
    </nz-modal>
  `,
  styles: `
    .erp-number-rule { display: grid; gap: var(--erp-space-2, 16px); color: var(--erp-color-text); }
    .erp-number-rule__header { display: flex; align-items: center; justify-content: space-between; gap: 16px; }
    .erp-number-rule__header h1 { margin: 0; font-size: 20px; }
    .erp-number-rule__filters { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; }
    .erp-number-rule__filters input { max-width: 320px; }
    nz-alert { margin-bottom: 12px; }
    code { font-family: var(--erp-font-mono, monospace); color: var(--erp-color-text); }
    .erp-number-rule-form { display: grid; gap: 8px; color: var(--erp-color-text); }
    .erp-number-rule-form input, .erp-number-rule-preview input {
      padding: 6px 8px; border: 1px solid var(--erp-color-border, #d9d9d9); border-radius: 6px;
      background: var(--erp-color-bg-container, #fff); color: var(--erp-color-text);
    }
    .erp-number-rule-form input:disabled { opacity: 0.6; }
    .erp-number-rule-form p[role='alert'] { color: var(--erp-status-error); margin: 0; }
    .erp-number-rule-form__sample { margin: 8px 0 0; }
    .erp-number-rule-form__hint { color: var(--erp-color-text-secondary, inherit); margin: 0; }
    .erp-number-rule-preview { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; margin-bottom: 12px; color: var(--erp-color-text); }
  `,
})
export class NumberRuleList {
  private readonly api = inject(NumberRuleService);
  private readonly company = inject(CompanyContextService);

  readonly dateFormats = DATE_FORMATS;
  readonly resetPeriods = RESET_PERIODS;
  readonly dateFormatKey = dateFormatKey;
  readonly resetPeriodKey = resetPeriodKey;
  readonly documentTypeMaxLength = DOCUMENT_TYPE_MAX_LENGTH;
  readonly nameMaxLength = NUMBER_RULE_NAME_MAX_LENGTH;
  readonly prefixMaxLength = PREFIX_MAX_LENGTH;
  readonly maxLength = MAX_SEQUENCE_LENGTH;

  readonly rows = signal<NumberRule[]>([]);
  readonly total = signal(0);
  readonly page = signal(1);
  readonly pageSize = signal(20);
  readonly sort = signal('documentType');
  readonly keyword = signal('');
  readonly loading = signal(false);
  readonly loadError = signal<string | null>(null);
  readonly notice = signal<string | null>(null);
  readonly noticeParams = signal<Record<string, string>>({});

  readonly dialog = signal<NumberRuleDialog | null>(null);
  readonly saving = signal(false);
  readonly saveError = signal<string | null>(null);
  /** 流水號長度下限：已發出單號時為原長度（不可縮短）。 */
  readonly minLength = signal(MIN_SEQUENCE_LENGTH);

  readonly previewRule = signal<NumberRule | null>(null);
  readonly previewDate = signal('');
  readonly previewLoading = signal(false);
  readonly previewResult = signal<NumberPreview | null>(null);
  readonly previewError = signal<string | null>(null);

  readonly form = new FormGroup(
    {
      documentType: new FormControl('', { nonNullable: true, validators: [Validators.required, documentTypeFormat] }),
      name: new FormControl('', { nonNullable: true, validators: [notBlank, Validators.maxLength(NUMBER_RULE_NAME_MAX_LENGTH)] }),
      prefix: new FormControl('', { nonNullable: true, validators: [prefixFormat] }),
      dateFormat: new FormControl<NumberDateFormat>('YearMonth', { nonNullable: true }),
      // 原生數字欄位清空時為 null；reset() 回到 4
      sequenceLength: new FormControl<number | null>(4, {
        nonNullable: true,
        validators: [Validators.required, Validators.min(MIN_SEQUENCE_LENGTH), Validators.max(MAX_SEQUENCE_LENGTH)],
      }),
      resetPeriod: new FormControl<NumberResetPeriod>('Monthly', { nonNullable: true }),
      isActive: new FormControl(true, { nonNullable: true }),
    },
    { validators: periodWithinFormat },
  );

  readonly dialogRule = computed(() => {
    const dialog = this.dialog();
    return dialog?.mode === 'edit' ? dialog.rule : null;
  });
  readonly dialogTitle = computed(() => (this.dialog()?.mode === 'edit' ? 'sys.numberRule.editTitle' : 'sys.numberRule.createTitle'));
  readonly dialogTitleParams = computed(() => ({ type: this.dialogRule()?.documentType ?? '' }));
  readonly previewTitleParams = computed(() => ({ type: this.previewRule()?.documentType ?? '' }));

  /** 預覽請求序號：關閉或重新查詢後，忽略較早的回應。 */
  private previewSeq = 0;
  private readonly companyId = computed(() => this.company.current()?.id ?? null);

  constructor() {
    // 規則屬於目前公司：切換公司後回到第一頁重新查詢
    effect(() => {
      this.companyId();
      untracked(() => {
        this.page.set(1);
        this.notice.set(null);
        this.closePreview();
        this.reload();
      });
    });
  }

  sortOrder(column: (typeof SORTABLE)[number]): 'ascend' | 'descend' | null {
    const sort = this.sort();
    if (sort === column) return 'ascend';
    return sort === `-${column}` ? 'descend' : null;
  }

  search(): void {
    this.page.set(1);
    this.reload();
  }

  onQueryParams(params: NzTableQueryParams): void {
    const sorted = params.sort.find((s) => s.value !== null && (SORTABLE as readonly string[]).includes(s.key));
    const sort = sorted ? `${sorted.value === 'descend' ? '-' : ''}${sorted.key}` : 'documentType';
    if (params.pageIndex === this.page() && params.pageSize === this.pageSize() && sort === this.sort()) return;
    this.page.set(params.pageIndex);
    this.pageSize.set(params.pageSize);
    this.sort.set(sort);
    this.reload();
  }

  reload(): void {
    this.loading.set(true);
    this.loadError.set(null);
    this.api.list({ page: this.page(), pageSize: this.pageSize(), keyword: this.keyword(), sort: this.sort() }).subscribe({
      next: (result) => {
        this.rows.set(result.items);
        this.total.set(result.totalCount);
        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.rows.set([]);
        this.total.set(0);
        this.loadError.set(numberRuleErrorKey(error));
        this.loading.set(false);
      },
    });
  }

  /** 對話框內依目前輸入即時組合今天第 1 號（不呼叫 API）。 */
  liveSample(): string {
    const value = this.form.getRawValue();
    const length = Math.min(Math.max(value.sequenceLength ?? MIN_SEQUENCE_LENGTH, MIN_SEQUENCE_LENGTH), MAX_SEQUENCE_LENGTH);
    return formatNumber(value.prefix.trim().toUpperCase(), value.dateFormat, length, new Date());
  }

  openCreate(): void {
    this.form.reset();
    this.form.controls.documentType.enable();
    this.unlockSettings();
    this.openDialog({ mode: 'create' });
  }

  openEdit(rule: NumberRule): void {
    this.form.reset({
      documentType: rule.documentType,
      name: rule.name,
      prefix: rule.prefix,
      dateFormat: rule.dateFormat,
      sequenceLength: rule.sequenceLength,
      resetPeriod: rule.resetPeriod,
      isActive: rule.isActive,
    });
    // 單據類型建立後不可修改
    this.form.controls.documentType.disable();
    if (rule.isInUse) {
      // 已發出單號：改日期格式或週期會產生重號，縮短長度會令既有計數超出上限
      this.form.controls.dateFormat.disable();
      this.form.controls.resetPeriod.disable();
      this.setMinLength(rule.sequenceLength);
    } else {
      this.unlockSettings();
    }
    this.openDialog({ mode: 'edit', rule });
  }

  closeDialog(): void {
    this.dialog.set(null);
    this.saveError.set(null);
  }

  submit(): void {
    const dialog = this.dialog();
    if (!dialog || this.saving()) return;
    this.form.markAllAsTouched();
    if (this.form.invalid) return;
    const value = this.form.getRawValue();
    const settings = {
      name: value.name.trim(),
      prefix: value.prefix.trim().toUpperCase(),
      dateFormat: value.dateFormat,
      sequenceLength: value.sequenceLength!,
      resetPeriod: value.resetPeriod,
    };
    const documentType = dialog.mode === 'create' ? value.documentType.trim().toUpperCase() : dialog.rule.documentType;
    const request: Observable<NumberRule> =
      dialog.mode === 'create'
        ? this.api.create({ documentType, ...settings })
        : this.api.update(dialog.rule.id, { ...settings, isActive: value.isActive, version: dialog.rule.version });
    this.saving.set(true);
    this.saveError.set(null);
    request.subscribe({
      next: () => {
        this.saving.set(false);
        this.noticeParams.set({ type: documentType });
        this.notice.set(dialog.mode === 'create' ? 'sys.numberRule.created' : 'sys.numberRule.updated');
        this.closeDialog();
        this.reload();
      },
      error: (error: unknown) => {
        this.saving.set(false);
        this.saveError.set(numberRuleErrorKey(error));
      },
    });
  }

  openPreview(rule: NumberRule): void {
    this.previewRule.set(rule);
    this.previewDate.set(isoDate(new Date()));
    this.runPreview();
  }

  runPreview(): void {
    const rule = this.previewRule();
    if (!rule) return;
    const seq = ++this.previewSeq;
    this.previewLoading.set(true);
    this.previewError.set(null);
    this.previewResult.set(null);
    this.api.preview(rule.id, this.previewDate() || undefined).subscribe({
      next: (result) => {
        if (seq !== this.previewSeq) return;
        this.previewLoading.set(false);
        this.previewResult.set(result);
      },
      error: (error: unknown) => {
        if (seq !== this.previewSeq) return;
        this.previewLoading.set(false);
        this.previewError.set(numberRuleErrorKey(error));
      },
    });
  }

  closePreview(): void {
    this.previewSeq++;
    this.previewRule.set(null);
    this.previewResult.set(null);
    this.previewError.set(null);
    this.previewLoading.set(false);
  }

  private openDialog(dialog: NumberRuleDialog): void {
    this.saveError.set(null);
    this.dialog.set(dialog);
  }

  private unlockSettings(): void {
    this.form.controls.dateFormat.enable();
    this.form.controls.resetPeriod.enable();
    this.setMinLength(MIN_SEQUENCE_LENGTH);
  }

  private setMinLength(min: number): void {
    this.minLength.set(min);
    const control = this.form.controls.sequenceLength;
    control.setValidators([Validators.required, Validators.min(min), Validators.max(MAX_SEQUENCE_LENGTH)]);
    control.updateValueAndValidity();
  }
}
