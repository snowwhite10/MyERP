import { mkdir } from 'node:fs/promises';
import { Page } from '@playwright/test';
import { chooseLanguage, E2E_AUTHORIZATION, expect, test } from '../support/fixtures';

/**
 * WU-SD-006c2 (ADR-171, ADR-173): the final approval of a sales order over the credit limit on the Approval Inbox
 * Interface 8.70. The first approve is refused with 422 SAL.CREDIT.LIMIT_EXCEEDED; the approver enters an override
 * reason and approves again. API mocked with page.route (ADR-013); the mock keeps the task until it is approved.
 */
const task = { instanceId: 52, documentType: 'SAL.ORDER', documentId: 81, documentNo: 'SO-0081', summary: 'Saved customer',
  amount: 1250, submittedBy: 23, submittedByUsername: 'sales', submittedAt: '2026-11-23T05:00:00', stepNo: 2, stepCount: 2,
  stepName: '業務經理', onBehalfOfUserId: null, onBehalfOfUsername: null, version: 3 };
const approved = { id: 52, documentType: 'SAL.ORDER', documentId: 81, documentNo: 'SO-0081', summary: 'Saved customer', amount: 1250,
  status: 'Approved', currentStepNo: null, submittedBy: 23, submittedByUsername: 'sales', submittedAt: '2026-11-23T05:00:00',
  completedAt: '2026-11-23T05:30:00', version: 4, allowSelfApproval: false, steps: [] };

interface Call { path: string; body: Record<string, unknown> | null }

async function mockInbox(page: Page, onApprove: (body: Record<string, unknown>) => { status: number; json: unknown }): Promise<Call[]> {
  const calls: Call[] = [];
  let pending = true;
  await page.route('**/api/v1/sys/approvals**', async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    if (request.method() === 'GET') {
      const items = pending ? [task] : [];
      await route.fulfill({ json: { items, totalCount: items.length, page: 1, pageSize: 20 } });
      return;
    }
    const body = request.postDataJSON() as Record<string, unknown>;
    calls.push({ path: url.pathname, body });
    const reply = onApprove(body);
    if (reply.status === 200) pending = false;
    await route.fulfill({ status: reply.status, json: reply.json });
  });
  return calls;
}

const limitExceeded = { status: 422, json: { status: 422, errorCode: 'SAL.CREDIT.LIMIT_EXCEEDED', excess: 250, currency: 'TWD' } };

async function capture(page: Page, name: string): Promise<void> {
  const path = '../../tests/qa/SD/screenshots';
  await mkdir(path, { recursive: true });
  await page.screenshot({ path: `${path}/8.70-credit-override-${name}.png`, fullPage: true, animations: 'disabled' });
}

/** Follow-system dark mode; wait until the dark stylesheet has loaded so the dialog is captured dark too. */
async function dark(page: Page): Promise<void> {
  await page.emulateMedia({ colorScheme: 'dark' });
  await page.waitForFunction(() => {
    const link = document.getElementById('erp-theme-dark') as HTMLLinkElement | null;
    return document.documentElement.classList.contains('erp-dark') && !!link?.sheet;
  });
  await expect(page.locator('.ant-modal-content:visible, .erp-approval-inbox').first()).toBeVisible();
}

async function confirm(page: Page): Promise<void> {
  await page.locator('.ant-modal-footer .ant-btn-primary').click();
}

test('SD-APPROVAL-OVR-E2E-001 over-limit final approval asks for a reason, sends it and the task leaves the inbox', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of (400|422)/);
  let reasonRejected = false;
  const calls = await mockInbox(page, (body) => {
    if (body['overrideReason'] === undefined) return limitExceeded;
    if (!reasonRejected) {
      reasonRejected = true;
      return { status: 400, json: { errorCode: 'COMMON.REQUEST.VALIDATION_FAILED', errors: { overrideReason: ['An override reason of 1 to 200 characters is required.'] } } };
    }
    return { status: 200, json: approved };
  });
  await page.goto('/sys/approvals');
  await page.getByTestId('sys-approval-approve-SO-0081').click();
  await page.getByTestId('sys-approval-comment').fill('同意出貨');
  await confirm(page);
  await expect(page.getByTestId('sys-approval-credit-excess')).toContainText('超出客戶可用信用額度 250 TWD，您的核准尚未保存');
  await expect(page.getByTestId('sys-approval-override-reason')).toBeFocused();
  await expect(page.getByTestId('sys-approval-decision-error')).toHaveCount(0);
  expect(calls).toHaveLength(1);
  expect(calls[0].body).toEqual({ comment: '同意出貨', version: 3 });
  await capture(page, 'reason-light-zh-Hant');
  await dark(page); await capture(page, 'reason-dark-zh-Hant');
  await page.emulateMedia({ colorScheme: 'light' });

  // 理由必填：只有空白不送出
  await page.getByTestId('sys-approval-override-reason').fill('   ');
  await confirm(page);
  await expect(page.getByTestId('sys-approval-override-reason-error')).toHaveText('請填寫信用覆核理由。');
  await expect(page.getByTestId('sys-approval-override-reason')).toBeFocused();
  expect(calls).toHaveLength(1);
  await capture(page, 'required-light-zh-Hant');

  // 伺服器拒絕理由（400 overrideReason）標在欄位下
  await page.getByTestId('sys-approval-override-reason').fill('長期客戶，財務長同意');
  await confirm(page);
  await expect(page.getByTestId('sys-approval-override-reason-error')).toContainText('伺服器不接受此理由');
  expect(calls).toHaveLength(2);

  await confirm(page);
  await expect(page.getByTestId('sys-approval-notice')).toContainText('已核准 SO-0081，並以您的理由覆核超出信用額度 250 TWD');
  // 畫面與資料一致：送出的理由已去除前後空白、待辦已移除
  expect(calls).toHaveLength(3);
  expect(calls[2]).toEqual({ path: '/api/v1/sys/approvals/52/approve',
    body: { comment: '同意出貨', version: 3, overrideReason: '長期客戶，財務長同意' } });
  await expect(page.getByTestId('sys-approval-task-SO-0081')).toHaveCount(0);
  await capture(page, 'success-light-zh-Hant');
  await dark(page); await capture(page, 'success-dark-zh-Hant');
});

