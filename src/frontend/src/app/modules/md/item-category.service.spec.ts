import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import {
  categorySubtreeIds, filterCategoryRows, flattenCategoryTree, ItemCategory, ItemCategoryService, parentOptions,
} from './item-category.service';

const node = (id: number, code: string, parentId: number | null, isActive = true): ItemCategory =>
  ({ id, code, name: `Name ${code}`, parentId, isActive, version: 1 });

describe('MD item category API', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] }));

  it('loads the whole tree and sends normalized, versioned writes', async () => {
    const api = TestBed.inject(ItemCategoryService);
    const http = TestBed.inject(HttpTestingController);
    const list = firstValueFrom(api.list());
    http.expectOne('/api/v1/md/item-categories').flush([node(1, 'RAW', null)]);
    expect((await list).map((x) => x.code)).toEqual(['RAW']);

    const created = firstValueFrom(api.create({ code: ' steel ', name: ' Steel ', parentId: 1 }));
    const post = http.expectOne('/api/v1/md/item-categories');
    expect(post.request.body).toEqual({ code: 'STEEL', name: 'Steel', parentId: 1 });
    post.flush(node(2, 'STEEL', 1));
    await created;

    const updated = firstValueFrom(api.update(2, { name: ' Steel bar ', isActive: false, parentId: null, version: 1 }));
    const put = http.expectOne('/api/v1/md/item-categories/2');
    expect(put.request.body).toEqual({ name: 'Steel bar', isActive: false, parentId: null, version: 1 });
    put.flush(node(2, 'STEEL', null));
    await updated;

    const removed = firstValueFrom(api.remove(2, 2));
    http.expectOne('/api/v1/md/item-categories/2?version=2').flush(null);
    await removed;
    http.verify();
  });

  it('maps tree rule errors and ignores other modules', () => {
    const api = TestBed.inject(ItemCategoryService);
    const problem = (errorCode: string) => new HttpErrorResponse({ status: 422, error: { errorCode } });
    for (const code of ['INVALID_PARENT', 'CYCLE', 'TOO_DEEP', 'HAS_CHILDREN', 'DUPLICATE_CODE']) {
      expect(api.errorKey(problem(`MD.ITEM_CATEGORY.${code}`))).toBe(`errors.MD.ITEM_CATEGORY.${code}`);
    }
    expect(api.errorKey(problem('MD.ITEM_BRAND.DUPLICATE_CODE'))).toBe('errors.http.unknown');
  });
});

describe('MD item category tree helpers', () => {
  // RAW ─ METAL ─ STEEL ─ BAR ; RAW ─ WOOD(停用) ; PACK ; ORPHAN 的上層不存在。
  const items = [node(5, 'STEEL', 2), node(1, 'RAW', null), node(4, 'PACK', null), node(2, 'METAL', 1),
    node(3, 'WOOD', 1, false), node(6, 'BAR', 5), node(7, 'ORPHAN', 99)];

  it('flattens parents before children, sorted by code, with level and height', () => {
    const rows = flattenCategoryTree(items);
    expect(rows.map((x) => `${x.code}:${x.level}:${x.height}`))
      .toEqual(['ORPHAN:0:1', 'PACK:0:1', 'RAW:0:4', 'METAL:1:3', 'STEEL:2:2', 'BAR:3:1', 'WOOD:1:1']);
  });

  it('filters by code or name and keeps ancestors of matches', () => {
    const rows = flattenCategoryTree(items);
    expect(filterCategoryRows(rows, ' bar ').map((x) => x.code)).toEqual(['RAW', 'METAL', 'STEEL', 'BAR']);
    expect(filterCategoryRows(rows, 'name pack').map((x) => x.code)).toEqual(['PACK']);
    expect(filterCategoryRows(rows, '').length).toBe(rows.length);
  });

  it('offers only active parents outside the subtree that keep the tree within five levels', () => {
    const rows = flattenCategoryTree(items);
    const byCode = (code: string) => rows.find((x) => x.code === code)!;
    expect([...categorySubtreeIds(rows, 2)].sort()).toEqual([2, 5, 6]);
    expect(parentOptions(rows, null).map((x) => x.code)).toEqual(['ORPHAN', 'PACK', 'RAW', 'METAL', 'STEEL', 'BAR']);
    // METAL 子樹高 3：只能掛在第 1 層，且不可掛到自己的子孫。
    expect(parentOptions(rows, byCode('METAL')).map((x) => x.code)).toEqual(['ORPHAN', 'PACK', 'RAW']);
    // 已停用的 WOOD 只在它本身就是目前上層時保留。
    const underWood = flattenCategoryTree([...items, node(8, 'OAK', 3)]);
    expect(parentOptions(underWood, underWood.find((x) => x.code === 'OAK')!).map((x) => x.code)).toContain('WOOD');
    expect(parentOptions(underWood, null).map((x) => x.code)).not.toContain('WOOD');
  });
});
