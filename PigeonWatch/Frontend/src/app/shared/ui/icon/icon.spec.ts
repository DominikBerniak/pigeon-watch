import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Icon, IconName, IconSize } from './icon';

@Component({
  imports: [Icon],
  template: `<app-icon [name]="name()" [size]="size()" />`,
})
class IconHost {
  readonly name = signal<IconName>('mail');
  readonly size = signal<IconSize>('md');
}

describe('Icon', () => {
  it.each<IconName>(['mail', 'user', 'report'])(
    'renders the decorative %s glyph that takes the current text colour',
    async (name) => {
      const fixture = TestBed.createComponent(IconHost);
      fixture.componentInstance.name.set(name);
      await fixture.whenStable();
      const icon: HTMLElement = fixture.nativeElement.querySelector('app-icon');
      const svg = icon.querySelector('svg');
      const paths = svg?.querySelectorAll('path') ?? [];

      expect(icon.getAttribute('aria-hidden')).toBe('true');
      expect(svg?.getAttribute('aria-hidden')).toBe('true');
      expect(svg?.getAttribute('focusable')).toBe('false');
      expect(svg?.getAttribute('viewBox')).toBe('0 0 24 24');
      expect(paths.length).toBe(1);
      expect(paths[0].getAttribute('fill')).toBe('currentColor');
      expect(icon.textContent?.trim()).toBe('');
    },
  );

  it('draws a different glyph for each name', async () => {
    const fixture = TestBed.createComponent(IconHost);
    const shapes = new Set<string | null | undefined>();

    for (const name of ['mail', 'user', 'report'] as const) {
      fixture.componentInstance.name.set(name);
      await fixture.whenStable();
      shapes.add(fixture.nativeElement.querySelector('app-icon path')?.getAttribute('d'));
    }

    expect(shapes.size).toBe(3);
  });

  it('switches to the large variant through the size input', async () => {
    const fixture = TestBed.createComponent(IconHost);
    await fixture.whenStable();
    const icon: HTMLElement = fixture.nativeElement.querySelector('app-icon');

    expect(icon.classList.contains('icon--lg')).toBe(false);

    fixture.componentInstance.size.set('lg');
    await fixture.whenStable();

    expect(icon.classList.contains('icon--lg')).toBe(true);
  });
});
