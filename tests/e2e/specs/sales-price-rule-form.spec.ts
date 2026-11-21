import { mkdir } from 'node:fs/promises';
import type { Page, Route } from '@playwright/test';
import { chooseLanguage, chooseTheme, E2E_AUTHORIZATION, expect, test } from '../support/fixtures';

// Price Rule Create/Edit Interfaces 2.12/2.13 (WU-SD-005b1b2, ADR-163) on a stateful simulated server:
// the screen message and the rule list must agree with what the server saved.
type Rule = Record<string, unknown> & { id: number; itemId: number; customerId: number | null; currencyCode: string;
  validFrom: string; validTo: string; isActive: boolean; version: number };
const seed = (): Rule[] => [{ id: 31, itemId: 9, customerId: null, currencyCode: 'TWD', validFrom: '2026-01-01',
  validTo: '2026-12-31', unitPrice: 1250.125, discountCeiling: 10, isActive: true, version: 2, createdAt: '2026-11-01T00:00:00',
  updatedAt: null, itemCode: 'ITEM-9', itemName: 'Stainless bolt M8', customerCode: null, customerName: null },
{ id: 30, itemId: 9, customerId: null, currencyCode: 'TWD', validFrom: '2025-01-01', validTo: '2025-12-31', unitPrice: 990,
  discountCeiling: 5, isActive: false, version: 4, createdAt: '2025-01-01T00:00:00', updatedAt: null, itemCode: 'ITEM-9',
  itemName: 'Stainless bolt M8', customerCode: null, customerName: null }];
const currencies = [{ code: 'TWD', name: '新台幣', decimalPlaces: 0 }, { code: 'USD', name: 'US Dollar', decimalPlaces: 2 }];

async function capture(page: Page, name: string): Promise<void> {
  const path = '../../tests/qa/SD/screenshots';
  await mkdir(path, { recursive: true });
  await page.screenshot({ path: `${path}/${name}.png`, fullPage: true, animations: 'disabled' });
}

/** Simulated pricing API keeping saved rules; overlap and version checks mirror ADR-157/158. */
async function server(page: Page, options: { failNext?: () => { status: number; json: unknown } | null } = {}) {
  const rules = seed();
  const writes: { method: string; body: string }[] = [];
  const masterData: string[] = [];
  await page.route('**/api/v1/md/**', route => { masterData.push(route.request().url());
    return route.fulfill({ status: 403, json: { errorCode: 'COMMON.PERMISSION.DENIED' } }); });
  await page.route('**/api/v1/sal/price-rules**', (route: Route) => {
    const request = route.request();
    const url = new URL(request.url());
    const path = url.pathname.replace('/api/v1/sal/price-rules', '');
    if (path === '/currency-options') return route.fulfill({ json: currencies });
    if (path === '/item-options') return route.fulfill({ json: { items: [{ id: 9, code: 'ITEM-9', name: 'Stainless bolt M8' }],
      totalCount: 1, page: 1, pageSize: 10 } });
    if (path === '/customer-options') return route.fulfill({ json: { items: [{ id: 7, code: 'C-7', name: 'Harbour Trading Co.' }],
      totalCount: 1, page: 1, pageSize: 10 } });
    if (request.method() === 'GET' && path === '') {
      const sorted = [...rules].sort((a, b) => b.validFrom.localeCompare(a.validFrom));
      return route.fulfill({ json: { items: sorted, totalCount: sorted.length, page: 1, pageSize: 20 } });
    }
    const id = Number(/^\/(\d+)/.exec(path)?.[1]);
    const saved = rules.find(rule => rule.id === id);
    if (request.method() === 'GET') return saved ? route.fulfill({ json: saved })
      : route.fulfill({ status: 404, json: { errorCode: 'SAL.PRICE_RULE.NOT_FOUND' } });
    const body = request.postData() ?? '';
    writes.push({ method: request.method(), body });
    const failure = options.failNext?.();
    if (failure) return route.fulfill(failure);
    const input = JSON.parse(body) as Rule;
    if (request.method() === 'PUT') {
      if (!saved) return route.fulfill({ status: 404, json: { errorCode: 'SAL.PRICE_RULE.NOT_FOUND' } });
      if (input.version !== saved.version) return route.fulfill({ status: 409, json: { errorCode: 'COMMON.CONCURRENCY.CONFLICT' } });
    }
    const overlaps = rules.filter(rule => rule.isActive && rule.id !== id && rule.itemId === input.itemId &&
      rule.currencyCode === input.currencyCode && rule.customerId === input.customerId &&
      rule.validFrom <= input.validTo && input.validFrom <= rule.validTo).map(rule => rule.id);
    if (overlaps.length) return route.fulfill({ status: 409, json: { errorCode: 'SAL.PRICING.RULE_OVERLAP', conflictingRuleIds: overlaps } });
    const labels = { itemCode: 'ITEM-9', itemName: 'Stainless bolt M8',
      customerCode: input.customerId ? 'C-7' : null, customerName: input.customerId ? 'Harbour Trading Co.' : null };
    if (request.method() === 'PUT') {
      Object.assign(saved!, input, labels, { version: saved!.version + 1, updatedAt: '2026-11-21T20:00:00' });
      return route.fulfill({ json: saved });
    }
    const created: Rule = { ...input, ...labels, id: 40 + rules.length, isActive: true, version: 1,
      createdAt: '2026-11-21T20:00:00', updatedAt: null };
    rules.push(created);
    return route.fulfill({ status: 201, json: created });
  });
  return { rules, writes, masterData };
}

