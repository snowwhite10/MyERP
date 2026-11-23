import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, HostListener, Injector, afterNextRender, effect, inject, signal,
  untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { TimeoutError } from 'rxjs';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { PermissionService } from '../../core/auth/permission.service';
import { CompanyContextService } from '../../core/company/company-context.service';
import { UNKNOWN_ERROR_KEY, apiErrorKey, apiErrorTraceId } from '../../core/http/api-error';
import { ApiErrorDisplay } from '../../shared/api-error-display';
import { formatDecimalText } from '../../shared/decimal-text';
import { ErpDialogService } from '../../shared/dialogs/dialog.service';
import { DeliveryReferenceOption, referenceLabel } from './delivery.service';
import { invoiceMoney } from './invoice.service';
import { DroppedReturnPart, RETURN_REMARK_MAX, ReturnDraftForm, ReturnLineForm, ReturnProblems, billedKey, draftFromSaved,
  draftFromSource, earliestReturnDate,
  lineTotal, matchesReturnReceipt, mergeDraft, returnRefusal, returnRequest, sourceLine, totalKey, unbilledKey,
  validateReturnDraft } from './return-form';
import { SalesReturnDetail, SalesReturnDraftRequest, SalesReturnService, SalesReturnSourceLine, SalesReturnSourceOrder,
  SalesReturnSourceOrderSummary, newReturnRequestKey } from './return.service';

/** Error codes the create form explains in the user's language (others fall back to the generic message). */
export const RETURN_CREATE_ERROR_CODES = ['SAL.RETURN.SOURCE_NOT_FOUND', 'SAL.RETURN.SOURCE_MIXED',
  'SAL.RETURN.ORDER_NOT_APPROVED', 'SAL.RETURN.DELIVERY_NOT_POSTED', 'SAL.RETURN.INVOICE_LINE_INVALID',
  'SAL.RETURN.DATE_BEFORE_DELIVERY', 'SAL.RETURN.DATE_BEFORE_INVOICE', 'SAL.RETURN.TRACKING_UNSUPPORTED',
  'SAL.RETURN.RETURN_EXCEEDED', 'SAL.RETURN.UNBILLED_EXCEEDED', 'SAL.RETURN.BILLED_EXCEEDED',
  'SAL.RETURN.REQUEST_CONFLICT', 'SAL.RETURN.SOURCE_INVALID', 'MD.WAREHOUSE.NOT_FOUND', 'MD.WAREHOUSE.INACTIVE',
  'MD.STORAGE_LOCATION.NOT_FOUND', 'MD.STORAGE_LOCATION.INACTIVE', 'COMMON.PERMISSION.DENIED'] as const;
/** PUT adds these to the CREATE codes (ADR-192/196). */
export const RETURN_EDIT_ERROR_CODES = [...RETURN_CREATE_ERROR_CODES, 'SAL.RETURN.NOT_FOUND', 'SAL.RETURN.INVALID_STATUS',
  'SAL.RETURN.REQUEST_KEY_IMMUTABLE'];
/** Only these saved states may be replaced; Rejected is revised back to Draft (ADR-184). */
const EDITABLE = ['Draft', 'Rejected'];
const LOAD_ERROR_CODES = ['SAL.RETURN.NOT_FOUND', 'COMMON.PERMISSION.DENIED'];
const SOURCE_ERROR_CODES = ['SAL.RETURN.SOURCE_NOT_FOUND', 'SAL.RETURN.ORDER_NOT_APPROVED', 'COMMON.PERMISSION.DENIED'];
/** Refusals where re-reading the advisory balances helps. */
const RELOADABLE = ['RETURN_EXCEEDED', 'UNBILLED_EXCEEDED', 'BILLED_EXCEEDED', 'DATE_BEFORE_DELIVERY', 'DATE_BEFORE_INVOICE',
  'DELIVERY_NOT_POSTED', 'SOURCE_NOT_FOUND', 'INVOICE_LINE_INVALID'].map(code => `errors.SAL.RETURN.${code}`);

function positiveId(value: string | null): number | undefined {
  const id = Number(value);
  return value && /^\d+$/.test(value) && Number.isSafeInteger(id) && id > 0 ? id : undefined;
}
const blank = (): ReturnDraftForm => ({ orderId: null, businessDate: '', remark: '', lines: [] });
/** A line's destination IDs with the MD labels to show (null code = soft-deleted, active false = inactive). */
export interface ReturnDestination {
  warehouseId: number; warehouseCode?: string | null; warehouseName?: string | null; warehouseActive?: boolean | null;
  locationId: number | null; locationCode?: string | null; locationName?: string | null; locationActive?: boolean | null;
}
const pickDestination = (from: ReturnDestination): ReturnDestination => ({ warehouseId: from.warehouseId,
  warehouseCode: from.warehouseCode, warehouseName: from.warehouseName, warehouseActive: from.warehouseActive,
  locationId: from.locationId ?? null, locationCode: from.locationCode, locationName: from.locationName,
  locationActive: from.locationActive });
const REFERENCE_COLUMNS = [
  { key: 'code', labelKey: 'sd.quotationCreate.code', value: (row: DeliveryReferenceOption) => row.code },
  { key: 'name', labelKey: 'sd.quotationCreate.name', value: (row: DeliveryReferenceOption) => row.name }];

/**
 * Sales Return Create Interface 2.17 (REQ_F4112.7.1, WU-SD-008b3a): pick an Approved order with returnable Posted
 * delivery lines, choose lines, split each returned quantity into an uninvoiced part and parts credited against Posted
 * invoice lines, and save a Draft. Nothing is received or credited until the return is submitted and finally approved.
 * The request key is kept while the outcome is unknown, so resending cannot create twice (ADR-185).
 * `/sal/returns/:id/edit` (WU-SD-008b3b2a, ADR-196) reuses the form for a saved Draft or Rejected return: the order is
 * fixed, the saved split is filled in, and PUT replaces it by the loaded version with the saved request key.
 * Each selected line's destination defaults to the delivery's (or the saved) warehouse/location and can be re-picked
 * from the active MD options under SAL.RETURN.CREATE or EDIT (WU-SD-008b3b2b, ADR-197); the server revalidates it.
 */
