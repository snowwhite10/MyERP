import { expect, Page } from '@playwright/test';

export async function mockInventoryOptions(page: Page, options: {
  items: { id: number; code: string; name: string; trackingMode?: string }[];
  warehouses: { id: number; code: string; name: string }[];
  locations?: { id: number; code: string; name: string }[];
}): Promise<void> {
  await page.route('**/api/v1/inv/selection-options/**', route => {
    const path = new URL(route.request().url()).pathname;
    const rows = path.endsWith('/items') ? options.items : path.endsWith('/locations') ? options.locations ?? [] : options.warehouses;
    return route.fulfill({ json: { items: rows, totalCount: rows.length } });
  });
}

export async function pickInventoryOption(page: Page, line: ReturnType<Page['locator']>, field: string, id: number): Promise<void> {
  await line.getByRole('button', { name: field }).click();
  await expect(page.getByTestId('erp-select-dialog')).toBeVisible();
  await page.getByTestId(`erp-select-row-${id}`).click();
  await page.getByTestId('erp-select-confirm').click();
}
