// 介面 8.68–8.70（REQ_F4118.29.1–3、29.5–9）：審批流程、代理、待辦頁與 API 服務、錯誤代碼、路由與選單。
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
import { ROLE_API } from '../roles/role.service';
import { SYS_ROUTES } from '../sys.routes';
import { USER_API } from '../users/user.service';
import { ApprovalDelegationList } from './approval-delegation-list';
import { ApprovalFlowList } from './approval-flow-list';
import { ApprovalInbox } from './approval-inbox';
import {
  APPROVAL_API,
  APPROVAL_DELEGATION_API,
  APPROVAL_FLOW_API,
  ApprovalDelegation,
  approvalErrorKey,
  ApprovalFlow,
  ApprovalInstanceDetail,
  ApprovalInstanceSummary,
  ApprovalService,
  approvalStatusKey,
  ApprovalTask,
  approverTypeKey,
  displayTime,
  statusColor,
  stepStatusKey,
} from './approval.service';

const page = (items: object[], totalCount = items.length) => ({ items, page: 1, pageSize: 20, totalCount });

const flow = (overrides: Partial<ApprovalFlow> = {}): ApprovalFlow => ({
  id: 5,
  documentType: 'PUR.ORDER',
  name: 'Purchase order',
  allowSelfApproval: false,
  isActive: true,
  steps: [
    { stepNo: 1, name: 'Supervisor', approverType: 'Role', roleId: 11, userId: null, minAmount: null },
    { stepNo: 2, name: 'General manager', approverType: 'User', roleId: null, userId: 21, minAmount: 100000 },
  ],
  version: 2,
  ...overrides,
});

const delegation = (overrides: Partial<ApprovalDelegation> = {}): ApprovalDelegation => ({
  id: 9,
  delegatorUserId: 21,
  delegatorUsername: 'gm',
  delegateUserId: 22,
  delegateUsername: 'deputy',
  startDate: '2026-10-01',
  endDate: '2099-12-31',
  remark: 'Leave',
  isActive: true,
  version: 1,
  ...overrides,
});

const task = (overrides: Partial<ApprovalTask> = {}): ApprovalTask => ({
  instanceId: 31,
  documentType: 'PUR.ORDER',
  documentId: 1001,
  documentNo: 'PO-0001',
  summary: 'Office chairs',
  amount: 250000.5,
  submittedBy: 23,
  submittedByUsername: 'buyer',
  submittedAt: '2026-10-11T09:15:30',
  stepNo: 1,
  stepCount: 3,
  stepName: 'Supervisor',
  onBehalfOfUserId: null,
  onBehalfOfUsername: null,
  version: 4,
  ...overrides,
});

const summary = (overrides: Partial<ApprovalInstanceSummary> = {}): ApprovalInstanceSummary => ({
  id: 31,
  documentType: 'PUR.ORDER',
  documentId: 1001,
  documentNo: 'PO-0001',
  summary: 'Office chairs',
  amount: 250000.5,
  status: 'Approved',
  currentStepNo: null,
  submittedBy: 23,
  submittedByUsername: 'buyer',
  submittedAt: '2026-10-11T09:15:30',
  completedAt: '2026-10-11T10:00:00',
  version: 6,
  ...overrides,
});

const detail = (overrides: Partial<ApprovalInstanceDetail> = {}): ApprovalInstanceDetail => ({
  ...summary(),
  allowSelfApproval: false,
  steps: [
    {
      stepNo: 1, name: 'Supervisor', approverType: 'Role', roleId: 11, roleName: 'Supervisors', userId: null, username: null, minAmount: null,
      status: 'Approved', actedBy: 22, actedByUsername: 'deputy', onBehalfOfUserId: 21, onBehalfOfUsername: 'gm',
      actedAt: '2026-10-11T09:30:00', comment: 'ok',
    },
    {
      stepNo: 2, name: 'General manager', approverType: 'User', roleId: null, roleName: null, userId: 21, username: 'gm', minAmount: 100000,
      status: 'Pending', actedBy: null, actedByUsername: null, onBehalfOfUserId: null, onBehalfOfUsername: null, actedAt: null, comment: null,
    },
  ],
  ...overrides,
});

const roles = [
  { id: 11, code: 'SUP', name: 'Supervisors', description: null, dataScope: 'Company', isFullAccess: false, isActive: true, version: 1, permissions: [] },
  { id: 12, code: 'OLD', name: 'Retired', description: null, dataScope: 'Company', isFullAccess: false, isActive: false, version: 1, permissions: [] },
];
const users = [
  { id: 21, username: 'gm', isActive: true, isLocked: false, passwordChangedAt: '2026-01-01T00:00:00', isPasswordExpired: false, version: 1 },
  { id: 22, username: 'deputy', isActive: true, isLocked: false, passwordChangedAt: '2026-01-01T00:00:00', isPasswordExpired: false, version: 1 },
  { id: 24, username: 'gone', isActive: false, isLocked: false, passwordChangedAt: '2026-01-01T00:00:00', isPasswordExpired: false, version: 1 },
];

