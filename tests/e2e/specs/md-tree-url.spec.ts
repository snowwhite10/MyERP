import { expect, test } from '../support/fixtures';

const trees = [
  { path: 'item-categories', prefix: 'md-item-category', api: 'item-categories', row: { id: 1, code: 'ROOT', name: 'Root', parentId: null, isActive: true, version: 1 } },
  { path: 'accounts', prefix: 'md-account', api: 'accounts', row: { id: 1, code: '1000', name: 'Root', parentId: null, accountType: 'Asset', balanceDirection: 'Debit', isActive: true, isPostable: false, version: 1 } },
] as const;

for (const tree of trees) {
  test(`${tree.path} 分享、草稿、返回前進與切公司`, async ({ page }) => {
    const requests: string[] = [];
    const child = { ...tree.row, id: 2, code: tree.path === 'accounts' ? '1001' : 'CHILD', name: 'Needle', parentId: 1 };
    await page.route(`**/api/v1/md/${tree.api}**`, route => {
      requests.push(route.request().headers()['x-company-id'] ?? '');
      return route.fulfill({ json: [tree.row, child] });
    });
    await page.goto(`/md/${tree.path}?keyword=root`);
    await expect(page.getByTestId(`${tree.prefix}-search`)).toHaveValue('root');
    await expect(page.getByTestId(`${tree.prefix}-row-${tree.row.code}`)).toBeVisible();
    await page.getByTestId(`${tree.prefix}-search`).fill('missing');
    await expect(page).toHaveURL(/[?&]keyword=root/);
    await page.getByTestId(`${tree.prefix}-search`).press('Enter');
    await expect(page).toHaveURL(/[?&]keyword=missing/);
    await expect(page.getByTestId(`${tree.prefix}-row-${tree.row.code}`)).toHaveCount(0);
    await page.goBack();
    await expect(page.getByTestId(`${tree.prefix}-search`)).toHaveValue('root');
    await expect(page.getByTestId(`${tree.prefix}-row-${tree.row.code}`)).toBeVisible();
    await page.goForward();
    await expect(page.getByTestId(`${tree.prefix}-search`)).toHaveValue('missing');
    await page.goto(`/md/${tree.path}?keyword=needle`);
    await expect(page.getByTestId(`${tree.prefix}-row-${child.code}`)).toBeVisible();
    await expect(page.getByTestId(`${tree.prefix}-row-${tree.row.code}`)).toBeVisible();
    await page.getByTestId('layout-company-select').click();
    await page.locator('nz-option-item', { hasText: 'HK01 Hong Kong' }).click();
    await expect.poll(() => requests.at(-1)).toBe('2');
    await expect(page).toHaveURL(/[?&]keyword=needle/);
  });

  test(`${tree.path} 空白及不適用 URL 參數回安全預設`, async ({ page }) => {
    await page.route(`**/api/v1/md/${tree.api}**`, route => route.fulfill({ json: [tree.row] }));
    await page.goto(`/md/${tree.path}?keyword=%20%20&page=-1&pageSize=10&sort=-name`);
    await expect(page.getByTestId(`${tree.prefix}-search`)).toHaveValue('');
    await expect(page).toHaveURL(new RegExp(`/md/${tree.path}$`));
    await expect(page.getByTestId(`${tree.prefix}-row-${tree.row.code}`)).toBeVisible();
  });
}
