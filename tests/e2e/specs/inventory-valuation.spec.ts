import { E2E_AUTHORIZATION, chooseLanguage, chooseTheme, expect, test } from '../support/fixtures';

test('INV-VAL-006 shows provisional Unpriced value, cutoff, translations and dark theme', async ({ page }) => {
  const requests: URL[] = [];
  await page.route('**/api/v1/inv/valuations/items?**', route => route.fulfill({ json: {
    items: [{ id: 7, code: 'BOLT', name: 'Bolt', isActive: true }], totalCount: 1 } }));
  await page.route('**/api/v1/inv/valuations/items/7**', route => {
    requests.push(new URL(route.request().url()));
    return route.fulfill({ json: { itemId: 7, cutoffPostedAt: '2026-10-14T09:30:00', onHand: 2,
      value: null, averageUnitCost: null, unpricedQuantity: 2, status: 'Unpriced', movementCount: 1, provisional: true } });
  });
  await page.goto('/inv/valuations');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('庫存估值');
  await page.getByTestId('inv-valuation-item').selectOption({ label: 'BOLT · Bolt' });
  await page.getByTestId('inv-valuation-cutoff').fill('2026-10-14T09:30');
  await page.getByTestId('inv-valuation-search').click();
  await expect(page.getByTestId('inv-valuation-unpriced')).toContainText('2.000000');
  await expect(page.getByTestId('inv-valuation-result')).toContainText('暫估金額: —');
  expect(requests.at(-1)?.searchParams.get('cutoffPostedAt')).toBe('2026-10-14T09:30');
  await chooseTheme(page, 'dark');
  await expect(page.locator('link#erp-theme-dark')).toHaveAttribute('href', 'theme-dark.css');
  await chooseLanguage(page, 'en');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Inventory valuation');
  await chooseLanguage(page, 'zh-Hans');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('库存估值');
});

test('INV-VAL-006 keeps explicit zero value and reports a valuation error', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 422/);
  await page.route('**/api/v1/inv/valuations/items?**', route => route.fulfill({ json: {
    items: [{ id: 7, code: 'BOLT', name: 'Bolt', isActive: true }], totalCount: 1 } }));
  let calls = 0;
  await page.route('**/api/v1/inv/valuations/items/7**', route => {
    calls++;
    return calls === 1 ? route.fulfill({ json: { itemId: 7, cutoffPostedAt: null, onHand: 1,
      value: 0, averageUnitCost: 0, unpricedQuantity: 0, status: 'Valued', movementCount: 1, provisional: true } })
      : route.fulfill({ status: 422, json: { errorCode: 'INV.VALUATION.NEGATIVE_POOL' } });
  });
  await page.goto('/inv/valuations');
  await page.getByTestId('inv-valuation-item').selectOption({ label: 'BOLT · Bolt' });
  await page.getByTestId('inv-valuation-search').click();
  await expect(page.getByTestId('inv-valuation-result')).toContainText('暫估金額: 0.0000');
  await expect(page.getByTestId('inv-valuation-unpriced')).toHaveCount(0);
  await page.getByTestId('inv-valuation-search').click();
  await expect(page.getByText('估值數量池為負，請檢查異動。')).toBeVisible();
  await expect(page.getByTestId('inv-valuation-result')).toHaveCount(0);
});

test.describe('INV-VAL-006 without valuation permission', () => {
  test.use({ authorization: { ...E2E_AUTHORIZATION, isFullAccess: false,
    permissions: [{ code: 'INV.INVENTORY.VIEW', dataScope: 'Company' }] } });
  test('hides menu entry and denies direct page', async ({ page }) => {
    await page.goto('/');
    await expect(page.getByTestId('layout-menu-inv-valuations')).toHaveCount(0);
    await page.goto('/inv/valuations');
    await expect(page).toHaveURL(/\/forbidden/);
  });
});

test.describe('INV-VAL-006 valuation-only role', () => {
  test.use({ authorization: { ...E2E_AUTHORIZATION, isFullAccess: false,
    permissions: [{ code: 'INV.VALUATION.VIEW', dataScope: 'Company' }] } });
  test('can select an item and calculate without MD.ITEM.VIEW', async ({ page }) => {
    await page.route('**/api/v1/inv/valuations/items?**', route => route.fulfill({ json: {
      items: [{ id: 7, code: 'BOLT', name: 'Bolt' }], page: 1, pageSize: 100, totalCount: 1 } }));
    await page.route('**/api/v1/inv/valuations/items/7**', route => route.fulfill({ json: {
      itemId: 7, cutoffPostedAt: null, onHand: 1, value: 2, averageUnitCost: 2,
      unpricedQuantity: 0, status: 'Valued', movementCount: 1, provisional: true } }));
    await page.goto('/inv/valuations');
    await page.getByTestId('inv-valuation-item').selectOption({ label: 'BOLT · Bolt' });
    await page.getByTestId('inv-valuation-search').click();
    await expect(page.getByTestId('inv-valuation-result')).toContainText('2.0000');
  });
});
