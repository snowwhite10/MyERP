import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzModalModule } from 'ng-zorro-antd/modal';
import { NzTableModule } from 'ng-zorro-antd/table';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { Observable } from 'rxjs';
import { PermissionService } from '../../core/auth/permission.service';
import { ErpDialogService } from '../../shared/dialogs/dialog.service';
import { toDataUrl } from '../../shared/images/image-data-url';
import { ItemImage, itemImageFileError, ItemImageService, MAX_IMAGES_PER_ITEM } from './item-image.service';
import { Item } from './item.service';
import { MAX_SORT_ORDER } from './process.service';

/**
 * 物料圖片對話框（介面 8.15，REQ_F4118.44.1–4，ADR-045）：列出圖片並以 data: URL 預覽；
 * 有 `MD.ITEM.EDIT` 才可上傳、改排序號、設主圖與刪除。主圖只能由另一張設為主圖來取代。
 */
@Component({
  selector: 'erp-item-image-dialog',
  imports: [FormsModule, TranslatePipe, NzAlertModule, NzButtonModule, NzModalModule, NzTableModule, NzTagModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <nz-modal [nzVisible]="item() !== null" [nzTitle]="'md.itemImage.title' | translate: { code: item()?.code }"
      [nzFooter]="null" nzWidth="840px" (nzOnCancel)="closed.emit()">
      <ng-container *nzModalContent>
        <div class="item-images" data-testid="md-item-image-dialog">
          <p class="hint">{{ 'md.itemImage.hint' | translate: { max: maxImages } }}</p>
          @if (error()) { <nz-alert nzType="error" nzShowIcon data-testid="md-item-image-error" [nzMessage]="error()! | translate" /> }
          @if (canEdit()) {
            <div class="upload">
              <input #picker type="file" hidden accept="image/png,image/jpeg" data-testid="md-item-image-file" (change)="picked(picker)" />
              <label><input type="checkbox" data-testid="md-item-image-primary" [(ngModel)]="uploadPrimary" />{{ 'md.itemImage.uploadPrimary' | translate }}</label>
              <button nz-button nzType="primary" data-testid="md-item-image-upload" [nzLoading]="busy()"
                [disabled]="loading() || busy() || rows().length >= maxImages" (click)="picker.click()">{{ 'md.itemImage.upload' | translate }}</button>
            </div>
          }
          <nz-table #table data-testid="md-item-image-table" nzSize="small" [nzData]="rows()" [nzLoading]="loading()"
            [nzShowPagination]="false" [nzPageSize]="rows().length || 1" [nzNoResult]="'md.itemImage.empty' | translate">
            <thead><tr><th>{{ 'md.itemImage.preview' | translate }}</th><th>{{ 'md.itemImage.fileName' | translate }}</th>
              <th>{{ 'md.itemImage.size' | translate }}</th><th>{{ 'md.itemImage.sortOrder' | translate }}</th>
              <th>{{ 'md.itemImage.primary' | translate }}</th><th>{{ 'md.itemImage.actions' | translate }}</th></tr></thead>
            <tbody>@for (row of table.data; track row.id) {
              <tr [attr.data-testid]="'md-item-image-row-' + row.id">
                <td class="preview">@if (previews()[row.id]; as src) { <img [src]="src" [alt]="row.fileName" [attr.data-testid]="'md-item-image-preview-' + row.id" /> }
                  @else { <span class="muted">{{ 'md.itemImage.unavailable' | translate }}</span> }</td>
                <td class="name">{{ row.fileName }}</td>
                <td>{{ 'md.itemImage.sizeValue' | translate: { kb: kilobytes(row.sizeBytes), width: row.width, height: row.height } }}</td>
                <td>@if (canEdit()) {
                    <input type="number" min="0" [max]="maxSortOrder" class="sort" [attr.data-testid]="'md-item-image-sort-' + row.id"
                      [attr.aria-label]="'md.itemImage.sortOrder' | translate" [value]="row.sortOrder" [disabled]="busy()"
                      #sortField (change)="changeSortOrder(row, sortField)" />
                  } @else { {{ row.sortOrder }} }</td>
                <td>@if (row.isPrimary) { <nz-tag nzColor="success" [attr.data-testid]="'md-item-image-primary-' + row.id">{{ 'md.itemImage.primary' | translate }}</nz-tag> }
                  @else if (canEdit()) { <button nz-button nzType="link" [disabled]="busy()" [attr.data-testid]="'md-item-image-set-primary-' + row.id"
                    (click)="setPrimary(row)">{{ 'md.itemImage.setPrimary' | translate }}</button> }</td>
                <td>@if (canEdit()) { <button nz-button nzType="link" nzDanger [disabled]="busy()" [attr.data-testid]="'md-item-image-delete-' + row.id"
                  (click)="confirmDelete(row)">{{ 'common.action.delete' | translate }}</button> }</td>
              </tr>
            }</tbody>
          </nz-table>
        </div>
      </ng-container>
    </nz-modal>
  `,
  styles: `
    .item-images { display: grid; gap: 12px; color: var(--erp-color-text); }
    .hint, .muted { margin: 0; color: var(--erp-color-text-secondary); }
    .upload { display: flex; gap: 12px; align-items: center; }
    .preview img { width: 72px; height: 72px; object-fit: contain; border: 1px solid var(--erp-color-border); }
    .name { word-break: break-all; } .sort { width: 88px; }
  `,
})
export class ItemImageDialog {
  readonly item = input<Item | null>(null);
  readonly closed = output<void>();
  readonly maxImages = MAX_IMAGES_PER_ITEM;
  readonly maxSortOrder = MAX_SORT_ORDER;
  private readonly api = inject(ItemImageService);
  private readonly permissions = inject(PermissionService);
  private readonly dialogs = inject(ErpDialogService);
  readonly rows = signal<ItemImage[]>([]);
  readonly previews = signal<Record<number, string>>({});
  readonly loading = signal(false);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly canEdit = computed(() => this.permissions.has('MD.ITEM.EDIT'));
  uploadPrimary = false;
  /** 每次重新載入遞增；切換物料或關閉後，較慢回來的回應不會覆蓋新資料。 */
  private generation = 0;

  constructor() {
    effect(() => {
      const item = this.item();
      untracked(() => {
        this.generation++;
        this.rows.set([]); this.previews.set({}); this.error.set(null); this.busy.set(false); this.uploadPrimary = false;
        if (item) this.reload();
      });
    });
  }

  kilobytes(bytes: number): string { return (bytes / 1024).toFixed(1); }

  reload(): void {
    const item = this.item();
    if (!item) return;
    const generation = ++this.generation;
    this.loading.set(true);
    this.api.list(item.id).subscribe({
      next: (rows) => {
        if (generation !== this.generation) return;
        this.rows.set(rows); this.loading.set(false);
        // 已載入的預覽按 id 保留（內容不變），只下載新圖片
        const kept = this.previews();
        this.previews.set(Object.fromEntries(rows.filter(r => kept[r.id]).map(r => [r.id, kept[r.id]])));
        rows.filter(r => !kept[r.id]).forEach(r => this.loadPreview(item.id, r.id, generation));
      },
      error: (error: unknown) => {
        if (generation !== this.generation) return;
        this.rows.set([]); this.error.set(this.api.errorKey(error)); this.loading.set(false);
      },
    });
  }

  picked(picker: HTMLInputElement): void {
    const file = picker.files?.[0];
    picker.value = ''; // 同一檔案可再選一次
    if (file) this.upload(file);
  }

  upload(file: File): void {
    const item = this.item();
    if (!item || !this.canEdit() || this.busy()) return;
    const invalid = this.rows().length >= MAX_IMAGES_PER_ITEM ? 'errors.MD.ITEM_IMAGE.LIMIT_REACHED' : itemImageFileError(file);
    if (invalid) { this.error.set(invalid); return; }
    this.run(this.api.upload(item.id, file, this.uploadPrimary), () => { this.uploadPrimary = false; });
  }

  changeSortOrder(row: ItemImage, field: HTMLInputElement): void {
    const text = field.value.trim(), value = Number(text);
    if (!/^\d+$/.test(text) || value > MAX_SORT_ORDER) {
      this.error.set('md.itemImage.invalidSortOrder');
      field.value = String(row.sortOrder); // 還原原排序號
      return;
    }
    if (value === row.sortOrder) return;
    this.update(row, { sortOrder: value, isPrimary: row.isPrimary });
  }

  setPrimary(row: ItemImage): void { this.update(row, { sortOrder: row.sortOrder, isPrimary: true }); }

  confirmDelete(row: ItemImage): void {
    const item = this.item();
    if (!item || !this.canEdit() || this.busy()) return;
    this.dialogs.confirm({ titleKey: 'md.itemImage.deleteConfirm', contentKey: 'common.confirm.irreversible',
      params: { name: row.fileName }, okKey: 'common.action.delete', danger: true }).subscribe((ok) => {
      if (ok) this.run(this.api.remove(item.id, row.id, row.version));
    });
  }

  private update(row: ItemImage, body: { sortOrder: number; isPrimary: boolean }): void {
    const item = this.item();
    if (!item || !this.canEdit() || this.busy()) return;
    this.run(this.api.update(item.id, row.id, { ...body, version: row.version }));
  }

  /** 修改類操作：成功後重新載入（主圖與排序可能連帶改變），404／409 也重新載入以取得最新版本。 */
  private run(request: Observable<unknown>, done?: () => void): void {
    const generation = this.generation;
    this.busy.set(true); this.error.set(null);
    request.subscribe({
      next: () => { if (generation !== this.generation) return; this.busy.set(false); done?.(); this.reload(); },
      error: (error: unknown) => {
        if (generation !== this.generation) return;
        this.busy.set(false);
        const key = this.api.errorKey(error);
        this.error.set(key);
        if (key === 'errors.COMMON.CONCURRENCY.CONFLICT' || key === 'errors.MD.ITEM_IMAGE.NOT_FOUND') this.reload();
      },
    });
  }

  private loadPreview(itemId: number, id: number, generation: number): void {
    this.api.content(itemId, id).subscribe({
      next: async (blob) => {
        const url = await toDataUrl(blob);
        if (generation !== this.generation || !url) return;
        this.previews.update(p => ({ ...p, [id]: url }));
      },
      // 預覽載入失敗只顯示「無法預覽」，不影響其他操作
      error: () => undefined,
    });
  }
}
