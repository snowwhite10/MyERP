import { expect, test } from '../support/fixtures';

const lists = [
  { path: 'payment-terms', prefix: 'md-payment-term', code: 'NET30', sort: '-name', row: { id: 1, code: 'NET30', name: 'Net 30', dueType: 'NetDays', days: 30, isActive: true, version: 1 } },
  { path: 'fund-accounts', prefix: 'md-fund', code: 'CASH', sort: '-name', filter: 'Cash', filterKey: 'fundType', row: { id: 1, code: 'CASH', name: 'Cash', fundType: 'Cash', currencyId: 1, bankName: '', bankAccountNo: '', ledgerAccountId: null, isActive: true, version: 1 } },
  { path: 'income-expense-categories', prefix: 'md-fund', code: 'INC', sort: '-name', filter: 'Income', filterKey: 'direction', row: { id: 1, code: 'INC', name: 'Income', direction: 'Income', ledgerAccountId: null, isActive: true, version: 1 } },
  { path: 'process-categories', prefix: 'md-process', code: 'ASM', sort: '-name', row: { id: 1, code: 'ASM', name: 'Assembly', sortOrder: 1, processCount: 0, isActive: true, version: 1 } },
  { path: 'processes', prefix: 'md-process', code: 'CUT', sort: '-name', filter: '1', filterKey: 'categoryId', row: { id: 1, code: 'CUT', name: 'Cut', categoryId: 1, uomId: null, standardRate: 10, sortOrder: 1, isActive: true, version: 1 } },
] as const;

for (const list of lists) {
  test(`${list.path} 分享連結、返回前進及切公司`, async ({ page }) => {
    const requests: { company: string; url: URL }[] = [];
    await page.route('**/api/v1/md/accounts**', route => route.fulfill({ json: [] }));
    await page.route('**/api/v1/md/currencies**', route => route.fulfill({ json: { items: [], page: 1, pageSize: 20, totalCount: 0 } }));
    await page.route('**/api/v1/md/uoms**', route => route.fulfill({ json: { items: [], page: 1, pageSize: 20, totalCount: 0 } }));
    await page.route('**/api/v1/md/process-categories**', route => list.path === 'processes'
      ? route.fulfill({ json: { items: [{ id: 1, code: 'ASM', name: 'Assembly', sortOrder: 1, isActive: true }], page: 1, pageSize: 20, totalCount: 1 } }) : route.fallback());
    await page.route(`**/api/v1/md/${list.path}**`, route => {
      const url = new URL(route.request().url());
      requests.push({ company: route.request().headers()['x-company-id'] ?? '', url });
      return route.fulfill({ json: { items: [list.row], page: Number(url.searchParams.get('page') ?? 1), pageSize: Number(url.searchParams.get('pageSize') ?? 20), totalCount: 100 } });
    });
    const filter = 'filter' in list ? `&filter=${list.filter}` : '';
    await page.goto(`/md/${list.path}?keyword=base&page=2&pageSize=10&sort=${list.sort}${filter}`);
    await expect(page.getByTestId(`${list.prefix}-search`)).toHaveValue('base');
    if (list.path === 'processes') await expect(page.getByTestId('md-process-filter').locator('option:checked')).toContainText('ASM');
    else if ('filter' in list) await expect(page.getByTestId('md-fund-filter')).toHaveValue(list.filter);
    await expect(page.getByTestId(`${list.prefix}-row-${list.code}`)).toBeVisible();
    expect(requests.at(-1)?.url.searchParams.get('keyword')).toBe('base');
    expect(requests.at(-1)?.url.searchParams.get('page')).toBe('2');
    expect(requests.at(-1)?.url.searchParams.get('sort')).toBe(list.sort);
    if ('filter' in list) expect(requests.at(-1)?.url.searchParams.get(list.filterKey)).toBe(list.filter);
    await page.getByTestId(`${list.prefix}-search`).fill('draft');
    await expect(page).toHaveURL(/[?&]keyword=base/);
    await page.getByTestId(`${list.prefix}-search`).press('Enter');
    await expect(page).toHaveURL(/[?&]keyword=draft/);
    await expect(page).not.toHaveURL(/[?&]page=2/);
    await page.goBack();
    await expect(page.getByTestId(`${list.prefix}-search`)).toHaveValue('base');
    await page.goForward();
    await expect(page.getByTestId(`${list.prefix}-search`)).toHaveValue('draft');
    await page.getByTestId('layout-company-select').click();
    await page.locator('nz-option-item', { hasText: 'HK01 Hong Kong' }).click();
    await expect.poll(() => requests.at(-1)?.company).toBe('2');
    expect(requests.at(-1)?.url.searchParams.get('page')).toBe('1');
    expect(requests.at(-1)?.url.searchParams.get('keyword')).toBe('draft');
    if (list.path === 'processes') {
      await expect(page).not.toHaveURL(/[?&]filter=/);
      expect(requests.at(-1)?.url.searchParams.get('categoryId')).toBeNull();
    } else if ('filter' in list) {
      await expect(page).toHaveURL(new RegExp(`[?&]filter=${list.filter}`));
      expect(requests.at(-1)?.url.searchParams.get(list.filterKey)).toBe(list.filter);
    }
  });

  test(`${list.path} 非法 URL 參數回預設`, async ({ page }) => {
    const requests: URL[] = [];
    await page.route('**/api/v1/md/accounts**', route => route.fulfill({ json: [] }));
    await page.route('**/api/v1/md/currencies**', route => route.fulfill({ json: { items: [], page: 1, pageSize: 20, totalCount: 0 } }));
    await page.route('**/api/v1/md/uoms**', route => route.fulfill({ json: { items: [], page: 1, pageSize: 20, totalCount: 0 } }));
    await page.route('**/api/v1/md/process-categories**', route => list.path === 'processes'
      ? route.fulfill({ json: { items: [], page: 1, pageSize: 20, totalCount: 0 } }) : route.fallback());
    await page.route(`**/api/v1/md/${list.path}**`, route => {
      requests.push(new URL(route.request().url()));
      return route.fulfill({ json: { items: [], page: 1, pageSize: 20, totalCount: 0 } });
    });
    for (const query of ['keyword=%20&page=0&pageSize=999&sort=createdAt&filter=invalid', 'page=-1', 'page=9007199254740992', 'page=2147483648']) {
      await page.goto(`/md/${list.path}?${query}`);
      await expect(page.getByTestId(`${list.prefix}-search`)).toHaveValue('');
      await expect(page).toHaveURL(new RegExp(`/md/${list.path}$`));
      expect(requests.at(-1)?.searchParams.get('page')).toBe('1');
      expect(requests.at(-1)?.searchParams.get('pageSize')).toBe('20');
      expect(requests.at(-1)?.searchParams.get('sort')).toBe(list.path.startsWith('process') ? 'sortOrder' : 'code');
    }
  });
}