const forbidden = (request: TestRequest) => request.flush({ errorCode: 'COMMON.PERMISSION.DENIED' }, { status: 403, statusText: 'Forbidden' });

describe('SYS approvals', () => {
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

  const get = (url: string) => (request: { method: string; url: string }) => request.method === 'GET' && request.url === url;

  function expectRoles(): TestRequest {
    return controller.expectOne(get(ROLE_API));
  }

  function expectUsers(): TestRequest {
    return controller.expectOne(get(USER_API));
  }

  describe('ApprovalService and helpers', () => {
    it('sends flow, delegation, task and history queries and posts decisions with the version', () => {
      const api = TestBed.inject(ApprovalService);
      api.listFlows({ page: 2, pageSize: 10, keyword: ' order ', sort: '-name' }).subscribe();
      const flows = controller.expectOne(get(APPROVAL_FLOW_API));
      expect(flows.request.params.toString()).toBe('page=2&pageSize=10&keyword=order&sort=-name');
      flows.flush(page([]));

      const input = { documentType: 'PUR.ORDER', name: 'PO', allowSelfApproval: true, steps: [] };
      api.createFlow(input).subscribe();
      const post = controller.expectOne((r) => r.method === 'POST' && r.url === APPROVAL_FLOW_API);
      expect(post.request.body).toEqual(input);
      post.flush(flow());
      api.updateFlow(5, { name: 'PO', allowSelfApproval: false, isActive: false, steps: [], version: 2 }).subscribe();
      const put = controller.expectOne(`${APPROVAL_FLOW_API}/5`);
      expect([put.request.method, put.request.body.version]).toEqual(['PUT', 2]);
      put.flush(flow());

      api.listDelegations({ page: 1, pageSize: 20, userId: 21, activeOn: '2026-10-11', sort: 'startDate' }).subscribe();
      const delegations = controller.expectOne(get(APPROVAL_DELEGATION_API));
      expect(delegations.request.params.toString()).toBe('page=1&pageSize=20&userId=21&activeOn=2026-10-11&sort=startDate');
      delegations.flush(page([]));
      api.listDelegations({ page: 1, pageSize: 20, userId: null }).subscribe();
      expect(controller.expectOne(get(APPROVAL_DELEGATION_API)).request.params.toString()).toBe('page=1&pageSize=20');
      api.createDelegation({ delegatorUserId: 21, delegateUserId: 22, startDate: '2026-10-01', endDate: '2026-10-31', remark: null }).subscribe();
      controller.expectOne((r) => r.method === 'POST' && r.url === APPROVAL_DELEGATION_API).flush(delegation());
      api.updateDelegation(9, { startDate: '2026-10-01', endDate: '2026-10-31', remark: null, isActive: false, version: 1 }).subscribe();
      expect(controller.expectOne(`${APPROVAL_DELEGATION_API}/9`).request.method).toBe('PUT');

      api.tasks({ page: 1, pageSize: 20, documentType: ' pur.order ', keyword: ' PO ', sort: 'submittedAt' }).subscribe();
      const tasks = controller.expectOne(get(`${APPROVAL_API}/tasks`));
      expect(tasks.request.params.toString()).toBe('page=1&pageSize=20&documentType=PUR.ORDER&keyword=PO&sort=submittedAt');
      tasks.flush(page([]));
      api.history({ page: 1, pageSize: 20, status: 'Rejected', sort: '-submittedAt' }).subscribe();
      const history = controller.expectOne(get(APPROVAL_API));
      expect(history.request.params.toString()).toBe('page=1&pageSize=20&sort=-submittedAt&status=Rejected');
      history.flush(page([]));
      api.get(31).subscribe();
      controller.expectOne(`${APPROVAL_API}/31`).flush(detail());

      api.approve(31, null, 4).subscribe();
      const approve = controller.expectOne(`${APPROVAL_API}/31/approve`);
      expect(approve.request.body).toEqual({ comment: null, version: 4 });
      approve.flush(detail());
      api.reject(31, 'Too expensive', 4).subscribe();
      expect(controller.expectOne(`${APPROVAL_API}/31/reject`).request.body).toEqual({ comment: 'Too expensive', version: 4 });
    });

    it('loads only active roles and users as approver options', async () => {
      const api = TestBed.inject(ApprovalService);
      const roleOptions = firstValueFrom(api.roleOptions());
      const roleRequest = expectRoles();
      expect(roleRequest.request.params.toString()).toBe('page=1&pageSize=200&sort=code');
      roleRequest.flush(page(roles));
      expect(await roleOptions).toEqual([{ id: 11, label: 'SUP Supervisors' }]);
      const userOptions = firstValueFrom(api.userOptions());
      const userRequest = expectUsers();
      expect(userRequest.request.params.toString()).toBe('page=1&pageSize=200&sort=username');
      userRequest.flush(page(users));
      expect(await userOptions).toEqual([{ id: 21, label: 'gm' }, { id: 22, label: 'deputy' }]);
    });

    it('maps labels, colors, times and approval error codes', () => {
      expect(approvalStatusKey('Cancelled')).toBe('sys.approval.statuses.cancelled');
      expect(stepStatusKey('Waiting')).toBe('sys.approval.stepStatuses.waiting');
      expect(approverTypeKey('User')).toBe('sys.approvalFlow.approverTypes.user');
      expect(['Approved', 'Rejected', 'Pending', 'Skipped'].map((s) => statusColor(s as 'Approved'))).toEqual(['success', 'error', 'processing', 'default']);
      expect(displayTime('2026-10-11T09:15:30.123')).toBe('2026-10-11 09:15');
      expect(displayTime(null)).toBe('');
      const error = (status: number, errorCode?: string) => new HttpErrorResponse({ status, error: errorCode ? { errorCode } : null });
      for (const code of [
        'SYS.APPROVAL_FLOW.DUPLICATE_TYPE',
        'SYS.APPROVAL_DELEGATION.OVERLAP',
        'SYS.APPROVAL.NOT_PENDING',
        'SYS.APPROVAL.NOT_APPROVER',
        'SYS.APPROVAL.SELF_APPROVAL',
      ]) {
        expect(approvalErrorKey(error(409, code))).toBe(`errors.${code}`);
      }
      expect(approvalErrorKey(error(400))).toBe('errors.COMMON.REQUEST.VALIDATION_FAILED');
      expect(approvalErrorKey(error(0))).toBe('errors.http.network');
      expect(approvalErrorKey(error(422, 'SYS.APPROVAL.USER_REQUIRED'))).toBe('errors.http.unknown');
    });
  });

  /** 對話框內容渲染在 CDK overlay（fixture 之外）。 */
  function dialogElement(testId: string): HTMLElement | null {
    return document.querySelector(`[data-testid="${testId}"]`);
  }

  function setCompanies(): void {
    TestBed.inject(CompanyContextService).setCompanies([{ id: 1, code: 'TW01', name: 'Taipei' }, { id: 2, code: 'HK01', name: 'Hong Kong' }]);
  }

  describe('ApprovalFlowList page', () => {
    let fixture: ComponentFixture<ApprovalFlowList>;

    async function open(): Promise<ApprovalFlowList> {
      setCompanies();
      fixture = TestBed.createComponent(ApprovalFlowList);
      fixture.detectChanges();
      await fixture.whenStable();
      return fixture.componentInstance;
    }

    async function render(): Promise<void> {
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();
    }

    function shown(testId: string): boolean {
      return fixture.nativeElement.querySelector(`[data-testid="${testId}"]`) !== null;
    }

    function flushStartup(rows: ApprovalFlow[] = []): void {
      expectRoles().flush(page(roles));
      expectUsers().flush(page(users));
      controller.expectOne(get(APPROVAL_FLOW_API)).flush(page(rows));
    }

    it('lists flows with step approvers, thresholds and tags; buttons follow permissions', async () => {
      await open();
      flushStartup([flow(), flow({ id: 6, documentType: 'SAL.ORDER', isActive: false, allowSelfApproval: true, steps: [
        { stepNo: 1, name: 'Old role', approverType: 'Role', roleId: 12, userId: null, minAmount: 0.5 },
      ] })]);
      await render();
      const steps = fixture.nativeElement.querySelector('[data-testid="sys-approval-flow-steps-PUR.ORDER"]').textContent as string;
      expect(steps).toContain('Supervisor（Role：SUP Supervisors）');
      expect(steps).toContain('General manager（User：gm）');
      expect(steps).toContain('≥ 100,000');
      // 停用角色不在選項內：以 ID 顯示
      expect(fixture.nativeElement.querySelector('[data-testid="sys-approval-flow-steps-SAL.ORDER"]').textContent).toContain('#12');
      expect(shown('sys-approval-flow-self-SAL.ORDER')).toBe(true);
      expect(shown('sys-approval-flow-self-PUR.ORDER')).toBe(false);
      expect(fixture.nativeElement.textContent).toContain('Inactive');
      expect(shown('sys-approval-flow-create')).toBe(false);
      expect(shown('sys-approval-flow-edit-PUR.ORDER')).toBe(false);

      await grant(['SYS.APPROVAL_FLOW.VIEW', 'SYS.APPROVAL_FLOW.EDIT']);
      await render();
      expect(shown('sys-approval-flow-edit-PUR.ORDER')).toBe(true);
      expect(shown('sys-approval-flow-create')).toBe(false);
    }, 15_000);

    it('searches, sorts, warns when options cannot load and reloads when the company changes', async () => {
      const list = await open();
      forbidden(expectRoles());
      expectUsers().flush(page(users));
      controller.expectOne(get(APPROVAL_FLOW_API)).flush(page([flow()]));
      expect(list.optionsError()).toBe(true);
      expect(list.approverLabel({ approverType: 'Role', roleId: 11, userId: null })).toBe('#11');

      list.keyword.set('order');
      list.page.set(3);
      list.search();
      const search = controller.expectOne(get(APPROVAL_FLOW_API));
      expect([search.request.params.get('keyword'), search.request.params.get('page')]).toEqual(['order', '1']);
      search.flush(page([]));

      list.onQueryParams({ pageIndex: 1, pageSize: 20, sort: [{ key: 'name', value: 'descend' }], filter: [] });
      const sorted = controller.expectOne(get(APPROVAL_FLOW_API));
      expect(sorted.request.params.get('sort')).toBe('-name');
      sorted.flush(page([]));
      expect([list.sortOrder('name'), list.sortOrder('documentType')]).toEqual(['descend', null]);
      list.onQueryParams({ pageIndex: 1, pageSize: 20, sort: [{ key: 'name', value: 'descend' }], filter: [] });
      controller.expectNone(APPROVAL_FLOW_API);

      list.openCreate();
      TestBed.inject(CompanyContextService).select(2);
      await render();
      expect(list.dialog()).toBeNull();
      flushStartup();
      expect(list.optionsError()).toBe(false);
      expect(list.page()).toBe(1);
    });

    it('shows a load error when the list request fails', async () => {
      const list = await open();
      expectRoles().flush(page(roles));
      expectUsers().flush(page(users));
      controller.expectOne(get(APPROVAL_FLOW_API)).flush({ errorCode: 'COMMON.PERMISSION.DENIED' }, { status: 403, statusText: 'Forbidden' });
      await render();
      expect(list.loadError()).toBe('errors.COMMON.PERMISSION.DENIED');
      expect(shown('sys-approval-flow-load-error')).toBe(true);
    });

    it('creates a flow: validates header and steps, edits the step list and normalizes the request', async () => {
      await grant(['SYS.APPROVAL_FLOW.VIEW', 'SYS.APPROVAL_FLOW.CREATE']);
      const list = await open();
      flushStartup();

      list.openCreate();
      await render();
      expect(list.steps.length).toBe(1);
      expect(list.form.getRawValue()).toEqual({
        documentType: '',
        name: '',
        allowSelfApproval: false,
        isActive: true,
        steps: [{ name: '', approverType: 'Role', roleId: null, userId: null, minAmount: '' }],
      });
      expect(dialogElement('sys-approval-flow-active')).toBeNull();
      expect(dialogElement('sys-approval-flow-step-role-1')).not.toBeNull();

      list.submit();
      controller.expectNone((r) => r.method === 'POST');
      expect(list.form.controls.documentType.invalid).toBe(true);
      expect(list.steps.at(0).hasError('approver')).toBe(true);
      await render();
      expect(dialogElement('sys-approval-flow-step-error-1')!.textContent).toContain('Select the approver.');

      // 金額門檻：負數、超過 4 位小數或超過 11 位整數皆不接受
      for (const bad of ['-1', '1.23456', '123456789012', '1e5']) {
        list.steps.at(0).controls.minAmount.setValue(bad);
        expect(list.steps.at(0).controls.minAmount.invalid).toBe(true);
      }
      list.steps.at(0).patchValue({ name: ' Supervisor ', roleId: 11, minAmount: '' });
      list.addStep();
      list.steps.at(1).patchValue({ name: 'GM', approverType: 'User', roleId: 11, userId: 21, minAmount: ' 100000.25 ' });
      list.addStep();
      list.steps.at(2).patchValue({ name: 'Manager', roleId: 11, minAmount: '10000' });
      await render();
      expect(dialogElement('sys-approval-flow-step-user-2')).not.toBeNull();

      // 經理移到總經理之前
      list.moveStep(2, -1);
      expect(list.steps.controls.map((s) => s.controls.name.value)).toEqual([' Supervisor ', 'Manager', 'GM']);
      list.moveStep(0, -1);
      list.moveStep(2, 1);
      expect(list.steps.controls.map((s) => s.controls.name.value)).toEqual([' Supervisor ', 'Manager', 'GM']);
      while (list.steps.length < 10) list.addStep();
      list.addStep();
      expect(list.steps.length).toBe(10);
      while (list.steps.length > 3) list.removeStep(3);

      list.form.patchValue({ documentType: ' pur.order ', name: ' Purchase order ', allowSelfApproval: true });
      expect(list.form.valid).toBe(true);
      list.submit();
      list.submit();
      const post = controller.expectOne((r) => r.method === 'POST' && r.url === APPROVAL_FLOW_API);
      expect(post.request.body).toEqual({
        documentType: 'PUR.ORDER',
        name: 'Purchase order',
        allowSelfApproval: true,
        steps: [
          { name: 'Supervisor', approverType: 'Role', roleId: 11, userId: null, minAmount: null },
          { name: 'Manager', approverType: 'Role', roleId: 11, userId: null, minAmount: 10000 },
          // 用戶步驟不送出殘留的角色
          { name: 'GM', approverType: 'User', roleId: null, userId: 21, minAmount: 100000.25 },
        ],
      });
      post.flush({ errorCode: 'SYS.APPROVAL_FLOW.DUPLICATE_TYPE' }, { status: 409, statusText: 'Conflict' });
      expect(list.saveError()).toBe('errors.SYS.APPROVAL_FLOW.DUPLICATE_TYPE');
      await render();
      expect(dialogElement('sys-approval-flow-save-error')!.textContent).toContain('already exists for this document type');

      list.submit();
      controller.expectOne((r) => r.method === 'POST').flush(flow(), { status: 201, statusText: 'Created' });
      expect(list.dialog()).toBeNull();
      expect([list.notice(), list.noticeParams()]).toEqual(['sys.approvalFlow.created', { type: 'PUR.ORDER' }]);
      controller.expectOne(get(APPROVAL_FLOW_API)).flush(page([flow()]));
      await render();
      expect(fixture.nativeElement.querySelector('[data-testid="sys-approval-flow-notice"]').textContent).toContain('Approval flow PUR.ORDER was created.');
    }, 15_000);

    it('edits a flow with the version, keeps the document type read-only and reports conflicts', async () => {
      await grant(['SYS.APPROVAL_FLOW.VIEW', 'SYS.APPROVAL_FLOW.EDIT']);
      const list = await open();
      flushStartup([flow()]);

      // 步驟的審批人已不在選項中（停用角色）：補上 #Id 選項，編輯時仍可顯示
      list.openEdit(flow({ steps: [{ stepNo: 1, name: 'Old', approverType: 'Role', roleId: 12, userId: null, minAmount: 5 }] }));
      expect(list.roleChoices(12)).toEqual([{ id: 11, label: 'SUP Supervisors' }, { id: 12, label: '#12' }]);
      expect(list.userChoices(null).length).toBe(2);
      expect(list.steps.at(0).controls.minAmount.value).toBe('5');
      list.closeDialog();

      list.openEdit(flow());
      await render();
      expect(list.form.controls.documentType.disabled).toBe(true);
      expect(list.steps.length).toBe(2);
      expect(dialogElement('sys-approval-flow-active')).not.toBeNull();
      list.form.patchValue({ isActive: false, name: 'PO v2' });
      list.removeStep(1);
      list.submit();
      const put = controller.expectOne(`${APPROVAL_FLOW_API}/5`);
      expect(put.request.body).toEqual({
        name: 'PO v2',
        allowSelfApproval: false,
        steps: [{ name: 'Supervisor', approverType: 'Role', roleId: 11, userId: null, minAmount: null }],
        isActive: false,
        version: 2,
      });
      put.flush({ errorCode: 'COMMON.CONCURRENCY.CONFLICT' }, { status: 409, statusText: 'Conflict' });
      expect(list.saveError()).toBe('errors.COMMON.CONCURRENCY.CONFLICT');
      list.submit();
      controller.expectOne(`${APPROVAL_FLOW_API}/5`).flush(flow());
      expect(list.notice()).toBe('sys.approvalFlow.updated');
      controller.expectOne(get(APPROVAL_FLOW_API)).flush(page([flow()]));
    });
  });

  describe('ApprovalDelegationList page', () => {
    let fixture: ComponentFixture<ApprovalDelegationList>;

    async function open(): Promise<ApprovalDelegationList> {
      setCompanies();
      fixture = TestBed.createComponent(ApprovalDelegationList);
      fixture.detectChanges();
      await fixture.whenStable();
      return fixture.componentInstance;
    }

    async function render(): Promise<void> {
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();
    }

    function shown(testId: string): boolean {
      return fixture.nativeElement.querySelector(`[data-testid="${testId}"]`) !== null;
    }

    function expectList(): TestRequest {
      return controller.expectOne(get(APPROVAL_DELEGATION_API));
    }

    it('lists delegations, marks the ones in effect and filters by user and date', async () => {
      const list = await open();
      expectUsers().flush(page(users));
      const first = expectList();
      expect(first.request.params.toString()).toBe('page=1&pageSize=20&sort=-startDate');
      first.flush(page([delegation(), delegation({ id: 10, startDate: '2020-01-01', endDate: '2020-01-31', isActive: false, remark: null })]));
      await render();
      expect(fixture.nativeElement.querySelector('[data-testid="sys-approval-delegation-row-9"]').textContent).toContain('gm');
      expect(shown('sys-approval-delegation-current-9')).toBe(true);
      expect(shown('sys-approval-delegation-current-10')).toBe(false);
      expect(shown('sys-approval-delegation-create')).toBe(false);
      expect(shown('sys-approval-delegation-edit-9')).toBe(false);

      list.userFilter.set(21);
      list.activeOn.set('2026-10-11');
      list.page.set(2);
      list.search();
      expect(expectList().request.params.toString()).toBe('page=1&pageSize=20&userId=21&activeOn=2026-10-11&sort=-startDate');

      // 切換公司：清除用戶篩選、重新載入用戶
      TestBed.inject(CompanyContextService).select(2);
      await render();
      expect(list.userFilter()).toBeNull();
      forbidden(expectUsers());
      forbidden(expectList());
      await render();
      expect(list.optionsError()).toBe(true);
      expect(list.loadError()).toBe('errors.COMMON.PERMISSION.DENIED');
      expect(shown('sys-approval-delegation-options-error')).toBe(true);

      list.onQueryParams({ pageIndex: 1, pageSize: 20, sort: [{ key: 'startDate', value: 'ascend' }], filter: [] });
      expect(expectList().request.params.get('sort')).toBe('startDate');
      expect(list.sortOrder()).toBe('ascend');
      list.onQueryParams({ pageIndex: 1, pageSize: 20, sort: [{ key: 'startDate', value: 'ascend' }], filter: [] });
      controller.expectNone(APPROVAL_DELEGATION_API);
    }, 15_000);

    it('creates a delegation: requires two different users and a valid period, shows overlap errors', async () => {
      await grant(['SYS.APPROVAL_DELEGATION.VIEW', 'SYS.APPROVAL_DELEGATION.CREATE', 'SYS.APPROVAL_DELEGATION.EDIT']);
      const list = await open();
      expectUsers().flush(page(users));
      expectList().flush(page([delegation()]));
      await render();
      expect(shown('sys-approval-delegation-create')).toBe(true);
      expect(shown('sys-approval-delegation-edit-9')).toBe(true);

      list.openCreate();
      await render();
      const today = isoDate(new Date());
      expect(list.form.getRawValue()).toEqual({ delegatorUserId: null, delegateUserId: null, startDate: today, endDate: today, remark: '', isActive: true });
      list.submit();
      controller.expectNone((r) => r.method === 'POST');

      list.form.patchValue({ delegatorUserId: 21, delegateUserId: 21, startDate: '2026-10-20', endDate: '2026-10-10', remark: 'x'.repeat(201) });
      await render();
      expect(list.form.hasError('sameUser')).toBe(true);
      expect(list.form.hasError('dateRange')).toBe(true);
      expect(list.form.controls.remark.invalid).toBe(true);
      expect(dialogElement('sys-approval-delegation-same-user')).not.toBeNull();
      expect(dialogElement('sys-approval-delegation-date-range')).not.toBeNull();
      list.submit();
      controller.expectNone((r) => r.method === 'POST');

      list.form.patchValue({ delegateUserId: 22, endDate: '2026-10-31', remark: '  ' });
      list.submit();
      const post = controller.expectOne((r) => r.method === 'POST' && r.url === APPROVAL_DELEGATION_API);
      expect(post.request.body).toEqual({ delegatorUserId: 21, delegateUserId: 22, startDate: '2026-10-20', endDate: '2026-10-31', remark: null });
      post.flush({ errorCode: 'SYS.APPROVAL_DELEGATION.OVERLAP' }, { status: 409, statusText: 'Conflict' });
      await render();
      expect(dialogElement('sys-approval-delegation-save-error')!.textContent).toContain('Periods cannot overlap');

      list.submit();
      controller.expectOne((r) => r.method === 'POST').flush(delegation(), { status: 201, statusText: 'Created' });
      expect([list.notice(), list.noticeParams()]).toEqual(['sys.approvalDelegation.created', { delegator: 'gm', delegate: 'deputy' }]);
      expectList().flush(page([delegation()]));
      await render();
      expect(fixture.nativeElement.querySelector('[data-testid="sys-approval-delegation-notice"]').textContent).toContain('deputy now approves on behalf of gm.');
    });

    it('edits only the period, remark and status with the version', async () => {
      await grant(['SYS.APPROVAL_DELEGATION.VIEW', 'SYS.APPROVAL_DELEGATION.EDIT']);
      const list = await open();
      expectUsers().flush(page(users));
      expectList().flush(page([delegation()]));

      list.openEdit(delegation());
      await render();
      expect(list.form.controls.delegatorUserId.disabled).toBe(true);
      expect(dialogElement('sys-approval-delegation-delegator-name')!.textContent).toContain('gm');
      expect(dialogElement('sys-approval-delegation-delegator')).toBeNull();
      list.form.patchValue({ endDate: '2026-10-15', isActive: false, remark: ' Back early ' });
      list.submit();
      const put = controller.expectOne(`${APPROVAL_DELEGATION_API}/9`);
      expect(put.request.body).toEqual({ startDate: '2026-10-01', endDate: '2026-10-15', remark: 'Back early', isActive: false, version: 1 });
      put.flush(delegation({ isActive: false }));
      expect(list.notice()).toBe('sys.approvalDelegation.updated');
      expectList().flush(page([]));
    });
  });

  describe('ApprovalInbox page', () => {
    let fixture: ComponentFixture<ApprovalInbox>;

    async function open(): Promise<ApprovalInbox> {
      setCompanies();
      fixture = TestBed.createComponent(ApprovalInbox);
      fixture.detectChanges();
      await fixture.whenStable();
      return fixture.componentInstance;
    }

    async function render(): Promise<void> {
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();
    }

    function shown(testId: string): boolean {
      return fixture.nativeElement.querySelector(`[data-testid="${testId}"]`) !== null;
    }

    function expectTasks(): TestRequest {
      return controller.expectOne(get(`${APPROVAL_API}/tasks`));
    }

    function expectHistory(): TestRequest {
      return controller.expectOne(get(APPROVAL_API));
    }

    it('lists my tasks oldest first with step, delegation and amount; history needs VIEW', async () => {
      await grant(['SYS.APPROVAL.PROCESS']);
      const inbox = await open();
      const first = expectTasks();
      expect(first.request.params.toString()).toBe('page=1&pageSize=20&sort=submittedAt');
      first.flush(page([task(), task({ instanceId: 32, documentNo: 'PO-0002', onBehalfOfUserId: 21, onBehalfOfUsername: 'gm', stepNo: 2, stepName: 'GM' })]));
      await render();
      const row = fixture.nativeElement.querySelector('[data-testid="sys-approval-task-PO-0001"]').textContent as string;
      expect(row).toContain('250,000.5');
      expect(row).toContain('Step 1 of 3');
      expect(row).toContain('2026-10-11 09:15');
      expect(fixture.nativeElement.querySelector('[data-testid="sys-approval-on-behalf-PO-0002"]').textContent).toContain('On behalf of gm');
      expect(shown('sys-approval-on-behalf-PO-0001')).toBe(false);
      expect(shown('sys-approval-view-tasks')).toBe(true);
      expect(shown('sys-approval-view-history')).toBe(false);
      expect(shown('sys-approval-detail-PO-0001')).toBe(false);
      expect(shown('sys-approval-status-filter')).toBe(false);

      inbox.documentType.set('pur.order');
      inbox.keyword.set('chair');
      inbox.search();
      expect(expectTasks().request.params.toString()).toBe('page=1&pageSize=20&documentType=PUR.ORDER&keyword=chair&sort=submittedAt');
      inbox.onQueryParams({ pageIndex: 1, pageSize: 20, sort: [{ key: 'submittedAt', value: 'descend' }], filter: [] });
      expect(expectTasks().request.params.get('sort')).toBe('-submittedAt');
      expect(inbox.sortOrder()).toBe('descend');
      // 沒有 VIEW：不能切到歷史
      inbox.switchView('history');
      expect(inbox.view()).toBe('tasks');
    }, 15_000);

    it('approves with an optional comment and reports the next step or completion', async () => {
      await grant(['SYS.APPROVAL.PROCESS']);
      const inbox = await open();
      expectTasks().flush(page([task()]));
      await render();

      inbox.openDecision('approve', task());
      await render();
      expect(dialogElement('sys-approval-decision-summary')!.textContent).toContain('PO-0001');
      inbox.comment.set('  ');
      inbox.submitDecision();
      inbox.submitDecision();
      const approve = controller.expectOne(`${APPROVAL_API}/31/approve`);
      expect(approve.request.body).toEqual({ comment: null, version: 4 });
      approve.flush(detail({ status: 'Pending', currentStepNo: 2 }));
      expect([inbox.notice(), inbox.noticeParams()]).toEqual(['sys.approval.forwarded', { no: 'PO-0001' }]);
      expect(inbox.decision()).toBeNull();
      expectTasks().flush(page([]));
      await render();
      expect(fixture.nativeElement.querySelector('[data-testid="sys-approval-notice"]').textContent).toContain('sent to the next step');

      inbox.openDecision('approve', task({ stepNo: 3 }));
      inbox.comment.set(' fine ');
      inbox.submitDecision();
      const last = controller.expectOne(`${APPROVAL_API}/31/approve`);
      expect(last.request.body.comment).toBe('fine');
      last.flush(detail());
      expect(inbox.notice()).toBe('sys.approval.approved');
      expectTasks().flush(page([]));
    });

    it('requires a comment to reject and shows approval errors in the dialog', async () => {
      await grant(['SYS.APPROVAL.PROCESS']);
      const inbox = await open();
      expectTasks().flush(page([task()]));

      inbox.openDecision('reject', task());
      await render();
      inbox.submitDecision();
      controller.expectNone(`${APPROVAL_API}/31/reject`);
      expect(inbox.commentMissing()).toBe(true);
      await render();
      expect(dialogElement('sys-approval-comment-error')).not.toBeNull();

      inbox.comment.set(' Too expensive ');
      for (const code of ['SYS.APPROVAL.SELF_APPROVAL', 'SYS.APPROVAL.NOT_APPROVER', 'COMMON.CONCURRENCY.CONFLICT']) {
        inbox.submitDecision();
        const reject = controller.expectOne(`${APPROVAL_API}/31/reject`);
        expect(reject.request.body).toEqual({ comment: 'Too expensive', version: 4 });
        reject.flush({ errorCode: code }, { status: code.startsWith('COMMON') ? 409 : 422, statusText: 'Error' });
        expect(inbox.decisionError()).toBe(`errors.${code}`);
      }
      await render();
      expect(dialogElement('sys-approval-decision-error')!.textContent).toContain('Someone else changed this record');
      inbox.submitDecision();
      controller.expectOne(`${APPROVAL_API}/31/reject`).flush(detail({ status: 'Rejected' }));
      expect(inbox.notice()).toBe('sys.approval.rejected');
      expectTasks().flush(page([]));
    });

    it('opens history by default with VIEW only, filters by status and shows the step record', async () => {
      await grant(['SYS.APPROVAL.VIEW']);
      const inbox = await open();
      expect(inbox.view()).toBe('history');
      const first = expectHistory();
      expect(first.request.params.toString()).toBe('page=1&pageSize=20&sort=-submittedAt');
      first.flush(page([summary(), summary({ id: 33, status: 'Pending', currentStepNo: 2, completedAt: null })]));
      await render();
      expect(shown('sys-approval-view-tasks')).toBe(false);
      expect(fixture.nativeElement.querySelector('[data-testid="sys-approval-history-33"]').textContent).toContain('at step 2');
      expect(fixture.nativeElement.querySelector('[data-testid="sys-approval-history-31"]').textContent).toContain('Approved');

      inbox.status.set('Rejected');
      inbox.search();
      expect(expectHistory().request.params.get('status')).toBe('Rejected');

      inbox.openDetail(summary(), 31);
      expect(inbox.detailParams()).toEqual({ type: 'PUR.ORDER', no: 'PO-0001' });
      inbox.openDetail(summary(), 31);
      const requests = controller.match(`${APPROVAL_API}/31`);
      expect(requests.length).toBe(2);
      requests[1].flush(detail());
      // 較早的請求晚到時不覆蓋
      requests[0].flush(detail({ documentNo: 'STALE' }));
      await render();
      expect(inbox.detail()!.documentNo).toBe('PO-0001');
      const step1 = dialogElement('sys-approval-detail-step-1')!.textContent!;
      expect(step1).toContain('Role：Supervisors');
      expect(step1).toContain('deputy');
      expect(step1).toContain('On behalf of gm');
      expect(dialogElement('sys-approval-detail-step-2')!.textContent).toContain('User：gm');
      inbox.closeDetail();
      expect(inbox.detailOpen()).toBe(false);

      inbox.openDetail(summary({ id: 40 }), 40);
      controller.expectOne(`${APPROVAL_API}/40`).flush({ errorCode: 'SYS.APPROVAL.NOT_FOUND' }, { status: 404, statusText: 'Not Found' });
      expect(inbox.detailError()).toBe('errors.SYS.APPROVAL.NOT_FOUND');
      inbox.closeDetail();
    });

    it('switches between tasks and history, ignores late responses and reloads when the company changes', async () => {
      await grant(['SYS.APPROVAL.PROCESS', 'SYS.APPROVAL.VIEW']);
      const inbox = await open();
      const staleTasks = expectTasks();
      inbox.switchView('history');
      await render();
      const history = expectHistory();
      staleTasks.flush(page([task()]));
      expect(inbox.tasks()).toEqual([]);
      history.flush(page([summary()]));
      expect(inbox.history().length).toBe(1);
      expect(inbox.sort()).toBe('-submittedAt');
      inbox.switchView('history');
      controller.expectNone(APPROVAL_API);

      TestBed.inject(CompanyContextService).select(2);
      await render();
      expectHistory().flush({ errorCode: 'COMMON.PERMISSION.DENIED' }, { status: 403, statusText: 'Forbidden' });
      expect(inbox.loadError()).toBe('errors.COMMON.PERMISSION.DENIED');

      inbox.switchView('tasks');
      await render();
      expect(inbox.sort()).toBe('submittedAt');
      expectTasks().flush(page([]));
      await render();
      expect(shown('sys-approval-task-table')).toBe(true);
    });

    it('switches labels with the language', async () => {
      await grant(['SYS.APPROVAL.PROCESS']);
      await open();
      expectTasks().flush(page([]));
      await render();
      expect(fixture.nativeElement.querySelector('[data-testid="sys-approval-title"]').textContent).toContain('Approvals');
      await firstValueFrom(TestBed.inject(TranslateService).use('zh-Hant'));
      await render();
      expect(fixture.nativeElement.querySelector('[data-testid="sys-approval-title"]').textContent).toContain('審批待辦');
      expect(fixture.nativeElement.textContent).toContain('沒有等待您審批的單據');
    });
  });

  it('routes approval pages behind their permissions and lists them in the menu', () => {
    const route = (path: string) => SYS_ROUTES.find((r) => r.path === path)!;
    expect(route('approval-flows').canActivate?.length).toBe(1);
    expect(route('approval-delegations').canActivate?.length).toBe(1);
    expect(route('approvals').canActivate?.length).toBe(1);
    const sys = MAIN_MENU.find((item) => item.testId === 'layout-menu-sys')!;
    expect(sys.children!.find((item) => item.route === '/sys/approvals')?.permission).toEqual(['SYS.APPROVAL.PROCESS', 'SYS.APPROVAL.VIEW']);
    expect(sys.children!.find((item) => item.route === '/sys/approval-flows')?.permission).toBe('SYS.APPROVAL_FLOW.VIEW');
    expect(sys.children!.find((item) => item.route === '/sys/approval-delegations')?.permission).toBe('SYS.APPROVAL_DELEGATION.VIEW');
  });
});
