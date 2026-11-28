import { AfterViewInit, Directive, ElementRef, inject } from '@angular/core';

/**
 * Focuses the host once it is rendered — for inputs that appear after a user action (for example a reason box opened by a
 * button), so keyboard and screen-reader users land where they must type next.
 */
@Directive({ selector: '[erpFocusOnInit]' })
export class FocusOnInit implements AfterViewInit {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  ngAfterViewInit(): void { this.host.nativeElement.focus(); }
}
