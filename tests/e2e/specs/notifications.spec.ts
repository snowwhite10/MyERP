import { expect, openHome, test } from '../support/fixtures';

test('notification inbox is scoped to the selected company and supports read actions', async ({ page }) => {
  const read = new Set<number>();
  const messages = [
    { id: 1, category: 'approval', title: 'Review order', body: 'Order 42 needs approval', targetPath: '/sys/approvals', createdAt: '2026-10-11T19:00:00+08:00', readAt: null },
    { id: 2, category: 'system', title: 'System notice', body: 'Check settings', targetPath: null, createdAt: '2026-10-11T18:00:00+08:00', readAt: null },
  ];
  await page.route('**/api/v1/sys/notifications/**', async route => {
    const request = route.request();
    const company = request.headers()['x-company-id'];
    const path = new URL(request.url()).pathname;
    if (path.endsWith('/unread-count')) {
      await route.fulfill({ json: { count: company === '2' ? 0 : 2 - read.size } });
    } else if (path.endsWith('/read-all')) {
      const updatedCount = 2 - read.size;
      read.add(1); read.add(2);
      await route.fulfill({ json: { updatedCount } });
    } else if (path.endsWith('/read')) {
      read.add(Number(path.split('/').at(-2)));
      await route.fulfill({ status: 204 });
    } else {
      await route.fulfill({ status: 404 });
    }
  });
  await page.route('**/api/v1/sys/notifications?*', async route => {
    const company = route.request().headers()['x-company-id'];
    const unreadOnly = new URL(route.request().url()).searchParams.get('unreadOnly') === 'true';
    const items = company === '2' ? [] : messages.map(item => ({ ...item, readAt: read.has(item.id) ? '2026-10-11T19:05:00+08:00' : null }))
      .filter(item => !unreadOnly || item.readAt === null);
    await route.fulfill({ json: { items, page: 1, pageSize: 20, totalCount: items.length } });
  });
  await openHome(page);
  await expect(page.getByTestId('layout-notification-count')).toHaveText('2');
  await page.getByTestId('layout-notifications').click();
  await expect(page.getByText('Review order')).toBeVisible();
  await page.getByTestId('sys-notification-1').getByRole('button', { name: '標記已讀' }).click();
  await expect(page.getByTestId('layout-notification-count')).toHaveText('1');
  await page.getByTestId('sys-notifications-read-all').click();
  await expect(page.getByTestId('layout-notification-count')).toBeHidden();
  await page.getByTestId('layout-company-select').click();
  await page.getByText('HK01 Hong Kong').click();
  await expect(page.getByTestId('sys-notifications-table')).toContainText('沒有通知');
});
