import { Injectable } from '@angular/core';
import { CategoryPage, MasterCategoryService } from './master-category.service';

@Injectable({ providedIn: 'root' })
export class SettlementMethodService extends MasterCategoryService {
  constructor() { super('/api/v1/md/settlement-methods', 'MD.SETTLEMENT_METHOD'); }
}

export const SETTLEMENT_METHOD_PAGE: CategoryPage = {
  api: SettlementMethodService, permission: 'MD.SETTLEMENT_METHOD', i18n: 'md.settlementMethod',
};
