import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';
import { LatestRequest, latestRequest } from './latest-request';

@Component({ selector: 'erp-latest-request-host', template: '' })
class Host {
  readonly request = latestRequest();
}

describe('SYS-F02 latest request', () => {
  it('cancels the previous request so a slow stale response cannot overwrite newer data', () => {
    const request = new LatestRequest();
    const older = new Subject<string>();
    const newer = new Subject<string>();
    const received: string[] = [];
    request.run(older, { next: (value) => received.push(value) });
    request.run(newer, { next: (value) => received.push(value) });
    expect(older.observed).toBe(false);
    newer.next('page 2');
    older.next('page 1');
    expect(received).toEqual(['page 2']);
    request.cancel();
    expect(newer.observed).toBe(false);
  });

  it('cancels the pending request when the component is destroyed', () => {
    const fixture = TestBed.createComponent(Host);
    const pending = new Subject<number>();
    fixture.componentInstance.request.run(pending, {});
    expect(pending.observed).toBe(true);
    fixture.destroy();
    expect(pending.observed).toBe(false);
  });
});
