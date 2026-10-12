import { ChangeDetectionStrategy, Component, effect, inject, input, output, signal, untracked } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzModalModule } from 'ng-zorro-antd/modal';
import { NzTableModule } from 'ng-zorro-antd/table';
import { finalize } from 'rxjs';
import { PartnerKind } from './business-partner.service';
import { PartnerImportResult, PartnerImportService } from './partner-import.service';

/** 客戶／供應商匯入對話框；標題與說明取 `md.{kind}Import`，其餘文字共用 `md.customerImport`。 */
@Component({
  selector: 'erp-partner-import-dialog',
  imports: [TranslatePipe, NzAlertModule, NzButtonModule, NzModalModule, NzTableModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <nz-modal [nzVisible]="open()" [nzTitle]="'md.' + kind() + 'Import.title' | translate" [nzFooter]="null"
      (nzOnCancel)="close()" nzWidth="850px">
      <ng-container *nzModalContent>
        <p>{{ 'md.' + kind() + 'Import.hint' | translate }}</p>
        <div class="controls">
          <button nz-button [attr.data-testid]="id('xlsx')" (click)="download('xlsx')">{{ 'md.customerImport.xlsx' | translate }}</button>
          <button nz-button [attr.data-testid]="id('csv')" (click)="download('csv')">{{ 'md.customerImport.csv' | translate }}</button>
          <input [attr.data-testid]="id('file')" type="file" accept=".xlsx,.csv" (change)="onFile($event)" />
          <button nz-button [attr.data-testid]="id('preview')" [disabled]="!file() || busy()" (click)="preview()">
            {{ 'md.customerImport.preview' | translate }}</button>
        </div>
        @if (file()) { <p>{{ file()!.name }}</p> }
        @if (error()) { <nz-alert nzType="error" nzShowIcon [nzMessage]="error()! | translate" /> }
        @if (result(); as previewResult) {
          <p [attr.data-testid]="id('summary')">{{ 'md.customerImport.summary' | translate: {
            create: previewResult.createdCount, skip: previewResult.skippedCount, error: previewResult.errorCount } }}</p>
          <nz-table #table [nzData]="previewResult.rows" [nzPageSize]="20" nzSize="small">
            <thead><tr><th>{{ 'md.customerImport.row' | translate }}</th><th>{{ 'md.' + kind() + '.code' | translate }}</th>
              <th>{{ 'md.' + kind() + '.name' | translate }}</th><th>{{ 'md.customerImport.status' | translate }}</th>
              <th>{{ 'md.customerImport.errors' | translate }}</th></tr></thead>
            <tbody>@for (row of table.data; track row.rowNumber) {
              <tr [attr.data-testid]="id('row-' + row.rowNumber)">
                <td>{{ row.rowNumber }}</td><td>{{ row.code }}</td><td>{{ row.name }}</td>
                <td>{{ ('md.customerImport.' + row.status.toLowerCase()) | translate }}</td>
                <td>@for (issue of row.errors; track $index) {
                  <span>{{ issue.field }}: {{ ('md.customerImport.fieldError.' + issue.code) | translate }}{{ $last ? '' : '; ' }}</span>
                }</td>
              </tr>
            }</tbody>
          </nz-table>
        }
        @if (completed()) { <nz-alert nzType="success" nzShowIcon [nzMessage]="'md.customerImport.completed' | translate" /> }
        <div class="footer">
          <button nz-button (click)="close()">{{ 'common.action.cancel' | translate }}</button>
          <button nz-button nzType="primary" [attr.data-testid]="id('commit')" [disabled]="!canCommit()"
            [nzLoading]="busy()" (click)="commit()">{{ 'md.customerImport.commit' | translate }}</button>
        </div>
      </ng-container>
    </nz-modal>
  `,
  styles: `.controls, .footer { display:flex; flex-wrap:wrap; gap:8px; align-items:center; margin:12px 0; }
    .footer { justify-content:flex-end; } input[type=file] { max-width:280px; }`,
})
export class PartnerImportDialog {
  readonly kind = input<PartnerKind>('customer');
  readonly open = input(false);
  readonly closed = output<void>();
  readonly imported = output<void>();
  private readonly api = inject(PartnerImportService);
  readonly file = signal<File | null>(null);
  readonly result = signal<PartnerImportResult | null>(null);
  readonly error = signal<string | null>(null);
  readonly busy = signal(false);
  readonly completed = signal(false);
  private revision = 0;
  private previewedFile: File | null = null;

  constructor() { effect(() => { if (!this.open()) untracked(() => this.reset()); }); }
  /** 此對話框的 data-testid，例如 `md-supplier-import-preview`。 */
  id(key: string): string { return `md-${this.kind()}-import-${key}`; }
  canCommit(): boolean { return !!this.result() && this.result()!.errorCount === 0 && this.file() === this.previewedFile
    && !this.busy() && !this.completed(); }
  selectFile(file: File | null): void {
    this.revision++; this.file.set(file); this.result.set(null); this.error.set(null); this.completed.set(false);
    this.previewedFile = null; this.busy.set(false);
  }
  onFile(event: Event): void { this.selectFile((event.target as HTMLInputElement).files?.[0] ?? null); }
  preview(): void {
    const file = this.file(); if (!file || this.busy()) return;
    const revision = ++this.revision; const kind = this.kind();
    this.busy.set(true); this.error.set(null); this.result.set(null); this.previewedFile = null; this.completed.set(false);
    this.api.preview(kind, file).pipe(finalize(() => { if (revision === this.revision) this.busy.set(false); })).subscribe({
      next: (result) => { if (revision === this.revision) { this.result.set(result); this.previewedFile = file; } },
      error: (error: unknown) => { if (revision === this.revision) this.error.set(this.api.errorKey(kind, error)); },
    });
  }
  commit(): void {
    if (!this.canCommit()) return;
    const file = this.file()!; const revision = ++this.revision; const kind = this.kind();
    this.busy.set(true); this.error.set(null);
    this.api.commit(kind, file).pipe(finalize(() => { if (revision === this.revision) this.busy.set(false); })).subscribe({
      next: (result) => { if (revision === this.revision) { this.result.set(result); this.completed.set(true); this.imported.emit(); } },
      error: (error: unknown) => { if (revision === this.revision) { this.error.set(this.api.errorKey(kind, error));
        this.previewedFile = null; this.result.set(null); } },
    });
  }
  download(format: 'xlsx' | 'csv'): void {
    const kind = this.kind();
    this.api.template(kind, format).subscribe({ next: (blob) => {
      const url = URL.createObjectURL(blob); const link = document.createElement('a');
      link.href = url; link.download = `${kind}-import.${format}`; link.click(); URL.revokeObjectURL(url);
    }, error: (error: unknown) => this.error.set(this.api.errorKey(kind, error)) });
  }
  close(): void { this.reset(); this.closed.emit(); }
  private reset(): void { this.selectFile(null); this.busy.set(false); }
}
