import { DOCUMENT } from '@angular/common';
import { Directive, ElementRef, OnDestroy, effect, inject, input } from '@angular/core';

const viewportGutter = 8;

let nextTooltipId = 0;

@Directive({
  selector: '[appTooltip]',
  host: {
    '(mouseenter)': 'show()',
    '(mouseleave)': 'hide()',
    '(focus)': 'showOnKeyboardFocus()',
    '(blur)': 'hide()',
    '(click)': 'hide()',
    '(keydown.escape)': 'hide()',
  },
})
export class Tooltip implements OnDestroy {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly document = inject(DOCUMENT);
  private readonly id = `pw-tooltip-${nextTooltipId++}`;
  private readonly descriptionId = `${this.id}-description`;
  private readonly hideOnScroll = (): void => this.hide();
  private bubble: HTMLElement | null = null;
  private description: HTMLElement | null = null;

  readonly appTooltip = input.required<string | null | undefined>();

  constructor() {
    effect(() => this.describe(this.appTooltip()));
  }

  ngOnDestroy(): void {
    this.hide();
    this.describe(null);
  }

  protected showOnKeyboardFocus(): void {
    if (this.host.nativeElement.matches(':focus-visible')) this.show();
  }

  protected show(): void {
    const text = this.appTooltip();

    if (!text) return;

    this.hide();

    const bubble = this.document.createElement('div');

    bubble.id = this.id;
    bubble.className = 'pw-tooltip';
    bubble.setAttribute('role', 'tooltip');
    bubble.setAttribute('aria-hidden', 'true');
    bubble.setAttribute('popover', 'manual');
    bubble.textContent = text;
    this.document.body.appendChild(bubble);
    this.bubble = bubble;

    if (typeof bubble.showPopover === 'function') bubble.showPopover();

    this.place(bubble);
    this.document.addEventListener('scroll', this.hideOnScroll, { capture: true, passive: true });
  }

  protected hide(): void {
    this.document.removeEventListener('scroll', this.hideOnScroll, { capture: true });
    this.bubble?.remove();
    this.bubble = null;
  }

  private describe(text: string | null | undefined): void {
    const host = this.host.nativeElement;
    const needed = !!text && text !== host.getAttribute('aria-label');
    const others = (host.getAttribute('aria-describedby') ?? '')
      .split(' ')
      .filter((id) => id && id !== this.descriptionId);

    if (!needed) {
      this.description?.remove();
      this.description = null;
      setDescribedBy(host, others);

      return;
    }

    if (!this.description) {
      this.description = this.document.createElement('div');
      this.description.id = this.descriptionId;
      this.description.className = 'pw-visually-hidden';
      this.document.body.appendChild(this.description);
    }

    this.description.textContent = text;
    setDescribedBy(host, [...others, this.descriptionId]);
  }

  private place(bubble: HTMLElement): void {
    const anchor = this.host.nativeElement.getBoundingClientRect();
    const width = bubble.offsetWidth;
    const height = bubble.offsetHeight;
    const viewport = this.document.documentElement;
    const centred = anchor.left + anchor.width / 2 - width / 2;
    const left = Math.min(
      Math.max(centred, viewportGutter),
      viewport.clientWidth - viewportGutter - width,
    );
    const above = anchor.bottom + height + viewportGutter > viewport.clientHeight;

    bubble.classList.toggle('pw-tooltip--above', above);
    bubble.style.left = `${left}px`;
    bubble.style.top = `${above ? anchor.top - height : anchor.bottom}px`;
  }
}

function setDescribedBy(host: HTMLElement, ids: readonly string[]): void {
  if (ids.length === 0) host.removeAttribute('aria-describedby');
  else host.setAttribute('aria-describedby', ids.join(' '));
}
