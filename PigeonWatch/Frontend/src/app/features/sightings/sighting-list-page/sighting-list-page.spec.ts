import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { DebugElement } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatPaginator } from '@angular/material/paginator';
import { MatSelect, MatSelectChange } from '@angular/material/select';
import { By } from '@angular/platform-browser';
import { Router, RouterLink, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Observable, Subject, isObservable, of, throwError } from 'rxjs';
import { GeoPoint } from '../../../core/geolocation/geo-point';
import {
  GeolocationOutcome,
  GeolocationService,
} from '../../../core/geolocation/geolocation.service';
import { SightingFilterRow } from '../shared/sighting-filters/sighting-filters';
import {
  Sighting,
  SightingCriticality,
  SightingFilters,
  SightingListRequest,
  SightingPage,
  SightingStatus,
} from '../sighting.types';
import { SightingsApi } from '../sightings-api';
import { SightingListPage } from './sighting-list-page';

const warsaw: GeoPoint = { latitude: 52.2297, longitude: 21.0122 };
const krakow: GeoPoint = { latitude: 50.0614, longitude: 19.9366 };
const noFilters: SightingFilters = { statuses: [], criticalities: [] };
const urgentOnly: SightingFilters = { statuses: [], criticalities: [SightingCriticality.urgent] };

const urgent: Sighting = {
  id: 'sighting-1',
  name: 'Limpy',
  description: 'Grey pigeon',
  behaviour: 'Cannot fly',
  latitude: 52.23,
  longitude: 21.01,
  status: SightingStatus.caught,
  criticality: SightingCriticality.urgent,
  reporterDisplayName: 'Jan K',
  createdAt: '2026-10-11T12:00:00Z',
};

const monitor: Sighting = {
  ...urgent,
  id: 'sighting-2',
  name: null,
  status: SightingStatus.spotted,
  criticality: SightingCriticality.monitor,
  createdAt: '2026-10-11T11:00:00Z',
};

function requestAt(
  centre: GeoPoint,
  overrides: Partial<SightingListRequest> = {},
): SightingListRequest {
  return { ...centre, statuses: [], criticalities: [], page: 1, pageSize: 10, ...overrides };
}

