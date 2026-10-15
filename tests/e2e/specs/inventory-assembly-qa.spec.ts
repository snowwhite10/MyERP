import { chooseLanguage, chooseTheme, expect, test } from '../support/fixtures';

const OUT = '../../tests/qa/INV/screenshots';
const row = { id: 7, documentNo: 'ASM-0007', businessDate: '2026-10-16', status: 'Draft',
  finishedItemId: 1, finishedItemCode: 'KIT', finishedItemName: 'Finished kit', finishedQuantity: 2, componentCount: 1, version: 1 };
const detail = { ...row, remark: 'Kit assembly', postedAt: null, createdAt: '2026-10-16T01:00:00', approval: null,
  lines: [{ id: 1, lineNo: 1, role: 'Finished', itemId: 1, warehouseId: 2, locationId: null, quantity: 2,
    postedUnitCost: null, tracking: [] }, { id: 2, lineNo: 2, role: 'Component', itemId: 3, warehouseId: 2,
    locationId: null, quantity: 4, postedUnitCost: null, tracking: [] }] };
const refs = { items: [{ id: 1, code: 'KIT', name: 'Finished kit', isActive: false, trackingMode: 'None' },
  { id: 3, code: 'PART', name: 'Component', isActive: true, trackingMode: 'None' }],
  warehouses: [{ id: 2, code: 'WH', name: 'Main', isActive: true }], locations: [] };

