import { Page } from '@playwright/test';
import { chooseLanguage, E2E_AUTHORIZATION, expect, openHome, test } from '../support/fixtures';

// 介面 8.68–8.70（REQ_F4118.29.1–3、29.5–9）：審批流程、代理與待辦頁。API 以 page.route 模擬（ADR-013）。

interface Step {
  stepNo: number;
  name: string;
  approverType: 'Role' | 'User';
  roleId: number | null;
  userId: number | null;
  minAmount: number | null;
}

interface Flow {
  id: number;
  documentType: string;
  name: string;
  allowSelfApproval: boolean;
  isActive: boolean;
  steps: Step[];
  version: number;
}

interface Delegation {
  id: number;
  delegatorUserId: number;
  delegatorUsername: string;
  delegateUserId: number;
  delegateUsername: string;
  startDate: string;
  endDate: string;
  remark: string | null;
  isActive: boolean;
  version: number;
}

interface Call {
  method: string;
  path: string;
  query: string;
  body: Record<string, unknown> | null;
}

const USERS = [
  { id: 21, username: 'gm.wang' },
  { id: 22, username: 'deputy.chen' },
  { id: 23, username: 'buyer.lin' },
];

const page1 = <T>(items: T[]) => ({ items, page: 1, pageSize: 20, totalCount: items.length });

const purchaseFlow = (): Flow => ({
  id: 5,
  documentType: 'PUR.ORDER',
  name: '採購訂單審批',
  allowSelfApproval: false,
  isActive: true,
  steps: [
    { stepNo: 1, name: '主管', approverType: 'Role', roleId: 11, userId: null, minAmount: null },
    { stepNo: 2, name: '總經理', approverType: 'User', roleId: null, userId: 21, minAmount: 100000 },
  ],
  version: 3,
});

/** 角色與用戶選項（審批人下拉）。 */
async function mockDirectory(page: Page): Promise<void> {
  await page.route('**/api/v1/sys/roles**', (route) =>
    route.fulfill({
      json: page1([
        { id: 11, code: 'SUP', name: '主管', description: null, dataScope: 'Company', isFullAccess: false, isActive: true, version: 1, permissions: [] },
        { id: 12, code: 'FIN', name: '財務', description: null, dataScope: 'Company', isFullAccess: false, isActive: true, version: 1, permissions: [] },
      ]),
    }),
  );
  await page.route('**/api/v1/sys/users**', (route) =>
    route.fulfill({
      json: page1(USERS.map((u) => ({ ...u, isActive: true, isLocked: false, passwordChangedAt: '2026-01-01T00:00:00', isPasswordExpired: false, version: 1 }))),
    }),
  );
}

/** 模擬流程 API：重複類型 409、版本不符 409。 */
async function mockFlowApi(page: Page, rows: Flow[]): Promise<Call[]> {
  const calls: Call[] = [];
  await page.route('**/api/v1/sys/approval-flows**', async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    const body = request.postDataJSON() as Record<string, unknown> | null;
    calls.push({ method: request.method(), path: url.pathname, query: url.search, body });
    const numbered = (steps: unknown) => (steps as Omit<Step, 'stepNo'>[]).map((s, i) => ({ ...s, stepNo: i + 1 }));
    if (request.method() === 'GET') {
      await route.fulfill({ json: page1(rows) });
    } else if (request.method() === 'POST') {
      if (rows.some((r) => r.documentType === body?.['documentType'])) {
        await route.fulfill({ status: 409, json: { status: 409, errorCode: 'SYS.APPROVAL_FLOW.DUPLICATE_TYPE' } });
        return;
      }
      const created = { ...(body as unknown as Flow), id: 100 + rows.length, isActive: true, steps: numbered(body?.['steps']), version: 1 };
      rows.push(created);
      await route.fulfill({ status: 201, json: created });
    } else {
      const row = rows.find((r) => r.id === Number(url.pathname.split('/')[5]))!;
      if (body?.['version'] !== row.version) {
        await route.fulfill({ status: 409, json: { status: 409, errorCode: 'COMMON.CONCURRENCY.CONFLICT' } });
        return;
      }
      Object.assign(row, body, { steps: numbered(body?.['steps']), version: row.version + 1 });
      await route.fulfill({ json: row });
    }
  });
  return calls;
}

