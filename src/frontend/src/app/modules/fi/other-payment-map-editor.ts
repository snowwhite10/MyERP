import { ChangeDetectionStrategy, Component, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzInputModule } from 'ng-zorro-antd/input';
import { apiErrorTraceId } from '../../core/http/api-error';
import { ApiErrorDisplay } from '../../shared/api-error-display';
import { OtherPaymentMapRow, OtherPaymentService } from './other-payment.service';

@Component({
  selector: 'erp-other-payment-map-editor',
  imports: [FormsModule, TranslatePipe, NzAlertModule, NzButtonModule, NzInputModule, ApiErrorDisplay],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="map-panel" data-testid="fi-op-map-panel" [attr.aria-label]="'fi.otherPayment.mapEditor.title' | translate">
      <header><h2>{{ 'fi.otherPayment.mapEditor.title' | translate }}</h2>
        <button nz-button type="button" (click)="dismissed.emit()">{{ 'fi.otherPayment.close' | translate }}</button></header>
      <p>{{ 'fi.otherPayment.mapEditor.hint' | translate }}</p>
      @if (notice(); as message) { <nz-alert [nzType]="message.warning ? 'warning' : 'success'" [nzMessage]="message.key | translate"
        nzShowIcon role="status" aria-live="polite" data-testid="fi-op-map-notice" /> }
      @if (error()) { <erp-api-error-display [messageKey]="error()!" [traceId]="traceId()" data-testid="fi-op-map-error" /> }
      @if (!rows().length) { <p>{{ 'fi.otherPayment.mapEditor.empty' | translate }}</p> }
      <div class="table-scroll"><table><thead><tr><th>{{ 'fi.otherPayment.category' | translate }}</th>
        <th>{{ 'fi.otherPayment.mapEditor.account' | translate }}</th><th>{{ 'fi.otherPayment.mapEditor.state' | translate }}</th>
        <th>{{ 'fi.otherPayment.mapEditor.action' | translate }}</th></tr></thead><tbody>
        @for (row of rows(); track row.expenseCategoryId) { <tr>
          <td>{{ row.expenseCategoryCode }} · {{ row.expenseCategoryName }}</td>
          <td>{{ row.expenseAccount ? row.expenseAccount.code + ' · ' + row.expenseAccount.name : '—' }}</td>
          <td>{{ (row.expenseCategoryActive ? row.configured ? row.expenseAccount?.isUsable ? 'fi.otherPayment.map.ready' : 'fi.otherPayment.map.accountInvalid' : 'fi.otherPayment.map.missing' : 'fi.otherPayment.map.categoryInactive') | translate }}</td>
          <td>@if (canEdit()) { <button nz-button type="button" [attr.data-testid]="'fi-op-map-edit-' + row.expenseCategoryId"
            (click)="begin(row)">{{ 'fi.otherPayment.edit' | translate }}</button> }</td>
        </tr> }</tbody></table></div>
      @if (selected(); as row) { <form (ngSubmit)="save()" class="edit" novalidate>
        <h3>{{ row.expenseCategoryCode }} · {{ row.expenseCategoryName }}</h3>
        <label>{{ 'fi.otherPayment.mapEditor.accountCode' | translate }}<input nz-input name="accountCode" autocomplete="off"
          spellcheck="false" maxlength="30" [(ngModel)]="code" data-testid="fi-op-map-code" /></label>
        @if (codeError()) { <small role="alert">{{ 'fi.otherPayment.mapEditor.codeInvalid' | translate }}</small> }
        <div class="actions"><button nz-button nzType="primary" type="submit" [disabled]="busy()" data-testid="fi-op-map-save">
          {{ (busy() ? 'fi.otherPayment.saving' : 'fi.otherPayment.mapEditor.save') | translate }}</button>
          <button nz-button type="button" (click)="selected.set(null)">{{ 'fi.otherPayment.cancel' | translate }}</button></div>
      </form> }
    </section>`,
  styles: `:host{display:block;min-width:0}.map-panel{display:grid;gap:16px;padding:24px;border:1px solid var(--erp-color-border);border-radius:8px;background:var(--erp-color-bg-container);min-width:0}header{display:flex;justify-content:space-between;align-items:center;gap:16px}h2{font-size:18px;margin:0;text-wrap:balance}h3{font-size:16px}.table-scroll{overflow-x:auto;overscroll-behavior-inline:contain}table{width:100%;min-width:650px;border-collapse:collapse}th,td{text-align:left;padding:8px;border-bottom:1px solid var(--erp-color-border);overflow-wrap:anywhere}.edit{display:grid;gap:8px;max-width:420px}.edit label{display:grid;gap:4px}.actions{display:flex;gap:8px}small{color:var(--erp-color-error-text)}input:focus-visible,button:focus-visible{outline:2px solid var(--erp-color-primary);outline-offset:2px}`,
})
export class OtherPaymentMapEditor {
  readonly rows = input.required<OtherPaymentMapRow[]>(); readonly canEdit = input(false);
  readonly changed = output<void>(); readonly dismissed = output<void>();
  private readonly api = inject(OtherPaymentService);
  readonly selected = signal<OtherPaymentMapRow | null>(null); readonly busy = signal(false);
  readonly error = signal<string | null>(null); readonly traceId = signal<string | null>(null);
  readonly codeError = signal(false); readonly notice = signal<{ key: string; warning: boolean } | null>(null);
  code = '';
  begin(row: OtherPaymentMapRow): void { if (!this.canEdit()) return; this.selected.set(row);
    this.code = row.expenseAccount?.code ?? ''; this.codeError.set(false); this.error.set(null); this.notice.set(null); }
  save(): void {
    const row = this.selected(); if (!row || !this.canEdit() || this.busy()) return;
    this.codeError.set(!this.code.trim() || this.code.trim().length > 30);
    if (this.codeError()) return;
    this.busy.set(true); this.error.set(null);
    this.api.saveMap(row.expenseCategoryId, row.version, this.code).subscribe({ next: result => {
      this.busy.set(false); this.notice.set({ key: result.changed ? 'fi.otherPayment.mapEditor.saved' : 'fi.otherPayment.mapEditor.unchanged',
        warning: !result.changed }); this.selected.set(null); this.changed.emit();
    }, error: e => { this.busy.set(false); this.error.set(this.api.errorKey(e)); this.traceId.set(apiErrorTraceId(e));
      if (e?.status === 409) { this.selected.set(null); this.changed.emit(); } } });
  }
}
