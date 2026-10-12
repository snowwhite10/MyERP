import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, effect, inject, input, output, signal, untracked } from '@angular/core';
import { AbstractControl, FormControl, FormGroup, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { Observable } from 'rxjs';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzModalModule } from 'ng-zorro-antd/modal';
import { NzTableModule } from 'ng-zorro-antd/table';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { HasPermissionDirective } from '../../core/auth/has-permission.directive';
import { ErpDialogService } from '../../shared/dialogs/dialog.service';
import { latestRequest } from '../../shared/list-page/latest-request';
import { Customer } from './customer.service';
import { AddressDraft, ContactDraft, CustomerAddress, CustomerContact, CustomerContactService, Revision } from './customer-contact.service';

interface ChildRow { id: number; isDefault: boolean; isActive: boolean; version: number }
interface ChildApi<T, D> {
  list(customerId: number): Observable<T[]>;
  create(customerId: number, draft: D): Observable<T>;
  update(customerId: number, id: number, draft: Revision<D>): Observable<T>;
  remove(customerId: number, id: number, version: number): Observable<void>;
}

/** 預設必須是啟用的資料（後端 CHECK，回 400 `isDefault`），在前端先擋。 */
function defaultNeedsActive(group: AbstractControl): ValidationErrors | null {
  const { isDefault, isActive } = group.value as { isDefault?: boolean; isActive?: boolean };
  return isDefault && isActive === false ? { defaultNeedsActive: true } : null;
}

const optional = (max: number) => new FormControl('', { nonNullable: true, validators: [Validators.maxLength(max)] });
const required = (max: number) => new FormControl('', { nonNullable: true,
  validators: [Validators.required, Validators.pattern(/.*\S.*/), Validators.maxLength(max)] });

/**
 * 一個子表（聯絡人或地址）的列表、表單與保存狀態。保存或刪除成功後重新讀取整個列表，
 * 因為設為預設會讓後端自動取消他筆的預設。
 */
class ChildSection<T extends ChildRow, D extends object> {
  readonly rows = signal<T[]>([]);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly editing = signal<T | null>(null);
  readonly saving = signal(false);
  private readonly request = latestRequest();

  constructor(private readonly api: ChildApi<T, D>, private readonly errorKey: (error: unknown) => string,
    readonly form: FormGroup, private readonly blank: Record<string, unknown>) {}

  clear(): void { this.request.cancel(); this.rows.set([]); this.error.set(null); this.loading.set(false); this.resetForm(); }

  reload(customerId: number): void {
    this.loading.set(true);
    this.request.run(this.api.list(customerId), {
      next: (rows) => { this.rows.set(rows); this.loading.set(false); },
      error: (error: unknown) => { this.rows.set([]); this.error.set(this.errorKey(error)); this.loading.set(false); },
    });
  }

  startEdit(row: T): void {
    const values = row as unknown as Record<string, unknown>;
    this.editing.set(row); this.error.set(null);
    this.form.reset(Object.fromEntries(Object.keys(this.blank).map((key) => [key, values[key] ?? this.blank[key]])));
  }
  resetForm(): void { this.editing.set(null); this.form.reset(this.blank); }

  save(customerId: number): void {
    if (this.saving()) return;
    this.form.markAllAsTouched(); if (this.form.invalid) return;
    const { isActive, ...draft } = this.form.getRawValue() as D & { isActive: boolean };
    const row = this.editing();
    this.run(customerId, row ? this.api.update(customerId, row.id, { ...(draft as D), isActive, version: row.version })
      : this.api.create(customerId, draft as D), () => this.resetForm());
  }

  remove(customerId: number, row: T): void {
    this.run(customerId, this.api.remove(customerId, row.id, row.version), () => {
      if (this.editing()?.id === row.id) this.resetForm();
    });
  }