/** 模擬代理 API：同一被代理人啟用期間重疊回 409。 */
async function mockDelegationApi(page: Page, rows: Delegation[]): Promise<Call[]> {
  const calls: Call[] = [];
  await page.route('**/api/v1/sys/approval-delegations**', async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    const body = request.postDataJSON() as Record<string, unknown> | null;
    calls.push({ method: request.method(), path: url.pathname, query: url.search, body });
    if (request.method() === 'GET') {
      const userId = Number(url.searchParams.get('userId') ?? 0);
      await route.fulfill({ json: page1(rows.filter((r) => !userId || r.delegatorUserId === userId || r.delegateUserId === userId)) });
    } else if (request.method() === 'POST') {
      const input = body as unknown as Delegation;
      if (rows.some((r) => r.isActive && r.delegatorUserId === input.delegatorUserId && r.startDate <= input.endDate && r.endDate >= input.startDate)) {
        await route.fulfill({ status: 409, json: { status: 409, errorCode: 'SYS.APPROVAL_DELEGATION.OVERLAP' } });
        return;
      }
      const name = (id: number) => USERS.find((u) => u.id === id)!.username;
      const created = { ...input, id: 200 + rows.length, delegatorUsername: name(input.delegatorUserId), delegateUsername: name(input.delegateUserId), isActive: true, version: 1 };
      rows.push(created);
      await route.fulfill({ status: 201, json: created });
    } else {
      const row = rows.find((r) => r.id === Number(url.pathname.split('/')[5]))!;
      Object.assign(row, body, { version: row.version + 1 });
      await route.fulfill({ json: row });
    }
  });
  return calls;
}

const task = (instanceId: number, documentNo: string, overrides: Record<string, unknown> = {}) => ({
  instanceId,
  documentType: 'PUR.ORDER',
  documentId: instanceId + 1000,
  documentNo,
  summary: '辦公椅',
  amount: 250000.5,
  submittedBy: 23,
  submittedByUsername: 'buyer.lin',
  submittedAt: '2026-10-11T09:15:30',
  stepNo: 1,
  stepCount: 2,
  stepName: '主管',
  onBehalfOfUserId: null,
  onBehalfOfUsername: null,
  version: 4,
  ...overrides,
});

const instanceDetail = {
  id: 31,
  documentType: 'PUR.ORDER',
  documentId: 1031,
  documentNo: 'PO-0001',
  summary: '辦公椅',
  amount: 250000.5,
  status: 'Rejected',
  currentStepNo: null,
  allowSelfApproval: false,
  submittedBy: 23,
  submittedByUsername: 'buyer.lin',
  submittedAt: '2026-10-11T09:15:30',
  completedAt: '2026-10-11T11:00:00',
  version: 6,
  steps: [
    {
      stepNo: 1, name: '主管', approverType: 'Role', roleId: 11, roleName: '主管', userId: null, username: null, minAmount: null,
      status: 'Approved', actedBy: 22, actedByUsername: 'deputy.chen', onBehalfOfUserId: 21, onBehalfOfUsername: 'gm.wang',
      actedAt: '2026-10-11T10:00:00', comment: '同意',
    },
    {
      stepNo: 2, name: '總經理', approverType: 'User', roleId: null, roleName: null, userId: 21, username: 'gm.wang', minAmount: 100000,
      status: 'Rejected', actedBy: 21, actedByUsername: 'gm.wang', onBehalfOfUserId: null, onBehalfOfUsername: null,
      actedAt: '2026-10-11T11:00:00', comment: '價格太高',
    },
  ],
};

/** 模擬審批 API：待辦、歷史、明細、核准（第 1 步後送往下一步）與駁回（必須有意見）。 */
async function mockApprovalApi(page: Page): Promise<Call[]> {
  const calls: Call[] = [];
  const tasks = [task(31, 'PO-0001'), task(32, 'PO-0002', { onBehalfOfUserId: 21, onBehalfOfUsername: 'gm.wang', stepNo: 2, stepName: '總經理' })];
  await page.route('**/api/v1/sys/approvals**', async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    const body = request.postDataJSON() as Record<string, unknown> | null;
    calls.push({ method: request.method(), path: url.pathname, query: url.search, body });
    const [, , , , , id, action] = url.pathname.split('/');
    if (request.method() === 'GET' && id === 'tasks') {
      await route.fulfill({ json: page1(tasks) });
    } else if (request.method() === 'GET' && id) {
      await route.fulfill({ json: instanceDetail });
    } else if (request.method() === 'GET') {
      const status = url.searchParams.get('status');
      const rows = [
        { ...instanceDetail, steps: undefined },
        { ...instanceDetail, id: 33, documentNo: 'PO-0003', status: 'Pending', currentStepNo: 2, completedAt: null },
      ].filter((r) => !status || r.status === status);
      await route.fulfill({ json: page1(rows) });
    } else {
      const index = tasks.findIndex((t) => t.instanceId === Number(id));
      if (action === 'reject' && !String(body?.['comment'] ?? '').trim()) {
        await route.fulfill({ status: 400, json: { status: 400, errorCode: 'COMMON.REQUEST.VALIDATION_FAILED' } });
        return;
      }
      const forwarded = action === 'approve' && tasks[index].stepNo < tasks[index].stepCount;
      tasks.splice(index, 1);
      await route.fulfill({ json: { ...instanceDetail, status: forwarded ? 'Pending' : action === 'approve' ? 'Approved' : 'Rejected' } });
    }
  });
  return calls;
}

