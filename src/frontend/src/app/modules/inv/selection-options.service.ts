import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { ErpDialogService } from '../../shared/dialogs/dialog.service';
import { SelectDialogQuery } from '../../shared/dialogs/select-dialog';
import { PagedResult } from '../../shared/list-page/table-query';

export interface InventoryOption { id: number; code: string; name: string; trackingMode?: 'None' | 'Lot' | 'Serial' | null; isActive?: boolean }
export interface InventorySavedReferences {
  items: InventoryOption[];
  warehouses: InventoryOption[];
  locations: InventoryOption[];
}
export type InventoryDocumentKind = 'other-receipts' | 'other-issues' | 'transfers' | 'opening-stocks' | 'stocktakes';
const root = '/api/v1/inv/selection-options';

@Injectable({ providedIn: 'root' })
export class InventorySelectionOptionsService {
  private readonly http = inject(HttpClient);
  private readonly dialogs = inject(ErpDialogService);

  items(query: SelectDialogQuery): Observable<PagedResult<InventoryOption>> { return this.get(`${root}/items`, query); }
  warehouses(query: SelectDialogQuery): Observable<PagedResult<InventoryOption>> { return this.get(`${root}/warehouses`, query); }
  locations(warehouseId: number, query: SelectDialogQuery): Observable<PagedResult<InventoryOption>> {
    return this.get(`${root}/warehouses/${warehouseId}/locations`, query);
  }

  /** 使用已授權單據路由取已保存引用；只供顯示，不加入新增選項。 */
  saved(document: InventoryDocumentKind, id: number): Observable<InventorySavedReferences> {
    if (!Number.isSafeInteger(id) || id <= 0) throw new Error('Saved document ID required');
    return this.http.get<InventorySavedReferences>(`/api/v1/inv/${document}/${id}/saved-references`);
  }

  choose(kind: 'item' | 'warehouse' | 'location', warehouseId?: number): Observable<InventoryOption[] | null> {
    if (kind === 'location' && (!warehouseId || warehouseId <= 0)) throw new Error('Warehouse required for location selection');
    const load = kind === 'item' ? (q: SelectDialogQuery) => this.items(q)
      : kind === 'warehouse' ? (q: SelectDialogQuery) => this.warehouses(q)
        : (q: SelectDialogQuery) => this.locations(warehouseId!, q);
    return this.dialogs.select<InventoryOption>({
      titleKey: `inv.selection.${kind}`,
      columns: [
        { key: 'code', labelKey: 'inv.selection.code', value: row => row.code },
        { key: 'name', labelKey: 'inv.selection.name', value: row => row.name },
      ],
      load, rowKey: row => row.id,
    });
  }

  private get(path: string, query: SelectDialogQuery): Observable<PagedResult<InventoryOption>> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    if (query.keyword.trim()) params = params.set('keyword', query.keyword.trim());
    return this.http.get<PagedResult<InventoryOption>>(path, { params });
  }
}