@Component({
  selector: 'erp-sales-return-create-page',
  imports: [FormsModule, RouterLink, TranslatePipe, NzAlertModule, NzButtonModule, ApiErrorDisplay],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="create" data-testid="sd-return-create">
      <header><h1>{{ (editId ? 'sd.returnEdit.title' : 'sd.returnCreate.title') | translate }}</h1>
        @if (permissions.has('SAL.RETURN.VIEW')) { <a routerLink="/sal/returns" [queryParams]="editId > 0 ? { open: editId } : {}" data-testid="sd-return-create-back">{{ 'sd.returnCreate.back' | translate }}</a> }</header>
      <nz-alert nzType="info" nzShowIcon [nzMessage]="(editId ? 'sd.returnEdit.intro' : 'sd.returnCreate.intro') | translate" data-testid="sd-return-create-intro" />
      @if (editing(); as current) { <p data-testid="sd-return-edit-identity"><span translate="no">{{ current.documentNo }}</span>{{ ' · ' + ('sd.return.version' | translate) + ' ' + current.version }}</p>
        @if (current.status === 'Rejected' && !saved()) { <nz-alert nzType="warning" nzShowIcon data-testid="sd-return-edit-rejected" [nzMessage]="'sd.returnEdit.rejectedHint' | translate" /> } }
      @if (editLoading()) { <p role="status" data-testid="sd-return-edit-loading">{{ 'sd.returnEdit.loading' | translate }}</p> }
      @if (editError()) { <erp-api-error-display data-testid="sd-return-edit-load-error" [messageKey]="editError()!" [traceId]="editTraceId()" />
        <div class="actions"><button nz-button type="button" (click)="loadSaved()" data-testid="sd-return-edit-retry">{{ 'sd.returnCreate.retry' | translate }}</button></div> }
      @if (blocked()) { <nz-alert nzType="warning" nzShowIcon role="alert" data-testid="sd-return-edit-blocked" [nzMessage]="'sd.returnEdit.blocked' | translate: { status: (blocked()! | translate) }" /> }
      @if (conflict() || unavailable()) { <nz-alert nzType="warning" nzShowIcon role="alert" data-testid="sd-return-edit-conflict"
          [nzMessage]="(conflict() ? 'sd.returnEdit.conflict' : 'sd.returnEdit.unavailable') | translate" />
        <div class="actions"><button nz-button type="button" (click)="refreshSaved()" data-testid="sd-return-edit-refresh">{{ 'sd.returnEdit.refresh' | translate }}</button></div> }
      @if (noChange()) { <nz-alert nzType="warning" nzShowIcon role="status" data-testid="sd-return-edit-no-change" [nzMessage]="'sd.returnEdit.noChange' | translate" /> }
      @if (dropped().length && !saved()) { <div class="dropped" role="alert" data-testid="sd-return-edit-dropped">
        <nz-alert nzType="warning" nzShowIcon [nzMessage]="'sd.returnEdit.dropped' | translate" />
        <ul>@for (part of dropped(); track $index) { <li [attr.data-testid]="'sd-return-edit-dropped-' + $index">{{ (part.invoiceDocumentNo ? 'sd.returnEdit.droppedCredit' : 'sd.returnEdit.droppedLine') | translate: { line: part.lineNo, delivery: part.deliveryDocumentNo, item: part.itemCode, invoice: part.invoiceDocumentNo, quantity: decimal(part.quantity) } }}</li> }</ul></div> }
      @if (saved(); as value) { <nz-alert nzType="success" nzShowIcon data-testid="sd-return-create-success"
          [nzMessage]="successKey() | translate: { number: value.documentNo }" />
        <p data-testid="sd-return-create-receipt" translate="no">{{ value.documentNo }} · {{ value.orderDocumentNo }} · {{ value.currencyCode }} {{ money(value.originalGross) }}</p>
        @if (!editId) { <div class="actions"><button nz-button nzType="primary" type="button" (click)="reset()" data-testid="sd-return-create-another">{{ 'sd.returnCreate.another' | translate }}</button></div> } }
      @if (uncertain()) { <nz-alert nzType="warning" nzShowIcon role="alert" data-testid="sd-return-create-uncertain"
          [nzMessage]="'sd.returnCreate.uncertain' | translate" [nzDescription]="(editId ? 'sd.returnEdit.uncertainHint' : 'sd.returnCreate.uncertainHint') | translate" />
        @if (traceId()) { <p class="hint" data-testid="sd-return-create-uncertain-trace">{{ 'sd.returnCreate.traceId' | translate: { id: traceId() } }}</p> }
        <div class="actions">@if (editId) { <button nz-button nzType="primary" type="button" [nzLoading]="saving()" (click)="checkSaved()" data-testid="sd-return-edit-check">{{ 'sd.returnEdit.check' | translate }}</button> }
          @else { <button nz-button nzType="primary" type="button" [nzLoading]="saving()" (click)="resend()" data-testid="sd-return-create-resend">{{ 'sd.returnCreate.resend' | translate }}</button>
            @if (permissions.has('SAL.RETURN.VIEW')) { <a nz-button routerLink="/sal/returns" data-testid="sd-return-create-check">{{ 'sd.returnCreate.checkList' | translate }}</a> } }</div> }
      @if (error()) { <erp-api-error-display data-testid="sd-return-create-error" [messageKey]="error()!" [traceId]="traceId()" />
        @if (canReload()) { <button nz-button type="button" (click)="reloadBalance()" data-testid="sd-return-create-reload">{{ 'sd.returnCreate.reload' | translate }}</button> } }
      @if (!saved() && (!editId || editing())) {
        <div class="field"><span id="sd-return-create-order-label">{{ 'sd.returnCreate.order' | translate }} <span aria-hidden="true">*</span></span><div class="picker">
          <span data-testid="sd-return-create-order" translate="no">{{ source() ? source()!.documentNo + ' · ' + source()!.customerCode + ' · ' + source()!.customerName : ('sd.returnCreate.noOrder' | translate) }}</span>
          @if (!editId) { <button nz-button type="button" (click)="pickOrder()" [disabled]="locked()" aria-describedby="sd-return-create-order-label" data-testid="sd-return-create-order-pick">{{ 'sd.returnCreate.chooseOrder' | translate }}</button> }</div>
          @if (problem('orderId')) { <small role="alert">{{ 'sd.returnCreate.validation.order' | translate }}</small> }</div>
        @if (orderLoading()) { <p role="status">{{ 'sd.returnCreate.loadingOrder' | translate }}</p> }
        @if (orderError()) { <erp-api-error-display [messageKey]="orderError()!" [traceId]="orderTraceId()" data-testid="sd-return-create-order-error" />
          @if (draft.orderId) { <button nz-button type="button" (click)="reloadBalance()" data-testid="sd-return-create-order-retry">{{ 'sd.returnCreate.retry' | translate }}</button> } }
        @if (source(); as order) {
          <dl class="facts" data-testid="sd-return-create-source">
            <div><dt>{{ 'sd.return.customer' | translate }}</dt><dd>{{ order.customerCode }} · {{ order.customerName }}</dd></div>
            <div><dt>{{ 'sd.return.currency' | translate }}</dt><dd>{{ order.currencyCode }} → {{ order.baseCurrencyCode }}</dd></div>
            <div><dt>{{ 'sd.return.exchangeRate' | translate }}</dt><dd data-testid="sd-return-create-rate">{{ decimal(order.exchangeRate) }}</dd></div>
          </dl>
          @if (!order.lines.length && !orderLoading()) { <nz-alert nzType="warning" nzShowIcon data-testid="sd-return-create-no-lines" [nzMessage]="'sd.returnCreate.noOpenLines' | translate" /> }
          @if (order.lines.length) { <form (ngSubmit)="save()" novalidate data-testid="sd-return-create-form">
            <div class="row">
              <label class="field date"><span>{{ 'sd.return.date' | translate }} <span aria-hidden="true">*</span></span>
                <input type="date" name="businessDate" autocomplete="off" [(ngModel)]="draft.businessDate" (ngModelChange)="revalidate()" [disabled]="locked()"
                  [attr.aria-invalid]="problem('businessDate') ? 'true' : null" data-testid="sd-return-create-date" />
                @if (earliestDate()) { <small class="hint" data-testid="sd-return-create-date-hint">{{ 'sd.returnCreate.dateHint' | translate: { date: earliestDate() } }}</small> }
                @if (problem('businessDate'); as reason) { <small role="alert" data-testid="sd-return-create-date-error">{{ ('sd.returnCreate.validation.' + reason) | translate: { date: earliestDate() } }}</small> }</label>
              <label class="field remark"><span>{{ 'sd.return.remark' | translate }}</span>
                <textarea name="remark" rows="2" autocomplete="off" [maxlength]="remarkMax" [(ngModel)]="draft.remark" (ngModelChange)="revalidate()" [disabled]="locked()"
                  [attr.aria-invalid]="problem('remark') ? 'true' : null" data-testid="sd-return-create-remark"></textarea>
                @if (problem('remark')) { <small role="alert">{{ 'sd.returnCreate.validation.remarkLength' | translate: { max: remarkMax } }}</small> }</label>
            </div>
            <p class="hint">{{ 'sd.returnCreate.balanceHint' | translate }}</p>
            <div class="scroll"><table class="lines" data-testid="sd-return-create-lines"><caption class="sr-only">{{ 'sd.return.lines' | translate }}</caption>
              <thead><tr><th scope="col"><span class="sr-only">{{ 'sd.returnCreate.select' | translate }}</span></th>
                <th scope="col">{{ 'sd.return.delivery' | translate }}</th><th scope="col">{{ 'sd.return.orderLine' | translate }}</th>
                <th scope="col">{{ 'sd.return.item' | translate }}</th><th scope="col">{{ 'sd.return.destination' | translate }}</th>
                <th scope="col" class="number">{{ 'sd.returnCreate.returnable' | translate }}</th>
                <th scope="col">{{ 'sd.returnCreate.split' | translate }}</th><th scope="col" class="number">{{ 'sd.return.quantity' | translate }}</th></tr></thead>
              <tbody>@for (line of draft.lines; track line.deliveryLineId) { @let facts = factsOf(line.deliveryLineId);
                <tr [class.failed]="lineError()?.deliveryLineId === line.deliveryLineId" [attr.data-testid]="'sd-return-create-line-' + line.deliveryLineId">
                  <td><input type="checkbox" [name]="'selected-' + line.deliveryLineId" [(ngModel)]="line.selected" (ngModelChange)="revalidate()"
                    [disabled]="locked() || !!facts?.trackingUnsupported"
                    [attr.aria-label]="'sd.returnCreate.selectLine' | translate: { delivery: facts?.deliveryDocumentNo, line: facts?.deliveryLineNo }"
                    [attr.aria-describedby]="facts?.trackingUnsupported ? 'sd-return-create-tracking-' + line.deliveryLineId : null"
                    [attr.data-testid]="'sd-return-create-select-' + line.deliveryLineId" /></td>
                  <td class="nowrap"><span translate="no">{{ facts?.deliveryDocumentNo }} #{{ facts?.deliveryLineNo }}</span><span class="sub">{{ facts?.deliveryDate }}</span></td>
                  <td>{{ facts?.orderLineNo }}</td>
                  <td><span translate="no">{{ facts?.itemCode }}</span> · {{ facts?.itemName }}
                    @if (facts?.trackingUnsupported) { <small class="hint" [id]="'sd-return-create-tracking-' + line.deliveryLineId" [attr.data-testid]="'sd-return-create-tracking-' + line.deliveryLineId">{{ 'sd.returnCreate.trackingUnsupported' | translate }}</small> }</td>
                  <td class="destination" [attr.data-testid]="'sd-return-create-destination-' + line.deliveryLineId">@let place = places().get(line.deliveryLineId);
                    @if (place) { <span class="sub" [attr.data-testid]="'sd-return-create-destination-text-' + line.deliveryLineId">{{ destination(place) }}</span>
                      @if (placeUnavailable(place)) { <small class="warning" [attr.data-testid]="'sd-return-create-destination-warning-' + line.deliveryLineId">{{ 'sd.returnCreate.destinationUnavailable' | translate }}</small> } }
                    @if (facts && !facts.trackingUnsupported) { @let names = { delivery: facts.deliveryDocumentNo, line: facts.deliveryLineNo };
                      <div class="picker">
                        <button nz-button nzSize="small" type="button" (click)="pickWarehouse(line.deliveryLineId)" [disabled]="locked() || !line.selected"
                          [attr.aria-label]="'sd.returnCreate.chooseWarehouseFor' | translate: names" [attr.data-testid]="'sd-return-create-warehouse-pick-' + line.deliveryLineId">{{ 'sd.returnCreate.chooseWarehouse' | translate }}</button>
                        <button nz-button nzSize="small" type="button" (click)="pickLocation(line.deliveryLineId)" [disabled]="locked() || !line.selected"
                          [attr.aria-label]="'sd.returnCreate.chooseLocationFor' | translate: names" [attr.data-testid]="'sd-return-create-location-pick-' + line.deliveryLineId">{{ 'sd.returnCreate.chooseLocation' | translate }}</button>
                        @if (line.locationId != null) { <button nz-button nzType="link" nzSize="small" type="button" (click)="clearLocation(line.deliveryLineId)" [disabled]="locked() || !line.selected"
                          [attr.aria-label]="'sd.returnCreate.clearLocationFor' | translate: names" [attr.data-testid]="'sd-return-create-location-clear-' + line.deliveryLineId">{{ 'sd.returnCreate.clearLocation' | translate }}</button> }
                      </div> }</td>
                  <td class="number" [attr.data-testid]="'sd-return-create-returnable-' + line.deliveryLineId">{{ facts ? decimal(facts.returnableQuantity) : '' }}</td>
                  <td class="split">
                    <label class="portion"><span>{{ 'sd.returnCreate.unbilled' | translate: { remaining: facts ? decimal(facts.unbilledRemainingQuantity) : '' } }}</span>
                      <input inputmode="decimal" autocomplete="off" spellcheck="false" placeholder="0" [name]="'unbilled-' + line.deliveryLineId" [(ngModel)]="line.unbilledQuantity" (ngModelChange)="revalidate()"
                        [disabled]="locked() || !line.selected" [attr.aria-invalid]="problem(unbilledKey(line.deliveryLineId)) ? 'true' : null"
                        [attr.data-testid]="'sd-return-create-unbilled-' + line.deliveryLineId" /></label>
                    @if (line.selected && problem(unbilledKey(line.deliveryLineId)); as reason) { <small role="alert" [attr.data-testid]="'sd-return-create-unbilled-error-' + line.deliveryLineId">{{ ('sd.returnCreate.validation.' + reason) | translate: { remaining: facts ? decimal(facts.unbilledRemainingQuantity) : '' } }}</small> }
                    @for (portion of line.billed; track portion.invoiceLineId) { @let invoice = invoiceOf(facts, portion.invoiceLineId);
                      <label class="portion"><span>{{ 'sd.returnCreate.billedFrom' | translate: { invoice: invoice?.invoiceDocumentNo, date: invoice?.invoiceDate, remaining: invoice ? decimal(invoice.remainingQuantity) : '' } }}</span>
                        <input inputmode="decimal" autocomplete="off" spellcheck="false" placeholder="0" [name]="'billed-' + line.deliveryLineId + '-' + portion.invoiceLineId" [(ngModel)]="portion.quantity" (ngModelChange)="revalidate()"
                          [disabled]="locked() || !line.selected" [attr.aria-invalid]="problem(billedKey(line.deliveryLineId, portion.invoiceLineId)) ? 'true' : null"
                          [attr.data-testid]="'sd-return-create-billed-' + line.deliveryLineId + '-' + portion.invoiceLineId" /></label>
                      @if (line.selected && problem(billedKey(line.deliveryLineId, portion.invoiceLineId)); as reason) { <small role="alert" [attr.data-testid]="'sd-return-create-billed-error-' + line.deliveryLineId + '-' + portion.invoiceLineId">{{ ('sd.returnCreate.validation.' + reason) | translate: { remaining: invoice ? decimal(invoice.remainingQuantity) : '' } }}</small> } }
                    @if (lineError()?.deliveryLineId === line.deliveryLineId) { <small role="alert" [attr.data-testid]="'sd-return-create-line-error-' + line.deliveryLineId">{{ lineError()!.key | translate: { remaining: lineError()!.remaining } }}</small> }</td>
                  <td class="number" [attr.data-testid]="'sd-return-create-total-' + line.deliveryLineId">{{ line.selected ? total(line) : '' }}
                    @if (line.selected && problem(totalKey(line.deliveryLineId)); as reason) { <small role="alert" [attr.data-testid]="'sd-return-create-total-error-' + line.deliveryLineId">{{ ('sd.returnCreate.validation.' + reason) | translate: { remaining: facts ? decimal(facts.returnableQuantity) : '' } }}</small> }</td>
                </tr> }</tbody></table></div>
            <p role="status" data-testid="sd-return-create-count">{{ 'sd.returnCreate.selectedCount' | translate: { count: selectedCount(), total: draft.lines.length } }}</p>
            @if (problem('lines'); as reason) { <p role="alert" data-testid="sd-return-create-lines-error">{{ ('sd.returnCreate.validation.' + reason) | translate }}</p> }
            @if (submitted() && count()) { <p role="alert" data-testid="sd-return-create-errors">{{ 'sd.returnCreate.errors' | translate: { count: count() } }}</p> }
            <div class="actions"><button nz-button nzType="primary" type="submit" [nzLoading]="saving()" [disabled]="locked()" data-testid="sd-return-create-submit">{{ (editId ? 'sd.returnEdit.save' : 'sd.returnCreate.submit') | translate }}</button></div>
          </form> }
        }
      }
    </section>`,
  styles: `.create{display:grid;gap:16px;min-width:0;color:var(--erp-color-text)}header{display:flex;justify-content:space-between;gap:12px;align-items:center;flex-wrap:wrap}h1{margin:0}form{display:grid;gap:12px;min-width:0}.row{display:flex;gap:16px;flex-wrap:wrap;align-items:start}.field{display:grid;gap:4px;min-width:0}.field.date{width:240px}.field.remark{flex:1 1 320px}.picker,.actions{display:flex;gap:8px;align-items:center;flex-wrap:wrap}.picker span,.facts dd{overflow-wrap:anywhere}input:not([type=checkbox]),textarea{width:100%;min-height:32px;padding:4px 8px;background:var(--erp-color-bg-container);color:var(--erp-color-text);border:1px solid var(--erp-color-border);border-radius:4px}input[type=checkbox]{width:16px;height:16px}input:disabled,textarea:disabled{opacity:.55;cursor:not-allowed}.facts{display:grid;grid-template-columns:repeat(auto-fill,minmax(180px,1fr));gap:8px 16px;margin:0}.facts dt{color:var(--erp-color-text-secondary)}.facts dd{margin:0}.scroll{overflow-x:auto;min-width:0}table.lines{width:100%;min-width:1080px;border-collapse:collapse}.lines th,.lines td{padding:8px;border-bottom:1px solid var(--erp-color-border);text-align:left;vertical-align:top}.lines th,.lines td.nowrap,.lines td.number{white-space:nowrap}.split{min-width:260px;display:grid;gap:6px}.lines td.destination{min-width:220px}.destination .picker{margin-top:6px;gap:4px}.portion{display:grid;gap:2px}.portion span,.sub{display:block;color:var(--erp-color-text-secondary)}tr.failed td{background:var(--erp-color-error-bg,transparent)}.number{text-align:right!important;font-variant-numeric:tabular-nums}small,p[role=alert]{color:var(--erp-color-error-text)}small{display:block;margin-top:4px;white-space:normal}small.hint,.hint{color:var(--erp-color-text-secondary);margin:0}.dropped{display:grid;gap:4px}.dropped ul{margin:0;padding-left:24px}.sr-only{position:absolute;width:1px;height:1px;overflow:hidden;clip:rect(0 0 0 0);white-space:nowrap}p{margin:0}button:focus-visible,input:focus-visible,textarea:focus-visible,a:focus-visible{outline:2px solid var(--erp-color-primary);outline-offset:2px}`,
})
export class SalesReturnCreatePage {
  private readonly returns = inject(SalesReturnService);
  private readonly dialogs = inject(ErpDialogService);
  private readonly company = inject(CompanyContextService);
  private readonly router = inject(Router);
  readonly permissions = inject(PermissionService);
  private readonly translate = inject(TranslateService);
  private readonly destroy = inject(DestroyRef);
  private readonly host: ElementRef<HTMLElement> = inject(ElementRef);
  private readonly injector = inject(Injector);
  private readonly route = inject(ActivatedRoute);
  /** 0 on `/new`; the return ID on `/:id/edit` (-1 when the ID is not a positive integer). */
  readonly editId = this.route.snapshot.paramMap.has('id') ? (positiveId(this.route.snapshot.paramMap.get('id')) ?? -1) : 0;
  readonly remarkMax = RETURN_REMARK_MAX;
  readonly unbilledKey = unbilledKey; readonly billedKey = billedKey; readonly totalKey = totalKey;
  draft = blank();
  readonly source = signal<SalesReturnSourceOrder | null>(null);
  readonly problems = signal<ReturnProblems>({}); readonly submitted = signal(false);
  readonly orderLoading = signal(false); readonly orderError = signal<string | null>(null); readonly orderTraceId = signal<string | null>(null);
  readonly saving = signal(false); readonly saved = signal<SalesReturnDetail | null>(null); readonly replayed = signal(false);
  readonly uncertain = signal(false); readonly error = signal<string | null>(null); readonly traceId = signal<string | null>(null);
  readonly lineError = signal<{ deliveryLineId: number; key: string; remaining: string } | null>(null);
  readonly canReload = signal(false);
  /** Edit mode: the saved return being replaced (its version and request key are sent back). */
  readonly editing = signal<SalesReturnDetail | null>(null);
  readonly editLoading = signal(false); readonly editError = signal<string | null>(null); readonly editTraceId = signal<string | null>(null);
  /** Status label key when the saved return is no longer Draft or Rejected. */
  readonly blocked = signal<string | null>(null);
  readonly conflict = signal(false); readonly unavailable = signal(false); readonly noChange = signal(false);
  readonly dropped = signal<DroppedReturnPart[]>([]);
  /** Destination labels per delivery line (ADR-197); the IDs sent are on the form lines. */
  readonly places = signal(new Map<number, ReturnDestination>());
  /** JSON of the request the loaded form would send; equal at save time means nothing changed. */
  private baseline = '';
  /** Bumped by every redraw-relevant change of `draft` (a plain object bound with ngModel). */
  private readonly tick = signal(0);
  private epoch = 0;
  private requestKey = newReturnRequestKey();
  /** The exact body of the last POST; resent unchanged while the outcome is unknown. */
  private sent: SalesReturnDraftRequest | null = null;
  constructor() {
    this.destroy.onDestroy(() => this.epoch++);
    effect(() => { this.company.current(); untracked(() => { this.reset(); if (this.editId) this.loadSaved(); }); });
  }
  canLeave(): boolean { return !this.dirty() || window.confirm(this.translate.instant('sd.returnCreate.unsaved')); }
  @HostListener('window:beforeunload', ['$event']) beforeUnload(event: BeforeUnloadEvent): void { if (this.dirty()) event.preventDefault(); }
  private dirty(): boolean {
    if (this.saved()) return false;
    if (this.uncertain()) return true;
    if (!this.editId) return this.draft.orderId != null;
    return !!this.editing() && !!this.source() && this.requestJson() !== this.baseline;
  }
  locked(): boolean { return this.saving() || this.uncertain() || !!this.saved() || this.orderLoading() || this.editLoading(); }
  successKey(): string {
    if (!this.editId) return this.replayed() ? 'sd.returnCreate.replayed' : 'sd.returnCreate.saved';
    return this.editing()?.status === 'Rejected' ? 'sd.returnEdit.revised' : 'sd.returnEdit.saved';
  }
  private requestJson(): string { return JSON.stringify(returnRequest(this.draft, this.editing()?.requestKey ?? '')); }
  problem(name: string) { return this.problems()[name]; }
  count(): number { return Object.keys(this.problems()).length; }
  decimal(value: string): string { return formatDecimalText(String(value), this.translate.getCurrentLang() || undefined); }
  money(value: string): string { return invoiceMoney(value); }
  factsOf(deliveryLineId: number): SalesReturnSourceLine | undefined { return sourceLine(this.source(), deliveryLineId); }
  invoiceOf(facts: SalesReturnSourceLine | undefined, invoiceLineId: number) {
    return facts?.invoiceLines.find(invoice => invoice.invoiceLineId === invoiceLineId);
  }
  /** The line's destination (delivery default, saved or picked); a soft-deleted one shows its ID. */
  destination(place: ReturnDestination): string {
    const warehouse = referenceLabel(place.warehouseCode, place.warehouseName)
      || this.translate.instant('sd.return.deletedWarehouse', { id: place.warehouseId });
    const inactive = this.translate.instant('sd.return.inactiveReference');
    let text = warehouse + (place.warehouseActive === false ? inactive : '');
    if (place.locationId != null)
      text += ' / ' + (referenceLabel(place.locationCode, place.locationName)
        || this.translate.instant('sd.return.deletedLocation', { id: place.locationId })) + (place.locationActive === false ? inactive : '');
    return text;
  }
  /** Inactive or soft-deleted: CREATE/PUT would refuse it (MD.WAREHOUSE.* / MD.STORAGE_LOCATION.*), so the user re-picks. */
  placeUnavailable(place: ReturnDestination): boolean {
    return !place.warehouseCode || place.warehouseActive === false ||
      (place.locationId != null && (!place.locationCode || place.locationActive === false));
  }
  private lineOf(deliveryLineId: number): ReturnLineForm | undefined {
    return this.draft.lines.find(line => line.deliveryLineId === deliveryLineId);
  }
  private setPlace(deliveryLineId: number, place: ReturnDestination): void {
    this.places.update(map => new Map(map).set(deliveryLineId, place));
  }
  /**
   * Labels for every form line after (re)reading the source: the first of the previous label (kept entries), the saved
   * line (edit) or the delivery default whose IDs match the line's warehouse/location.
   */
  private placesFor(order: SalesReturnSourceOrder): Map<number, ReturnDestination> {
    const previous = this.places(), saved = this.editing()?.lines ?? [];
    const result = new Map<number, ReturnDestination>();
    for (const line of this.draft.lines) {
      const facts = sourceLine(order, line.deliveryLineId);
      const candidates: (ReturnDestination | undefined)[] = [previous.get(line.deliveryLineId),
        saved.find(candidate => candidate.deliveryLineId === line.deliveryLineId), facts];
      const match = candidates.find(candidate => candidate?.warehouseId === line.warehouseId &&
        (candidate.locationId ?? null) === line.locationId);
      result.set(line.deliveryLineId, match ? pickDestination(match) : { warehouseId: line.warehouseId, locationId: line.locationId });
    }
    return result;
  }
  /** WU-SD-008b3b2b (ADR-197): an active warehouse of the company; another warehouse clears the location. */
  pickWarehouse(deliveryLineId: number): void {
    if (this.locked()) return;
    const epoch = this.epoch;
    this.dialogs.select<DeliveryReferenceOption>({ titleKey: 'sd.returnCreate.chooseWarehouse',
      descriptionKey: 'sd.returnCreate.chooseWarehouseHint', columns: REFERENCE_COLUMNS,
      load: query => this.returns.warehouses(query), rowKey: row => row.id,
      errorCodes: ['COMMON.PERMISSION.DENIED'], timeoutKey: 'sd.return.timeout' }).subscribe(rows => {
      const line = this.lineOf(deliveryLineId), row = rows?.[0];
      if (epoch !== this.epoch || !row || !line || this.locked()) return;
      const old = this.places().get(deliveryLineId);
      const same = line.warehouseId === row.id;
      line.warehouseId = row.id;
      if (!same) line.locationId = null;
      this.setPlace(deliveryLineId, { ...(same && old ? old : { locationId: null }), warehouseId: row.id,
        warehouseCode: row.code, warehouseName: row.name, warehouseActive: true });
      this.revalidate();
    });
  }
  /** Active locations of the line's warehouse; a warehouse deactivated meanwhile answers MD.WAREHOUSE.NOT_FOUND. */
  pickLocation(deliveryLineId: number): void {
    const warehouseId = this.lineOf(deliveryLineId)?.warehouseId;
    if (this.locked() || !warehouseId) return;
    const epoch = this.epoch;
    this.dialogs.select<DeliveryReferenceOption>({ titleKey: 'sd.returnCreate.chooseLocation', columns: REFERENCE_COLUMNS,
      load: query => this.returns.locations(warehouseId, query), rowKey: row => row.id,
      errorCodes: ['COMMON.PERMISSION.DENIED', 'MD.WAREHOUSE.NOT_FOUND'], timeoutKey: 'sd.return.timeout' }).subscribe(rows => {
      const line = this.lineOf(deliveryLineId), row = rows?.[0], old = this.places().get(deliveryLineId);
      if (epoch !== this.epoch || !row || !line || !old || line.warehouseId !== warehouseId || this.locked()) return;
      line.locationId = row.id;
      this.setPlace(deliveryLineId, { ...old, locationId: row.id, locationCode: row.code, locationName: row.name, locationActive: true });
      this.revalidate();
    });
  }
  clearLocation(deliveryLineId: number): void {
    const line = this.lineOf(deliveryLineId), old = this.places().get(deliveryLineId);
    if (this.locked() || !line || !old) return;
    line.locationId = null;
    this.setPlace(deliveryLineId, { ...old, locationId: null, locationCode: null, locationName: null, locationActive: null });
    this.revalidate();
  }
  total(line: ReturnLineForm): string { this.tick(); const value = lineTotal(line); return value == null ? '—' : this.decimal(value); }
  earliestDate(): string | null { this.tick(); return earliestReturnDate(this.draft, this.source()); }
  selectedCount(): number { this.tick(); return this.draft.lines.filter(line => line.selected).length; }
  /** Inline messages follow edits once the user has tried to save. */
  revalidate(): void {
    this.tick.update(value => value + 1);
    if (this.submitted()) this.problems.set(validateReturnDraft(this.draft, this.source()));
  }
  reset(): void {
    this.epoch++; this.draft = blank(); this.source.set(null); this.problems.set({}); this.submitted.set(false);
    this.orderLoading.set(false); this.orderError.set(null); this.orderTraceId.set(null); this.saving.set(false);
    this.saved.set(null); this.replayed.set(false); this.uncertain.set(false); this.clearOutcome();
    this.editing.set(null); this.editLoading.set(false); this.editError.set(null); this.editTraceId.set(null);
    this.blocked.set(null); this.dropped.set([]); this.places.set(new Map()); this.baseline = '';
    this.requestKey = newReturnRequestKey(); this.sent = null; this.tick.update(value => value + 1);
  }
  private clearOutcome(): void {
    this.error.set(null); this.traceId.set(null); this.lineError.set(null); this.canReload.set(false);
    this.conflict.set(false); this.unavailable.set(false); this.noChange.set(false);
  }
  /** Edit mode: read the saved return, then its order's current balances; only Draft and Rejected open the form. */
  loadSaved(): void {
    if (!this.editId) return;
    this.reset();
    if (this.editId < 0) { this.editError.set('errors.SAL.RETURN.NOT_FOUND'); return; }
    const epoch = this.epoch;
    this.editLoading.set(true);
    this.returns.get(this.editId).subscribe({
      next: saved => { if (epoch !== this.epoch) return; this.editLoading.set(false);
        if (saved?.id !== this.editId || !Array.isArray(saved.lines) || !Number.isSafeInteger(saved.orderId)) {
          this.editError.set(UNKNOWN_ERROR_KEY); return; }
        if (!EDITABLE.includes(saved.status)) { this.blocked.set(`sd.return.state.${saved.status.toLowerCase()}`); return; }
        this.editing.set(saved); this.draft.orderId = saved.orderId;
        this.loadOrder(saved.orderId, 'saved');
      },
      error: failure => { if (epoch !== this.epoch) return; this.editLoading.set(false);
        this.editError.set(failure instanceof TimeoutError ? 'sd.return.timeout' : apiErrorKey(failure, LOAD_ERROR_CODES));
        this.editTraceId.set(apiErrorTraceId(failure)); },
    });
  }
  /** After a conflict or a vanished return: discards the input (after confirmation when edited) and reads again. */
  refreshSaved(): void {
    if (this.saving()) return;
    if (this.dirty() && !window.confirm(this.translate.instant('sd.returnEdit.discard'))) return;
    this.loadSaved();
  }
  /**
   * After an unknown PUT outcome: the saved return decides. Our content at a newer version means it was saved; the
   * loaded version unchanged means it was not (the form unlocks for another try); anything else changed elsewhere.
   */
  checkSaved(): void {
    const loaded = this.editing(), request = this.sent;
    if (!loaded || !request || !this.uncertain() || this.saving()) return;
    const epoch = this.epoch;
    this.saving.set(true); this.clearOutcome();
    this.returns.get(this.editId).subscribe({
      next: current => { if (epoch !== this.epoch) return; this.saving.set(false);
        if (current?.id === this.editId && current.version > loaded.version && matchesReturnReceipt(current, request)) {
          this.accept(current); return; }
        this.uncertain.set(false); this.sent = null;
        if (current?.id === this.editId && current.version === loaded.version && current.status === loaded.status)
          this.error.set('sd.returnEdit.notSaved');
        else this.conflict.set(true);
      },
      error: failure => { if (epoch !== this.epoch) return; this.saving.set(false);
        const status = failure instanceof HttpErrorResponse ? failure.status : null;
        if (status === 404) { this.uncertain.set(false); this.sent = null; this.unavailable.set(true); return; }
        this.traceId.set(apiErrorTraceId(failure)); },
    });
  }
  pickOrder(): void {
    if (this.locked()) return;
    const epoch = this.epoch;
    this.dialogs.select<SalesReturnSourceOrderSummary>({ titleKey: 'sd.returnCreate.chooseOrder',
      descriptionKey: 'sd.returnCreate.chooseOrderHint',
      columns: [{ key: 'documentNo', labelKey: 'sd.return.order', value: row => row.documentNo },
        { key: 'customerName', labelKey: 'sd.return.customer', value: row => `${row.customerCode} · ${row.customerName}` },
        { key: 'businessDate', labelKey: 'sd.returnCreate.orderDate', value: row => row.businessDate },
        { key: 'currencyCode', labelKey: 'sd.return.currency', value: row => row.currencyCode },
        { key: 'openLineCount', labelKey: 'sd.returnCreate.openLines', value: row => String(row.openLineCount) }],
      load: query => this.returns.sourceOrders(query), rowKey: row => row.id,
      errorCodes: ['COMMON.PERMISSION.DENIED'], timeoutKey: 'sd.return.timeout' }).subscribe(rows => {
      if (epoch !== this.epoch || !rows?.[0]) return;
      const { businessDate, remark } = this.draft;
      this.reset(); this.draft.businessDate = businessDate; this.draft.remark = remark; this.draft.orderId = rows[0].id;
      this.loadOrder(rows[0].id, 'fresh');
    });
  }
  /** Reread the advisory balances; keeps the entries of lines that are still returnable. */
  reloadBalance(): void {
    if (!this.draft.orderId || this.orderLoading() || this.saving() || this.uncertain()) return;
    // An edit whose balances never loaded still has to fill in the saved split.
    this.loadOrder(this.draft.orderId, this.editId && !this.source() ? 'saved' : 'keep');
  }
  private loadOrder(orderId: number, mode: 'fresh' | 'keep' | 'saved'): void {
    const epoch = ++this.epoch;
    this.orderLoading.set(true); this.orderError.set(null); this.orderTraceId.set(null);
    this.returns.sourceOrder(orderId).subscribe({
      next: order => { if (epoch !== this.epoch) return; this.orderLoading.set(false);
        if (order?.id !== orderId || !Array.isArray(order.lines) || !order.lines.every(line => Array.isArray(line.invoiceLines))) {
          this.source.set(null); this.draft.lines = []; this.orderError.set(UNKNOWN_ERROR_KEY); return; }
        this.source.set(order);
        const saved = this.editing();
        if (mode === 'saved' && saved) {
          const { draft, dropped } = draftFromSaved(order, saved);
          this.draft = draft; this.dropped.set(dropped);
          // A dropped part is a change by itself, so the unchanged check only applies to a fully restored form.
          this.baseline = dropped.length ? '' : this.requestJson();
        } else this.draft = mode === 'keep' && this.draft.lines.length ? mergeDraft(this.draft, order)
          : draftFromSource(order, this.draft.businessDate, this.draft.remark);
        this.places.set(this.placesFor(order));
        if (mode === 'keep') this.clearOutcome();
        this.revalidate();
      },
      error: failure => { if (epoch !== this.epoch) return; this.orderLoading.set(false);
        this.source.set(null); this.draft.lines = [];
        this.orderError.set(failure instanceof TimeoutError ? 'sd.return.timeout' : apiErrorKey(failure, SOURCE_ERROR_CODES));
        this.orderTraceId.set(apiErrorTraceId(failure)); },
    });
  }
  save(): void {
    if (this.locked()) return;
    this.submitted.set(true);
    const problems = validateReturnDraft(this.draft, this.source());
    this.problems.set(problems);
    if (Object.keys(problems).length) { this.focusFirstProblem(Object.keys(problems)[0]); return; }
    const loaded = this.editing();
    if (this.editId && loaded) {
      // A Draft with nothing changed sends nothing; a Rejected one is still revised back to Draft.
      this.noChange.set(loaded.status === 'Draft' && this.requestJson() === this.baseline);
      if (!this.noChange()) this.replace(loaded, returnRequest(this.draft, loaded.requestKey));
      return;
    }
    if (!this.editId) this.send(returnRequest(this.draft, this.requestKey));
  }
  /** PUT by the loaded version with the saved request key (422 REQUEST_KEY_IMMUTABLE otherwise). */
  private replace(loaded: SalesReturnDetail, request: SalesReturnDraftRequest): void {
    const epoch = this.epoch;
    this.sent = request; this.saving.set(true); this.clearOutcome();
    this.returns.replace(loaded.id, loaded.version, request).subscribe({
      next: result => { if (epoch !== this.epoch) return; this.saving.set(false);
        if (result?.id !== loaded.id || result.version <= loaded.version || !matchesReturnReceipt(result, request)) {
          this.uncertain.set(true); return; }
        this.accept(result);
      },
      error: failure => { if (epoch !== this.epoch) return; this.saving.set(false); this.refused(failure); },
    });
  }
  private accept(result: SalesReturnDetail): void {
    this.uncertain.set(false); this.sent = null; this.saved.set(result);
    this.dialogs.success(this.successKey(), { number: result.documentNo });
    if (this.permissions.has('SAL.RETURN.VIEW'))
      void this.router.navigate(['/sal/returns'], { queryParams: { open: result.id } });
  }
  /** Same key, same body: a saved return comes back as 200 without a second Draft. */
  resend(): void { if (this.sent && this.uncertain() && !this.saving()) this.send(this.sent); }
  private send(request: SalesReturnDraftRequest): void {
    const epoch = this.epoch;
    this.sent = request; this.saving.set(true); this.clearOutcome();
    this.returns.create(request).subscribe({
      next: result => { if (epoch !== this.epoch) return; this.saving.set(false);
        if (!matchesReturnReceipt(result.saved, request)) { this.uncertain.set(true); return; }
        this.uncertain.set(false); this.replayed.set(result.replayed); this.saved.set(result.saved);
        this.dialogs.success(result.replayed ? 'sd.returnCreate.replayed' : 'sd.returnCreate.saved', { number: result.saved.documentNo });
        if (this.permissions.has('SAL.RETURN.VIEW'))
          void this.router.navigate(['/sal/returns'], { queryParams: { open: result.saved.id } });
      },
      error: failure => { if (epoch !== this.epoch) return; this.saving.set(false); this.refused(failure); },
    });
  }
  private refused(failure: unknown): void {
    const status = failure instanceof HttpErrorResponse ? failure.status : null;
    const key = apiErrorKey(failure, this.editId ? RETURN_EDIT_ERROR_CODES : RETURN_CREATE_ERROR_CODES);
    this.traceId.set(apiErrorTraceId(failure));
    // A 5xx without a code the server raises before commit, a network failure or a timeout leaves the outcome unknown.
    if (failure instanceof TimeoutError || status === 0 || (status != null && status >= 500 && key === UNKNOWN_ERROR_KEY)) {
      this.uncertain.set(true); return; }
    // Definite refusal: nothing was saved, so the next attempt (possibly with other content) uses a new key.
    this.uncertain.set(false); this.requestKey = newReturnRequestKey(); this.sent = null;
    // Edit: a stale version keeps the input; a vanished or hidden return can only be re-read.
    if (this.editId && status === 409) { this.conflict.set(true); return; }
    if (this.editId && status === 404 && key === 'errors.SAL.RETURN.NOT_FOUND') { this.unavailable.set(true); return; }
    const { lineNo, invoiceLineId, remaining } = returnRefusal(failure);
    const dateKey = key === 'errors.SAL.RETURN.DATE_BEFORE_DELIVERY' || key === 'errors.SAL.RETURN.DATE_BEFORE_INVOICE';
    const line = lineNo != null ? this.draft.lines.filter(candidate => candidate.selected)[lineNo - 1] : undefined;
    if (line && !dateKey) {
      const exceeded = remaining != null && ['errors.SAL.RETURN.RETURN_EXCEEDED', 'errors.SAL.RETURN.UNBILLED_EXCEEDED',
        'errors.SAL.RETURN.BILLED_EXCEEDED'].includes(key);
      this.lineError.set({ deliveryLineId: line.deliveryLineId, remaining: remaining != null ? this.decimal(remaining) : '',
        key: exceeded ? 'sd.returnCreate.lineBalance' : key });
      const portion = invoiceLineId != null && line.billed.some(candidate => candidate.invoiceLineId === invoiceLineId);
      // A refused destination is fixed by picking another warehouse / location (ADR-197).
      const place = key.startsWith('errors.MD.WAREHOUSE.') ? 'warehouse' : key.startsWith('errors.MD.STORAGE_LOCATION.') ? 'location' : null;
      this.focus(place ? `sd-return-create-${place}-pick-${line.deliveryLineId}`
        : portion ? `sd-return-create-billed-${line.deliveryLineId}-${invoiceLineId}`
        : `sd-return-create-unbilled-${line.deliveryLineId}`);
    } else if (dateKey) this.focus('sd-return-create-date');
    this.canReload.set(RELOADABLE.includes(key));
    this.error.set(status === 422 && key === UNKNOWN_ERROR_KEY ? 'sd.returnCreate.rejected' : key);
  }
  private focusFirstProblem(name: string): void {
    const target = name === 'orderId' ? 'sd-return-create-order-pick' : name === 'businessDate' ? 'sd-return-create-date'
      : name === 'remark' ? 'sd-return-create-remark' : name === 'lines' ? `sd-return-create-select-${this.firstSelectable()}`
      : name.startsWith('line-') ? `sd-return-create-unbilled-${name.slice('line-'.length)}`
      : `sd-return-create-${name.startsWith('unbilled-') ? 'unbilled-' + name.slice('unbilled-'.length) : 'billed-' + name.slice('billed-'.length)}`;
    this.focus(target);
  }
  private firstSelectable(): number | undefined {
    return this.draft.lines.find(line => !this.factsOf(line.deliveryLineId)?.trackingUnsupported)?.deliveryLineId;
  }
  /**
   * After a refusal the inputs are re-enabled by the next render, and ngModel applies `[disabled]` in a microtask after
   * that render; focusing earlier hits a still-disabled input and is ignored (found by SD-INV-E2E-004).
   */
  private focus(testId: string): void {
    afterNextRender(() => queueMicrotask(() =>
      this.host.nativeElement.querySelector<HTMLElement>(`[data-testid="${testId}"]`)?.focus()), { injector: this.injector });
  }
}