async function saveDialog(page: Page): Promise<void> {
  await page.locator('.ant-modal-footer .ant-btn-primary').click();
}

async function choose(page: Page, testId: string, label: string): Promise<void> {
  await page.getByTestId(testId).click();
  await page.locator('nz-option-item', { hasText: label }).click();
}

test.describe('審批（全權限）', () => {
  test('審批流程：由選單進入，新增兩級流程（角色、用戶＋金額門檻）、步驟驗證、重複類型；編輯帶版本並停用', async ({ page, pageErrors }) => {
    pageErrors.allow(/status of 409/);
    await mockDirectory(page);
    const calls = await mockFlowApi(page, [purchaseFlow()]);
    await openHome(page);

    await page.getByTestId('layout-menu-sys').click();
    await page.getByTestId('layout-menu-sys-approval-flows').click();
    await expect(page).toHaveURL(/\/sys\/approval-flows$/);
    const steps = page.getByTestId('sys-approval-flow-steps-PUR.ORDER');
    await expect(steps).toContainText('主管（角色：SUP 主管）');
    await expect(steps).toContainText('總經理（用戶：gm.wang）');
    await expect(steps).toContainText('≥ 100,000');

    await page.getByTestId('sys-approval-flow-create').click();
    await page.getByTestId('sys-approval-flow-type').fill('pur.order');
    await page.getByTestId('sys-approval-flow-name').fill('採購訂單審批');
    await page.getByTestId('sys-approval-flow-step-name-1').fill('主管');
    await saveDialog(page);
    await expect(page.getByTestId('sys-approval-flow-step-error-1')).toContainText('請選擇審批人');
    expect(calls.some((c) => c.method === 'POST')).toBe(false);

    await choose(page, 'sys-approval-flow-step-role-1', 'SUP 主管');
    await page.getByTestId('sys-approval-flow-add-step').click();
    await page.getByTestId('sys-approval-flow-step-name-2').fill('總經理');
    await choose(page, 'sys-approval-flow-step-type-2', '用戶');
    await choose(page, 'sys-approval-flow-step-user-2', 'gm.wang');
    await page.getByTestId('sys-approval-flow-step-amount-2').fill('-5');
    await saveDialog(page);
    await expect(page.getByTestId('sys-approval-flow-step-error-2')).toContainText('金額須為非負數');
    await page.getByTestId('sys-approval-flow-step-amount-2').fill('100000');
    await saveDialog(page);
    await expect(page.getByTestId('sys-approval-flow-save-error')).toContainText('目前公司已有此單據類型的審批流程');

    await page.getByTestId('sys-approval-flow-type').fill(' sal.order ');
    await page.getByTestId('sys-approval-flow-self').click();
    await saveDialog(page);
    await expect(page.getByTestId('sys-approval-flow-notice')).toContainText('已新增審批流程 SAL.ORDER');
    await expect(page.getByTestId('sys-approval-flow-self-SAL.ORDER')).toBeVisible();
    expect(calls.filter((c) => c.method === 'POST').at(-1)?.body).toEqual({
      documentType: 'SAL.ORDER',
      name: '採購訂單審批',
      allowSelfApproval: true,
      steps: [
        { name: '主管', approverType: 'Role', roleId: 11, userId: null, minAmount: null },
        { name: '總經理', approverType: 'User', roleId: null, userId: 21, minAmount: 100000 },
      ],
    });

    await page.getByTestId('sys-approval-flow-edit-PUR.ORDER').click();
    await expect(page.locator('.ant-modal-title', { hasText: '編輯審批流程 PUR.ORDER' })).toBeVisible();
    await expect(page.getByTestId('sys-approval-flow-type')).toBeDisabled();
    await page.getByTestId('sys-approval-flow-step-up-2').click();
    await expect(page.getByTestId('sys-approval-flow-step-name-1')).toHaveValue('總經理');
    await page.getByTestId('sys-approval-flow-step-remove-2').click();
    await page.getByTestId('sys-approval-flow-active').click();
    await saveDialog(page);
    await expect(page.getByTestId('sys-approval-flow-notice')).toContainText('已更新審批流程 PUR.ORDER');
    await expect(page.getByTestId('sys-approval-flow-row-PUR.ORDER')).toContainText('停用');
    expect(calls.find((c) => c.method === 'PUT')?.body).toEqual({
      name: '採購訂單審批',
      allowSelfApproval: false,
      steps: [{ name: '總經理', approverType: 'User', roleId: null, userId: 21, minAmount: 100000 }],
      isActive: false,
      version: 3,
    });
  });

  test('審批代理：同一人與日期倒置不可送出、期間重疊 409、新增成功；依用戶篩選；停用', async ({ page, pageErrors }) => {
    pageErrors.allow(/status of 409/);
    await mockDirectory(page);
    const existing: Delegation = {
      id: 9, delegatorUserId: 21, delegatorUsername: 'gm.wang', delegateUserId: 22, delegateUsername: 'deputy.chen',
      startDate: '2026-10-01', endDate: '2099-12-31', remark: '出差', isActive: true, version: 1,
    };
    const calls = await mockDelegationApi(page, [existing]);
    await openHome(page);
    await page.getByTestId('layout-menu-sys').click();
    await page.getByTestId('layout-menu-sys-approval-delegations').click();
    await expect(page.getByTestId('sys-approval-delegation-row-9')).toContainText('gm.wang');
    await expect(page.getByTestId('sys-approval-delegation-current-9')).toContainText('生效中');

    await page.getByTestId('sys-approval-delegation-create').click();
    await choose(page, 'sys-approval-delegation-delegator', 'gm.wang');
    await choose(page, 'sys-approval-delegation-delegate', 'gm.wang');
    await page.getByTestId('sys-approval-delegation-start').fill('2026-11-10');
    await page.getByTestId('sys-approval-delegation-end').fill('2026-11-01');
    await expect(page.getByTestId('sys-approval-delegation-same-user')).toBeVisible();
    await expect(page.getByTestId('sys-approval-delegation-date-range')).toBeVisible();
    await saveDialog(page);
    expect(calls.some((c) => c.method === 'POST')).toBe(false);

    await choose(page, 'sys-approval-delegation-delegate', 'buyer.lin');
    await page.getByTestId('sys-approval-delegation-end').fill('2026-11-20');
    await saveDialog(page);
    await expect(page.getByTestId('sys-approval-delegation-save-error')).toContainText('期間不可重疊');

    await choose(page, 'sys-approval-delegation-delegator', 'deputy.chen');
    await page.getByTestId('sys-approval-delegation-remark').fill(' 年假 ');
    await saveDialog(page);
    await expect(page.getByTestId('sys-approval-delegation-notice')).toContainText('已設定 buyer.lin 代理 deputy.chen 審批');
    expect(calls.filter((c) => c.method === 'POST').at(-1)?.body).toEqual({
      delegatorUserId: 22, delegateUserId: 23, startDate: '2026-11-10', endDate: '2026-11-20', remark: '年假',
    });

    await choose(page, 'sys-approval-delegation-user-filter', 'buyer.lin');
    await page.getByTestId('sys-approval-delegation-search-button').click();
    await expect(page.getByTestId('sys-approval-delegation-row-9')).toHaveCount(0);
    expect(calls.filter((c) => c.method === 'GET').at(-1)?.query).toContain('userId=23');

    await page.getByTestId('sys-approval-delegation-edit-201').click();
    await expect(page.getByTestId('sys-approval-delegation-delegator-name')).toHaveText('deputy.chen');
    await page.getByTestId('sys-approval-delegation-active').click();
    await saveDialog(page);
    await expect(page.getByTestId('sys-approval-delegation-row-201')).toContainText('停用');
    expect(calls.find((c) => c.method === 'PUT')?.body).toEqual({
      startDate: '2026-11-10', endDate: '2026-11-20', remark: '年假', isActive: false, version: 1,
    });
  });

  test('審批待辦：代理標示、核准送往下一步、駁回必填意見；審批歷史篩選與步驟明細', async ({ page }) => {
    const calls = await mockApprovalApi(page);
    await openHome(page);
    await page.getByTestId('layout-menu-sys').click();
    await page.getByTestId('layout-menu-sys-approvals').click();
    await expect(page.getByTestId('sys-approval-task-PO-0001')).toContainText('第 1 / 2 步');
    await expect(page.getByTestId('sys-approval-task-PO-0001')).toContainText('250,000.5');
    await expect(page.getByTestId('sys-approval-on-behalf-PO-0002')).toContainText('代 gm.wang');

    await page.getByTestId('sys-approval-approve-PO-0001').click();
    await page.getByTestId('sys-approval-comment').fill('同意');
    await saveDialog(page);
    await expect(page.getByTestId('sys-approval-notice')).toContainText('已核准 PO-0001，送往下一步');
    await expect(page.getByTestId('sys-approval-task-PO-0001')).toHaveCount(0);
    expect(calls.find((c) => c.path.endsWith('/31/approve'))?.body).toEqual({ comment: '同意', version: 4 });

    await page.getByTestId('sys-approval-reject-PO-0002').click();
    await saveDialog(page);
    await expect(page.getByTestId('sys-approval-comment-error')).toContainText('駁回時必須填寫意見');
    expect(calls.some((c) => c.path.endsWith('/reject'))).toBe(false);
    await page.getByTestId('sys-approval-comment').fill('價格太高');
    await saveDialog(page);
    await expect(page.getByTestId('sys-approval-notice')).toContainText('已駁回 PO-0002');

    await page.getByTestId('sys-approval-view-history').click();
    await expect(page.getByTestId('sys-approval-history-33')).toContainText('審批中');
    await choose(page, 'sys-approval-status-filter', '已駁回');
    await page.getByTestId('sys-approval-search-button').click();
    await expect(page.getByTestId('sys-approval-history-33')).toHaveCount(0);
    expect(calls.filter((c) => c.method === 'GET').at(-1)?.query).toContain('status=Rejected');

    await page.getByTestId('sys-approval-history-detail-31').click();
    await expect(page.locator('.ant-modal-title', { hasText: 'PUR.ORDER PO-0001 的審批記錄' })).toBeVisible();
    await expect(page.getByTestId('sys-approval-detail-step-1')).toContainText('deputy.chen');
    await expect(page.getByTestId('sys-approval-detail-step-1')).toContainText('代 gm.wang');
    await expect(page.getByTestId('sys-approval-detail-step-2')).toContainText('價格太高');
    await expect(page.getByTestId('sys-approval-detail-step-2')).toContainText('已駁回');
  });

  test('英文介面', async ({ page }) => {
    await mockApprovalApi(page);
    await page.goto('/sys/approvals');
    await chooseLanguage(page, 'en');
    await expect(page.getByTestId('sys-approval-title')).toHaveText('Approvals');
    await expect(page.getByTestId('sys-approval-task-PO-0001')).toContainText('Step 1 of 2');
    await expect(page.getByTestId('sys-approval-on-behalf-PO-0002')).toContainText('On behalf of gm.wang');
  });
});

