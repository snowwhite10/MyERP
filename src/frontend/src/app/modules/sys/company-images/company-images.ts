import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzCardModule } from 'ng-zorro-antd/card';
import { NzSpinModule } from 'ng-zorro-antd/spin';
import { PermissionService } from '../../../core/auth/permission.service';
import { CompanyContextService } from '../../../core/company/company-context.service';
import { ErpDialogService } from '../../../shared/dialogs/dialog.service';
import {
  COMPANY_IMAGE_KINDS, CompanyImageDetail, companyImageErrorKey, CompanyImageKind, CompanyImageService, imageFileError, toDataUrl,
} from './company-image.service';

interface ImageMessage { kind: CompanyImageKind; key: string; conflict?: boolean; }

/**
 * Logo 與印章頁（介面 8.57，REQ_F4118.19.1–2）：目前公司的 Logo 和印章各一張卡片，預覽、上傳／替換、刪除。
 * 權限沿用系統參數（ADR-028）；是否列印在列印設定頁設定。
 */
@Component({
  selector: 'erp-company-images',
  imports: [TranslatePipe, NzAlertModule, NzButtonModule, NzCardModule, NzSpinModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="image-page" data-testid="sys-company-images">
      <header class="image-header">
        <h2>{{ 'sys.companyImages.title' | translate }}</h2>
        <button nz-button data-testid="image-reload" [disabled]="loading() || busy() !== null" (click)="reload()">
          {{ 'sys.parameters.reload' | translate }}</button>
      </header>
      <p class="image-hint" data-testid="image-hint">{{ 'sys.companyImages.hint' | translate }}</p>
      @if (!canEdit()) { <nz-alert nzType="info" nzShowIcon [nzMessage]="'sys.companyImages.readOnly' | translate" data-testid="image-read-only" /> }
      @if (loadError()) { <nz-alert nzType="error" nzShowIcon [nzMessage]="loadError()! | translate" data-testid="image-load-error" /> }
      <nz-spin [nzSpinning]="loading()">
        <div class="image-grid">
          @for (kind of kinds; track kind) {
            <nz-card [nzTitle]="label(kind) | translate" [attr.data-testid]="'image-card-' + kind">
              <div class="image-preview" [class.image-stamp]="kind === 'Stamp'">
                @if (previews()[kind]; as src) {
                  <img [src]="src" [alt]="label(kind) | translate" [attr.data-testid]="'image-preview-' + kind" />
                } @else {
                  <span class="image-empty" [attr.data-testid]="'image-empty-' + kind">
                    {{ (details()[kind] ? 'sys.companyImages.unavailable' : 'sys.companyImages.none') | translate }}</span>
                }
              </div>
              @if (details()[kind]; as d) {
                <dl class="image-meta" [attr.data-testid]="'image-meta-' + kind">
                  <dt>{{ 'sys.companyImages.fileName' | translate }}</dt><dd>{{ d.fileName }}</dd>
                  <dt>{{ 'sys.companyImages.size' | translate }}</dt>
                  <dd>{{ 'sys.companyImages.sizeValue' | translate: { kb: kilobytes(d.sizeBytes), width: d.width, height: d.height } }}</dd>
                  <dt>{{ 'sys.companyImages.updatedAt' | translate }}</dt><dd>{{ timestamp(d.updatedAt) }}</dd>
                </dl>
              }
              @if (message(kind); as m) {
                <nz-alert [nzType]="m.key === 'sys.companyImages.saved' || m.key === 'sys.companyImages.removed' ? 'success' : 'error'" nzShowIcon
                  [nzMessage]="m.key | translate" [nzAction]="m.conflict ? reloadAction : null" [attr.data-testid]="'image-message-' + kind" />
                <ng-template #reloadAction><button nz-button nzSize="small" (click)="reload()">{{ 'sys.parameters.reload' | translate }}</button></ng-template>
              }
              @if (canEdit()) {
                <div class="image-actions">
                  <input #picker type="file" hidden accept="image/png,image/jpeg" [attr.data-testid]="'image-file-' + kind"
                    (change)="picked(kind, picker)" />
                  <button nz-button nzType="primary" [attr.data-testid]="'image-upload-' + kind" [nzLoading]="busy() === kind"
                    [disabled]="loading() || (busy() !== null && busy() !== kind)" (click)="picker.click()">
                    {{ (details()[kind] ? 'sys.companyImages.replace' : 'sys.companyImages.upload') | translate }}</button>
                  @if (details()[kind]) {
                    <button nz-button nzDanger [attr.data-testid]="'image-remove-' + kind" [disabled]="loading() || busy() !== null"
                      (click)="confirmRemove(kind)">{{ 'common.action.delete' | translate }}</button>
                  }
                </div>
              }
            </nz-card>
          }
        </div>
      </nz-spin>
    </section>
  `,
  styles: `.image-page { display: grid; gap: 16px; color: var(--erp-color-text); }
    .image-header { display: flex; justify-content: space-between; align-items: center; gap: 12px; flex-wrap: wrap; }
    .image-header h2 { margin: 0; }
    .image-hint { margin: 0; color: var(--erp-color-text-secondary); }
    .image-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(280px, 1fr)); gap: 16px; }
    .image-preview { display: flex; align-items: center; justify-content: center; height: 180px; padding: 12px;
      border: 1px dashed var(--erp-color-border); background: var(--erp-color-bg-container, transparent); }
    .image-preview img { max-width: 100%; max-height: 100%; object-fit: contain; }
    .image-stamp img { max-width: 156px; max-height: 156px; }
    .image-empty { color: var(--erp-color-text-secondary); }
    .image-meta { display: grid; grid-template-columns: auto 1fr; gap: 4px 12px; margin: 12px 0; }
    .image-meta dt { color: var(--erp-color-text-secondary); } .image-meta dd { margin: 0; word-break: break-all; }
    .image-actions { display: flex; gap: 8px; margin-top: 12px; }
    nz-alert { margin-top: 12px; }`,
})
export class CompanyImages {
  private readonly api = inject(CompanyImageService);
  private readonly permissions = inject(PermissionService);
  private readonly company = inject(CompanyContextService);
  private readonly dialogs = inject(ErpDialogService);

  readonly kinds = COMPANY_IMAGE_KINDS;
  readonly details = signal<Partial<Record<CompanyImageKind, CompanyImageDetail>>>({});
  readonly previews = signal<Partial<Record<CompanyImageKind, string>>>({});
  readonly loading = signal(false);
  readonly loadError = signal<string | null>(null);
  readonly busy = signal<CompanyImageKind | null>(null);
  readonly messages = signal<readonly ImageMessage[]>([]);
  readonly canEdit = computed(() => this.permissions.has('SYS.PARAMETER.EDIT'));

  private readonly companyId = computed(() => this.company.current()?.id ?? null);
  /** 每次重新載入遞增；切換公司後，舊公司較慢回來的回應不會覆蓋新資料。 */
  private generation = 0;

  constructor() {
    effect(() => {
      this.companyId();
      untracked(() => this.reload());
    });
  }

  /** i18n key 段落為 camelCase（i18n-keys.spec）。 */
  label(kind: CompanyImageKind): string { return `sys.companyImages.kinds.${kind.toLowerCase()}`; }
  message(kind: CompanyImageKind): ImageMessage | undefined { return this.messages().find((m) => m.kind === kind); }
  kilobytes(bytes: number): string { return (bytes / 1024).toFixed(1); }
  /** 伺服器時間為 UTC+8 的本地時間（沒有時區），只換成「yyyy-MM-dd HH:mm」。 */
  timestamp(value: string): string { return value.replace('T', ' ').slice(0, 16); }

  reload(): void {
    const generation = ++this.generation;
    this.loading.set(true); this.loadError.set(null); this.messages.set([]);
    this.api.list().subscribe({
      next: (items) => {
        if (generation !== this.generation) return;
        this.details.set(Object.fromEntries(items.map((d) => [d.kind, d])));
        this.previews.set({});
        this.loading.set(false);
        items.forEach((d) => this.loadPreview(d.kind, generation));
      },
      error: (error) => {
        if (generation !== this.generation) return;
        this.details.set({}); this.previews.set({});
        this.loadError.set(companyImageErrorKey(error)); this.loading.set(false);
      },
    });
  }

  picked(kind: CompanyImageKind, input: HTMLInputElement): void {
    const file = input.files?.[0];
    input.value = ''; // 同一檔案可再選一次
    if (file) this.upload(kind, file);
  }

  upload(kind: CompanyImageKind, file: File): void {
    if (!this.canEdit() || this.busy() !== null) return;
    const invalid = imageFileError(file);
    if (invalid) { this.setMessage(kind, invalid); return; }
    this.busy.set(kind); this.setMessage(kind, null);
    const generation = this.generation;
    this.api.upload(kind, file, this.details()[kind]?.version ?? 0).subscribe({
      next: (detail) => {
        this.busy.set(null);
        if (generation !== this.generation) return;
        this.details.update((d) => ({ ...d, [kind]: detail }));
        this.setMessage(kind, 'sys.companyImages.saved');
        this.loadPreview(kind, generation);
      },
      error: (error) => { this.busy.set(null); this.setError(kind, error); },
    });
  }

  confirmRemove(kind: CompanyImageKind): void {
    if (!this.canEdit() || !this.details()[kind]) return;
    this.dialogs.confirm({
      titleKey: `sys.companyImages.removeConfirm.${kind.toLowerCase()}`, contentKey: 'common.confirm.irreversible',
      okKey: 'common.action.delete', danger: true,
    }).subscribe((ok) => { if (ok) this.remove(kind); });
  }

  remove(kind: CompanyImageKind): void {
    const current = this.details()[kind];
    if (!this.canEdit() || !current || this.busy() !== null) return;
    this.busy.set(kind); this.setMessage(kind, null);
    this.api.remove(kind, current.version).subscribe({
      next: () => {
        this.busy.set(null);
        this.details.update((d) => { const next = { ...d }; delete next[kind]; return next; });
        this.previews.update((p) => { const next = { ...p }; delete next[kind]; return next; });
        this.setMessage(kind, 'sys.companyImages.removed');
      },
      error: (error) => { this.busy.set(null); this.setError(kind, error); },
    });
  }

  private loadPreview(kind: CompanyImageKind, generation: number): void {
    this.api.content(kind).subscribe({
      next: async (blob) => {
        const url = await toDataUrl(blob);
        if (generation !== this.generation) return;
        this.previews.update((p) => {
          const next = { ...p };
          if (url) next[kind] = url; else delete next[kind];
          return next;
        });
      },
      // 預覽載入失敗只顯示「無法預覽」，不影響其他操作
      error: () => undefined,
    });
  }

  private setError(kind: CompanyImageKind, error: unknown): void {
    const key = companyImageErrorKey(error);
    this.messages.update((list) => [...list.filter((m) => m.kind !== kind),
      { kind, key, conflict: key === 'errors.COMMON.CONCURRENCY.CONFLICT' || key === 'errors.SYS.COMPANY_IMAGE.NOT_FOUND' }]);
  }

  private setMessage(kind: CompanyImageKind, key: string | null): void {
    this.messages.update((list) => [...list.filter((m) => m.kind !== kind), ...(key ? [{ kind, key }] : [])]);
  }
}
