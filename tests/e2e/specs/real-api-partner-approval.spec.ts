import { expect, request, test, type APIRequestContext } from '@playwright/test';

// WU-MD-022d: exercise the running API, SQL Server and SYS approval workflow together.
const apiUrl = process.env['E2E_API_URL'];
const root = '/api/v1';

type Partner = { id: number; code: string; name: string; version: number; reviewStatus: string;
  defaultPaymentTermId: number | null; defaultSettlementMethodId: number | null };

test.describe('真實 API 往來對象審批與付款預設值', () => {
  test.describe.configure({ mode: 'serial' });
  test.skip(!apiUrl, '需要 E2E_API_URL：請用 tools/scripts/Invoke-RealApiE2E.ps1 執行');
  test.setTimeout(120_000);
  let admin: APIRequestContext;
  let manager: APIRequestContext;
  let finance: APIRequestContext;

  test.beforeAll(async () => {
    admin = await session('admin', 'az5911');
    manager = await session('manager', 'Demo@2026');
    finance = await session('finance', 'Demo@2026');
  });

  test.afterAll(async () => {
    await Promise.all([admin?.dispose(), manager?.dispose(), finance?.dispose()]);
  });

  for (const kind of ['customers', 'suppliers'] as const) {
    test(`${kind}：兩級審批、審批中保護及預設付款資料`, async () => {
      let originalFlow: any;
      let flowId: number | undefined;
      try {
        const run = `${Date.now()}${Math.floor(Math.random() * 10000)}`;
        const code = `E2E${kind === 'customers' ? 'C' : 'S'}${run}`;
        const documentType = kind === 'customers' ? 'MD.CUSTOMER' : 'MD.SUPPLIER';
        const users = await json(admin.get(`${root}/sys/users?pageSize=100`));
        const managerId = users.items.find((user: { username: string }) => user.username === 'manager')?.id;
        const financeId = users.items.find((user: { username: string }) => user.username === 'finance')?.id;
        expect(managerId).toBeTruthy();
        expect(financeId).toBeTruthy();

        const flows = await json(admin.get(`${root}/sys/approval-flows?pageSize=100`));
        const existing = flows.items.find((flow: { documentType: string }) => flow.documentType === documentType);
        originalFlow = existing;
        const steps = [managerId, financeId].map((userId, index) => ({
          name: `E2E step ${index + 1}`, approverType: 'User', roleId: null, userId, minAmount: null,
        }));
        const flowInput = { name: `E2E ${documentType}`, allowSelfApproval: false, steps };
        if (existing) {
          const changed = await admin.put(`${root}/sys/approval-flows/${existing.id}`, {
            data: { ...flowInput, isActive: true, version: existing.version },
          });
          expect(changed.status(), await changed.text()).toBe(200);
          flowId = existing.id;
        } else {
          const created = await admin.post(`${root}/sys/approval-flows`, {
            data: { ...flowInput, documentType },
          });
          expect(created.status(), await created.text()).toBe(201);
          flowId = (await created.json()).id;
        }

        const term = await json(admin.post(`${root}/md/payment-terms`, {
          data: { code: `T${run}`, name: `E2E net 30 ${run}`, dueType: 'NetDays', days: 30,
            closingDay: null, months: null, paymentDay: null },
        }), 201);
        const method = await json(admin.post(`${root}/md/settlement-methods`, {
          data: { code: `M${run}`, name: `E2E transfer ${run}` },
        }), 201);
        const created: Partner = await json(admin.post(`${root}/md/${kind}`, {
          data: { code, name: `E2E ${kind} ${run}`, categoryId: null,
            defaultPaymentTermId: term.id, defaultSettlementMethodId: method.id },
        }), 201);
        expect(created.defaultPaymentTermId).toBe(term.id);
        expect(created.defaultSettlementMethodId).toBe(method.id);
        for (const [resource, value, errorCode] of [
          ['payment-terms', term, 'MD.PAYMENT_TERM.IN_USE'],
          ['settlement-methods', method, 'MD.SETTLEMENT_METHOD.IN_USE'],
        ] as const) {
          const deletion = await admin.delete(`${root}/md/${resource}/${value.id}?version=${value.version}`);
          expect(deletion.status(), await deletion.text()).toBe(409);
          expect((await deletion.json()).errorCode).toBe(errorCode);
        }

        const submit = await json(admin.post(`${root}/md/${kind}/${created.id}/submit`, {
          data: { version: created.version },
        }));
        expect(submit).toMatchObject({ status: 'Pending', currentStepNo: 1, stepCount: 2 });
        const pending = await partner(admin, kind, code);
        const update = { name: `${created.name} changed`, isActive: true, categoryId: null,
          version: pending.version, defaultPaymentTermId: term.id, defaultSettlementMethodId: method.id };
        for (const response of [
          await admin.put(`${root}/md/${kind}/${created.id}`, { data: update }),
          await admin.delete(`${root}/md/${kind}/${created.id}?version=${pending.version}`),
        ]) {
          expect(response.status(), await response.text()).toBe(422);
          expect((await response.json()).errorCode).toBe(`${documentType}.APPROVAL_PENDING`);
        }
        expect((await partner(admin, kind, code)).name).toBe(created.name);

        const first = await json(manager.get(`${root}/sys/approvals/${submit.instanceId}`));
        const afterFirst = await json(manager.post(`${root}/sys/approvals/${submit.instanceId}/approve`, {
          data: { comment: 'E2E first approval', version: first.version },
        }));
        expect(afterFirst).toMatchObject({ status: 'Pending', currentStepNo: 2 });
        const afterSecond = await json(finance.post(`${root}/sys/approvals/${submit.instanceId}/approve`, {
          data: { comment: 'E2E final approval', version: afterFirst.version },
        }));
        expect(afterSecond.status).toBe('Approved');
        const approved = await partner(admin, kind, code);
        expect(approved).toMatchObject({ reviewStatus: 'Approved',
          defaultPaymentTermId: term.id, defaultSettlementMethodId: method.id });
        const changed = await json(admin.put(`${root}/md/${kind}/${created.id}`, {
          data: { ...update, version: approved.version },
        }));
        expect(changed.name).toBe(update.name);
        expect(changed.reviewStatus).toBe('Approved');
      } finally {
        if (flowId) {
          const current = await json(admin.get(`${root}/sys/approval-flows/${flowId}`));
          const restore = originalFlow
            ? { name: originalFlow.name, allowSelfApproval: originalFlow.allowSelfApproval,
                isActive: originalFlow.isActive, steps: originalFlow.steps, version: current.version }
            : { name: current.name, allowSelfApproval: current.allowSelfApproval,
                isActive: false, steps: current.steps, version: current.version };
          await json(admin.put(`${root}/sys/approval-flows/${flowId}`, { data: restore }));
        }
      }
    });
  }
});

