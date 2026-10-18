import { HttpErrorResponse } from '@angular/common/http';

type Translate = (key: string, params?: { number: number }) => string;

/** Format disassembly validation paths as visible row labels; retain unknown server reasons. */
export function disassemblyValidationDetails(error: unknown, translate: Translate): string[] {
  if (!(error instanceof HttpErrorResponse) || (error.status !== 400 && error.status !== 422)) return [];
  const errors = (error.error as { errors?: unknown } | null)?.errors;
  if (!errors || typeof errors !== 'object' || Array.isArray(errors)) return [];
  return Object.entries(errors).slice(0, 20).flatMap(([path, messages]) => {
    if (!Array.isArray(messages)) return [];
    const field = fieldLabel(path.slice(0, 120), translate);
    return messages.filter((message): message is string => typeof message === 'string').slice(0, 3)
      .map(message => `${field}${translate('inv.disassembly.validationSeparator')}${reasonLabel(message.slice(0, 500), translate)}`);
  });
}

function fieldLabel(path: string, translate: Translate): string {
  const parts: string[] = [];
  const line = /^(finished|components\[(\d+)\])(?:\.|$)/.exec(path);
  if (line) parts.push(line[1] === 'finished' ? translate('inv.disassembly.finished')
    : translate('inv.disassembly.componentLine', { number: Number(line[2]) + 1 }));
  const tracking = /tracking\[(\d+)\]/.exec(path);
  if (tracking) parts.push(translate('inv.disassembly.trackingLine', { number: Number(tracking[1]) + 1 }));
  const field = /(?:^|\.)(businessDate|remark|components|lineNo|itemId|warehouseId|locationId|quantity|allocationWeight|lotNo|serialNo|expiryDate|tracking)$/.exec(path)?.[1];
  if (field) parts.push(translate(`inv.disassembly.${field}`));
  return parts.length ? parts.join(' · ') : path;
}

function reasonLabel(reason: string, translate: Translate): string {
  const reasons: Record<string, string> = {
    'Quantity must be positive, below 10^13 and have at most 6 decimals.': 'quantityInvalid',
    'Quantity must be positive.': 'quantityInvalid',
    'Business date is required.': 'dateRequired',
    'Remark must not exceed 1000 characters.': 'remarkTooLong',
    'Finished line is required.': 'finishedRequired',
    'A disassembly needs 1 to 999 component lines.': 'componentsRequired',
    'Component allocation weight must be positive and have at most 6 decimals.': 'weightInvalid',
    'Finished line cannot have allocation weight.': 'finishedWeightForbidden',
    'Line is required.': 'lineRequired',
    'Line number must be positive.': 'lineNumberInvalid',
    'Item is required.': 'itemRequired',
    'Warehouse is required.': 'warehouseRequired',
    'Location must be positive.': 'locationInvalid',
    'Line numbers must be unique.': 'lineNumbersUnique',
    'Finished item cannot be a component.': 'finishedComponentConflict',
    'A disassembly must contain at most 10000 tracking entries.': 'trackingLimit',
    'A serial number can appear only once per item within a disassembly.': 'serialUnique',
    'Lot or serial entry is required.': 'trackingRequired',
    'Entry must have either a lot number or a serial number.': 'trackingKindRequired',
    'Each serial number represents exactly one base unit.': 'serialQuantity',
    'Manufacture and expiry dates apply only to received lots.': 'lotDatesOnly',
    'Expiry date cannot be earlier than manufacture date.': 'expiryOrder',
    'A line cannot mix lot and serial numbers.': 'trackingKindMixed',
    'Lot or serial numbers must be unique within a line.': 'trackingUnique',
    'Item is not lot or serial tracked.': 'notTracked',
    'Item is lot tracked; use lot numbers.': 'useLots',
    'Item is serial tracked; use serial numbers.': 'useSerials',
    'Serial-tracked quantity must be a whole number.': 'serialWhole',
  };
  const code = reasons[reason] ?? (/^Lot number must contain 1 to \d+ characters\.$/.test(reason) ? 'lotLength'
    : /^Serial number must contain 1 to \d+ characters\.$/.test(reason) ? 'serialLength' : null);
  return code ? translate(`inv.disassembly.validation.${code}`) : reason;
}
