import { expect, test } from '../support/fixtures';

test('P0-GUARD-001 catches an unexpected API 5xx even when console noise is allowed', async ({ page, pageErrors }) => {
  test.fail();
  pageErrors.allow(/Failed to load resource: the server responded with a status of 503/);
  await page.route('**/api/v1/guard/unexpected', route => route.fulfill({ status: 503, body: 'unavailable' }));
  await page.goto('/');
  await page.evaluate(() => fetch('/api/v1/guard/unexpected').then(() => undefined));
});

test('P0-GUARD-002 permits an explicitly expected API 5xx', async ({ page, pageErrors }) => {
  pageErrors.allowApi5xx(/\/api\/v1\/guard\/expected$/, 500);
  pageErrors.allow(/Failed to load resource: the server responded with a status of 500/);
  await page.route('**/api/v1/guard/expected', route => route.fulfill({ status: 500, body: 'expected' }));
  await page.goto('/');
  const responseStatus = await page.evaluate(() => fetch('/api/v1/guard/expected').then(response => response.status));
  expect(responseStatus).toBe(500);
});

test('P0-GUARD-003 catches an unhandled Promise rejection', async ({ page }) => {
  test.fail();
  await page.goto('/');
  await page.evaluate(() => { void Promise.reject(new Error('guard rejection probe')); });
  await page.waitForTimeout(100);
});

test('P0-GUARD-004 catches a visible untranslated i18n key', async ({ page }) => {
  test.fail();
  await page.goto('/');
  await page.evaluate(() => {
    const label = document.createElement('span');
    label.textContent = 'inv.receipt.title';
    document.body.append(label);
  });
});

test('P0-GUARD-005 ignores hidden keys and ordinary dotted text', async ({ page }) => {
  await page.goto('/');
  await page.evaluate(() => {
    const hidden = document.createElement('span');
    hidden.style.display = 'none';
    hidden.textContent = 'inv.receipt.title';
    document.body.append(hidden);
    const visible = document.createElement('span');
    visible.textContent = 'Version 1.2.3';
    document.body.append(visible);
  });
});
