import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ANIMATION_MODULE_TYPE } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideNzIcons } from 'ng-zorro-antd/icon';
import { provideTestTranslations } from '../../../testing/i18n-fixtures';
import { APP_ICONS } from '../../core/icons';
import { OtherPaymentMapEditor } from './other-payment-map-editor';

describe('FI-OP-UI-013 account mapping editor', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(),
    ...provideTestTranslations('en'), provideNzIcons(APP_ICONS), { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' }] }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());
  it('sends the held row version and marks unchanged as a warning', () => {
    const fixture = TestBed.createComponent(OtherPaymentMapEditor);
    fixture.componentRef.setInput('rows', [{ expenseCategoryId: 3, expenseCategoryCode: 'UTIL', expenseCategoryName: 'Utilities',
      expenseCategoryActive: true, configured: false, version: 0, expenseAccount: null }]);
    fixture.componentRef.setInput('canEdit', true); fixture.detectChanges();
    fixture.nativeElement.querySelector('[data-testid="fi-op-map-edit-3"]').click(); fixture.detectChanges();
    fixture.componentInstance.code = '6101';
    fixture.componentInstance.save();
    const req = TestBed.inject(HttpTestingController).expectOne('/api/v1/gl/other-payment-account-map/3');
    expect(req.request.body).toEqual({ version: 0, expenseAccountCode: '6101' });
    req.flush({ changed: false, row: { expenseCategoryId: 3, version: 0 } }); fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="fi-op-map-notice"]').textContent).toContain('No account mapping changed');
  });
  it('FI-OP-UI-013 stale version closes the editor and requests a fresh map read', () => {
    const fixture = TestBed.createComponent(OtherPaymentMapEditor);
    fixture.componentRef.setInput('rows', [{ expenseCategoryId: 3, expenseCategoryCode: 'UTIL', expenseCategoryName: 'Utilities',
      expenseCategoryActive: true, configured: true, version: 2, expenseAccount: { id: 10, code: '6101', name: 'Expenses',
        isActive: true, isPostable: true, isUsable: true } }]);
    fixture.componentRef.setInput('canEdit', true); fixture.detectChanges();
    let refreshes = 0; fixture.componentInstance.changed.subscribe(() => refreshes++);
    fixture.componentInstance.begin(fixture.componentInstance.rows()[0]);
    fixture.componentInstance.code = '6102'; fixture.componentInstance.save();
    TestBed.inject(HttpTestingController).expectOne('/api/v1/gl/other-payment-account-map/3')
      .flush({ errorCode: 'FI.OTHER_PAYMENT_MAP.VERSION_CONFLICT' }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();
    expect(fixture.componentInstance.selected()).toBeNull();
    expect(refreshes).toBe(1);
    expect(fixture.nativeElement.textContent).toContain('Another user changed');
  });
});
