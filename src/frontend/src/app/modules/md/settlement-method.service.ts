import { Injectable } from '@angular/core';
import { CategoryPage, MasterCategoryService } from './master-category.service';

export const SETTLEMENT_METHOD_API = '/api/v1/md/settlement-methods';

@Injectable({ providedIn: 'root' })
export class SettlementMethodService extends MasterCategoryService {
  constructor() { super(SETTLEMENT_METHOD_API, 'MD.SETTLEMENT_METHOD'); }
}

export const SETTLEMENT_METHOD_PAGE: CategoryPage = {
  api: SettlementMethodService, permission: 'MD.SETTLEMENT_METHOD', i18n: 'md.settlementMethod',
};
