// 介面 8.67（REQ_F4118.28.1–4、REQ_F4118.28.6）：單號規則頁、API 服務、樣式組合、週期檢查、錯誤代碼與路由。
import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting, TestRequest } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TranslateService } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { seedSession } from '../../../../testing/auth-fixtures';
import { provideTestTranslations } from '../../../../testing/i18n-fixtures';
import { AUTHORIZATION_API, PermissionService } from '../../../core/auth/permission.service';
import { CompanyContextService } from '../../../core/company/company-context.service';
import { MAIN_MENU } from '../../../layout/menu';
import { isoDate } from '../../../shared/date';
import { SYS_ROUTES } from '../sys.routes';
import { NumberRuleList } from './number-rule-list';
import {
  DATE_FORMATS,
  dateFormatKey,
  formatNumber,
  NUMBER_RULE_API,
  NumberRule,
  numberRuleErrorKey,
  NumberRuleService,
  periodCovered,
  RESET_PERIODS,
  resetPeriodKey,
} from './number-rule.service';

const page = (items: object[], totalCount = items.length) => ({ items, page: 1, pageSize: 20, totalCount });

const rule = (overrides: Partial<NumberRule> = {}): NumberRule => ({
  id: 7,
  documentType: 'SD.ORDER',
  name: 'Sales order',
  prefix: 'SO',
  dateFormat: 'YearMonth',
  sequenceLength: 4,
  resetPeriod: 'Monthly',
  isActive: true,
  isInUse: false,
  sample: 'SO2026100001',
  version: 3,
  ...overrides,
});

