import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { MapMarkerKind } from '../../map/map-marker';
import { CriticalityBadge } from './criticality-badge';

@Component({
  imports: [CriticalityBadge],
  template: `<app-criticality-badge [kind]="kind()">Urgent</app-criticality-badge>`,
})
class CriticalityBadgeHost {
  readonly kind = signal<MapMarkerKind>('urgent');
}

describe('CriticalityBadge', () => {
  async function render(kind: MapMarkerKind): Promise<HTMLElement> {
    const fixture = TestBed.createComponent(CriticalityBadgeHost);
    fixture.componentInstance.kind.set(kind);
    await fixture.whenStable();

    return fixture.nativeElement.querySelector('app-criticality-badge');
  }

  it.each([
    ['monitor', 'criticality-badge--monitor'],
    ['needsHelpSoon', 'criticality-badge--needs-help-soon'],
    ['urgent', 'criticality-badge--urgent'],
  ] as const)('marks the %s kind with %s only', async (kind, className) => {
    const badge = await render(kind);
    const variants = [...badge.classList].filter((name) => name.startsWith('criticality-badge--'));

    expect(variants).toEqual([className]);
  });

  it('shows the projected label next to a decorative dot', async () => {
    const badge = await render('urgent');
    const dot = badge.querySelector('.criticality-badge__dot');

    expect(badge.textContent?.trim()).toBe('Urgent');
    expect(dot?.getAttribute('aria-hidden')).toBe('true');
    expect(dot?.textContent).toBe('');
    expect(badge.firstElementChild).toBe(dot);
  });
});
