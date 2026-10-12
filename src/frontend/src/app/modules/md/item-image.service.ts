import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { apiErrorKey } from '../../core/http/api-error';
import { IMAGE_TYPES } from '../../shared/images/image-data-url';
import { ITEM_API } from './item.service';

/** 與後端相同的限制（ADR-045）；用戶端只作即時提示，伺服器以檔案內容作最終檢查。 */
export const ITEM_IMAGE_MAX_BYTES = 2 * 1024 * 1024;
export const MAX_IMAGES_PER_ITEM = 10;

/** 後端 `ItemImageView`（不含內容）；主圖排第一，其餘按排序號。 */
export interface ItemImage {
  id: number; itemId: number; contentType: string; fileName: string; sizeBytes: number;
  width: number; height: number; sortOrder: number; isPrimary: boolean; version: number;
}

/** 選檔後的用戶端檢查：回傳錯誤 i18n key，合法回傳 null。 */
export function itemImageFileError(file: File): string | null {
  if (!IMAGE_TYPES.includes(file.type)) return 'errors.MD.ITEM_IMAGE.UNSUPPORTED_TYPE';
  if (file.size === 0) return 'errors.COMMON.REQUEST.VALIDATION_FAILED';
  return file.size > ITEM_IMAGE_MAX_BYTES ? 'errors.MD.ITEM_IMAGE.TOO_LARGE' : null;
}

const images = (itemId: number): string => `${ITEM_API}/${itemId}/images`;

/** 物料圖片（介面 8.15，REQ_F4118.44.1–4）：查看需 `MD.ITEM.VIEW`，上傳／設定／刪除需 `MD.ITEM.EDIT`。 */
@Injectable({ providedIn: 'root' })
export class ItemImageService {
  private readonly http = inject(HttpClient);
  errorKey(error: unknown): string {
    return apiErrorKey(error, ['MD.ITEM.NOT_FOUND', 'MD.ITEM_IMAGE.NOT_FOUND', 'MD.ITEM_IMAGE.UNSUPPORTED_TYPE',
      'MD.ITEM_IMAGE.TOO_LARGE', 'MD.ITEM_IMAGE.INVALID_DIMENSIONS', 'MD.ITEM_IMAGE.LIMIT_REACHED',
      'COMMON.CONCURRENCY.CONFLICT', 'COMMON.PERMISSION.DENIED']);
  }
  list(itemId: number): Observable<ItemImage[]> { return this.http.get<ItemImage[]>(images(itemId)); }
  content(itemId: number, id: number): Observable<Blob> {
    return this.http.get(`${images(itemId)}/${id}/content`, { responseType: 'blob' });
  }
  /** 未指定排序號時由後端排在最後。 */
  upload(itemId: number, file: File, isPrimary: boolean): Observable<ItemImage> {
    const form = new FormData();
    form.append('file', file, file.name);
    form.append('isPrimary', String(isPrimary));
    return this.http.post<ItemImage>(images(itemId), form);
  }
  update(itemId: number, id: number, body: { sortOrder: number; isPrimary: boolean; version: number }): Observable<ItemImage> {
    return this.http.put<ItemImage>(`${images(itemId)}/${id}`, body);
  }
  remove(itemId: number, id: number, version: number): Observable<void> {
    return this.http.delete<void>(`${images(itemId)}/${id}`, { params: new HttpParams().set('version', version) });
  }
}
