import { Injectable } from '@angular/core';

/** localStorage key 一律加前綴，避免與同網域其他應用衝突。 */
export const PREFERENCE_KEY_PREFIX = 'myerp.';

/**
 * 用戶偏好（語言、主題、公司）的本機儲存。
 * localStorage 不可用（隱私模式、配額滿）時靜默退回記憶體，不影響使用。
 * WU-SYS-011b 的個人設定在此瀏覽器保存；跨裝置同步須另設計後端偏好 API。
 */
@Injectable({ providedIn: 'root' })
export class PreferenceStorage {
  private readonly memory = new Map<string, string>();

  get(key: string): string | null {
    const fullKey = PREFERENCE_KEY_PREFIX + key;
    try {
      return globalThis.localStorage?.getItem(fullKey) ?? this.memory.get(fullKey) ?? null;
    } catch {
      return this.memory.get(fullKey) ?? null;
    }
  }

  set(key: string, value: string): void {
    const fullKey = PREFERENCE_KEY_PREFIX + key;
    this.memory.set(fullKey, value);
    try {
      globalThis.localStorage?.setItem(fullKey, value);
    } catch {
      // 保留記憶體值即可
    }
  }

  remove(key: string): void {
    const fullKey = PREFERENCE_KEY_PREFIX + key;
    this.memory.delete(fullKey);
    try {
      globalThis.localStorage?.removeItem(fullKey);
    } catch {
      // 忽略
    }
  }
}
