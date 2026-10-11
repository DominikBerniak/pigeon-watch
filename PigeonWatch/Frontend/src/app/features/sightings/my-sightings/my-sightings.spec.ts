import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Observable, Subject, of, throwError } from 'rxjs';
import { Sighting, SightingCriticality, SightingList, SightingStatus } from '../sighting.types';
import { SightingsApi } from '../sightings-api';
import { MySightings } from './my-sightings';

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
  createdAt: '2026-10-11T12:00:00Z',
};

const unnamed: Sighting = {
  ...named,
  id: 'sighting-2',
  name: null,
  status: SightingStatus.spotted,
  criticality: SightingCriticality.monitor,
  createdAt: '2026-10-10T08:30:00Z',
};

describe('MySightings', () => {
  let mine: ReturnType<typeof vi.fn<() => Observable<SightingList>>>;
  let fixture: ComponentFixture<MySightings>;

  async function render(result: Observable<SightingList>): Promise<HTMLElement> {
    mine = vi.fn<() => Observable<SightingList>>(() => result);
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: SightingsApi, useValue: { mine } },
      ],
    });
    fixture = TestBed.createComponent(MySightings);
    await fixture.whenStable();

    return fixture.nativeElement as HTMLElement;
  }

  async function settle(): Promise<void> {
    await fixture.whenStable();
    fixture.detectChanges();
    await fixture.whenStable();
  }

  it('shows a spinner while loading', async () => {
    const pending = new Subject<SightingList>();
    const root = await render(pending);

    expect(root.querySelector('h2')?.textContent?.trim()).toBe('My sightings');
    expect(root.querySelector('mat-progress-spinner')).not.toBeNull();

    pending.next({ items: [] });
    pending.complete();
    await settle();

    expect(root.querySelector('mat-progress-spinner')).toBeNull();
  });

  it('shows the empty state when the user has no sightings', async () => {
    const root = await render(of({ items: [] }));

    expect(mine).toHaveBeenCalledOnce();
    expect(root.textContent).toContain("You haven't reported any sightings yet.");
    expect(root.querySelector('li')).toBeNull();
    expect(root.querySelector('app-alert')).toBeNull();
  });

  it('shows an error with a retry that loads again', async () => {
    const root = await render(throwError(() => new HttpErrorResponse({ status: 500 })));

    expect(root.querySelector('app-alert[role="alert"]')?.textContent?.trim()).toBe(
      "We couldn't load your sightings.",
    );

    mine.mockReturnValue(of({ items: [named] }));
    root.querySelector<HTMLButtonElement>('button')?.click();
    await settle();

    expect(mine).toHaveBeenCalledTimes(2);
    expect(root.querySelector('app-alert')).toBeNull();
    expect(root.querySelectorAll('li').length).toBe(1);
  });

  it('lists each sighting with its name, status, criticality, date and a detail link', async () => {
    const root = await render(of({ items: [named, unnamed] }));
    const items = [...root.querySelectorAll('li')];

    expect(items.length).toBe(2);
    expect(items.map((item) => item.querySelector('a')?.textContent?.trim())).toEqual([
      'Limpy',
      'Unnamed bird',
    ]);
    expect(items.map((item) => item.querySelector('a')?.getAttribute('href'))).toEqual([
      '/sightings/sighting-1',
      '/sightings/sighting-2',
    ]);
    expect(items[0].textContent).toContain('At the vet');
    expect(items[0].textContent).toContain('Urgent');
    expect(items[1].textContent).toContain('Spotted');
    expect(items[1].textContent).toContain('Monitor');
    expect(items[0].querySelector('time')?.getAttribute('datetime')).toBe(named.createdAt);
    expect(items[0].querySelector('time')?.textContent?.trim()).not.toBe('');
  });
});
