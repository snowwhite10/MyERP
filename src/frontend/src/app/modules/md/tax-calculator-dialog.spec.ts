import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ANIMATION_MODULE_TYPE } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideNzIcons } from 'ng-zorro-antd/icon';
import { provideTestTranslations } from '../../../testing/i18n-fixtures';
import { APP_ICONS } from '../../core/icons';
import { isoDate } from '../../shared/date';
import { TAX_AMOUNT_PATTERN, TaxCalculatorDialog } from './tax-calculator-dialog';
import { TaxCode } from './tax.service';

const vat: TaxCode = { id: 1, code: 'VAT5', name: 'Standard 5%', taxType: 'Both', currentRatePercent: 5, isActive: true, version: 3 };
const API = '/api/v1/md/tax-rates/calculate';

describe('MD-TAX-CALC-UI tax calculator dialog', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(),
    ...provideTestTranslations('en'), provideNzIcons(APP_ICONS),
    { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' }] }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  function open(target: TaxCode = vat) {
    const fixture = TestBed.createComponent(TaxCalculatorDialog);
    fixture.componentRef.setInput('taxCode', target);
    TestBed.tick(); fixture.detectChanges();
    return { dialog: fixture.componentInstance, http: TestBed.inject(HttpTestingController), fixture };
  }

  it('accepts signed amounts with at most 11 integer and 4 decimal digits', () => {
    expect(['0', '105', '-105', '12345678901.1234'].every((x) => TAX_AMOUNT_PATTERN.test(x))).toBe(true);
    expect(['', '1.12345', '123456789012', '1e3', '.5', '+1'].some((x) => TAX_AMOUNT_PATTERN.test(x))).toBe(false);
  });

  it('limits directions by tax type and defaults the date to today', () => {
    const { dialog, fixture } = open();
    expect(dialog.directions()).toEqual(['Output', 'Input']);
    expect(dialog.form.controls.date.value).toBe(isoDate(new Date()));
    fixture.componentRef.setInput('taxCode', { ...vat, taxType: 'Input' }); TestBed.tick();
    expect(dialog.directions()).toEqual(['Input']);
    expect(dialog.form.controls.direction.value).toBe('Input');
  });

  it('validates, sends the calculation, shows the result and clears it when inputs change', () => {
    const { dialog, http, fixture } = open();
    dialog.form.patchValue({ currencyCode: 'tw', amount: '105' }); dialog.calculate();
    http.expectNone(API);
    dialog.form.patchValue({ currencyCode: 'twd', amount: '1.23456' }); dialog.calculate();
    http.expectNone(API);
    dialog.form.patchValue({ date: '2026-02-01', amount: '105', priceIncludesTax: true }); dialog.calculate();
    const req = http.expectOne(API);
    expect(req.request.body).toEqual({ taxCode: 'VAT5', direction: 'Output', date: '2026-02-01', amount: 105,
      priceIncludesTax: true, currencyCode: 'TWD' });
    req.flush({ ratePercent: 5, net: 100, tax: 5, gross: 105 });
    fixture.detectChanges();
    expect(document.body.querySelector('[data-testid="md-tax-calc-net"]')?.textContent).toContain('100');
    expect(document.body.querySelector('[data-testid="md-tax-calc-tax"]')?.textContent).toContain('5');
    dialog.form.patchValue({ amount: '210' });
    expect(dialog.result()).toBeNull();

    dialog.form.patchValue({ direction: 'Input' }); dialog.calculate();
    http.expectOne(API).flush({ errorCode: 'MD.TAX_RATE.NOT_FOUND' }, { status: 422, statusText: 'Unprocessable' });
    expect(dialog.error()).toBe('errors.MD.TAX_RATE.NOT_FOUND');
    expect(dialog.result()).toBeNull();
  });
});