test.describe('pricing maintainer without MD permission', () => {
  test.use({ authorization: { ...E2E_AUTHORIZATION, isFullAccess: false, permissions: ['VIEW', 'CREATE', 'EDIT']
    .map(action => ({ code: `SAL.PRICING.${action}`, dataScope: 'Company' })) } });

  test('SD-PRULE-UI-106 creates a rule: overlap is explained, success matches the list', async ({ page, pageErrors }) => {
    pageErrors.allow(/status of 409/);
    const api = await server(page);
    await page.goto('/sal/price-rules');
    await page.getByTestId('sd-price-rule-create').click();
    await expect(page).toHaveURL(/\/sal\/price-rules\/new$/);
    await page.getByTestId('sd-price-rule-form-save').click();
    await expect(page.getByTestId('sd-price-rule-form-errors')).toContainText('有 5 個欄位需要修正');
    await expect(page.getByTestId('sd-price-rule-form-item-error')).toContainText('請選擇物料');
    expect(api.writes).toEqual([]);
    await capture(page, '2.12-validation-light-zh-Hant');
    await page.getByTestId('sd-price-rule-form-item-pick').click();
    await expect(page.getByTestId('erp-select-description')).toContainText('只列出啟用的物料');
    await page.getByTestId('erp-select-row-9').click(); await page.getByTestId('erp-select-confirm').click();
    await expect(page.getByTestId('sd-price-rule-form-item')).toHaveText('ITEM-9 · Stainless bolt M8');
    await page.getByTestId('sd-price-rule-form-currency').selectOption('TWD');
    await expect(page.getByTestId('sd-price-rule-form-currency-hint')).toContainText('0 位小數');
    await page.getByTestId('sd-price-rule-form-from').fill('2026-12-01');
    await page.getByTestId('sd-price-rule-form-to').fill('2027-03-31');
    await page.getByTestId('sd-price-rule-form-price').fill('1300.5');
    await page.getByTestId('sd-price-rule-form-ceiling').fill('8');
    await capture(page, '2.12-filled-light-zh-Hant');
    await page.getByTestId('sd-price-rule-form-save').click();
    await expect(page.getByTestId('sd-price-rule-form-overlap')).toContainText('未儲存');
    await expect(page.getByTestId('sd-price-rule-form-overlap')).toContainText('#31');
    await expect(page.getByTestId('sd-price-rule-form-success')).toHaveCount(0);
    expect(api.rules).toHaveLength(2);
    await capture(page, '2.12-overlap-light-zh-Hant');
    await chooseTheme(page, 'dark'); await capture(page, '2.12-overlap-dark-zh-Hant');
    await page.getByTestId('sd-price-rule-form-from').fill('2027-01-01');
    await expect(page.getByTestId('sd-price-rule-form-overlap')).toHaveCount(0);
    await page.getByTestId('sd-price-rule-form-save').click();
    await expect(page.getByTestId('sd-price-rule-form-success')).toContainText('價格規則 42 已建立並生效');
    await expect(page.getByTestId('sd-price-rule-form-save')).toBeDisabled();
    await capture(page, '2.12-created-dark-zh-Hant');
    expect(api.writes.at(-1)!.body).toBe('{"itemId":9,"customerId":null,"currencyCode":"TWD","validFrom":"2027-01-01",' +
      '"validTo":"2027-03-31","unitPrice":1300.5,"discountCeiling":8}');
    expect(api.rules).toHaveLength(3);
    await page.getByTestId('sd-price-rule-form-to-list').click();
    await expect(page.getByTestId('sd-price-rule-row-42')).toContainText('1,300.5');
    await expect(page.getByTestId('sd-price-rule-row-42')).toContainText('2027-01-01');
    expect(api.masterData).toEqual([]);
  });

  test('SD-PRULE-UI-107 edits with the version read; a stale version is explained and reload recovers', async ({ page, pageErrors }) => {
    pageErrors.allow(/status of 409/);
    const api = await server(page);
    await page.goto('/sal/price-rules');
    await expect(page.getByTestId('sd-price-rule-edit-30')).toHaveCount(0); // inactive row has no Edit
    await page.getByTestId('sd-price-rule-edit-31').click();
    await expect(page).toHaveURL(/\/sal\/price-rules\/31\/edit$/);
    await expect(page.getByTestId('sd-price-rule-form-price')).toHaveValue('1250.125');
    await capture(page, '2.13-normal-light-zh-Hant');
    await chooseLanguage(page, 'en'); await capture(page, '2.13-normal-light-en');
    await chooseLanguage(page, 'zh-Hans'); await capture(page, '2.13-normal-light-zh-Hans');
    await chooseLanguage(page, 'zh-Hant');
    // Someone else saves first: the server is at version 3 while the page still holds version 2.
    api.rules[0].version = 3;
    await page.getByTestId('sd-price-rule-form-price').fill('1288');
    await page.getByTestId('sd-price-rule-form-save').click();
    await expect(page.getByTestId('sd-price-rule-form-stale')).toContainText('其他人修改了此規則');
    await expect(page.getByTestId('sd-price-rule-form-save')).toBeDisabled();
    await expect(page.getByTestId('sd-price-rule-form-locked')).toContainText('請先重新載入規則');
    await capture(page, '2.13-stale-light-zh-Hant');
    expect(api.writes.at(-1)!.body).toMatch(/"version":2}$/);
    await page.getByTestId('sd-price-rule-form-refresh').click();
    await expect(page.getByTestId('sd-price-rule-form-stale')).toHaveCount(0);
    await page.getByTestId('sd-price-rule-form-price').fill('1288');
    await page.getByTestId('sd-price-rule-form-save').click();
    await expect(page.getByTestId('sd-price-rule-form-success')).toContainText('價格規則 31 已儲存（版本 4）');
    expect(api.writes.at(-1)!.body).toMatch(/"unitPrice":1288,"discountCeiling":10,"version":3}$/);
    await chooseTheme(page, 'dark'); await capture(page, '2.13-saved-dark-zh-Hant');
    await page.getByTestId('sd-price-rule-form-to-list').click();
    await expect(page.getByTestId('sd-price-rule-row-31')).toContainText('1,288');
    await expect(page.getByTestId('sd-price-rule-row-31')).toContainText('4');
  });

  test('SD-PRULE-UI-108 inactive rule, unknown outcome with trace ID, and tablet layout', async ({ page, pageErrors }) => {
    pageErrors.allowApi5xx(/\/api\/v1\/sal\/price-rules/, 500);
    pageErrors.allow(/status of 500/);
    let fail = true;
    const api = await server(page, { failNext: () => fail ? { status: 500, json: { traceId: 'trace-rule-500' } } : null });
    await page.goto('/sal/price-rules/30/edit');
    await expect(page.getByTestId('sd-price-rule-form-inactive')).toContainText('已停用，不可修改');
    await expect(page.getByTestId('sd-price-rule-form-save')).toBeDisabled();
    await expect(page.getByTestId('sd-price-rule-form-price')).toBeDisabled();
    // UX review 2.13 (P2): disabled inputs must not look editable.
    expect(Number(await page.getByTestId('sd-price-rule-form-price').evaluate(element => getComputedStyle(element).opacity))).toBeLessThan(1);
    await capture(page, '2.13-inactive-light-zh-Hant');
    await page.goto('/sal/price-rules/new');
    await page.getByTestId('sd-price-rule-form-item-pick').click();
    await page.getByTestId('erp-select-row-9').click(); await page.getByTestId('erp-select-confirm').click();
    await page.getByTestId('sd-price-rule-form-scope').selectOption('customer');
    await page.getByTestId('sd-price-rule-form-customer-pick').click();
    await page.getByTestId('erp-select-row-7').click(); await page.getByTestId('erp-select-confirm').click();
    await page.getByTestId('sd-price-rule-form-currency').selectOption('USD');
    await page.getByTestId('sd-price-rule-form-from').fill('2027-01-01');
    await page.getByTestId('sd-price-rule-form-to').fill('2027-12-31');
    await page.getByTestId('sd-price-rule-form-price').fill('39.99');
    await page.getByTestId('sd-price-rule-form-save').click();
    await expect(page.getByTestId('sd-price-rule-form-uncertain')).toContainText('無法確認規則是否已建立');
    await expect(page.getByTestId('sd-price-rule-form-uncertain')).toContainText('trace-rule-500');
    await expect(page.getByTestId('sd-price-rule-form-save')).toBeDisabled();
    await expect(page.getByTestId('sd-price-rule-form-success')).toHaveCount(0);
    expect(api.writes).toHaveLength(1);
    await expect(page.getByTestId('sd-price-rule-form-to-list')).toBeVisible(); // UX review 2.12 (P3): way to check the list
    await expect(page.getByTestId('sd-price-rule-form-another')).toHaveCount(0);
    expect(api.writes[0].body).toContain('"customerId":7,');
    fail = false;
    await capture(page, '2.12-unknown-light-zh-Hant');
    await chooseTheme(page, 'dark'); await capture(page, '2.12-unknown-dark-zh-Hant');
    await chooseLanguage(page, 'en'); await capture(page, '2.12-unknown-dark-en');
    await chooseLanguage(page, 'zh-Hant');
    await page.setViewportSize({ width: 768, height: 1000 });
    await expect.poll(() => page.locator('nz-sider').evaluate(element => element.getBoundingClientRect().width))
      .toBeLessThanOrEqual(65);
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    expect(overflow).toBeLessThanOrEqual(0);
    const form = await page.getByTestId('sd-price-rule-form').evaluate(element => element.scrollWidth - element.clientWidth);
    expect(form).toBeLessThanOrEqual(0);
    await capture(page, '2.12-tablet-dark-zh-Hant');
    expect(api.rules).toHaveLength(2); // the 500 saved nothing on this server
  });
});
