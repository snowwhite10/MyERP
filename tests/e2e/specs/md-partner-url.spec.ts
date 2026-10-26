import { expect, test } from '../support/fixtures';

for (const resource of ['customers', 'suppliers'] as const) {
  const prefix = resource === 'customers' ? 'md-customer' : 'md-supplier';
  test(`${resource} 分享網址、搜尋草稿、返回與切公司`, async ({ page }) => {
    const requests: { company: string; keyword: string | null; page: string | null; pageSize: string | null; sort: string | null }[] = [];
    await page.route(`**/api/v1/md/${resource === 'customers' ? 'customer' : 'supplier'}-categories**`, route =>
      route.fulfill({ json: { items: [], page: 1, pageSize: 100, totalCount: 0 } }));
    await page.route(`**/api/v1/md/${resource}**`, async (route) => {
      const url = new URL(route.request().url());
      requests.push({ company: route.request().headers()['x-company-id'] ?? '', keyword: url.searchParams.get('keyword'),
        page: url.searchParams.get('page'), pageSize: url.searchParams.get('pageSize'), sort: url.searchParams.get('sort') });
      await route.fulfill({ json: { items: [{ id: 1, code: 'BASE', name: 'Base', isActive: true, version: 1, reviewStatus: 'Approved' }],
        page: Number(url.searchParams.get('page') ?? 1), pageSize: Number(url.searchParams.get('pageSize') ?? 20), totalCount: 100 } });
    });
    await page.goto(`/md/${resource}?keyword=base&page=2&pageSize=10&sort=-name`);
    await expect(page.getByTestId(`${prefix}-search`)).toHaveValue('base');
    await expect(page.getByTestId(`${prefix}-row-BASE`)).toBeVisible();
    expect(requests.at(-1)).toMatchObject({ company: '1', keyword: 'base', page: '2', pageSize: '10', sort: '-name' });
    await page.getByTestId(`${prefix}-search`).fill('draft');
    await expect(page).toHaveURL(/[?&]keyword=base/);
    await page.getByTestId(`${prefix}-search`).press('Enter');
    await expect(page).toHaveURL(/[?&]keyword=draft/);
    await expect(page).not.toHaveURL(/[?&]page=2/);
    await expect.poll(() => requests.at(-1)?.keyword).toBe('draft');
    await page.goBack();
    await expect(page.getByTestId(`${prefix}-search`)).toHaveValue('base');
    await expect(page).toHaveURL(/[?&]page=2/);
    await expect.poll(() => requests.at(-1)?.keyword).toBe('base');
    await page.getByTestId('layout-company-select').click();
    await page.locator('nz-option-item', { hasText: 'HK01 Hong Kong' }).click();
    await expect(page).not.toHaveURL(/[?&]page=2/);
    await expect(page.getByTestId(`${prefix}-search`)).toHaveValue('base');
    await expect.poll(() => requests.at(-1)?.company).toBe('2');
    expect(requests.at(-1)).toMatchObject({ keyword: 'base', page: '1' });
  });

  test(`${resource} 非法網址參數回退安全預設`, async ({ page }) => {
    const requests: URL[] = [];
    await page.route(`**/api/v1/md/${resource === 'customers' ? 'customer' : 'supplier'}-categories**`, route =>
      route.fulfill({ json: { items: [], page: 1, pageSize: 100, totalCount: 0 } }));
    await page.route(`**/api/v1/md/${resource}**`, async (route) => {
      requests.push(new URL(route.request().url()));
      await route.fulfill({ json: { items: [], page: 1, pageSize: 20, totalCount: 0 } });
    });
    await page.goto(`/md/${resource}?keyword=%20%20&page=0&pageSize=999&sort=createdAt`);
    await expect(page.getByTestId(`${prefix}-search`)).toHaveValue('');
    await expect(page).toHaveURL(new RegExp(`/md/${resource}$`));
    expect(requests.at(-1)?.searchParams.get('page')).toBe('1');
    expect(requests.at(-1)?.searchParams.get('pageSize')).toBe('20');
    expect(requests.at(-1)?.searchParams.get('sort')).toBe('code');
    for (const invalidPage of ['-1', '9007199254740992']) {
      await page.goto(`/md/${resource}?page=${invalidPage}`);
      await expect(page).toHaveURL(new RegExp(`/md/${resource}$`));
      expect(requests.at(-1)?.searchParams.get('page')).toBe('1');
    }
  });
}
