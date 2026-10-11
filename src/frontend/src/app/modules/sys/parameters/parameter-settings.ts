import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzCardModule } from 'ng-zorro-antd/card';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzSelectModule } from 'ng-zorro-antd/select';
import { NzSpinModule } from 'ng-zorro-antd/spin';
import { NzSwitchModule } from 'ng-zorro-antd/switch';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { PermissionService } from '../../../core/auth/permission.service';
import { CompanyContextService } from '../../../core/company/company-context.service';
import { HasUnsavedChanges } from '../../../shared/document-page/unsaved-changes.guard';
import {
  ParameterDetail, parameterErrorKey, parameterFieldErrors, ParameterService, parameterValueError, ParameterValueUpdate,
} from './parameter.service';

interface ParameterGroup { id: string; module: string; group: string; items: ParameterDetail[]; }
interface PrintPreview { name: string; address: string; phone: string; fax: string; logo: boolean; stamp: boolean; }

/** 列印設定分組（ADR-027）：在 /sys/print-settings 維護，/sys/parameters 不重複顯示。 */
export const PRINT_GROUP = 'print';
export type ParameterView = 'parameters' | 'print';
/** 超過此長度的文字參數用多行輸入框（例如頁尾備註）。 */
const MULTILINE_LENGTH = 100;

/**
 * 系統參數頁（介面 8.55，REQ_F4118.17.4）：依模組／分組列出目前公司的參數，只保存有修改的項目。
 * 路由資料 `view: 'print'` 時為列印設定頁（介面 8.56，REQ_F4118.18.1–2）：只顯示 print 分組並加抬頭預覽。
 */