for (const theme of ['light', 'dark'] as const) {
  test(`5.9/5.10/5.11 assembly QA ${theme}`, async ({ page, pageErrors }) => {
    pageErrors.allow(/Failed to load resource: the server responded with a status of (409|422|500)/);
    pageErrors.allowApi5xx(/\/api\/v1\/inv\/assemblies(?:\?.*)?$/, 500);
    let listState: 'normal' | 'empty' | 'filtered' | 'server' = 'normal';
    let submitStatus = 0;
    let editStatus = 0;
    await page.route('**/api/v1/inv/assemblies**', route => {
      const path = new URL(route.request().url()).pathname;
      if (path.endsWith('/saved-references')) return route.fulfill({ json: refs });
      if (path.endsWith('/submit') && submitStatus) return route.fulfill({ status: submitStatus,
        json: { errorCode: 'INV.SERIAL.NOT_AVAILABLE', traceId: '00-qa-assembly-01', errors: {
          'components[0].tracking[0].serialNo': ['序號不在來源倉庫。'],
        } } });
      if (route.request().method() === 'PUT' && editStatus) return route.fulfill({ status: editStatus,
        json: { errorCode: 'COMMON.CONCURRENCY.CONFLICT' } });
      if (path.endsWith('/7')) return route.fulfill({ json: detail });
      if (listState === 'server') return route.fulfill({ status: 500,
        json: { errorCode: 'COMMON.SERVER.ERROR', traceId: '00-qa-assembly-01' } });
      return route.fulfill({ json: { items: listState === 'normal' ? [row] : [], totalCount: listState === 'normal' ? 1 : 0 } });
    });
    await page.goto('/inv/assemblies');
    if (theme === 'dark') await chooseTheme(page, 'dark');
    await expect(page.getByTestId('inv-assembly-row-7')).toBeVisible();
    await page.screenshot({ path: `${OUT}/5.9-normal-${theme}-zh-Hant.png`, fullPage: true });
    await page.getByTestId('inv-assembly-row-7').getByRole('button', { name: 'ASM-0007' }).click();
    await expect(page.getByTestId('inv-assembly-detail')).toContainText('Finished kit');
    await page.screenshot({ path: `${OUT}/5.9-detail-${theme}-zh-Hant.png`, fullPage: true });
    await page.getByTestId('inv-assembly-detail').getByRole('button', { name: '關閉' }).click();
    listState = 'empty'; await page.getByTestId('inv-assembly-filter').click();
    await expect(page.getByTestId('inv-assembly-table')).toContainText('沒有組裝單');
    await page.screenshot({ path: `${OUT}/5.9-empty-${theme}-zh-Hant.png`, fullPage: true });
    listState = 'filtered'; await page.getByTestId('inv-assembly-search').fill('missing');
    await page.getByTestId('inv-assembly-filter').click();
    await expect(page.getByTestId('inv-assembly-table')).toContainText('沒有符合篩選');
    await page.screenshot({ path: `${OUT}/5.9-no-results-${theme}-zh-Hant.png`, fullPage: true });
    listState = 'normal'; await page.getByTestId('inv-assembly-search').fill(''); await page.getByTestId('inv-assembly-filter').click();
    await expect(page.getByTestId('inv-assembly-row-7')).toBeVisible();
    submitStatus = 422; await page.getByTestId('inv-assembly-row-7').getByRole('button', { name: '送審' }).click();
    await expect(page.getByTestId('inv-assembly-error')).toContainText('components[0].tracking[0].serialNo: 序號不在來源倉庫。');
    await page.screenshot({ path: `${OUT}/5.9-error422-${theme}-zh-Hant.png`, fullPage: true });
    listState = 'server'; await page.getByTestId('inv-assembly-filter').click();
    await expect(page.getByTestId('inv-assembly-error')).toContainText('00-qa-assembly-01');
    await expect(page.getByTestId('inv-assembly-table')).toContainText('載入失敗');
    await page.screenshot({ path: `${OUT}/5.9-error500-${theme}-zh-Hant.png`, fullPage: true });
    await page.goto('/inv/assemblies/new');
    await expect(page.getByTestId('inv-assembly-save')).toBeVisible();
    await page.screenshot({ path: `${OUT}/5.10-normal-${theme}-zh-Hant.png`, fullPage: true });
    await page.getByTestId('inv-assembly-save').click();
    await expect(page.getByTestId('inv-assembly-error')).toBeVisible();
    await page.screenshot({ path: `${OUT}/5.10-validation-${theme}-zh-Hant.png`, fullPage: true });
    await page.goto('/inv/assemblies/7/edit');
    await expect(page.getByTestId('inv-assembly-remark')).toHaveValue('Kit assembly');
    await page.screenshot({ path: `${OUT}/5.11-normal-${theme}-zh-Hant.png`, fullPage: true });
    editStatus = 409; await page.getByTestId('inv-assembly-save').click();
    await expect(page.getByTestId('inv-assembly-error')).toBeVisible();
    await page.screenshot({ path: `${OUT}/5.11-error409-${theme}-zh-Hant.png`, fullPage: true });
    if (theme === 'light') {
      editStatus = 0; await page.goto('/inv/assemblies/7/edit');
      await expect(page.getByTestId('inv-assembly-remark')).toHaveValue('Kit assembly');
      await chooseLanguage(page, 'en'); await page.screenshot({ path: `${OUT}/5.11-normal-light-en.png`, fullPage: true });
      await page.goto('/inv/assemblies/new'); await expect(page.getByRole('heading', { level: 1 })).toHaveText('New assembly');
      await page.screenshot({ path: `${OUT}/5.10-normal-light-en.png`, fullPage: true });
      await chooseLanguage(page, 'zh-Hans'); await expect(page.getByRole('heading', { level: 1 })).toHaveText('新增组装单');
      await page.screenshot({ path: `${OUT}/5.10-normal-light-zh-Hans.png`, fullPage: true });
      listState = 'normal'; await page.goto('/inv/assemblies');
      await expect(page.getByTestId('inv-assembly-row-7')).toBeVisible();
      await page.screenshot({ path: `${OUT}/5.9-normal-light-zh-Hans.png`, fullPage: true });
      await chooseLanguage(page, 'en'); await page.screenshot({ path: `${OUT}/5.9-normal-light-en.png`, fullPage: true });
      await chooseLanguage(page, 'zh-Hans'); await page.goto('/inv/assemblies/7/edit');
      await expect(page.getByTestId('inv-assembly-remark')).toHaveValue('Kit assembly');
      await page.screenshot({ path: `${OUT}/5.11-normal-light-zh-Hans.png`, fullPage: true });
    }
  });
}
