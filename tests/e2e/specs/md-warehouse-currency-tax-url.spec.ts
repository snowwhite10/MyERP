import { expect, test } from '../support/fixtures';

const lists = [
  { path: 'warehouses', prefix: 'md-warehouse', code: 'MAIN', row: { id: 1, code: 'MAIN', name: 'Main', isActive: true, version: 1 } },
  { path: 'currencies', prefix: 'md-currency', code: 'USD', row: { id: 1, code: 'USD', name: 'Dollar', symbol: '$', decimalPlaces: 2, isBase: false, isActive: true, version: 1 } },
  { path: 'tax-codes', prefix: 'md-tax-code', code: 'VAT5', row: { id: 1, code: 'VAT5', name: 'VAT', taxType: 'Both', currentRatePercent: 5, isActive: true, version: 1 } },
] as const;

for (const list of lists) {
  test(`${list.path} 分享網址、搜尋草稿、返回前進與切公司`, async ({ page }) => {
    const requests: { company: string; keyword: string | null; page: string | null; pageSize: string | null; sort: string | null }[] = [];
    await page.route(`**/api/v1/md/${list.path}**`, async route => {
      const url = new URL(route.request().url());
      requests.push({ company: route.request().headers()['x-company-id'] ?? '', keyword: url.searchParams.get('keyword'),
        page: url.searchParams.get('page'), pageSize: url.searchParams.get('pageSize'), sort: url.searchParams.get('sort') });
      await route.fulfill({ json: { items: [list.row], page: Number(url.searchParams.get('page') ?? 1),
        pageSize: Number(url.searchParams.get('pageSize') ?? 20), totalCount: 100 } });
    });
    await page.goto(`/md/${list.path}?keyword=base&page=2&pageSize=10&sort=-name`);
    await expect(page.getByTestId(`${list.prefix}-search`)).toHaveValue('base');
    await expect(page.getByTestId(`${list.prefix}-row-${list.code}`)).toBeVisible();
    expect(requests.at(-1)).toMatchObject({ company: '1', keyword: 'base', page: '2', pageSize: '10', sort: '-name' });
    await page.getByTestId(`${list.prefix}-search`).fill('draft');
    await expect(page).toHaveURL(/[?&]keyword=base/);
    await page.getByTestId(`${list.prefix}-search`).press('Enter');
    await expect(page).toHaveURL(/[?&]keyword=draft/);
    await expect(page).not.toHaveURL(/[?&]page=2/);
    await expect.poll(() => requests.at(-1)?.keyword).toBe('draft');
    await page.goBack();
    await expect(page.getByTestId(`${list.prefix}-search`)).toHaveValue('base');
    await expect(page).toHaveURL(/[?&]page=2/);
    await expect.poll(() => requests.at(-1)?.keyword).toBe('base');
    await page.goForward();
    await expect(page.getByTestId(`${list.prefix}-search`)).toHaveValue('draft');
    await page.getByTestId('layout-company-select').click();
    await page.locator('nz-option-item', { hasText: 'HK01 Hong Kong' }).click();
    await expect(page).not.toHaveURL(/[?&]page=2/);
    await expect.poll(() => requests.at(-1)?.company).toBe('2');
    expect(requests.at(-1)).toMatchObject({ keyword: 'draft', page: '1', pageSize: '10', sort: '-name' });
  });

  test(`${list.path} 非法網址參數回安全預設`, async ({ page }) => {
    const requests: URL[] = [];
    await page.route(`**/api/v1/md/${list.path}**`, async route => {
      requests.push(new URL(route.request().url()));
      await route.fulfill({ json: { items: [], page: 1, pageSize: 20, totalCount: 0 } });
    });
    for (const query of ['keyword=%20%20&page=0&pageSize=999&sort=createdAt', 'page=-1', 'page=9007199254740992']) {
      await page.goto(`/md/${list.path}?${query}`);
      await expect(page.getByTestId(`${list.prefix}-search`)).toHaveValue('');
      await expect(page).toHaveURL(new RegExp(`/md/${list.path}$`));
      expect(requests.at(-1)?.searchParams.get('page')).toBe('1');
      expect(requests.at(-1)?.searchParams.get('pageSize')).toBe('20');
      expect(requests.at(-1)?.searchParams.get('sort')).toBe('code');
    }
  });
}