  private run(customerId: number, request: Observable<unknown>, done: () => void): void {
    this.saving.set(true); this.error.set(null);
    request.subscribe({
      next: () => { this.saving.set(false); done(); this.reload(customerId); },
      error: (error: unknown) => {
        this.saving.set(false); this.error.set(this.errorKey(error));
        // 資料已被刪除（404）或被別人修改（409）：重新讀取列表，避免使用者繼續面對過期資料
        const status = error instanceof HttpErrorResponse ? error.status : 0;
        if (status === 404) this.resetForm();
        if (status === 404 || status === 409) this.reload(customerId);
      },
    });
  }
}

/**
 * 客戶聯絡人與地址對話框（介面 8.4，ADR-043）：查看需 `MD.CUSTOMER.VIEW`，新增／修改／刪除需 `MD.CUSTOMER.EDIT`。
 * 子資料逐筆保存，不與客戶主檔一起保存；停用的客戶仍可維護。
 */
@Component({
  selector: 'erp-customer-contact-dialog',
  imports: [ReactiveFormsModule, TranslatePipe, NzAlertModule, NzButtonModule, NzInputModule,
    NzModalModule, NzTableModule, NzTagModule, HasPermissionDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <nz-modal [nzVisible]="customer() !== null" [nzTitle]="'md.customerContact.title' | translate: { code: customer()?.code }"
      [nzFooter]="null" nzWidth="1000px" (nzOnCancel)="closed.emit()">
      <ng-container *nzModalContent>
        <div class="children" data-testid="md-customer-contact-dialog">
          <p class="hint">{{ 'md.customerContact.hint' | translate }}</p>

          <h3>{{ 'md.customerContact.contacts' | translate }}</h3>
          @if (contacts.error()) { <nz-alert nzType="error" nzShowIcon data-testid="md-contact-error" [nzMessage]="contacts.error()! | translate" /> }
          <nz-table #contactTable data-testid="md-contact-table" nzSize="small" [nzData]="contacts.rows()" [nzLoading]="contacts.loading()"
            [nzShowPagination]="false" [nzFrontPagination]="false" [nzNoResult]="'md.customerContact.noContacts' | translate">
            <thead><tr><th>{{ 'md.customerContact.name' | translate }}</th><th>{{ 'md.customerContact.position' | translate }}</th>
              <th>{{ 'md.customerContact.mobile' | translate }}</th><th>{{ 'md.customerContact.phone' | translate }}</th>
              <th>{{ 'md.customerContact.email' | translate }}</th><th>{{ 'md.customerContact.messagingId' | translate }}</th>
              <th>{{ 'md.customerContact.status' | translate }}</th><th>{{ 'md.customerContact.actions' | translate }}</th></tr></thead>
            <tbody>@for (row of contactTable.data; track row.id) {
              <tr [attr.data-testid]="'md-contact-row-' + row.id">
                <td>{{ row.name }} @if (row.isDefault) { <nz-tag nzColor="processing" [attr.data-testid]="'md-contact-default-' + row.id">{{ 'md.customerContact.default' | translate }}</nz-tag> }
                  @if (row.note) { <div class="note">{{ row.note }}</div> }</td>
                <td>{{ row.position }}</td><td>{{ row.mobile }}</td><td>{{ row.phone }}</td><td>{{ row.email }}</td><td>{{ row.messagingId }}</td>
                <td><nz-tag [nzColor]="row.isActive ? 'success' : 'default'">{{ (row.isActive ? 'md.customerContact.active' : 'md.customerContact.inactive') | translate }}</nz-tag></td>
                <td class="actions">
                  <button nz-button nzType="link" *erpHasPermission="'MD.CUSTOMER.EDIT'"
                    [attr.data-testid]="'md-contact-edit-' + row.id" (click)="contacts.startEdit(row)">{{ 'common.action.edit' | translate }}</button>
                  <button nz-button nzType="link" nzDanger *erpHasPermission="'MD.CUSTOMER.EDIT'" [disabled]="contacts.saving()"
                    [attr.data-testid]="'md-contact-delete-' + row.id" (click)="confirmDeleteContact(row)">{{ 'common.action.delete' | translate }}</button>
                </td></tr>
            }</tbody>
          </nz-table>
          <form *erpHasPermission="'MD.CUSTOMER.EDIT'" class="child-form" [formGroup]="contactForm" (ngSubmit)="saveContact()" data-testid="md-contact-form">
            <input nz-input data-testid="md-contact-name" formControlName="name" maxlength="100"
              [attr.aria-label]="'md.customerContact.name' | translate" [placeholder]="'md.customerContact.name' | translate" />
            <input nz-input data-testid="md-contact-position" formControlName="position" maxlength="100"
              [attr.aria-label]="'md.customerContact.position' | translate" [placeholder]="'md.customerContact.position' | translate" />
            <input nz-input data-testid="md-contact-mobile" formControlName="mobile" maxlength="50"
              [attr.aria-label]="'md.customerContact.mobile' | translate" [placeholder]="'md.customerContact.mobile' | translate" />
            <input nz-input data-testid="md-contact-phone" formControlName="phone" maxlength="50"
              [attr.aria-label]="'md.customerContact.phone' | translate" [placeholder]="'md.customerContact.phone' | translate" />
            <input nz-input type="email" data-testid="md-contact-email" formControlName="email" maxlength="200"
              [attr.aria-label]="'md.customerContact.email' | translate" [placeholder]="'md.customerContact.email' | translate" />
            <input nz-input data-testid="md-contact-messaging" formControlName="messagingId" maxlength="100"
              [attr.aria-label]="'md.customerContact.messagingId' | translate" [placeholder]="'md.customerContact.messagingId' | translate" />
            <input nz-input class="wide" data-testid="md-contact-note" formControlName="note" maxlength="500"
              [attr.aria-label]="'md.customerContact.note' | translate" [placeholder]="'md.customerContact.note' | translate" />
            <div class="flags">
              <label><input type="checkbox" formControlName="isDefault" data-testid="md-contact-default" />{{ 'md.customerContact.setDefault' | translate }}</label>
              @if (contacts.editing()) { <label><input type="checkbox" formControlName="isActive" data-testid="md-contact-active" />{{ 'md.customerContact.active' | translate }}</label> }
              <button nz-button nzType="primary" type="submit" data-testid="md-contact-save" [nzLoading]="contacts.saving()">
                {{ (contacts.editing() ? 'common.action.save' : 'md.customerContact.addContact') | translate }}</button>
              @if (contacts.editing()) { <button nz-button type="button" data-testid="md-contact-cancel" (click)="contacts.resetForm()">{{ 'common.action.cancel' | translate }}</button> }
            </div>
            @if (contactForm.controls.name.touched && contactForm.controls.name.invalid) { <p role="alert">{{ 'md.customerContact.invalidName' | translate }}</p> }
            @if (contactForm.controls.email.touched && contactForm.controls.email.invalid) { <p role="alert">{{ 'md.customerContact.invalidEmail' | translate }}</p> }
            @if (contactForm.hasError('defaultNeedsActive')) { <p role="alert" data-testid="md-contact-default-inactive">{{ 'md.customerContact.defaultNeedsActive' | translate }}</p> }
          </form>

          <h3>{{ 'md.customerContact.addresses' | translate }}</h3>
          @if (addresses.error()) { <nz-alert nzType="error" nzShowIcon data-testid="md-address-error" [nzMessage]="addresses.error()! | translate" /> }
          <nz-table #addressTable data-testid="md-address-table" nzSize="small" [nzData]="addresses.rows()" [nzLoading]="addresses.loading()"
            [nzShowPagination]="false" [nzFrontPagination]="false" [nzNoResult]="'md.customerContact.noAddresses' | translate">
            <thead><tr><th>{{ 'md.customerContact.address' | translate }}</th><th>{{ 'md.customerContact.postalCode' | translate }}</th>
              <th>{{ 'md.customerContact.recipient' | translate }}</th><th>{{ 'md.customerContact.phone' | translate }}</th>
              <th>{{ 'md.customerContact.status' | translate }}</th><th>{{ 'md.customerContact.actions' | translate }}</th></tr></thead>
            <tbody>@for (row of addressTable.data; track row.id) {
              <tr [attr.data-testid]="'md-address-row-' + row.id">
                <td>{{ row.address }} @if (row.isDefault) { <nz-tag nzColor="processing" [attr.data-testid]="'md-address-default-' + row.id">{{ 'md.customerContact.default' | translate }}</nz-tag> }</td>
                <td>{{ row.postalCode }}</td><td>{{ row.recipient }}</td><td>{{ row.phone }}</td>
                <td><nz-tag [nzColor]="row.isActive ? 'success' : 'default'">{{ (row.isActive ? 'md.customerContact.active' : 'md.customerContact.inactive') | translate }}</nz-tag></td>
                <td class="actions">
                  <button nz-button nzType="link" *erpHasPermission="'MD.CUSTOMER.EDIT'"
                    [attr.data-testid]="'md-address-edit-' + row.id" (click)="addresses.startEdit(row)">{{ 'common.action.edit' | translate }}</button>
                  <button nz-button nzType="link" nzDanger *erpHasPermission="'MD.CUSTOMER.EDIT'" [disabled]="addresses.saving()"
                    [attr.data-testid]="'md-address-delete-' + row.id" (click)="confirmDeleteAddress(row)">{{ 'common.action.delete' | translate }}</button>
                </td></tr>
            }</tbody>
          </nz-table>
          <form *erpHasPermission="'MD.CUSTOMER.EDIT'" class="child-form" [formGroup]="addressForm" (ngSubmit)="saveAddress()" data-testid="md-address-form">
            <input nz-input class="wide" data-testid="md-address-address" formControlName="address" maxlength="300"
              [attr.aria-label]="'md.customerContact.address' | translate" [placeholder]="'md.customerContact.address' | translate" />
            <input nz-input data-testid="md-address-postal" formControlName="postalCode" maxlength="20"
              [attr.aria-label]="'md.customerContact.postalCode' | translate" [placeholder]="'md.customerContact.postalCode' | translate" />
            <input nz-input data-testid="md-address-recipient" formControlName="recipient" maxlength="100"
              [attr.aria-label]="'md.customerContact.recipient' | translate" [placeholder]="'md.customerContact.recipient' | translate" />
            <input nz-input data-testid="md-address-phone" formControlName="phone" maxlength="50"
              [attr.aria-label]="'md.customerContact.phone' | translate" [placeholder]="'md.customerContact.phone' | translate" />
            <div class="flags">
              <label><input type="checkbox" formControlName="isDefault" data-testid="md-address-default" />{{ 'md.customerContact.setDefault' | translate }}</label>
              @if (addresses.editing()) { <label><input type="checkbox" formControlName="isActive" data-testid="md-address-active" />{{ 'md.customerContact.active' | translate }}</label> }
              <button nz-button nzType="primary" type="submit" data-testid="md-address-save" [nzLoading]="addresses.saving()">
                {{ (addresses.editing() ? 'common.action.save' : 'md.customerContact.addAddress') | translate }}</button>
              @if (addresses.editing()) { <button nz-button type="button" data-testid="md-address-cancel" (click)="addresses.resetForm()">{{ 'common.action.cancel' | translate }}</button> }
            </div>
            @if (addressForm.controls.address.touched && addressForm.controls.address.invalid) { <p role="alert">{{ 'md.customerContact.invalidAddress' | translate }}</p> }
            @if (addressForm.hasError('defaultNeedsActive')) { <p role="alert" data-testid="md-address-default-inactive">{{ 'md.customerContact.defaultNeedsActive' | translate }}</p> }
          </form>
        </div>
      </ng-container>
    </nz-modal>
  `,
  styles: `
    .children { display: grid; gap: 12px; color: var(--erp-color-text); }
    .hint { margin: 0; color: var(--erp-color-text-secondary); }
    h3 { margin: 8px 0 0; font-size: 16px; }
    .note { color: var(--erp-color-text-secondary); font-size: 12px; }
    .actions { white-space: nowrap; }
    .child-form { display: grid; grid-template-columns: repeat(auto-fill, minmax(180px, 1fr)); gap: 8px; align-items: center; }
    .child-form .wide { grid-column: 1 / -1; }
    .flags { grid-column: 1 / -1; display: flex; gap: 12px; align-items: center; flex-wrap: wrap; }
    .child-form p { color: var(--erp-status-error); margin: 0; grid-column: 1 / -1; }
  `,
})
export class CustomerContactDialog {
  readonly customer = input<Customer | null>(null);
  readonly closed = output<void>();
  private readonly api = inject(CustomerContactService);
  private readonly dialogs = inject(ErpDialogService);
  readonly contactForm = new FormGroup({
    name: required(100), position: optional(100), mobile: optional(50), phone: optional(50),
    email: new FormControl('', { nonNullable: true, validators: [Validators.email, Validators.maxLength(200)] }),
    messagingId: optional(100), note: optional(500),
    isDefault: new FormControl(false, { nonNullable: true }), isActive: new FormControl(true, { nonNullable: true }),
  }, { validators: defaultNeedsActive });
  readonly addressForm = new FormGroup({
    address: required(300), postalCode: optional(20), recipient: optional(100), phone: optional(50),
    isDefault: new FormControl(false, { nonNullable: true }), isActive: new FormControl(true, { nonNullable: true }),
  }, { validators: defaultNeedsActive });
  readonly contacts = new ChildSection<CustomerContact, ContactDraft>({
    list: (id) => this.api.listContacts(id), create: (id, draft) => this.api.createContact(id, draft),
    update: (id, rowId, draft) => this.api.updateContact(id, rowId, draft), remove: (id, rowId, version) => this.api.removeContact(id, rowId, version),
  }, (error) => this.api.errorKey(error), this.contactForm, { name: '', position: '', mobile: '', phone: '', email: '',
    messagingId: '', note: '', isDefault: false, isActive: true });
  readonly addresses = new ChildSection<CustomerAddress, AddressDraft>({
    list: (id) => this.api.listAddresses(id), create: (id, draft) => this.api.createAddress(id, draft),
    update: (id, rowId, draft) => this.api.updateAddress(id, rowId, draft), remove: (id, rowId, version) => this.api.removeAddress(id, rowId, version),
  }, (error) => this.api.errorKey(error), this.addressForm, { address: '', postalCode: '', recipient: '', phone: '',
    isDefault: false, isActive: true });

  constructor() {
    effect(() => {
      const customer = this.customer();
      untracked(() => {
        this.contacts.clear(); this.addresses.clear();
        if (customer) { this.contacts.reload(customer.id); this.addresses.reload(customer.id); }
      });
    });
  }

  saveContact(): void { const customer = this.customer(); if (customer) this.contacts.save(customer.id); }
  saveAddress(): void { const customer = this.customer(); if (customer) this.addresses.save(customer.id); }

  confirmDeleteContact(row: CustomerContact): void {
    this.confirmDelete('md.customerContact.deleteContactConfirm', { name: row.name }, () => this.contacts.remove(this.customer()!.id, row));
  }
  confirmDeleteAddress(row: CustomerAddress): void {
    this.confirmDelete('md.customerContact.deleteAddressConfirm', { address: row.address }, () => this.addresses.remove(this.customer()!.id, row));
  }

  private confirmDelete(titleKey: string, params: Record<string, unknown>, remove: () => void): void {
    if (!this.customer()) return;
    this.dialogs.confirm({ titleKey, contentKey: 'common.confirm.irreversible', params, okKey: 'common.action.delete', danger: true })
      .subscribe((ok) => { if (ok && this.customer()) remove(); });
  }
}
