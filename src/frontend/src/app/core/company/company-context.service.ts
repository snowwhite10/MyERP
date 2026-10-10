import { computed, inject, Injectable, signal } from '@angular/core';
import { PreferenceStorage } from '../storage/preference-storage';

/** 可切換的公司（來源：登入後 /api/v1/sys/... 用戶可存取公司清單，WU-SYS-001 / 003 接上）。 */
export interface CompanyOption {
  id: number;
  code: string;
  name: string;
}

export const COMPANY_STORAGE_KEY = 'companyId';

/**
 * 目前公司（多公司隔離）。HTTP 攔截器以 X-Company-Id 傳給後端，後端仍會驗證用戶是否有權存取該公司。
 */
@Injectable({ providedIn: 'root' })
export class CompanyContextService {
  private readonly storage = inject(PreferenceStorage);

  private readonly companiesSignal = signal<readonly CompanyOption[]>([]);
  private readonly selectedId = signal<number | null>(null);

  readonly companies = this.companiesSignal.asReadonly();
  readonly current = computed<CompanyOption | null>(
    () => this.companiesSignal().find((c) => c.id === this.selectedId()) ?? null,
  );

  /** 設定可存取公司；保留原選擇（或本機偏好），不在清單內時選第一間。 */
  setCompanies(companies: readonly CompanyOption[]): void {
    this.companiesSignal.set([...companies]);
    const preferred = this.selectedId() ?? this.readStoredId();
    const next = companies.find((c) => c.id === preferred) ?? companies[0] ?? null;
    this.applySelection(next?.id ?? null);
  }

  /** 切換公司；不在可存取清單內時忽略並回傳 false。 */
  select(companyId: number): boolean {
    if (!this.companiesSignal().some((c) => c.id === companyId)) {
      return false;
    }
    this.applySelection(companyId);
    return true;
  }

  clear(): void {
    this.companiesSignal.set([]);
    this.selectedId.set(null);
  }

  private applySelection(companyId: number | null): void {
    this.selectedId.set(companyId);
    if (companyId === null) {
      this.storage.remove(COMPANY_STORAGE_KEY);
    } else {
      this.storage.set(COMPANY_STORAGE_KEY, String(companyId));
    }
  }

  private readStoredId(): number | null {
    const raw = this.storage.get(COMPANY_STORAGE_KEY);
    const id = raw === null ? NaN : Number(raw);
    return Number.isInteger(id) ? id : null;
  }
}
