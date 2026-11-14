import {
  afterEveryRender, afterNextRender, contentChildren, Directive, ElementRef, inject, Injector, OnDestroy, OnInit,
} from '@angular/core';
import { AbstractControl, FormGroupDirective, NgControl } from '@angular/forms';

/**
 * 表單欄位錯誤的無障礙處理（WU-UI-ERRFOCUS，ui-conventions 4.1）。
 *
 * 套在 `<form [formGroup] erpFormErrors>` 上：
 * - 每次畫面更新後，已觸碰且無效、或後面緊接錯誤訊息（`[role="alert"]`）的欄位設 `aria-invalid="true"`；
 * - 錯誤訊息自動補 id，並以 `aria-describedby` 關聯到該欄位（保留欄位原有的描述 id）；
 * - 元件送出驗證失敗時呼叫 `focusFirstInvalid(this.form)`，下一次繪製後焦點移到第一個有錯誤的欄位。
 *
 * 錯誤訊息的位置慣例：寫在欄位（或其包裝元素）之後、下一個欄位或 `<label>` 之前，中間可有提示文字。
 */
@Directive({ selector: 'form[erpFormErrors]', exportAs: 'erpFormErrors' })
export class ErpFormErrors implements OnInit, OnDestroy {
  private readonly host = inject<ElementRef<HTMLFormElement>>(ElementRef);
  private readonly formDirective = inject(FormGroupDirective, { self: true });
  private readonly injector = inject(Injector);
  private readonly controls = contentChildren(NgControl, { descendants: true });
  private readonly controlElements = contentChildren(NgControl, { descendants: true, read: ElementRef });
  private registered: AbstractControl | null = null;

  constructor() {
    afterEveryRender({ write: () => this.sync() });
  }

  ngOnInit(): void {
    this.registered = this.formDirective.form;
    registry.set(this.registered, this);
  }

  ngOnDestroy(): void {
    if (this.registered && registry.get(this.registered) === this) registry.delete(this.registered);
  }

  /** 下一次繪製（錯誤訊息出現）後，把焦點移到第一個有錯誤的欄位。 */
  focusFirstInvalid(): void {
    afterNextRender({ write: () => { this.sync(); this.firstErrorTarget()?.focus(); } }, { injector: this.injector });
  }

  /** 目前第一個有錯誤的欄位的可聚焦元素（依畫面次序）。 */
  firstErrorTarget(): HTMLElement | null {
    return this.fields().find((field) => field.hasError)?.target ?? null;
  }

  private fields(): Field[] {
    const controls = this.controls();
    const elements = this.controlElements().map((ref) => ref.nativeElement as HTMLElement);
    const fields = elements.map((element, index): Field => {
      const control = controls[index]?.control ?? null;
      const alert = followingAlert(element, elements, this.host.nativeElement);
      const invalid = !!control && control.invalid && control.touched && control.enabled;
      return { element, target: focusTarget(element), alert, hasError: invalid || alert !== null };
    });
    return fields.sort((a, b) => (a.element.compareDocumentPosition(b.element) & Node.DOCUMENT_POSITION_FOLLOWING ? -1 : 1));
  }

  private sync(): void {
    for (const field of this.fields()) {
      const target = field.target;
      if (field.hasError) target.setAttribute('aria-invalid', 'true');
      else if (target.getAttribute('aria-invalid') === 'true' && target.hasAttribute(MANAGED)) target.removeAttribute('aria-invalid');
      if (field.hasError) target.setAttribute(MANAGED, '');
      else target.removeAttribute(MANAGED);
      linkDescription(target, field.alert);
    }
  }
}

/** 元件送出驗證失敗時呼叫；表單沒有套 `erpFormErrors` 時不做任何事。 */
export function focusFirstInvalid(form: AbstractControl): void {
  registry.get(form)?.focusFirstInvalid();
}

interface Field { element: HTMLElement; target: HTMLElement; alert: HTMLElement | null; hasError: boolean }

const registry = new WeakMap<AbstractControl, ErpFormErrors>();
const MANAGED = 'data-erp-invalid';
const DESCRIBED = 'data-erp-error-id';
const FOCUSABLE = 'input:not([type="hidden"]):not([disabled]), select:not([disabled]), textarea:not([disabled]), button:not([disabled]), [tabindex]:not([tabindex="-1"])';
let nextId = 0;

/** nz-select、nz-input-number 等元件的宿主不可聚焦，改用其內第一個可聚焦元素。 */
function focusTarget(element: HTMLElement): HTMLElement {
  if (element.matches(FOCUSABLE)) return element;
  return element.querySelector<HTMLElement>(FOCUSABLE) ?? element;
}

/** 從欄位（及最多兩層包裝元素）往後找錯誤訊息；遇到 `<label>` 或另一個欄位即停止。 */
function followingAlert(element: HTMLElement, controls: HTMLElement[], form: HTMLElement): HTMLElement | null {
  let node: HTMLElement | null = element;
  for (let level = 0; level < 3 && node && node !== form; level++) {
    for (let sibling = node.nextElementSibling as HTMLElement | null; sibling; sibling = sibling.nextElementSibling as HTMLElement | null) {
      if (sibling.matches('[role="alert"]')) return sibling;
      if (sibling.tagName === 'LABEL' || controls.some((control) => sibling === control || sibling!.contains(control))) return null;
    }
    node = node.parentElement;
  }
  return null;
}

function linkDescription(target: HTMLElement, alert: HTMLElement | null): void {
  const previous = target.getAttribute(DESCRIBED);
  if (alert && !alert.id) alert.id = `erp-field-error-${++nextId}`;
  const next = alert?.id ?? null;
  if (previous === next) return;
  const tokens = (target.getAttribute('aria-describedby') ?? '').split(/\s+/).filter((token) => token && token !== previous);
  if (next) tokens.push(next);
  if (tokens.length) target.setAttribute('aria-describedby', tokens.join(' '));
  else target.removeAttribute('aria-describedby');
  if (next) target.setAttribute(DESCRIBED, next);
  else target.removeAttribute(DESCRIBED);
}