async function session(username: string, password: string): Promise<APIRequestContext> {
  const anonymous = await request.newContext({ baseURL: apiUrl });
  try {
    const captcha = await json(anonymous.get(`${root}/sys/auth/captcha`));
    const tokens = await json(anonymous.post(`${root}/sys/auth/login`, {
      data: { username, password, captchaId: captcha.captchaId, captchaCode: '123456' },
    }));
    const authorization = await json(anonymous.get(`${root}/sys/auth/authorization`, {
      headers: { Authorization: `Bearer ${tokens.accessToken}` },
    }));
    expect(authorization.companyId).toBeTruthy();
    return request.newContext({ baseURL: apiUrl, extraHTTPHeaders: {
      Authorization: `Bearer ${tokens.accessToken}`, 'X-Company-Id': String(authorization.companyId),
    } });
  } finally {
    await anonymous.dispose();
  }
}

async function partner(api: APIRequestContext, kind: string, code: string): Promise<Partner> {
  const page = await json(api.get(`${root}/md/${kind}?keyword=${code}`));
  const found = page.items.find((item: Partner) => item.code === code);
  expect(found).toBeDefined();
  return found;
}

async function json(response: Promise<import('@playwright/test').APIResponse>, status = 200): Promise<any> {
  const value = await response;
  expect(value.status(), await value.text()).toBe(status);
  return value.json();
}
