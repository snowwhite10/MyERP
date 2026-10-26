import { expect, test } from '../support/fixtures';

test('客戶分類分享網址還原查詢，返回導覽與切公司保留條件並重設頁碼', async ({ page }) => {
  const requests: { company: string; keyword: string | null; page: string | null; pageSize: string | null; sort: string | null }[] = [];
  await page.route('**/api/v1/md/customer-categories**', async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    requests.push({ company: request.headers()['x-company-id'] ?? '', keyword: url.searchParams.get('keyword'),
      page: url.searchParams.get('page'), pageSize: url.searchParams.get('pageSize'), sort: url.searchParams.get('sort') });
    await route.fulfill({ json: { items: [{ id: 1, code: 'RETAIL', name: 'Retail', isActive: true, version: 1 }],
      page: Number(url.searchParams.get('page') ?? 1), pageSize: Number(url.searchParams.get('pageSize') ?? 20), totalCount: 100 } });
  });

  await page.goto('/md/customer-categories?keyword=retail&page=2&pageSize=10&sort=-name');
  await expect(page.getByTestId('md-category-search')).toHaveValue('retail');
  await expect(page.getByTestId('md-category-row-RETAIL')).toBeVisible();
  expect(requests.at(-1)).toMatchObject({ company: '1', keyword: 'retail', page: '2', pageSize: '10', sort: '-name' });

  await page.getByTestId('md-category-search').fill('wholesale');
  await page.getByTestId('md-category-search').press('Enter');
  await expect(page).toHaveURL(/keyword=wholesale/);
  await expect(page).not.toHaveURL(/[?&]page=2/);
  expect(requests.at(-1)).toMatchObject({ keyword: 'wholesale', page: '1' });

  await page.goBack();
  await expect(page.getByTestId('md-category-search')).toHaveValue('retail');
  await expect(page).toHaveURL(/[?&]page=2/);
  await expect.poll(() => requests.at(-1)?.keyword).toBe('retail');

  await page.getByTestId('layout-company-select').click();
  await page.locator('nz-option-item', { hasText: 'HK01 Hong Kong' }).click();
  await expect(page).not.toHaveURL(/[?&]page=2/);
  await expect(page.getByTestId('md-category-search')).toHaveValue('retail');
  await expect.poll(() => requests.at(-1)?.company).toBe('2');
  expect(requests.at(-1)).toMatchObject({ keyword: 'retail', page: '1' });
});

test('客戶分類非法網址參數回到安全預設值', async ({ page }) => {
  const requests: URL[] = [];
  await page.route('**/api/v1/md/customer-categories**', async (route) => {
    requests.push(new URL(route.request().url()));
    await route.fulfill({ json: { items: [], page: 1, pageSize: 20, totalCount: 0 } });
  });
  await page.goto('/md/customer-categories?keyword=%20%20&page=-2&pageSize=999&sort=createdAt');
  await expect(page.getByTestId('md-category-search')).toHaveValue('');
  await expect(page).toHaveURL(/\/md\/customer-categories$/);
  expect(requests.at(-1)?.searchParams.get('page')).toBe('1');
  expect(requests.at(-1)?.searchParams.get('pageSize')).toBe('20');
  expect(requests.at(-1)?.searchParams.get('sort')).toBe('code');
});
