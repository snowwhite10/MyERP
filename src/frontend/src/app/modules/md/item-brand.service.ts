import { Injectable } from '@angular/core';
import { CategoryPage, MasterCategory, MasterCategoryService } from './master-category.service';

export const ITEM_BRAND_API = '/api/v1/md/item-brands';

export type ItemBrand = MasterCategory;

/** 物料品牌與扁平分類規則相同（代碼公司內唯一、軟刪除、版本），直接沿用分類 API 與 `CategoryList`。 */
@Injectable({ providedIn: 'root' })
export class ItemBrandService extends MasterCategoryService {
  constructor() { super(ITEM_BRAND_API, 'MD.ITEM_BRAND'); }
}

export const ITEM_BRAND_PAGE: CategoryPage = {
  api: ItemBrandService, permission: 'MD.ITEM_BRAND', i18n: 'md.itemBrand',
};