describe('SYS number rules', () => {
  let controller: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    seedSession();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), ...provideTestTranslations('en')],
    });
    controller = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    controller.verify();
    localStorage.clear();
  });

  async function grant(codes: string[]): Promise<void> {
    const done = firstValueFrom(TestBed.inject(PermissionService).load());
    controller.expectOne(AUTHORIZATION_API).flush({
      companies: [{ id: 1, code: 'TW01', name: 'Taipei', isDefault: true }, { id: 2, code: 'HK01', name: 'Hong Kong', isDefault: false }],
      companyId: 1,
      isFullAccess: false,
      permissions: codes.map((code) => ({ code, dataScope: 'Company' })),
    });
    await done;
  }

  function expectList(): TestRequest {
    return controller.expectOne((request) => request.method === 'GET' && request.url === NUMBER_RULE_API);
  }

  function expectPreview(id = 7): TestRequest {
    return controller.expectOne((request) => request.method === 'GET' && request.url === `${NUMBER_RULE_API}/${id}/preview`);
  }

  describe('NumberRuleService and helpers', () => {
    it('sends paging, keyword, sort and preview date; posts and puts with the version', () => {
      const api = TestBed.inject(NumberRuleService);
      api.list({ page: 2, pageSize: 10, keyword: ' order ', sort: '-name' }).subscribe();
      const list = expectList();
      expect(list.request.params.toString()).toBe('page=2&pageSize=10&keyword=order&sort=-name');
      list.flush(page([]));

      api.list({ page: 1, pageSize: 20, keyword: ' ' }).subscribe();
      const blank = expectList();
      expect(blank.request.params.toString()).toBe('page=1&pageSize=20');
      blank.flush(page([]));

      api.preview(7, '2026-01-31').subscribe();
      const dated = expectPreview();
      expect(dated.request.params.get('date')).toBe('2026-01-31');
      dated.flush({ documentType: 'SD.ORDER', documentDate: '2026-01-31', nextNumber: 'SO2026010001' });

      api.preview(7).subscribe();
      const today = expectPreview();
      expect(today.request.params.has('date')).toBe(false);
      today.flush({ documentType: 'SD.ORDER', documentDate: '2026-10-11', nextNumber: 'SO2026100001' });

      const input = { documentType: 'SD.ORDER', name: 'SO', prefix: 'SO', dateFormat: 'YearMonth' as const, sequenceLength: 4, resetPeriod: 'Monthly' as const };
      api.create(input).subscribe();
      const post = controller.expectOne((r) => r.method === 'POST' && r.url === NUMBER_RULE_API);
      expect(post.request.body).toEqual(input);
      post.flush(rule());

      api.update(7, { name: 'SO', prefix: '', dateFormat: 'None', sequenceLength: 6, resetPeriod: 'Never', isActive: false, version: 3 }).subscribe();
      const put = controller.expectOne(`${NUMBER_RULE_API}/7`);
      expect(put.request.method).toBe('PUT');
      expect(put.request.body.version).toBe(3);
      put.flush(rule());
    });

    it('formats numbers like the backend for every date format', () => {
      const date = new Date(2026, 0, 5);
      expect(DATE_FORMATS.map((format) => formatNumber('SO', format, 4, date))).toEqual([
        'SO0001',
        'SO20260001',
        'SO2026010001',
        'SO202601050001',
        'SO26010001',
        'SO2601050001',
      ]);
      expect(formatNumber('', 'None', 3, date, 42)).toBe('042');
      expect(formatNumber('P', 'Year', 10, date, 123)).toBe('P20260000000123');
    });

    it('requires the reset period to be within the date format', () => {
      const covered = (period: (typeof RESET_PERIODS)[number]) => DATE_FORMATS.filter((format) => periodCovered(format, period));
      expect(covered('Never')).toEqual(DATE_FORMATS);
      expect(covered('Yearly')).toEqual(['Year', 'YearMonth', 'YearMonthDay', 'ShortYearMonth', 'ShortYearMonthDay']);
      expect(covered('Monthly')).toEqual(['YearMonth', 'YearMonthDay', 'ShortYearMonth', 'ShortYearMonthDay']);
      expect(covered('Daily')).toEqual(['YearMonthDay', 'ShortYearMonthDay']);
    });

    it('maps enum labels and number rule error codes', () => {
      expect(dateFormatKey('ShortYearMonthDay')).toBe('sys.numberRule.dateFormats.shortYearMonthDay');
      expect(resetPeriodKey('Never')).toBe('sys.numberRule.resetPeriods.never');
      const error = (status: number, errorCode?: string) => new HttpErrorResponse({ status, error: errorCode ? { errorCode } : null });
      for (const code of ['SYS.NUMBER_RULE.DUPLICATE_TYPE', 'SYS.NUMBER_RULE.NOT_FOUND', 'SYS.NUMBER_RULE.IN_USE', 'SYS.NUMBER_RULE.SEQUENCE_EXHAUSTED']) {
        expect(numberRuleErrorKey(error(409, code))).toBe(`errors.${code}`);
      }
      expect(numberRuleErrorKey(error(409, 'COMMON.CONCURRENCY.CONFLICT'))).toBe('errors.COMMON.CONCURRENCY.CONFLICT');
      expect(numberRuleErrorKey(error(403, 'COMMON.PERMISSION.DENIED'))).toBe('errors.COMMON.PERMISSION.DENIED');
      expect(numberRuleErrorKey(error(400))).toBe('errors.COMMON.REQUEST.VALIDATION_FAILED');
      expect(numberRuleErrorKey(error(0))).toBe('errors.http.network');
      expect(numberRuleErrorKey(error(422, 'SYS.NUMBER_RULE.NOT_CONFIGURED'))).toBe('errors.http.unknown');
    });
  });

  describe('NumberRuleList page', () => {
    let fixture: ComponentFixture<NumberRuleList>;

    async function open(): Promise<NumberRuleList> {
      TestBed.inject(CompanyContextService).setCompanies([{ id: 1, code: 'TW01', name: 'Taipei' }, { id: 2, code: 'HK01', name: 'Hong Kong' }]);
      fixture = TestBed.createComponent(NumberRuleList);
      fixture.detectChanges();
      await fixture.whenStable();
      return fixture.componentInstance;
    }

    function shown(testId: string): boolean {
      return fixture.nativeElement.querySelector(`[data-testid="${testId}"]`) !== null;
    }

    /** 對話框內容渲染在 CDK overlay（fixture 之外）。 */
    function dialogElement(testId: string): HTMLElement | null {
      return document.querySelector(`[data-testid="${testId}"]`);
    }

    async function render(): Promise<void> {
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();
    }

    it('lists rules with sample, labels and tags; buttons follow permissions', async () => {
      await open();
      const first = expectList();
      expect(first.request.params.get('sort')).toBe('documentType');
      first.flush(page([rule(), rule({ id: 8, documentType: 'PUR.ORDER', sample: 'PO0001', dateFormat: 'None', resetPeriod: 'Never', isActive: false, isInUse: true })]));
      await render();
      expect(shown('sys-number-rule-row-SD.ORDER')).toBe(true);
      expect(fixture.nativeElement.querySelector('[data-testid="sys-number-rule-sample-SD.ORDER"]').textContent).toContain('SO2026100001');
      const text = fixture.nativeElement.textContent as string;
      expect(text).toContain('yyyyMM');
      expect(text).toContain('Monthly');
      expect(text).toContain('No date');
      expect(text).toContain('Inactive');
      expect(shown('sys-number-rule-in-use-PUR.ORDER')).toBe(true);
      expect(shown('sys-number-rule-in-use-SD.ORDER')).toBe(false);
      expect(shown('sys-number-rule-preview-SD.ORDER')).toBe(true);
      expect(shown('sys-number-rule-create')).toBe(false);
      expect(shown('sys-number-rule-edit-SD.ORDER')).toBe(false);

      await grant(['SYS.NUMBER_RULE.VIEW', 'SYS.NUMBER_RULE.EDIT']);
      await render();
      expect(shown('sys-number-rule-edit-SD.ORDER')).toBe(true);
      expect(shown('sys-number-rule-create')).toBe(false);
    }, 15_000);

    it('searches from the first page, sorts by name and reloads when the company changes', async () => {
      const list = await open();
      expectList().flush(page([]));
      list.keyword.set('order');
      list.page.set(3);
      list.search();
      const search = expectList();
      expect(search.request.params.get('keyword')).toBe('order');
      expect(search.request.params.get('page')).toBe('1');
      search.flush(page([]));

      list.onQueryParams({ pageIndex: 1, pageSize: 20, sort: [{ key: 'documentType', value: null }, { key: 'name', value: 'descend' }], filter: [] });
      const sorted = expectList();
      expect(sorted.request.params.get('sort')).toBe('-name');
      sorted.flush(page([]));
      expect(list.sortOrder('name')).toBe('descend');
      expect(list.sortOrder('documentType')).toBeNull();

      list.onQueryParams({ pageIndex: 1, pageSize: 20, sort: [{ key: 'name', value: 'descend' }], filter: [] });
      controller.expectNone(NUMBER_RULE_API);

      list.onQueryParams({ pageIndex: 2, pageSize: 50, sort: [], filter: [] });
      const reset = expectList();
      expect(reset.request.params.get('sort')).toBe('documentType');
      expect(reset.request.params.get('pageSize')).toBe('50');
      reset.flush(page([]));
      expect(list.sortOrder('documentType')).toBe('ascend');

      // 切換公司：回第一頁並關閉預覽
      list.openPreview(rule());
      expectPreview().flush({ documentType: 'SD.ORDER', documentDate: '2026-10-11', nextNumber: 'SO2026100001' });
      TestBed.inject(CompanyContextService).select(2);
      await render();
      const switched = expectList();
      expect(switched.request.params.get('page')).toBe('1');
      switched.flush(page([]));
      expect(list.previewRule()).toBeNull();
    });

    it('shows a load error when the list request fails', async () => {
      await open();
      expectList().flush({ errorCode: 'COMMON.PERMISSION.DENIED' }, { status: 403, statusText: 'Forbidden' });
      await render();
      expect(fixture.componentInstance.loadError()).toBe('errors.COMMON.PERMISSION.DENIED');
      expect(shown('sys-number-rule-load-error')).toBe(true);
    });

    it('creates a rule: validates input, previews the sample live, flags an uncovered period and normalizes the request', async () => {
      await grant(['SYS.NUMBER_RULE.VIEW', 'SYS.NUMBER_RULE.CREATE']);
      const list = await open();
      expectList().flush(page([]));

      list.openCreate();
      await render();
      expect(list.form.getRawValue()).toEqual({
        documentType: '',
        name: '',
        prefix: '',
        dateFormat: 'YearMonth',
        sequenceLength: 4,
        resetPeriod: 'Monthly',
        isActive: true,
      });
      expect(list.form.controls.documentType.enabled).toBe(true);
      expect(dialogElement('sys-number-rule-active')).toBeNull();

      list.form.patchValue({ documentType: 'order', name: '  ', prefix: 'S O', sequenceLength: 11 });
      list.submit();
      expect(list.form.controls.documentType.invalid).toBe(true);
      expect(list.form.controls.name.invalid).toBe(true);
      expect(list.form.controls.prefix.invalid).toBe(true);
      expect(list.form.controls.sequenceLength.invalid).toBe(true);
      list.form.patchValue({ sequenceLength: null });
      expect(list.form.controls.sequenceLength.invalid).toBe(true);
      list.form.patchValue({ sequenceLength: 2 });
      expect(list.form.controls.sequenceLength.invalid).toBe(true);
      controller.expectNone((r) => r.method === 'POST');

      // 每日重設但日期只有年月：表單錯誤並顯示提示
      list.form.patchValue({ documentType: ' sd.order ', name: ' Sales order ', prefix: ' so ', sequenceLength: 5, resetPeriod: 'Daily' });
      await render();
      expect(list.form.hasError('period')).toBe(true);
      expect(dialogElement('sys-number-rule-period-error')).not.toBeNull();
      list.submit();
      controller.expectNone((r) => r.method === 'POST');

      list.form.patchValue({ dateFormat: 'ShortYearMonthDay' });
      await render();
      expect(list.form.valid).toBe(true);
      expect(dialogElement('sys-number-rule-period-error')).toBeNull();
      const today = new Date();
      expect(list.liveSample()).toBe(formatNumber('SO', 'ShortYearMonthDay', 5, today));
      expect(dialogElement('sys-number-rule-live-sample')!.textContent).toContain(list.liveSample());

      list.submit();
      list.submit();
      const post = controller.expectOne((r) => r.method === 'POST' && r.url === NUMBER_RULE_API);
      expect(post.request.body).toEqual({
        documentType: 'SD.ORDER',
        name: 'Sales order',
        prefix: 'SO',
        dateFormat: 'ShortYearMonthDay',
        sequenceLength: 5,
        resetPeriod: 'Daily',
      });
      post.flush({ errorCode: 'SYS.NUMBER_RULE.DUPLICATE_TYPE' }, { status: 409, statusText: 'Conflict' });
      expect(list.saveError()).toBe('errors.SYS.NUMBER_RULE.DUPLICATE_TYPE');
      expect(list.dialog()).toEqual({ mode: 'create' });
      await render();
      expect(dialogElement('sys-number-rule-save-error')!.textContent).toContain('already exists for this document type');

      list.submit();
      controller.expectOne((r) => r.method === 'POST').flush(rule(), { status: 201, statusText: 'Created' });
      expect(list.dialog()).toBeNull();
      expect(list.saveError()).toBeNull();
      expect(list.notice()).toBe('sys.numberRule.created');
      expect(list.noticeParams()).toEqual({ type: 'SD.ORDER' });
      expectList().flush(page([rule()]));
      await render();
      expect(fixture.nativeElement.querySelector('[data-testid="sys-number-rule-notice"]').textContent).toContain('Number rule SD.ORDER was created.');
    });

    it('clamps the live sample length while the length input is out of range', async () => {
      const list = await open();
      expectList().flush(page([]));
      list.openCreate();
      list.form.patchValue({ prefix: 'x', dateFormat: 'None', sequenceLength: null });
      expect(list.liveSample()).toBe('X001');
      list.form.patchValue({ sequenceLength: 20 });
      expect(list.liveSample()).toBe('X0000000001');
      list.closeDialog();
    });

    it('edits an unused rule with the read version; document type stays read-only', async () => {
      await grant(['SYS.NUMBER_RULE.VIEW', 'SYS.NUMBER_RULE.EDIT']);
      const list = await open();
      expectList().flush(page([rule()]));

      list.openEdit(list.rows()[0]);
      await render();
      expect(list.dialogTitle()).toBe('sys.numberRule.editTitle');
      expect(list.dialogTitleParams()).toEqual({ type: 'SD.ORDER' });
      expect(list.form.controls.documentType.disabled).toBe(true);
      expect(list.form.controls.dateFormat.enabled).toBe(true);
      expect(list.form.controls.resetPeriod.enabled).toBe(true);
      expect(list.minLength()).toBe(3);
      expect(dialogElement('sys-number-rule-locked')).toBeNull();
      expect(dialogElement('sys-number-rule-active')).not.toBeNull();

      list.form.patchValue({ prefix: '', dateFormat: 'None', resetPeriod: 'Never', sequenceLength: 3, isActive: false });
      list.submit();
      const put = controller.expectOne(`${NUMBER_RULE_API}/7`);
      expect(put.request.body).toEqual({
        name: 'Sales order',
        prefix: '',
        dateFormat: 'None',
        sequenceLength: 3,
        resetPeriod: 'Never',
        isActive: false,
        version: 3,
      });
      put.flush({ errorCode: 'COMMON.CONCURRENCY.CONFLICT' }, { status: 409, statusText: 'Conflict' });
      expect(list.saveError()).toBe('errors.COMMON.CONCURRENCY.CONFLICT');

      list.submit();
      controller.expectOne(`${NUMBER_RULE_API}/7`).flush(rule({ version: 4 }));
      expect(list.notice()).toBe('sys.numberRule.updated');
      expect(list.noticeParams()).toEqual({ type: 'SD.ORDER' });
      expectList().flush(page([]));
    });

    it('locks the date format and reset period of a rule in use and forbids a shorter sequence', async () => {
      await grant(['SYS.NUMBER_RULE.VIEW', 'SYS.NUMBER_RULE.EDIT']);
      const list = await open();
      expectList().flush(page([rule({ isInUse: true, sequenceLength: 5 })]));

      list.openEdit(list.rows()[0]);
      await render();
      expect(dialogElement('sys-number-rule-locked')).not.toBeNull();
      expect(list.form.controls.dateFormat.disabled).toBe(true);
      expect(list.form.controls.resetPeriod.disabled).toBe(true);
      expect(list.minLength()).toBe(5);

      list.form.patchValue({ sequenceLength: 4 });
      list.submit();
      expect(list.form.controls.sequenceLength.hasError('min')).toBe(true);
      controller.expectNone((r) => r.method === 'PUT');
      await render();
      expect(document.body.textContent).toContain('Enter a whole number from 5 to 10');

      list.form.patchValue({ sequenceLength: 6, prefix: 'SO-' });
      list.submit();
      const put = controller.expectOne(`${NUMBER_RULE_API}/7`);
      // 停用的欄位仍以原值送出
      expect(put.request.body).toMatchObject({ prefix: 'SO-', dateFormat: 'YearMonth', resetPeriod: 'Monthly', sequenceLength: 6 });
      put.flush({ errorCode: 'SYS.NUMBER_RULE.IN_USE' }, { status: 422, statusText: 'Unprocessable Entity' });
      expect(list.saveError()).toBe('errors.SYS.NUMBER_RULE.IN_USE');
      list.closeDialog();
      expect(list.saveError()).toBeNull();

      // 之後開新增：鎖定解除、長度下限恢復
      list.openCreate();
      expect(list.form.controls.dateFormat.enabled).toBe(true);
      expect(list.form.controls.resetPeriod.enabled).toBe(true);
      expect(list.minLength()).toBe(3);
      expect(list.form.controls.sequenceLength.value).toBe(4);
      list.closeDialog();
    });

    it('previews the next number for today and a chosen date, ignoring stale responses', async () => {
      const list = await open();
      expectList().flush(page([rule()]));

      list.openPreview(list.rows()[0]);
      expect(list.previewDate()).toBe(isoDate(new Date()));
      expect(list.previewTitleParams()).toEqual({ type: 'SD.ORDER' });
      const today = expectPreview();
      expect(today.request.params.get('date')).toBe(list.previewDate());
      expect(list.previewLoading()).toBe(true);
      today.flush({ documentType: 'SD.ORDER', documentDate: list.previewDate(), nextNumber: 'SO2026100005' });
      expect(list.previewLoading()).toBe(false);
      await render();
      expect(dialogElement('sys-number-rule-preview-result')!.textContent).toContain('SO2026100005');

      // 較早的請求在新請求之後才回來：不覆蓋
      list.previewDate.set('2026-01-31');
      list.runPreview();
      const stale = expectPreview();
      list.previewDate.set('2026-02-28');
      list.runPreview();
      const latest = controller.match((r) => r.url === `${NUMBER_RULE_API}/7/preview`);
      expect(latest).toHaveLength(1);
      latest[0].flush({ documentType: 'SD.ORDER', documentDate: '2026-02-28', nextNumber: 'SO2026020001' });
      stale.flush({ documentType: 'SD.ORDER', documentDate: '2026-01-31', nextNumber: 'SO2026010009' });
      expect(list.previewResult()?.nextNumber).toBe('SO2026020001');

      // 清空日期：由後端用今天
      list.previewDate.set('');
      list.runPreview();
      const blank = expectPreview();
      expect(blank.request.params.has('date')).toBe(false);
      blank.flush({ errorCode: 'SYS.NUMBER_RULE.SEQUENCE_EXHAUSTED' }, { status: 422, statusText: 'Unprocessable Entity' });
      expect(list.previewError()).toBe('errors.SYS.NUMBER_RULE.SEQUENCE_EXHAUSTED');
      expect(list.previewResult()).toBeNull();
      await render();
      expect(dialogElement('sys-number-rule-preview-error')).not.toBeNull();

      // 關閉後遲到的回應不套用
      list.runPreview();
      const late = expectPreview();
      list.closePreview();
      late.error(new ProgressEvent('error'));
      expect(list.previewError()).toBeNull();
      expect(list.previewRule()).toBeNull();
      list.runPreview();
      controller.expectNone(`${NUMBER_RULE_API}/7/preview`);
    });

    it('switches translated labels', async () => {
      await open();
      expectList().flush(page([rule()]));
      await render();
      expect(fixture.nativeElement.textContent).toContain('Document number rules');
      TestBed.inject(TranslateService).use('zh-Hant');
      await render();
      expect(fixture.nativeElement.textContent).toContain('單號規則');
      expect(fixture.nativeElement.textContent).toContain('每月');
      TestBed.inject(TranslateService).use('zh-Hans');
      await render();
      expect(fixture.nativeElement.textContent).toContain('单号规则');
    });
  });

  it('routes /sys/number-rules behind SYS.NUMBER_RULE.VIEW, lazy-loads the page and lists it in the menu', async () => {
    const route = SYS_ROUTES.find((r) => r.path === 'number-rules')!;
    expect(route.canActivate).toHaveLength(1);
    expect(await route.loadComponent!()).toBe(NumberRuleList);
    const sys = MAIN_MENU.find((item) => item.testId === 'layout-menu-sys')!;
    expect(sys.children!.find((item) => item.route === '/sys/number-rules')?.permission).toBe('SYS.NUMBER_RULE.VIEW');
  });
});
