import { Page } from '@playwright/test';
import { chooseLanguage, E2E_AUTHORIZATION, expect, openHome, test } from '../support/fixtures';

// 介面 8.67（REQ_F4118.28.1–4、REQ_F4118.28.6）：單號規則頁。API 以 page.route 模擬（ADR-013）。

interface NumberRule {
  id: number;
  documentType: string;
  name: string;
  prefix: string;
  dateFormat: string;
  sequenceLength: number;
  resetPeriod: string;
  isActive: boolean;
  isInUse: boolean;
  sample: string;
  version: number;
}

interface Call {
  method: string;
  path: string;
  query: string;
  body: Record<string, unknown> | null;
}

const rule = (id: number, documentType: string, overrides: Partial<NumberRule> = {}): NumberRule => ({
  id,
  documentType,
  name: documentType,
  prefix: 'SO',
  dateFormat: 'YearMonth',
  sequenceLength: 4,
  resetPeriod: 'Monthly',
  isActive: true,
  isInUse: false,
  sample: 'SO2026100001',
  version: 1,
  ...overrides,
});

/** 模擬單號規則 API；重複類型回 409、版本不符回 409；預覽回傳以日期組成的單號。 */
async function mockNumberRuleApi(page: Page, rows: NumberRule[]): Promise<Call[]> {
  const calls: Call[] = [];
  await page.route('**/api/v1/sys/number-rules**', async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    const body = request.postDataJSON() as Record<string, unknown> | null;
    calls.push({ method: request.method(), path: url.pathname, query: url.search, body });
    const segments = url.pathname.split('/');
    const id = Number(segments[5]);
    const row = rows.find((r) => r.id === id);

    if (request.method() === 'GET' && segments[6] === 'preview') {
      const date = url.searchParams.get('date') ?? '2026-10-11';
      await route.fulfill({ json: { documentType: row!.documentType, documentDate: date, nextNumber: `${row!.prefix}${date.replaceAll('-', '').slice(0, 6)}0008` } });
    } else if (request.method() === 'GET') {
      const keyword = (url.searchParams.get('keyword') ?? '').toLowerCase();
      const items = rows
        .filter((r) => !keyword || r.documentType.toLowerCase().includes(keyword) || r.name.toLowerCase().includes(keyword))
        .sort((a, b) => a.documentType.localeCompare(b.documentType));
      await route.fulfill({ json: { items, page: 1, pageSize: 20, totalCount: items.length } });
    } else if (request.method() === 'POST') {
      const documentType = String(body?.['documentType']);
      if (rows.some((r) => r.documentType === documentType)) {
        await route.fulfill({ status: 409, json: { status: 409, errorCode: 'SYS.NUMBER_RULE.DUPLICATE_TYPE' } });
        return;
      }
      const created = rule(100 + rows.length, documentType, { ...(body as Partial<NumberRule>), sample: 'NEW' });
      rows.push(created);
      await route.fulfill({ status: 201, json: created });
    } else if (!row) {
      await route.fulfill({ status: 404, json: { status: 404, errorCode: 'SYS.NUMBER_RULE.NOT_FOUND' } });
    } else {
      if (body?.['version'] !== row.version) {
        await route.fulfill({ status: 409, json: { status: 409, errorCode: 'COMMON.CONCURRENCY.CONFLICT' } });
        return;
      }
      Object.assign(row, body, { version: row.version + 1 });
      await route.fulfill({ json: row });
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

test.describe('單號規則（全權限）', () => {
  test('由選單進入，新增規則：格式檢查、週期不在日期格式內的提示、即時樣式、重複類型與成功', async ({ page, pageErrors }) => {
    pageErrors.allow(/status of 409/);
    const calls = await mockNumberRuleApi(page, [rule(1, 'SD.ORDER', { name: '銷售訂單' })]);
    await openHome(page);

    await page.getByTestId('layout-menu-sys').click();
    await page.getByTestId('layout-menu-sys-number-rules').click();
    await expect(page).toHaveURL(/\/sys\/number-rules$/);
    await expect(page.getByTestId('sys-number-rule-row-SD.ORDER')).toContainText('每月');
    await expect(page.getByTestId('sys-number-rule-sample-SD.ORDER')).toHaveText('SO2026100001');

    await page.getByTestId('sys-number-rule-create').click();
    await page.getByTestId('sys-number-rule-type').fill('order');
    await page.getByTestId('sys-number-rule-name').fill('採購單');
    await saveDialog(page);
    await expect(page.getByTestId('sys-number-rule-form')).toContainText('格式為 {PREFIX}.{DOCUMENT}');
    expect(calls.some((c) => c.method === 'POST')).toBe(false);

    await page.getByTestId('sys-number-rule-prefix').fill('po');
    await choose(page, 'sys-number-rule-reset', '每日');
    await expect(page.getByTestId('sys-number-rule-period-error')).toBeVisible();
    await choose(page, 'sys-number-rule-date-format', 'yyMMdd（短年月日）');
    await expect(page.getByTestId('sys-number-rule-period-error')).toHaveCount(0);
    await page.getByTestId('sys-number-rule-length').fill('5');
    await expect(page.getByTestId('sys-number-rule-live-sample')).toHaveText(/^PO\d{6}00001$/);

    await page.getByTestId('sys-number-rule-type').fill('sd.order');
    await saveDialog(page);
    await expect(page.getByTestId('sys-number-rule-save-error')).toContainText('目前公司已有此單據類型');

    await page.getByTestId('sys-number-rule-type').fill(' pur.order ');
    await saveDialog(page);
    await expect(page.getByTestId('sys-number-rule-notice')).toContainText('已新增單號規則 PUR.ORDER');
    await expect(page.getByTestId('sys-number-rule-row-PUR.ORDER')).toBeVisible();
    expect(calls.filter((c) => c.method === 'POST').at(-1)?.body).toEqual({
      documentType: 'PUR.ORDER',
      name: '採購單',
      prefix: 'PO',
      dateFormat: 'ShortYearMonthDay',
      sequenceLength: 5,
      resetPeriod: 'Daily',
    });
  });

  test('已發號規則鎖定日期格式與週期、長度不可縮短；編輯帶版本並停用；預覽指定日期的下一號', async ({ page }) => {
    const calls = await mockNumberRuleApi(page, [rule(1, 'SD.ORDER', { name: '銷售訂單', isInUse: true, version: 4 })]);
    await page.goto('/sys/number-rules');
    await expect(page.getByTestId('sys-number-rule-in-use-SD.ORDER')).toContainText('已發號');

    await page.getByTestId('sys-number-rule-edit-SD.ORDER').click();
    await expect(page.locator('.ant-modal-title')).toContainText('編輯單號規則 SD.ORDER');
    await expect(page.getByTestId('sys-number-rule-locked')).toBeVisible();
    await expect(page.getByTestId('sys-number-rule-type')).toBeDisabled();
    await expect(page.getByTestId('sys-number-rule-date-format')).toHaveClass(/ant-select-disabled/);
    await expect(page.getByTestId('sys-number-rule-reset')).toHaveClass(/ant-select-disabled/);
    await page.getByTestId('sys-number-rule-length').fill('3');
    await saveDialog(page);
    await expect(page.getByTestId('sys-number-rule-form')).toContainText('請輸入 4–10 的整數');
    expect(calls.some((c) => c.method === 'PUT')).toBe(false);

    await page.getByTestId('sys-number-rule-length').fill('6');
    await page.getByTestId('sys-number-rule-active').click();
    await saveDialog(page);
    await expect(page.getByTestId('sys-number-rule-notice')).toContainText('已更新單號規則 SD.ORDER');
    await expect(page.getByTestId('sys-number-rule-row-SD.ORDER')).toContainText('停用');
    expect(calls.find((c) => c.method === 'PUT')?.body).toEqual({
      name: '銷售訂單',
      prefix: 'SO',
      dateFormat: 'YearMonth',
      sequenceLength: 6,
      resetPeriod: 'Monthly',
      isActive: false,
      version: 4,
    });

    await page.getByTestId('sys-number-rule-preview-SD.ORDER').click();
    await expect(page.getByTestId('sys-number-rule-preview-result')).toBeVisible();
    await page.getByTestId('sys-number-rule-preview-date').fill('2026-01-31');
    await page.getByTestId('sys-number-rule-preview-run').click();
    await expect(page.getByTestId('sys-number-rule-preview-result')).toContainText('2026-01-31 的下一個單號');
    await expect(page.getByTestId('sys-number-rule-preview-result')).toContainText('SO2026010008');
    expect(calls.filter((c) => c.path.endsWith('/preview')).at(-1)?.query).toBe('?date=2026-01-31');
  });

  test('英文介面', async ({ page }) => {
    await mockNumberRuleApi(page, [rule(1, 'SD.ORDER', { dateFormat: 'None', resetPeriod: 'Never', sample: 'SO0001' })]);
    await page.goto('/sys/number-rules');
    await chooseLanguage(page, 'en');
    await expect(page.getByTestId('sys-number-rule-title')).toHaveText('Document number rules');
    await expect(page.getByTestId('sys-number-rule-row-SD.ORDER')).toContainText('No date');
    await expect(page.getByTestId('sys-number-rule-row-SD.ORDER')).toContainText('Never');
  });
});

test.describe('單號規則（只可檢視）', () => {
  test.use({
    authorization: { ...E2E_AUTHORIZATION, isFullAccess: false, permissions: [{ code: 'SYS.NUMBER_RULE.VIEW', dataScope: 'Company' }] },
  });

  test('沒有新增與編輯按鈕，仍可預覽', async ({ page }) => {
    await mockNumberRuleApi(page, [rule(1, 'SD.ORDER')]);
    await openHome(page);
    await page.getByTestId('layout-menu-sys').click();
    await page.getByTestId('layout-menu-sys-number-rules').click();
    await expect(page.getByTestId('sys-number-rule-row-SD.ORDER')).toBeVisible();
    await expect(page.getByTestId('sys-number-rule-create')).toHaveCount(0);
    await expect(page.getByTestId('sys-number-rule-edit-SD.ORDER')).toHaveCount(0);
    await expect(page.getByTestId('sys-number-rule-preview-SD.ORDER')).toBeVisible();
  });
});

test.describe('單號規則（無權限）', () => {
  test.use({
    authorization: { ...E2E_AUTHORIZATION, isFullAccess: false, permissions: [{ code: 'SYS.USER.VIEW', dataScope: 'Company' }] },
  });

  test('選單沒有單號規則項目，直接進入顯示 403', async ({ page }) => {
    await openHome(page);
    await page.getByTestId('layout-menu-sys').click();
    await expect(page.getByTestId('layout-menu-sys-number-rules')).toHaveCount(0);
    await page.goto('/sys/number-rules');
    await expect(page.getByTestId('error-page-403')).toBeVisible();
  });
});
