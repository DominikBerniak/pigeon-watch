import { formatDate } from '@angular/common';
import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { RouterLink, provideRouter } from '@angular/router';
import { Sighting, SightingCriticality, SightingStatus } from '../../sighting.types';
import { SightingRow } from './sighting-row';

const now = new Date('2026-10-11T12:00:00Z');

const named: Sighting = {
  id: 'sighting-1',
  name: 'Limpy',
  description: 'Grey pigeon',
  behaviour: 'Cannot fly',
  latitude: 52.23,
  longitude: 21.01,
  status: SightingStatus.atTheVet,
  criticality: SightingCriticality.urgent,
  reporterDisplayName: 'Jan K',
  createdAt: '2026-10-11T11:55:00Z',
  hasPhoto: false,
};

@Component({
  imports: [SightingRow],
  template: `<ul>
    <li app-sighting-row [sighting]="sighting()" [now]="now()" [linkState]="linkState()"></li>
  </ul>`,
})
class SightingRowHost {
  readonly sighting = signal<Sighting>(named);
  readonly now = signal<Date>(now);
  readonly linkState = signal<object | undefined>(undefined);
}

describe('SightingRow', () => {
  let fixture: ComponentFixture<SightingRowHost>;

  async function render(sighting: Sighting): Promise<HTMLLIElement> {
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
    fixture = TestBed.createComponent(SightingRowHost);
    fixture.componentInstance.sighting.set(sighting);
    await fixture.whenStable();

    return (fixture.nativeElement as HTMLElement).querySelector('li')!;
  }

  it('makes the whole row one link to the detail page', async () => {
    const row = await render(named);
    const links = row.querySelectorAll('a');

    expect(links).toHaveLength(1);
    expect(links[0].getAttribute('href')).toBe('/sightings/sighting-1');
    expect(links[0].querySelector('.sighting-row__name')?.textContent?.trim()).toBe('Limpy');
    expect(links[0].querySelector('time')).not.toBeNull();
    expect(links[0].querySelector('app-criticality-badge')).not.toBeNull();
  });

  it('sends no navigation state by default and the given state when the parent passes one', async () => {
    await render(named);
    const link = (): RouterLink =>
      fixture.debugElement.query(By.css('li a')).injector.get(RouterLink);

    expect(link().state).toBeUndefined();

    fixture.componentInstance.linkState.set({ returnToList: 'state' });
    await fixture.whenStable();

    expect(link().state).toEqual({ returnToList: 'state' });
  });

  it('shows the unnamed label for a sighting without a name', async () => {
    const row = await render({ ...named, name: null });

    expect(row.querySelector('.sighting-row__name')?.textContent?.trim()).toBe('Unnamed bird');
  });

  it('shows the status and the criticality as a badge', async () => {
    const row = await render(named);

    expect(row.querySelector('.sighting-row__meta')?.textContent).toContain('At the vet');
    expect(row.querySelector('app-criticality-badge')?.textContent?.trim()).toBe('Urgent');
    expect(row.querySelector('app-criticality-badge')?.classList).toContain(
      'criticality-badge--urgent',
    );
  });

  it('shows the relative age with the report time in datetime and the full date as title', async () => {
    const row = await render(named);
    const time = row.querySelector('time')!;

    expect(time.textContent?.trim()).toBe('5 min ago');
    expect(time.getAttribute('datetime')).toBe(named.createdAt);
    expect(time.getAttribute('title')).toBe(formatDate(named.createdAt, 'medium', 'en-US'));
  });

  it.each([
    ['2026-10-11T11:59:30Z', 'just now'],
    ['2026-10-11T09:00:00Z', '3 h ago'],
    ['2026-10-09T12:00:00Z', '2 d ago'],
  ])('measures a report from %s against the given now as "%s"', async (createdAt, text) => {
    const row = await render({ ...named, createdAt });

    expect(row.querySelector('time')?.textContent?.trim()).toBe(text);
  });

  it('follows a new now from the parent', async () => {
    const row = await render(named);

    fixture.componentInstance.now.set(new Date('2026-10-11T13:00:00Z'));
    await fixture.whenStable();

    expect(row.querySelector('time')?.textContent?.trim()).toBe('1 h ago');
  });
});
