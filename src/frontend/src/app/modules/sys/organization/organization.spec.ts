import { TestBed } from '@angular/core/testing';
import { TranslateService } from '@ngx-translate/core';
import { provideTestTranslations } from '../../../../testing/i18n-fixtures';
import { routes } from '../../../app.routes';
import { MAIN_MENU } from '../../../layout/menu';
import { OrganizationForm } from './organization-form';
import { OrganizationList } from './organization-list';

describe('SYS organization pages', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: provideTestTranslations() }));

  it('filters and pages company rows without exposing another company in a branch list', () => {
    const fixture = TestBed.createComponent(OrganizationList);
    fixture.componentRef.setInput('kind', 'branch');
    fixture.componentRef.setInput('companyId', 1);
    fixture.componentRef.setInput('rows', [
      { id: 1, companyId: 1, code: 'NORTH', name: 'North' },
      { id: 2, companyId: 2, code: 'SOUTH', name: 'South' },
      { id: 3, companyId: 1, code: 'EAST', name: 'East' },
    ]);
    fixture.detectChanges();
    const page = fixture.componentInstance;
    expect(page.visibleRows().map((row) => row.code)).toEqual(['EAST', 'NORTH']);
    page.search.set('north');
    expect(page.visibleRows().map((row) => row.code)).toEqual(['NORTH']);
    fixture.componentRef.setInput('companyId', null);
    fixture.detectChanges();
    expect(page.visibleRows()).toEqual([]);
  });

  it('validates company code and name before emitting a normalized draft', () => {
    const fixture = TestBed.createComponent(OrganizationForm);
    fixture.componentRef.setInput('kind', 'company');
    fixture.detectChanges();
    const saved: unknown[] = [];
    fixture.componentInstance.save.subscribe((value) => saved.push(value));
    fixture.componentInstance.form.setValue({ code: 'bad code', name: 'Acme', branchId: null, parentDepartmentId: null });
    fixture.componentInstance.submit();
    expect(saved).toEqual([]);
    fixture.componentInstance.form.patchValue({ code: 'acme_1' });
    fixture.componentInstance.submit();
    expect(saved).toEqual([{ code: 'ACME_1', name: 'Acme', branchId: null, parentDepartmentId: null }]);
    fixture.componentInstance.form.patchValue({ name: '   ' });
    fixture.componentInstance.submit();
    expect(saved).toHaveLength(1);
  });

  it('renders branch and department references and switches translated labels', () => {
    const fixture = TestBed.createComponent(OrganizationForm);
    fixture.componentRef.setInput('kind', 'department');
    fixture.componentRef.setInput('branches', [{ id: 10, code: 'MAIN', name: 'Main' }]);
    fixture.componentRef.setInput('departments', [{ id: 20, code: 'HR', name: 'Human Resources' }]);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="sys-branch-id"]')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('[data-testid="sys-parent-id"]')).not.toBeNull();
    TestBed.inject(TranslateService).use('en');
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Parent department');
  });

  it('keeps organization pages unreachable until authentication and permissions are wired', () => {
    expect(JSON.stringify(routes)).not.toContain('sys');
    expect(MAIN_MENU.some((item) => item.route?.startsWith('/sys'))).toBe(false);
  });
});