test.describe('審批（只可處理待辦）', () => {
  test.use({
    authorization: { ...E2E_AUTHORIZATION, isFullAccess: false, permissions: [{ code: 'SYS.APPROVAL.PROCESS', dataScope: 'Company' }] },
  });

  test('選單只有審批待辦；沒有歷史頁籤與明細按鈕', async ({ page }) => {
    await mockApprovalApi(page);
    await openHome(page);
    await page.getByTestId('layout-menu-sys').click();
    await expect(page.getByTestId('layout-menu-sys-approval-flows')).toHaveCount(0);
    await expect(page.getByTestId('layout-menu-sys-approval-delegations')).toHaveCount(0);
    await page.getByTestId('layout-menu-sys-approvals').click();
    await expect(page.getByTestId('sys-approval-task-PO-0001')).toBeVisible();
    await expect(page.getByTestId('sys-approval-view-history')).toHaveCount(0);
    await expect(page.getByTestId('sys-approval-detail-PO-0001')).toHaveCount(0);
  });
});

test.describe('審批（無權限）', () => {
  test.use({
    authorization: { ...E2E_AUTHORIZATION, isFullAccess: false, permissions: [{ code: 'SYS.USER.VIEW', dataScope: 'Company' }] },
  });

  test('選單沒有審批項目，直接進入顯示 403', async ({ page }) => {
    await openHome(page);
    await page.getByTestId('layout-menu-sys').click();
    await expect(page.getByTestId('layout-menu-sys-approvals')).toHaveCount(0);
    for (const path of ['/sys/approvals', '/sys/approval-flows', '/sys/approval-delegations']) {
      await page.goto(path);
      await expect(page.getByTestId('error-page-403')).toBeVisible();
    }
  });
});
