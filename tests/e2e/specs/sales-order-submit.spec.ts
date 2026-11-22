import { mkdir } from 'node:fs/promises';
import { chooseLanguage, chooseTheme, expect, test } from '../support/fixtures';

/** WU-SD-006c1 (ADR-172): order submit with credit preview and override reason, Sales Order View Interface 2.4. */
const summary = { id: 81, documentNo: 'SO-0081', customerId: 7, customerName: 'Saved customer',
  businessDate: '2026-11-18', status: 'Draft', originalGross: 1250, version: 2 };
const detail = (status: string, version: number) => ({ ...summary, status, version, customerCode: 'C-7', currencyCode: 'TWD',
  baseCurrencyCode: 'TWD', exchangeRate: 1, sourceQuotationId: null, originalNet: 1250, originalTax: 0,
  baseNet: 1250, baseTax: 0, baseGross: 1250, remark: null, approvalInstanceId: status === 'Pending' ? 52 : null,
  lines: [], approvalHistory: [] });
const overLimit = { customerId: 7, orderId: 81, status: 'OverLimit', reasonCode: 'SAL.CREDIT.LIMIT_EXCEEDED',
  evaluationDate: '2026-11-23', baseCurrency: 'TWD', limit: 10000, postedOpenAr: 6000, otherCommitments: 3000,
  candidate: 1250, headroom: -250, excess: 250, limitVersion: 3, requiresRecheck: true };
async function capture(page: import('@playwright/test').Page, name: string): Promise<void> {
  const path = '../../tests/qa/SD/screenshots';
  await mkdir(path, { recursive: true });
  await page.screenshot({ path: `${path}/2.4-submit-${name}.png`, fullPage: true, animations: 'disabled' });
}

