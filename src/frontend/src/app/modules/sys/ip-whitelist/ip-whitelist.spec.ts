import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ANIMATION_MODULE_TYPE } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideTestTranslations } from '../../../../testing/i18n-fixtures';
import { provideNzIcons } from 'ng-zorro-antd/icon';
import { APP_ICONS } from '../../../core/icons';
import { IpWhitelistList } from './ip-whitelist-list';
import { IP_WHITELIST_API, IpWhitelistService, validIpRange } from './ip-whitelist.service';

const entry = { id: 5, scope: 'System', targetId: null, ipRange: '10.0.0.0/8', validFrom: null,
  validTo: null, description: null, isActive: true, version: 2 } as const;

describe('SYS-IPW-007 IP whitelist page', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(),
    ...provideTestTranslations('en'), provideNzIcons(APP_ICONS),
    { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' }] }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('validates IPv4 CIDR network boundaries', () => {
    expect(validIpRange('10.0.0.0/8')).toBe(true);
    expect(validIpRange('10.1.2.3/8')).toBe(false);
    expect(validIpRange('192.168.1.1')).toBe(true);
    expect(validIpRange('256.1.1.1')).toBe(false);
    expect(validIpRange('2001:db8::1')).toBe(true);
    expect(validIpRange('::ffff:10.0.0.1')).toBe(true);
    expect(validIpRange('2001:db8::1/129')).toBe(false);
    expect(validIpRange('2001:db8:::1')).toBe(false);
  });

  it('sends query and versioned update', () => {
    const service = TestBed.inject(IpWhitelistService);
    const http = TestBed.inject(HttpTestingController);
    service.list({ page: 2, pageSize: 10, keyword: ' 10. ', sort: '-ipRange' }).subscribe();
    const query = http.expectOne(r => r.url === IP_WHITELIST_API && r.method === 'GET');
    expect(query.request.params.get('keyword')).toBe('10.');
    expect(query.request.params.get('sort')).toBe('-ipRange');
    query.flush({ items: [], page: 2, pageSize: 10, totalCount: 0 });
    service.update(5, { validFrom: null, validTo: null, description: null, isActive: false, version: 2 }).subscribe();
    const update = http.expectOne(`${IP_WHITELIST_API}/5`);
    expect(update.request.body).toMatchObject({ isActive: false, version: 2 });
    update.flush({ ...entry, isActive: false, version: 3 });
  });

  it('requires a target for role rules and a valid date order before POST', () => {
    const fixture = TestBed.createComponent(IpWhitelistList);
    const page = fixture.componentInstance;
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(r => r.url === IP_WHITELIST_API && r.method === 'GET').flush({ items: [entry], page: 1, pageSize: 20, totalCount: 1 });
    page.openCreate();
    page.form.patchValue({ scope: 'Role', ipRange: '10.0.0.0/8' });
    page.submit();
    http.expectNone(r => r.method === 'POST');
    page.form.patchValue({ targetId: 7, validFrom: '2026-10-12', validTo: '2026-10-11' });
    page.submit();
    http.expectNone(r => r.method === 'POST');
    page.form.patchValue({ validTo: '2026-10-12' });
    page.submit();
    const create = http.expectOne(r => r.url === IP_WHITELIST_API && r.method === 'POST');
    expect(create.request.body).toMatchObject({ scope: 'Role', targetId: 7, ipRange: '10.0.0.0/8' });
    create.flush({ ...entry, id: 6 });
    http.expectOne(r => r.url === IP_WHITELIST_API && r.method === 'GET').flush({ items: [], page: 1, pageSize: 20, totalCount: 0 });
  });
});