@Component({
  selector: 'erp-parameter-settings',
  imports: [FormsModule, TranslatePipe, NzAlertModule, NzButtonModule, NzCardModule, NzInputModule, NzSelectModule,
    NzSpinModule, NzSwitchModule, NzTagModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="param-page" data-testid="sys-parameters">
      <header class="param-header">
        <h2>{{ (view() === 'print' ? 'sys.printSettings.title' : 'sys.parameters.title') | translate }}</h2>
        <div class="param-actions">
          <button nz-button data-testid="param-reload" [disabled]="loading() || saving()" (click)="reload()">
            {{ 'sys.parameters.reload' | translate }}</button>
          @if (canEdit()) {
            <button nz-button data-testid="param-discard" [disabled]="!dirty() || saving()" (click)="discard()">
              {{ 'sys.parameters.discard' | translate }}</button>
            <button nz-button nzType="primary" data-testid="param-save" [nzLoading]="saving()"
              [disabled]="!dirty() || invalid() || loading()" (click)="save()">{{ 'common.action.save' | translate }}</button>
          }
        </div>
      </header>
      @if (!canEdit()) { <nz-alert nzType="info" nzShowIcon [nzMessage]="'sys.parameters.readOnly' | translate" data-testid="param-read-only" /> }
      @if (loadError()) {
        <nz-alert nzType="error" nzShowIcon [nzMessage]="loadError()! | translate" data-testid="param-load-error" />
      }
      @if (saveError()) {
        <nz-alert nzType="error" nzShowIcon [nzMessage]="saveError()! | translate" data-testid="param-save-error"
          [nzAction]="conflict() ? reloadAction : null" />
        <ng-template #reloadAction><button nz-button nzSize="small" data-testid="param-conflict-reload" (click)="reload()">
          {{ 'sys.parameters.reload' | translate }}</button></ng-template>
      }
      @if (notice()) { <nz-alert nzType="success" nzShowIcon [nzMessage]="notice()! | translate" data-testid="param-notice" /> }
      <nz-spin [nzSpinning]="loading()">
        @if (preview(); as header) {
          <nz-card class="param-card" [nzTitle]="'sys.printSettings.preview' | translate" data-testid="print-preview">
            <div class="print-preview">
              @if (header.logo) { <span class="print-badge" data-testid="print-preview-logo">{{ 'sys.printSettings.logo' | translate }}</span> }
              <div class="print-preview-text">
                <strong data-testid="print-preview-name">{{ header.name }}</strong>
                @if (header.address) { <span data-testid="print-preview-address">{{ header.address }}</span> }
                <span>
                  @if (header.phone) { <span data-testid="print-preview-phone">{{ 'sys.printSettings.phone' | translate: { value: header.phone } }}</span> }
                  @if (header.fax) { <span data-testid="print-preview-fax">{{ 'sys.printSettings.fax' | translate: { value: header.fax } }}</span> }
                </span>
              </div>
              @if (header.stamp) { <span class="print-badge" data-testid="print-preview-stamp">{{ 'sys.printSettings.stamp' | translate }}</span> }
            </div>
            <p class="print-hint">{{ 'sys.printSettings.previewHint' | translate }}</p>
          </nz-card>
        }
        @for (group of groups(); track group.id) {
          <nz-card class="param-card" [nzTitle]="groupTitle" [attr.data-testid]="'param-group-' + group.id">
            <ng-template #groupTitle>{{ 'sys.parameters.modules.' + group.module | translate }} ·
              {{ 'sys.parameters.groups.' + group.group | translate }}</ng-template>
            @for (p of group.items; track p.key) {
              <div class="param-row" [attr.data-testid]="'param-row-' + p.key">
                <label class="param-label" [for]="'param-' + p.key">
                  <span>{{ 'sys.parameters.keys.' + p.key | translate }}</span>
                  <code>{{ p.key }}</code>
                </label>
                <div class="param-input">
                  @switch (p.type) {
                    @case ('Boolean') {
                      <nz-switch [nzId]="'param-' + p.key" [attr.data-testid]="'param-input-' + p.key" [nzDisabled]="!canEdit()"
                        [ngModel]="draft(p) === 'true'" (ngModelChange)="setValue(p, $event ? 'true' : 'false')" />
                    }
                    @case ('Option') {
                      <nz-select [nzId]="'param-' + p.key" [attr.data-testid]="'param-input-' + p.key" [nzDisabled]="!canEdit()"
                        [ngModel]="draft(p)" (ngModelChange)="setValue(p, $event)">
                        @for (option of p.options; track option) { <nz-option [nzValue]="option" [nzLabel]="option" /> }
                      </nz-select>
                    }
                    @case ('Text') {
                      @if (p.maxLength > multilineLength) {
                        <textarea nz-input [id]="'param-' + p.key" [attr.data-testid]="'param-input-' + p.key" [disabled]="!canEdit()"
                          rows="3" [attr.maxlength]="p.maxLength" [value]="draft(p)" (input)="setValue(p, $any($event.target).value)"></textarea>
                      } @else {
                        <input nz-input [id]="'param-' + p.key" [attr.data-testid]="'param-input-' + p.key" [disabled]="!canEdit()"
                          [attr.maxlength]="p.maxLength" autocomplete="off" [value]="draft(p)" (input)="setValue(p, $any($event.target).value)" />
                      }
                    }
                    @default {
                      <input nz-input [id]="'param-' + p.key" [attr.data-testid]="'param-input-' + p.key" [disabled]="!canEdit()"
                        [attr.inputmode]="p.type === 'WholeNumber' ? 'numeric' : 'decimal'" autocomplete="off"
                        [value]="draft(p)" (input)="setValue(p, $any($event.target).value)" />
                    }
                  }
                  @if (changed(p)) { <nz-tag nzColor="processing" [attr.data-testid]="'param-changed-' + p.key">{{ 'sys.parameters.changed' | translate }}</nz-tag> }
                </div>
                <div class="param-meta">
                  <span [attr.data-testid]="'param-default-' + p.key">{{ 'sys.parameters.default' | translate: { value: (display(p, p.defaultValue) | translate) } }}</span>
                  @if (p.minimum !== null || p.maximum !== null) {
                    <span>{{ 'sys.parameters.range' | translate: { min: p.minimum ?? '—', max: p.maximum ?? '—' } }}</span>
                  }
                  @if (canEdit() && draft(p) !== p.defaultValue) {
                    <button nz-button nzType="link" nzSize="small" [attr.data-testid]="'param-restore-' + p.key"
                      (click)="setValue(p, p.defaultValue)">{{ 'sys.parameters.restore' | translate }}</button>
                  }
                </div>
                @if (fieldError(p); as error) {
                  <p class="param-error" role="alert" [attr.data-testid]="'param-error-' + p.key">{{ error | translate: { min: p.minimum, max: p.maximum, length: p.maxLength } }}</p>
                }
              </div>
            }
          </nz-card>
        } @empty {
          @if (!loading() && !loadError()) { <p data-testid="param-empty">{{ 'sys.parameters.empty' | translate }}</p> }
        }
      </nz-spin>
    </section>
  `,
  styles: `.param-page { display: grid; gap: 16px; color: var(--erp-color-text); }
    .param-header { display: flex; justify-content: space-between; align-items: center; gap: 12px; flex-wrap: wrap; }
    .param-header h2 { margin: 0; }
    .param-actions { display: flex; gap: 8px; }
    .param-card { margin-bottom: 16px; }
    .param-row { display: grid; grid-template-columns: minmax(180px, 1fr) minmax(200px, 1fr) minmax(200px, 1.2fr); gap: 8px 16px;
      align-items: center; padding: 8px 0; border-bottom: 1px solid var(--erp-color-border); }
    .param-row:last-child { border-bottom: none; }
    .param-label { display: grid; } .param-label code { font-size: 12px; color: var(--erp-color-text-secondary); }
    .param-input { display: flex; gap: 8px; align-items: center; } .param-input input, .param-input nz-select { max-width: 240px; width: 100%; }
    .param-input textarea { max-width: 420px; width: 100%; }
    .print-preview { display: flex; gap: 16px; align-items: center; justify-content: space-between; padding: 12px;
      border: 1px dashed var(--erp-color-border); }
    .print-preview-text { display: grid; gap: 4px; flex: 1; text-align: center; }
    .print-preview-text strong { font-size: 18px; }
    .print-preview-text span span + span { margin-left: 16px; }
    .print-badge { padding: 4px 8px; border: 1px solid var(--erp-color-border); color: var(--erp-color-text-secondary); }
    .print-hint { margin: 8px 0 0; color: var(--erp-color-text-secondary); }
    .param-meta { display: flex; gap: 12px; align-items: center; flex-wrap: wrap; color: var(--erp-color-text-secondary); }
    .param-error { grid-column: 1 / -1; color: var(--erp-status-error); margin: 0; }
    @media (max-width: 768px) { .param-row { grid-template-columns: 1fr; } }`,
})
export class ParameterSettings implements HasUnsavedChanges {
  private readonly api = inject(ParameterService);
  private readonly permissions = inject(PermissionService);
  private readonly company = inject(CompanyContextService);

  /** 路由資料（withComponentInputBinding）：'print' 為列印設定頁。 */
  readonly view = input<ParameterView>('parameters');
  readonly multilineLength = MULTILINE_LENGTH;

  readonly parameters = signal<ParameterDetail[]>([]);
  /** 只記錄與目前值不同的草稿（鍵 → 值）。 */
  readonly drafts = signal<Record<string, string>>({});
  readonly serverErrors = signal<readonly string[]>([]);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly loadError = signal<string | null>(null);
  readonly saveError = signal<string | null>(null);
  readonly notice = signal<string | null>(null);
  readonly conflict = computed(() => this.saveError() === 'errors.COMMON.CONCURRENCY.CONFLICT');
  readonly canEdit = computed(() => this.permissions.has('SYS.PARAMETER.EDIT'));

  readonly groups = computed<ParameterGroup[]>(() => {
    const groups = new Map<string, ParameterGroup>();
    for (const p of this.parameters()) {
      const id = `${p.module}-${p.group}`;
      const group = groups.get(id) ?? { id, module: p.module, group: p.group, items: [] };
      group.items.push(p);
      groups.set(id, group);
    }
    return [...groups.values()];
  });
  readonly changes = computed<ParameterValueUpdate[]>(() => {
    const drafts = this.drafts();
    return this.parameters().filter(p => this.changed(p)).map(p => ({ key: p.key, value: drafts[p.key].trim(), version: p.version }));
  });
  readonly dirty = computed(() => this.changes().length > 0);
  /** 列印抬頭預覽：依草稿即時更新；公司名稱空白時與伺服器一樣用目前公司名稱。 */
  readonly preview = computed<PrintPreview | null>(() => {
    if (this.view() !== 'print' || this.parameters().length === 0) return null;
    const value = (name: string): string => {
      const p = this.parameters().find(x => x.key === `SYS.PRINT_${name}`);
      return p ? this.draft(p).trim() : '';
    };
    return {
      name: value('COMPANY_NAME') || (this.company.current()?.name ?? ''),
      address: [value('POSTAL_CODE'), value('ADDRESS')].filter(Boolean).join(' '),
      phone: value('PHONE'), fax: value('FAX'), logo: value('SHOW_LOGO') === 'true', stamp: value('SHOW_STAMP') === 'true',
    };
  });
  readonly invalid = computed(() => this.parameters().some(p => this.fieldError(p) !== null));

  private readonly companyId = computed(() => this.company.current()?.id ?? null);

  constructor() {
    // 首次載入及切換公司時重新查詢（參數值按公司保存），未保存的草稿一併丟棄
    effect(() => {
      this.companyId();
      untracked(() => this.reload());
    });
  }

  hasUnsavedChanges(): boolean { return this.dirty(); }
  draft(p: ParameterDetail): string { return this.drafts()[p.key] ?? p.value; }
  /** 伺服器會去除前後空白，所以只差空白不算修改（草稿保留原字，避免輸入時游標後的空白被吃掉）。 */
  changed(p: ParameterDetail): boolean {
    const draft = this.drafts()[p.key];
    return draft !== undefined && draft.trim() !== p.value;
  }
  fieldError(p: ParameterDetail): string | null {
    if (!this.changed(p)) return null;
    return parameterValueError(p, this.draft(p)) ?? (this.serverErrors().includes(p.key) ? 'sys.parameters.errors.invalid' : null);
  }
  /** 布林以「是／否」顯示，其他型別顯示原值（i18n pipe 對非 key 字串原樣回傳）。 */
  display(p: ParameterDetail, value: string): string {
    return p.type === 'Boolean' ? (value === 'true' ? 'sys.parameters.yes' : 'sys.parameters.no') : value;
  }

  setValue(p: ParameterDetail, value: string): void {
    this.notice.set(null);
    this.serverErrors.update(keys => keys.filter(k => k !== p.key));
    this.drafts.update(drafts => {
      const next = { ...drafts };
      if (value === p.value) delete next[p.key]; else next[p.key] = value;
      return next;
    });
  }
  discard(): void { this.drafts.set({}); this.serverErrors.set([]); this.saveError.set(null); }

  reload(): void {
    this.loading.set(true); this.loadError.set(null); this.saveError.set(null); this.notice.set(null);
    this.api.list().subscribe({
      next: items => { this.parameters.set(this.visible(items)); this.discard(); this.loading.set(false); },
      error: error => { this.parameters.set([]); this.discard(); this.loadError.set(parameterErrorKey(error)); this.loading.set(false); },
    });
  }

  /** 列印設定頁只顯示 print 分組，參數頁則排除該分組。 */
  private visible(items: ParameterDetail[]): ParameterDetail[] {
    const print = this.view() === 'print';
    return items.filter(p => (p.group === PRINT_GROUP) === print);
  }

  save(): void {
    const items = this.changes();
    if (!this.canEdit() || this.saving() || items.length === 0 || this.invalid()) return;
    this.saving.set(true); this.saveError.set(null); this.notice.set(null);
    this.api.save(items).subscribe({
      next: result => { this.parameters.set(this.visible(result)); this.discard(); this.saving.set(false); this.notice.set('sys.parameters.saved'); },
      error: error => {
        this.saving.set(false);
        this.serverErrors.set(parameterFieldErrors(error, items));
        this.saveError.set(parameterErrorKey(error));
      },
    });
  }
}
