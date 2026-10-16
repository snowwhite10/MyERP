import { HttpErrorResponse } from '@angular/common/http';
import { assemblyValidationDetails } from './assembly-validation';

describe('assemblyValidationDetails', () => {
  const words: Record<string, string> = {
    'inv.assembly.componentLine': '耗料第 {{number}} 行',
    'inv.assembly.trackingLine': '批序號第 {{number}} 行',
    'inv.assembly.quantity': '數量',
    'inv.assembly.validationSeparator': '：',
    'inv.assembly.serialNo': '序號',
    'inv.assembly.validation.quantityInvalid': '數量須為正數，少於 10^13，且最多六位小數。',
  };
  const translate = (key: string, params?: { number: number }) =>
    (words[key] ?? key).replace('{{number}}', String(params?.number ?? ''));

  it('names the component and quantity and translates a known server reason', () => {
    const error = new HttpErrorResponse({ status: 400, error: { errors: {
      'components[0].quantity': ['Quantity must be positive, below 10^13 and have at most 6 decimals.'],
    } } });
    expect(assemblyValidationDetails(error, translate)).toEqual([
      '耗料第 1 行 · 數量：數量須為正數，少於 10^13，且最多六位小數。',
    ]);
  });

  it('names the exact tracking row and keeps an unfamiliar reason visible', () => {
    const error = new HttpErrorResponse({ status: 422, error: { errors: {
      'components[1].tracking[0].serialNo': ['序號不在來源倉庫。'],
    } } });
    expect(assemblyValidationDetails(error, translate)).toEqual([
      '耗料第 2 行 · 批序號第 1 行 · 序號：序號不在來源倉庫。',
    ]);
  });
});