test('SD-APPROVAL-OVR-E2E-002 a server error shows the trace ID and keeps the task; blocked credit removes the reason field', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of (422|500)/);
  pageErrors.allowApi5xx(/\/api\/v1\/sys\/approvals\/52\/approve/, 500);
  let attempt = 0;
  await mockInbox(page, () => {
    attempt++;
    if (attempt === 1) return limitExceeded;
    if (attempt === 2) return { status: 500, json: { traceId: 'trace-approve-500' } };
    return { status: 422, json: { errorCode: 'SAL.CREDIT.LIMIT_NOT_SET' } };
  });
  await page.goto('/sys/approvals');
  await page.getByTestId('sys-approval-approve-SO-0081').click();
  await confirm(page);
  await page.getByTestId('sys-approval-override-reason').fill('財務長同意');
  await confirm(page);
  await expect(page.getByTestId('sys-approval-decision-error')).toContainText('trace-approve-500');
  await expect(page.getByTestId('sys-approval-credit-excess')).toContainText('上一次送出的結果未能確認');
  await expect(page.getByTestId('sys-approval-credit-excess')).not.toContainText('尚未保存');
  await expect(page.getByTestId('sys-approval-override-reason')).toHaveValue('財務長同意');
  await capture(page, 'server-error-light-zh-Hant');
  await dark(page); await capture(page, 'server-error-dark-zh-Hant');
  await page.emulateMedia({ colorScheme: 'light' });

  await confirm(page);
  await expect(page.getByTestId('sys-approval-decision-error')).toContainText('今天沒有生效的信用額度');
  await expect(page.getByTestId('sys-approval-override-reason')).toHaveCount(0);
  await expect(page.getByTestId('sys-approval-credit-excess')).toHaveCount(0);
  await capture(page, 'blocked-light-zh-Hant');
  await page.locator('.ant-modal-footer .ant-btn').first().click();
  await expect(page.getByTestId('sys-approval-task-SO-0081')).toBeVisible();
  await expect(page.getByTestId('sys-approval-notice')).toHaveCount(0);
});

test('SD-APPROVAL-OVR-E2E-004 the over-limit refusal and reason field in English and Simplified Chinese', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 422/);
  await mockInbox(page, () => limitExceeded);
  await page.goto('/sys/approvals');
  const cases = [
    { lang: 'en', excess: 'exceeds the customer\'s available credit by 250 TWD. Your approval was not saved.', label: 'Credit override reason' },
    { lang: 'zh-Hans', excess: '超出客户可用信用额度 250 TWD，您的核准尚未保存', label: '信用复核理由' },
  ] as const;
  for (const c of cases) {
    await chooseLanguage(page, c.lang);
    await page.getByTestId('sys-approval-approve-SO-0081').click();
    await confirm(page);
    await expect(page.getByTestId('sys-approval-credit-excess')).toContainText(c.excess);
    await expect(page.locator('label[for="approval-override-reason"]')).toContainText(c.label);
    await capture(page, `reason-light-${c.lang}`);
    await dark(page); await capture(page, `reason-dark-${c.lang}`);
    await page.emulateMedia({ colorScheme: 'light' });
    await page.locator('.ant-modal-footer .ant-btn').first().click();
    await expect(page.locator('.ant-modal-wrap:visible')).toHaveCount(0);
  }
});

test.describe('without SAL.CREDIT.OVERRIDE', () => {
  test.use({
    authorization: { ...E2E_AUTHORIZATION, isFullAccess: false, permissions: [{ code: 'SYS.APPROVAL.PROCESS', dataScope: 'Company' }] },
  });

  test('SD-APPROVAL-OVR-E2E-003 approve is disabled with the reason; reject still works', async ({ page, pageErrors }) => {
    pageErrors.allow(/Failed to load resource: the server responded with a status of 422/);
    const calls = await mockInbox(page, (body) => body['comment'] === '超出額度' ? { status: 200, json: { ...approved, status: 'Rejected' } } : limitExceeded);
    await page.goto('/sys/approvals');
    await page.getByTestId('sys-approval-approve-SO-0081').click();
    await confirm(page);
    await expect(page.getByTestId('sys-approval-credit-excess')).toContainText('您沒有信用覆核權限');
    await expect(page.getByTestId('sys-approval-override-reason')).toHaveCount(0);
    await expect(page.locator('.ant-modal-footer .ant-btn-primary')).toBeDisabled();
    await capture(page, 'no-permission-light-zh-Hant');
    await dark(page); await capture(page, 'no-permission-dark-zh-Hant');
    await page.emulateMedia({ colorScheme: 'light' });
    await page.locator('.ant-modal-footer .ant-btn').first().click();

    await page.getByTestId('sys-approval-reject-SO-0081').click();
    await expect(page.getByTestId('sys-approval-credit-excess')).toHaveCount(0);
    await page.getByTestId('sys-approval-comment').fill('超出額度');
    await confirm(page);
    await expect(page.getByTestId('sys-approval-notice')).toContainText('已駁回 SO-0081');
    expect(calls.at(-1)).toEqual({ path: '/api/v1/sys/approvals/52/reject', body: { comment: '超出額度', version: 3 } });
  });
});