describe('SightingListPage', () => {
  let currentPosition: ReturnType<
    typeof vi.fn<(highAccuracy: boolean) => Observable<GeolocationOutcome>>
  >;
  let list: ReturnType<typeof vi.fn<(request: SightingListRequest) => Observable<SightingPage>>>;
  let fixture: ComponentFixture<unknown>;
  let host: DebugElement;

  function configure(
    outcome: GeolocationOutcome | Observable<GeolocationOutcome>,
    result: Observable<SightingPage>,
  ): void {
    const position = isObservable(outcome) ? outcome : of(outcome);

    currentPosition = vi.fn<(highAccuracy: boolean) => Observable<GeolocationOutcome>>(
      () => position,
    );
    list = vi.fn<(request: SightingListRequest) => Observable<SightingPage>>(() => result);
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([{ path: 'sightings/list', component: SightingListPage }]),
        { provide: GeolocationService, useValue: { currentPosition } },
        { provide: SightingsApi, useValue: { list } },
      ],
    });
  }

  async function render(
    outcome: GeolocationOutcome | Observable<GeolocationOutcome>,
    result: Observable<SightingPage> = of({ items: [urgent, monitor], totalCount: 2 }),
  ): Promise<void> {
    configure(outcome, result);
    const created = TestBed.createComponent(SightingListPage);

    fixture = created;
    host = created.debugElement;
    await fixture.whenStable();
  }

  async function navigateWith(
    state: unknown,
    outcome: GeolocationOutcome = { status: 'denied' },
    result: Observable<SightingPage> = of({ items: [urgent], totalCount: 1 }),
  ): Promise<void> {
    configure(outcome, result);
    const harness = await RouterTestingHarness.create();

    await TestBed.inject(Router).navigateByUrl('/sightings/list', {
      state: state as Record<string, unknown>,
    });
    fixture = harness.fixture;
    host = harness.routeDebugElement!;
    await settle();
  }

  async function settle(): Promise<void> {
    await fixture.whenStable();
    fixture.detectChanges();
    await fixture.whenStable();
  }

  function root(): HTMLElement {
    return host.nativeElement as HTMLElement;
  }

  function text(selector: string): string | undefined {
    return root().querySelector(selector)?.textContent?.trim();
  }

  function filterRow(): SightingFilterRow {
    return host.query(By.directive(SightingFilterRow)).componentInstance as SightingFilterRow;
  }

  function paginator(): MatPaginator | null {
    const element = host.query(By.directive(MatPaginator));

    return element ? (element.componentInstance as MatPaginator) : null;
  }

  async function applyFilters(filters: SightingFilters): Promise<void> {
    filterRow().filtersChange.emit(filters);
    await settle();
  }

  function pageSizeSelect(): MatSelect {
    return host.query(By.css('.sighting-list__page-size mat-select'))
      .componentInstance as MatSelect;
  }

  async function choosePageSize(pageSize: number): Promise<void> {
    const select = pageSizeSelect();

    select.selectionChange.emit(new MatSelectChange(select, pageSize));
    await settle();
  }

  async function turnTo(pageIndex: number, pageSize = 10): Promise<void> {
    const control = paginator();

    if (!control) throw new Error('No paginator');

    control.page.emit({ pageIndex, pageSize, length: control.length, previousPageIndex: 0 });
    await settle();
  }

  function mapViewLink(): RouterLink {
    const link = host
      .queryAll(By.directive(RouterLink))
      .find((candidate) => candidate.nativeElement.textContent?.trim() === 'Map view');

    if (!link) throw new Error('No Map view link');

    return link.injector.get(RouterLink);
  }

  function rowLinkStates(): unknown[] {
    return host
      .queryAll(By.css('li[app-sighting-row] a'))
      .map((link) => link.injector.get(RouterLink).state);
  }

  function rowNames(): string[] {
    return [...root().querySelectorAll('li[app-sighting-row] .sighting-row__name')].map(
      (name) => name.textContent?.trim() ?? '',
    );
  }

  it('asks for a low-accuracy position and requests the first page there when opened directly', async () => {
    await render({ status: 'ok', ...warsaw });

    expect(currentPosition).toHaveBeenCalledExactlyOnceWith(false);
    expect(list).toHaveBeenCalledExactlyOnceWith(requestAt(warsaw));
    expect(rowNames()).toEqual(['Limpy', 'Unnamed bird']);
  });

  it('heads the card with the title, the radius subtitle and a Map view link', async () => {
    await render({ status: 'ok', ...warsaw });

    expect(text('h1')).toBe('Sightings nearby (2)');
    expect(root().textContent).toContain('Within 10 km, most urgent first');
    expect(mapViewLink().href).toBe('/');
  });

  it('hands the current filters back to the map through the Map view link', async () => {
    await navigateWith({ centre: krakow, filters: urgentOnly });

    expect(mapViewLink().state).toEqual({ mapFilters: urgentOnly });

    await applyFilters(noFilters);

    expect(mapViewLink().state).toEqual({ mapFilters: noFilters });
  });

  it('offers all nine statuses in the filters', async () => {
    await render({ status: 'ok', ...warsaw });

    expect(filterRow().statusOptions()).toHaveLength(9);
    expect(filterRow().initial()).toEqual(noFilters);
  });

  it('shows the locating indicator and keeps the filters available while the position is pending', async () => {
    await render(new Subject<GeolocationOutcome>());

    expect(text('app-loading-indicator')).toBe('Finding your location…');
    expect(root().querySelector('app-sighting-filters')).not.toBeNull();
    expect(text('h1')).toBe('Sightings nearby');
    expect(list).not.toHaveBeenCalled();
  });

  it('keeps the filters behind a toggle that names the active filter count', async () => {
    await navigateWith({
      centre: warsaw,
      filters: { statuses: [1], criticalities: [3] },
    });

    const toggle = root().querySelector<HTMLButtonElement>(
      'button[aria-controls="sighting-list-filters"]',
    );
    const filters = (): HTMLElement | null =>
      root().querySelector<HTMLElement>('#sighting-list-filters');

    expect(filters()?.hidden).toBe(true);
    expect(toggle?.getAttribute('aria-expanded')).toBe('false');
    expect(toggle?.textContent?.trim()).toBe('Show filters (2)');

    toggle?.click();
    await settle();

    expect(filters()?.hidden).toBe(false);
    expect(toggle?.getAttribute('aria-expanded')).toBe('true');
    expect(toggle?.textContent?.trim()).toBe('Hide filters');
  });

  it.each(['denied', 'unavailable', 'timeout', 'unsupported'] as const)(
    'shows the location message with a map link and sends no request when the position is %s',
    async (status) => {
      await render({ status });

      expect(text('app-alert')).toBe(
        "We couldn't get your location, so the list can't be shown. Open the map and search an area, then choose List view.",
      );

      const openMap = [...root().querySelectorAll('a')].find(
        (anchor) => anchor.textContent?.trim() === 'Open the map',
      );

      expect(openMap?.getAttribute('href')).toBe('/');
      expect(list).not.toHaveBeenCalled();
      expect(paginator()).toBeNull();
    },
  );

  it('only stores a filter change while the position is unavailable', async () => {
    await render({ status: 'denied' });
    await applyFilters(urgentOnly);

    expect(list).not.toHaveBeenCalled();
    expect(root().querySelector('app-alert')).not.toBeNull();
  });

  it('uses the centre and filters handed over by the map without asking for a position', async () => {
    await navigateWith({ centre: krakow, filters: urgentOnly });

    expect(currentPosition).not.toHaveBeenCalled();
    expect(list).toHaveBeenCalledExactlyOnceWith(
      requestAt(krakow, { criticalities: [SightingCriticality.urgent] }),
    );
    expect(filterRow().initial()).toEqual(urgentOnly);
    expect(rowNames()).toEqual(['Limpy']);
  });

  it('passes the current centre and filters to the detail page through each row link', async () => {
    await navigateWith({ centre: krakow, filters: urgentOnly });

    expect(rowLinkStates()).toEqual([{ returnToList: { centre: krakow, filters: urgentOnly } }]);

    await applyFilters(noFilters);

    expect(rowLinkStates()).toEqual([{ returnToList: { centre: krakow, filters: noFilters } }]);
  });

  it('falls back to the position when the navigation state is not a valid hand-over', async () => {
    await navigateWith(
      { centre: { latitude: 120, longitude: 0 }, filters: noFilters },
      { status: 'ok', ...warsaw },
    );

    expect(currentPosition).toHaveBeenCalledExactlyOnceWith(false);
    expect(list).toHaveBeenCalledExactlyOnceWith(requestAt(warsaw));
    expect(filterRow().initial()).toEqual(noFilters);
  });

  it('shows a spinner while the page loads', async () => {
    const pending = new Subject<SightingPage>();

    await render({ status: 'ok', ...warsaw }, pending);

    expect(
      root()
        .querySelector('app-loading-indicator mat-progress-spinner')
        ?.getAttribute('aria-label'),
    ).toBe('Loading…');
    expect(root().querySelector('ul')).toBeNull();

    pending.next({ items: [urgent], totalCount: 1 });
    pending.complete();
    await settle();

    expect(root().querySelector('app-loading-indicator')).toBeNull();
    expect(rowNames()).toEqual(['Limpy']);
  });

  it('renders each row with its relative age against one load time', async () => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date('2026-10-11T12:30:00Z'));

    try {
      await render({ status: 'ok', ...warsaw });

      expect(
        [...root().querySelectorAll('li[app-sighting-row] time')].map((time) =>
          time.textContent?.trim(),
        ),
      ).toEqual(['30 min ago', '1 h ago']);
      expect(root().querySelectorAll('li[app-sighting-row] app-criticality-badge')).toHaveLength(2);
    } finally {
      vi.useRealTimers();
    }
  });

  it('shows the page size on the left, the page number in the centre and the page buttons on the right', async () => {
    await render({ status: 'ok', ...warsaw }, of({ items: [urgent, monitor], totalCount: 25 }));

    const control = paginator();
    const pager = [...(root().querySelector('.sighting-list__pager')?.children ?? [])];

    expect(pager.map((zone) => zone.className.split(' ')[0])).toEqual([
      'sighting-list__page-size',
      'sighting-list__page-number',
      'mat-mdc-paginator',
    ]);
    expect(pager[2]?.classList.contains('sighting-list__page-actions')).toBe(true);
    expect(pageSizeSelect().options.map((option) => option.value as number)).toEqual([10, 20, 30]);
    expect(pageSizeSelect().value).toBe(10);
    expect(text('#sighting-list-page-size-label')).toBe('Sightings per page');
    expect(control?.pageSize).toBe(10);
    expect(control?.pageIndex).toBe(0);
    expect(control?.length).toBe(25);
    expect(text('.sighting-list__page-number')).toBe('1 / 3');
    expect(text('mat-paginator')).toBe('');
    expect(
      [...root().querySelectorAll('mat-paginator button')].map((button) =>
        button.getAttribute('aria-label'),
      ),
    ).toEqual(['First page', 'Previous page', 'Next page', 'Last page']);
  });

  it('requests the chosen page and keeps the page size', async () => {
    await render({ status: 'ok', ...warsaw }, of({ items: [urgent, monitor], totalCount: 25 }));
    await turnTo(2);

    expect(list).toHaveBeenLastCalledWith(requestAt(warsaw, { page: 3 }));
    expect(paginator()?.pageIndex).toBe(2);
    expect(text('.sighting-list__page-number')).toBe('3 / 3');
  });

  it('returns to the first page when the page size changes', async () => {
    await render({ status: 'ok', ...warsaw }, of({ items: [urgent, monitor], totalCount: 45 }));
    await turnTo(2);
    await choosePageSize(20);

    expect(list).toHaveBeenLastCalledWith(requestAt(warsaw, { page: 1, pageSize: 20 }));
    expect(paginator()?.pageIndex).toBe(0);
    expect(paginator()?.pageSize).toBe(20);
    expect(text('.sighting-list__page-number')).toBe('1 / 3');
  });

  it('returns to the first page with the chosen filters when a filter changes', async () => {
    await render({ status: 'ok', ...warsaw }, of({ items: [urgent, monitor], totalCount: 25 }));
    await turnTo(1);
    await applyFilters(urgentOnly);

    expect(list).toHaveBeenLastCalledWith(
      requestAt(warsaw, { criticalities: [SightingCriticality.urgent], page: 1 }),
    );
    expect(paginator()?.pageIndex).toBe(0);
  });

  it('asks once for the last page when a later page comes back empty', async () => {
    await render({ status: 'ok', ...warsaw }, of({ items: [urgent, monitor], totalCount: 25 }));
    list.mockImplementation((request) =>
      of(request.page === 3 ? { items: [], totalCount: 15 } : { items: [monitor], totalCount: 15 }),
    );

    await turnTo(2);

    expect(list.mock.calls.map(([request]) => request.page)).toEqual([1, 3, 2]);
    expect(paginator()?.pageIndex).toBe(1);
    expect(rowNames()).toEqual(['Unnamed bird']);
  });

  it('falls back to the first page when the last page comes back empty as well', async () => {
    await render({ status: 'ok', ...warsaw }, of({ items: [urgent, monitor], totalCount: 25 }));
    list.mockImplementation((request) =>
      of(
        request.page === 3
          ? { items: [], totalCount: 15 }
          : request.page === 2
            ? { items: [], totalCount: 5 }
            : { items: [monitor], totalCount: 5 },
      ),
    );

    await turnTo(2);

    expect(list.mock.calls.map(([request]) => request.page)).toEqual([1, 3, 2, 1]);
    expect(paginator()?.pageIndex).toBe(0);
    expect(rowNames()).toEqual(['Unnamed bird']);
    expect(text('.sighting-list__page-number')).toBe('1 / 1');
  });

  it('stops after the first page when every page comes back empty', async () => {
    await render({ status: 'ok', ...warsaw }, of({ items: [urgent, monitor], totalCount: 25 }));
    list.mockReturnValue(of({ items: [], totalCount: 15 }));

    await turnTo(2);

    expect(list.mock.calls.map(([request]) => request.page)).toEqual([1, 3, 2, 1]);
    expect(root().querySelector('ul')).toBeNull();
  });

  it('shows the empty text and no paginator when nothing is nearby', async () => {
    await render({ status: 'ok', ...warsaw }, of({ items: [], totalCount: 0 }));

    expect(root().textContent).toContain('No open sightings within 10 km.');
    expect(paginator()).toBeNull();
  });

  it('shows the no-match text when a filtered list is empty', async () => {
    await render({ status: 'ok', ...warsaw });
    list.mockReturnValue(of({ items: [], totalCount: 0 }));

    await applyFilters(urgentOnly);

    expect(root().textContent).toContain('No sightings within 10 km match the selected filters.');
    expect(root().textContent).not.toContain('No open sightings within 10 km.');
    expect(paginator()).toBeNull();
  });

  it('shows an error with a retry that requests the same page again', async () => {
    await render(
      { status: 'ok', ...warsaw },
      throwError(() => new HttpErrorResponse({ status: 500 })),
    );

    expect(text('app-alert[role="alert"]')).toBe("We couldn't load the sighting list.");
    expect(paginator()).toBeNull();

    list.mockReturnValue(of({ items: [urgent], totalCount: 1 }));
    [...root().querySelectorAll<HTMLButtonElement>('app-error-retry button')]
      .find((button) => button.textContent?.trim() === 'Try again')
      ?.click();
    await settle();

    expect(list).toHaveBeenCalledTimes(2);
    expect(list).toHaveBeenLastCalledWith(requestAt(warsaw));
    expect(root().querySelector('app-alert')).toBeNull();
    expect(rowNames()).toEqual(['Limpy']);
  });
});
