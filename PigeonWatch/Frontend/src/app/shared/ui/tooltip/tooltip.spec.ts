import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Tooltip } from './tooltip';

@Component({
  imports: [Tooltip],
  template: `<button type="button" [appTooltip]="text()">Go</button>`,
})
class TooltipHost {
  readonly text = signal('Messages');
}

@Component({
  imports: [Tooltip],
  template: `<button type="button" [attr.aria-label]="label()" [appTooltip]="text()">Go</button>`,
})
class LabelledTooltipHost {
  readonly label = signal('Messages');
  readonly text = signal('Messages');
}

describe('Tooltip', () => {
  let fixture: ComponentFixture<TooltipHost>;

  beforeEach(async () => {
    fixture = TestBed.createComponent(TooltipHost);
    await fixture.whenStable();
  });

  afterEach(() => {
    fixture.destroy();
  });

  function button(): HTMLButtonElement {
    return fixture.nativeElement.querySelector('button');
  }

  function bubbles(): HTMLElement[] {
    return Array.from(document.body.querySelectorAll<HTMLElement>('.pw-tooltip'));
  }

  it('shows one bubble with the text on hover and removes it on leave', () => {
    button().dispatchEvent(new MouseEvent('mouseenter'));
    button().dispatchEvent(new MouseEvent('mouseenter'));

    expect(bubbles()).toHaveLength(1);
    expect(bubbles()[0].textContent).toBe('Messages');
    expect(bubbles()[0].getAttribute('role')).toBe('tooltip');

    button().dispatchEvent(new MouseEvent('mouseleave'));

    expect(bubbles()).toHaveLength(0);
  });

  it('hides on click and on Escape', () => {
    button().dispatchEvent(new MouseEvent('mouseenter'));
    button().click();

    expect(bubbles()).toHaveLength(0);

    button().dispatchEvent(new MouseEvent('mouseenter'));
    button().dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));

    expect(bubbles()).toHaveLength(0);
  });

  it('shows nothing for an empty text', async () => {
    fixture.componentInstance.text.set('');
    await fixture.whenStable();

    button().dispatchEvent(new MouseEvent('mouseenter'));

    expect(bubbles()).toHaveLength(0);
  });

  it('opens below its host when there is room and flips above near the bottom of the viewport', () => {
    const viewport = document.documentElement;
    const original = Object.getOwnPropertyDescriptor(viewport, 'clientHeight');

    Object.defineProperty(viewport, 'clientHeight', { configurable: true, value: 1000 });
    vi.spyOn(button(), 'getBoundingClientRect').mockReturnValue(
      DOMRect.fromRect({ x: 100, y: 100, width: 40, height: 40 }),
    );
    button().dispatchEvent(new MouseEvent('mouseenter'));

    expect(bubbles()[0].classList.contains('pw-tooltip--above')).toBe(false);
    expect(bubbles()[0].style.top).toBe('140px');

    button().dispatchEvent(new MouseEvent('mouseleave'));
    vi.spyOn(button(), 'getBoundingClientRect').mockReturnValue(
      DOMRect.fromRect({ x: 100, y: 990, width: 40, height: 40 }),
    );
    button().dispatchEvent(new MouseEvent('mouseenter'));

    expect(bubbles()[0].classList.contains('pw-tooltip--above')).toBe(true);

    if (original) Object.defineProperty(viewport, 'clientHeight', original);
    else delete (viewport as { clientHeight?: number }).clientHeight;
  });

  it('hides when anything scrolls', () => {
    button().dispatchEvent(new MouseEvent('mouseenter'));
    document.dispatchEvent(new Event('scroll'));

    expect(bubbles()).toHaveLength(0);
  });

  it('describes its host for screen readers with a hidden element holding the text', async () => {
    const id = button().getAttribute('aria-describedby');
    const description = document.getElementById(id ?? '');

    expect(description?.textContent).toBe('Messages');
    expect(description?.classList.contains('pw-visually-hidden')).toBe(true);

    fixture.componentInstance.text.set('Inbox');
    await fixture.whenStable();

    expect(
      document.getElementById(button().getAttribute('aria-describedby') ?? '')?.textContent,
    ).toBe('Inbox');

    fixture.destroy();

    expect(document.getElementById(id ?? '')).toBeNull();
  });

  it('adds no description when the text only repeats the aria-label', async () => {
    const labelled = TestBed.createComponent(LabelledTooltipHost);
    await labelled.whenStable();
    const host: HTMLButtonElement = labelled.nativeElement.querySelector('button');

    expect(host.hasAttribute('aria-describedby')).toBe(false);

    labelled.componentInstance.text.set('Open your messages');
    await labelled.whenStable();

    expect(document.getElementById(host.getAttribute('aria-describedby') ?? '')?.textContent).toBe(
      'Open your messages',
    );

    labelled.destroy();
  });

  it('removes its bubble when the host is destroyed', () => {
    button().dispatchEvent(new MouseEvent('mouseenter'));
    fixture.destroy();

    expect(bubbles()).toHaveLength(0);
  });
});
