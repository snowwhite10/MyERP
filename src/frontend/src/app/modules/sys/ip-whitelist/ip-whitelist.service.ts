import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { apiErrorKey } from '../../../core/http/api-error';
import { PagedResult } from '../../../shared/list-page/table-query';

export const IP_WHITELIST_API = '/api/v1/sys/ip-whitelist';
export type IpScope = 'System' | 'Role' | 'User';
export interface IpEntry {
  id: number;
  scope: IpScope;
  targetId: number | null;
  ipRange: string;
  validFrom: string | null;
  validTo: string | null;
  description: string | null;
  isActive: boolean;
  version: number;
}
export type IpCreate = Pick<IpEntry, 'scope' | 'targetId' | 'ipRange' | 'validFrom' | 'validTo' | 'description'>;
export type IpUpdate = Pick<IpEntry, 'validFrom' | 'validTo' | 'description' | 'isActive' | 'version'>;

export function validIpRange(value: string): boolean {
  const ip = value.trim();
  const [address, prefix, extra] = ip.split('/');
  if (!address || extra !== undefined || (prefix !== undefined && !/^\d+$/.test(prefix))) return false;
  const parts = address.split('.');
  if (parts.length === 4 && !address.includes(':')) {
    if (!parts.every(p => /^(0|[1-9]\d{0,2})$/.test(p) && Number(p) <= 255)) return false;
    if (prefix === undefined) return true;
    const bits = Number(prefix);
    if (bits > 32) return false;
    const number = parts.reduce((n, p) => (n << 8) | Number(p), 0) >>> 0;
    return bits === 0 ? number === 0 : (number & (0xffffffff << (32 - bits))) >>> 0 === number;
  }
  if (!address.includes(':') || (prefix !== undefined && Number(prefix) > 128)) return false;
  const mapped = /^::ffff:(\d+(?:\.\d+){3})$/i.exec(address);
  if (mapped) return prefix === undefined && validIpRange(mapped[1]);
  try {
    // URL parses full IPv6 (including IPv4-mapped addresses); the server performs CIDR network validation.
    return new URL(`http://[${address}]/`).hostname.length > 0;
  } catch {
    return false;
  }
}

export const IP_ERRORS = ['SYS.IP_WHITELIST.NOT_FOUND', 'SYS.IP_WHITELIST.DUPLICATE',
  'COMMON.CONCURRENCY.CONFLICT', 'COMMON.PERMISSION.DENIED', 'COMMON.REQUEST.VALIDATION_FAILED'] as const;
export const ipErrorKey = (error: unknown): string => apiErrorKey(error, IP_ERRORS);

@Injectable({ providedIn: 'root' })
export class IpWhitelistService {
  private readonly http = inject(HttpClient);
  list(query: { page: number; pageSize: number; keyword?: string; sort?: string }): Observable<PagedResult<IpEntry>> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    if (query.keyword?.trim()) params = params.set('keyword', query.keyword.trim());
    if (query.sort) params = params.set('sort', query.sort);
    return this.http.get<PagedResult<IpEntry>>(IP_WHITELIST_API, { params });
  }
  create(input: IpCreate): Observable<IpEntry> { return this.http.post<IpEntry>(IP_WHITELIST_API, input); }
  update(id: number, input: IpUpdate): Observable<IpEntry> { return this.http.put<IpEntry>(`${IP_WHITELIST_API}/${id}`, input); }
}
