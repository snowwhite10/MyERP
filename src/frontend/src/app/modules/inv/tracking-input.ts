import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzInputModule } from 'ng-zorro-antd/input';
import { ItemTrackingMode } from '../md/item.service';
import { serialsFromText, TrackingDraft, trackingError } from './tracking';

@Component({
  selector: 'erp-tracking-input',
  imports: [FormsModule, TranslatePipe, NzButtonModule, NzInputModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (mode() === 'Lot') {
      <div class="tracking-rows">
        @for (row of rows(); track $index; let index = $index) {
          <div class="tracking-row">
            <input nz-input [ngModel]="row.lotNo" (ngModelChange)="update(index, 'lotNo', $event)" maxlength="50"
              [attr.aria-label]="'inv.tracking.lotNo' | translate" />
            <input nz-input [ngModel]="row.quantity" (ngModelChange)="update(index, 'quantity', $event)" inputmode="decimal"
              [attr.aria-label]="'inv.tracking.quantity' | translate" />
            @if (incoming()) {
              <input nz-input type="date" [ngModel]="row.manufactureDate" (ngModelChange)="update(index, 'manufactureDate', $event)"
                [attr.aria-label]="'inv.tracking.manufactureDate' | translate" />
              <input nz-input type="date" [ngModel]="row.expiryDate" (ngModelChange)="update(index, 'expiryDate', $event)"
                [attr.aria-label]="'inv.tracking.expiryDate' | translate" />
            }
            <button nz-button type="button" (click)="remove(index)" [attr.aria-label]="'inv.tracking.remove' | translate">×</button>
          </div>
        }
      </div>
      <button nz-button type="button" (click)="addLot()">{{ 'inv.tracking.addLot' | translate }}</button>
    } @else if (mode() === 'Serial') {
      <label>{{ 'inv.tracking.serials' | translate }}
        <textarea nz-input [value]="serialText()" (input)="pasteSerials($event)" rows="4"
          [attr.aria-label]="'inv.tracking.serials' | translate"></textarea>
      </label>
    }
    @if (mode() !== 'None' && error(); as key) { <p class="error">{{ key | translate }}</p> }
  `,
  styles: `.tracking-rows{display:grid;gap:8px}.tracking-row{display:flex;gap:8px;flex-wrap:wrap}.tracking-row input{min-width:120px;max-width:180px}.error{color:var(--erp-color-error,#ff4d4f);margin:4px 0}textarea{display:block;width:100%;max-width:420px}`,
})
export class TrackingInput {
  readonly mode = input.required<ItemTrackingMode>();
  readonly quantity = input.required<string>();
  readonly rows = input<TrackingDraft[]>([]);
  readonly incoming = input(true);
  readonly rowsChange = output<TrackingDraft[]>();
  error(): string | null { return trackingError(this.mode(), this.quantity(), this.rows(), this.incoming()); }
  serialText(): string { return this.rows().map(row => row.serialNo ?? '').join('\n'); }
  pasteSerials(event: Event): void { this.rowsChange.emit(serialsFromText((event.target as HTMLTextAreaElement).value)); }
  addLot(): void { this.rowsChange.emit([...this.rows(), { lotNo: '', quantity: '' }]); }
  remove(index: number): void { this.rowsChange.emit(this.rows().filter((_, i) => i !== index)); }
  update(index: number, key: keyof TrackingDraft, value: string): void {
    this.rowsChange.emit(this.rows().map((row, i) => i === index ? { ...row, [key]: value } : row));
  }
}
