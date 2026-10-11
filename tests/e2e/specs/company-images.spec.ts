import { Page } from '@playwright/test';
import { chooseLanguage, expect, openHome, test } from '../support/fixtures';

/** 1×1 的有效 PNG：確認 data: URL 在前端 CSP（img-src 'self' data:）下可以顯示。 */
const PNG = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==', 'base64');

interface Image { kind: 'Logo' | 'Stamp'; contentType: string; fileName: string; sizeBytes: number; width: number; height: number;
  version: number; updatedAt: string; }

/** 模擬 Logo／印章 API（ADR-028）；記錄每次上傳的種類與 version、每次刪除的 URL。 */
async function mockApi(page: Page, images: Image[]) {
  const uploads: { kind: string; version: string; fileName: string }[] = [];
  const deletes: string[] = [];
  await page.route('**/api/v1/sys/company-images**', async route => {
    const req = route.request();
    const url = new URL(req.url());
    const [, kind, content] = /company-images(?:\/(\w+))?(\/content)?$/.exec(url.pathname) ?? [];
    const find = () => images.find(i => i.kind.toLowerCase() === kind);
    if (req.method() === 'GET' && !kind) {
      await route.fulfill({ json: images });
    } else if (req.method() === 'GET' && content) {
      await (find() ? route.fulfill({ body: PNG, contentType: 'image/png' }) : route.fulfill({ status: 404, json: {} }));
    } else if (req.method() === 'PUT') {
      const body = req.postDataBuffer()!.toString('latin1');
      const version = /name="version"\r\n\r\n(\d+)/.exec(body)![1];
      const fileName = /name="file"; filename="([^"]+)"/.exec(body)![1];
      uploads.push({ kind: kind!, version, fileName });
      const current = find();
      const saved: Image = { kind: kind === 'logo' ? 'Logo' : 'Stamp', contentType: 'image/png', fileName, sizeBytes: PNG.length,
        width: 120, height: 120, version: (current?.version ?? 0) + 1, updatedAt: '2026-10-11T16:40:00' };
      images.splice(0, images.length, ...images.filter(i => i !== current), saved);
      await route.fulfill({ json: saved });
    } else {
      deletes.push(`${url.pathname}${url.search}`);
      images.splice(0, images.length, ...images.filter(i => i !== find()));
      await route.fulfill({ status: 204 });
    }
  });
  return { uploads, deletes };
}

test('SYS-IMG-008 Logo 與印章：預覽、上傳新印章、拒絕非圖片檔、確認後刪除 Logo、三語', async ({ page }) => {
  const api = await mockApi(page, [{ kind: 'Logo', contentType: 'image/png', fileName: 'logo.png', sizeBytes: PNG.length, width: 1, height: 1,
    version: 1, updatedAt: '2026-10-11T09:15:30.5' }]);
  await openHome(page);
  await page.getByTestId('layout-menu-sys').click();
  await page.getByTestId('layout-menu-sys-logo-stamp').click();
  await expect(page.getByRole('heading', { name: 'Logo 與印章' })).toBeVisible();

  const logo = page.getByTestId('image-preview-Logo');
  await expect(logo).toHaveAttribute('src', /^data:image\/png;base64,/);
  await expect.poll(() => logo.evaluate(img => (img as HTMLImageElement).naturalWidth)).toBe(1);
  await expect(page.getByTestId('image-meta-Logo')).toContainText('logo.png');
  await expect(page.getByTestId('image-meta-Logo')).toContainText('2026-10-11 09:15');
  await expect(page.getByTestId('image-empty-Stamp')).toHaveText('尚未上傳');

  // 用戶端先擋下非 PNG／JPEG 檔案，不送出請求
  await page.getByTestId('image-file-Stamp').setInputFiles({ name: 'stamp.gif', mimeType: 'image/gif', buffer: Buffer.from('GIF89a') });
  await expect(page.getByTestId('image-message-Stamp')).toContainText('只接受 PNG 和 JPEG 圖片');
  expect(api.uploads).toEqual([]);

  await page.getByTestId('image-file-Stamp').setInputFiles({ name: '公司印章.png', mimeType: 'image/png', buffer: PNG });
  await expect(page.getByTestId('image-message-Stamp')).toContainText('圖片已保存。');
  await expect(page.getByTestId('image-preview-Stamp')).toBeVisible();
  await expect(page.getByTestId('image-upload-Stamp')).toHaveText('替換');
  expect(api.uploads).toEqual([{ kind: 'stamp', version: '0', fileName: expect.any(String) }]);

  await page.getByTestId('image-remove-Logo').click();
  await expect(page.getByTestId('erp-message-title')).toHaveText('刪除公司 Logo？');
  await page.getByTestId('erp-message-ok').click();
  await expect(page.getByTestId('image-empty-Logo')).toHaveText('尚未上傳');
  await expect(page.getByTestId('image-message-Logo')).toContainText('圖片已刪除。');
  expect(api.deletes).toEqual(['/api/v1/sys/company-images/logo?version=1']);

  await chooseLanguage(page, 'en');
  await expect(page.getByRole('heading', { name: 'Logo & stamp' })).toBeVisible();
  await expect(page.getByTestId('image-card-Stamp')).toContainText('Company stamp');
  await chooseLanguage(page, 'zh-Hans');
  await expect(page.getByTestId('image-hint')).toContainText('1 MB 以内');
});
