import { expect, openHome, test } from '../support/fixtures';

// REQ_F4118.25.7：進入主框架先載入權限與可存取公司；切換公司以新公司重新載入權限（API 以 page.route 模擬，ADR-013）
test.describe('權限與公司切換', () => {
  test('載入可存取公司並預設選取預設公司', async ({ page, authorizationApi }) => {
    await openHome(page);

    await expect(page.getByTestId('layout-company-select')).toContainText('TW01 Taipei');
    expect(authorizationApi.length).toBeGreaterThanOrEqual(1);
  });

  test('切換公司後帶新的 X-Company-Id 重新載入權限，重新整理後保留', async ({ page, authorizationApi }) => {
    await openHome(page);
    await expect(page.getByTestId('layout-company-select')).toContainText('TW01 Taipei');

    await page.getByTestId('layout-company-select').click();
    await page.locator('nz-option-item', { hasText: 'HK01 Hong Kong' }).click();

    await expect(page.getByTestId('layout-company-select')).toContainText('HK01 Hong Kong');
    await expect.poll(() => authorizationApi.at(-1)).toBe('2');

    await page.reload();
    await expect(page.getByTestId('home-title')).toBeVisible();
    await expect(page.getByTestId('layout-company-select')).toContainText('HK01 Hong Kong');
  });

  test.describe('沒有任何公司', () => {
    test.use({ authorization: { companies: [], companyId: null as unknown as number, isFullAccess: false, permissions: [] } });

    test('仍可進入首頁，公司選單停用', async ({ page }) => {
      await openHome(page);
      await expect(page.locator('[data-testid="layout-company-select"].ant-select-disabled')).toBeVisible();
    });
  });
});
