import { DestroyRef, inject } from '@angular/core';
import { Observable, Observer, Subscription } from 'rxjs';

/**
 * 只保留最新一次請求：開始新請求時取消上一個（HTTP 請求一併中止），
 * 快速翻頁、排序或切換公司時，較慢的舊回應不會覆蓋新資料（SYS-F02）。元件銷毀時取消未完成的請求。
 */
export class LatestRequest {
  private current: Subscription | null = null;

  run<T>(source: Observable<T>, observer: Partial<Observer<T>>): void {
    this.cancel();
    this.current = source.subscribe(observer);
  }

  cancel(): void {
    this.current?.unsubscribe();
    this.current = null;
  }
}

/** 在注入環境中建立 {@link LatestRequest}，並在元件銷毀時自動取消。 */
export function latestRequest(): LatestRequest {
  const request = new LatestRequest();
  inject(DestroyRef).onDestroy(() => request.cancel());
  return request;
}