test('SD-ORDER-SUBMIT-E2E-001 over-limit preview, reason required, saved override shown and list matches the saved status', async ({ page }) => {
  let saved = detail('Draft', 2);
  const bodies: unknown[] = [];
  await page.route('**/api/v1/sal/credit-checks/**', route => route.fulfill({ json: overLimit }));
  await page.route('**/api/v1/sal/orders**', route => {
    const url = new URL(route.request().url());
    if (url.pathname.endsWith('/81/submit')) {
      bodies.push(route.request().postDataJSON());
      saved = detail('Pending', 3);
      return route.fulfill({ json: { order: saved, credit: { ...overLimit, status: 'OverLimit' },
        creditOverride: { id: 9, stage: 'Submit', approvalInstanceId: 52, orderVersion: 2, reason: 'Key account; CFO agreed',
          excess: 250, baseCurrency: 'TWD', overrideBy: 1, overrideAt: '2026-11-23T05:10:00' } } });
    }
    if (url.pathname.endsWith('/81')) return route.fulfill({ json: saved });
    return route.fulfill({ json: { items: [{ ...summary, status: saved.status, version: saved.version }], totalCount: 1, page: 1, pageSize: 20 } });
  });
  await page.goto('/sal/orders');
  await page.getByTestId('sd-order-open-81').click();
  await page.getByTestId('sd-order-submit').click();
  await expect(page.getByTestId('sd-order-credit-status')).toHaveText('超出額度');
  await expect(page.getByTestId('sd-order-credit-excess')).toContainText('250.00 TWD');
  await page.getByTestId('sd-order-submit-confirm').click();
  await expect(page.getByTestId('sd-order-override-error')).toHaveText('請輸入覆核信用額度的理由。');
  expect(bodies).toHaveLength(0);
  await capture(page, 'over-limit-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'over-limit-dark-zh-Hant');
  await chooseTheme(page, 'light'); await chooseLanguage(page, 'en');
  await expect(page.getByTestId('sd-order-credit-status')).toHaveText('Over limit');
  await capture(page, 'over-limit-light-en');
  await chooseLanguage(page, 'zh-Hans');
  await expect(page.getByTestId('sd-order-credit-status')).toHaveText('超出额度');
  await capture(page, 'over-limit-light-zh-Hans');
  await chooseLanguage(page, 'zh-Hant');
  await page.getByTestId('sd-order-override-reason').fill('  Key account; CFO agreed ');
  await page.getByTestId('sd-order-submit-confirm').click();
  await expect(page.getByTestId('sd-order-submit-notice')).toContainText('SO-0081 已送審，等待審批（版本 3）');
  await expect(page.getByTestId('sd-order-submit-override')).toContainText('超額 250.00 TWD。理由：Key account; CFO agreed');
  expect(bodies).toEqual([{ version: 2, creditOverrideReason: 'Key account; CFO agreed' }]);
  // Visible status equals the saved order: detail re-read and the list row both say Pending.
  await expect(page.getByTestId('sd-order-detail-status')).toHaveText('審批中');
  await expect(page.getByTestId('sd-order-row-status-81')).toHaveText('審批中');
  await expect(page.getByTestId('sd-order-submit')).toHaveCount(0);
  await capture(page, 'success-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'success-dark-zh-Hant');
});

test('SD-ORDER-SUBMIT-E2E-002 blocked credit disables submit; a server error keeps the order Draft with a trace ID', async ({ page, pageErrors }) => {
  let blocked = true;
  let submits = 0;
  await page.route('**/api/v1/sal/credit-checks/**', route => blocked
    ? route.fulfill({ status: 422, json: { errorCode: 'SAL.CREDIT.LIMIT_NOT_SET' } })
    : route.fulfill({ json: { ...overLimit, status: 'WithinLimit', reasonCode: null, limit: 20000, headroom: 9750, excess: null } }));
  await page.route('**/api/v1/sal/orders**', route => {
    const url = new URL(route.request().url());
    if (url.pathname.endsWith('/81/submit')) { submits++; return route.fulfill({ status: 500, json: { traceId: 'trace-submit-500' } }); }
    if (url.pathname.endsWith('/81')) return route.fulfill({ json: detail('Draft', 2) });
    return route.fulfill({ json: { items: [summary], totalCount: 1, page: 1, pageSize: 20 } });
  });
  pageErrors.allowApi5xx(/\/api\/v1\/sal\/orders\/81\/submit/, 500);
  pageErrors.allow(/Failed to load resource: the server responded with a status of (422|500)/);
  await page.goto('/sal/orders');
  await page.getByTestId('sd-order-open-81').click();
  await page.getByTestId('sd-order-submit').click();
  await expect(page.getByTestId('sd-order-credit-error')).toContainText('今天沒有生效的信用額度');
  await expect(page.getByTestId('sd-order-submit-confirm')).toBeDisabled();
  await expect(page.getByTestId('sd-order-submit-hint')).toContainText('重新檢查成功前不能送審');
  await capture(page, 'blocked-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'blocked-dark-zh-Hant');
  await chooseTheme(page, 'light');
  blocked = false;
  await page.getByTestId('sd-order-credit-recheck').click();
  await expect(page.getByTestId('sd-order-credit-status')).toHaveText('額度內');
  await page.getByTestId('sd-order-submit-confirm').click();
  await expect(page.getByTestId('sd-order-submit-stale')).toContainText('無法確認結果');
  await expect(page.getByTestId('sd-order-submit-error')).toContainText('trace-submit-500');
  await expect(page.getByTestId('sd-order-submit-notice')).toHaveCount(0);
  await expect(page.getByTestId('sd-order-submit-confirm')).toBeDisabled();
  await expect(page.getByTestId('sd-order-submit-unchanged')).toHaveCount(0);
  expect(submits).toBe(1);
  await capture(page, 'server-error-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'server-error-dark-zh-Hant');
  // Reload shows the saved order is still Draft.
  await page.getByTestId('sd-order-submit-refresh').click();
  await expect(page.getByTestId('sd-order-detail-status')).toHaveText('草稿');
  await expect(page.getByTestId('sd-order-submit')).toBeVisible();
});
