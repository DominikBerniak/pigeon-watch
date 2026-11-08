import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { By } from '@angular/platform-browser';
import { Router, RouterLink, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { NEVER, Observable, Subject, of, throwError } from 'rxjs';
import { ErrorReportingService } from '../../../core/error-reporting/error-reporting.service';
import { MapView } from '../../../shared/map/map-view/map-view';
import { MapViewStub } from '../../../shared/map/testing/map-view-stub';
import {
  Sighting,
  SightingCommentPage,
  SightingCriticality,
  SightingStatus,
  SightingThreadPage,
  SightingThreadStart,
} from '../sighting.types';
import { PhotoEncoder } from '../shared/photo-encoder';
import { SightingsApi } from '../sightings-api';
import { SightingDetailPage } from './sighting-detail-page';

const sighting: Sighting = {
  id: 'sighting-1',
  name: 'Limpy',
  description: 'Grey pigeon with a ruffled wing',
  behaviour: 'Cannot fly',
  latitude: 52.2297,
  longitude: 21.0122,
  status: SightingStatus.caught,
  criticality: SightingCriticality.needsHelpSoon,
  reporterDisplayName: 'Jan K',
  createdAt: '2026-10-11T12:00:00Z',
  hasPhoto: false,
  isMine: false,
};

const withPhoto: Sighting = { ...sighting, hasPhoto: true };
const started: SightingThreadStart = {
  thread: {
    id: 'thread-9',
    sightingId: 'sighting-1',
    sightingName: 'Limpy',
    counterpartDisplayName: 'Jan K',
    isOwner: false,
    lastMessageAt: '2026-11-07T12:00:00Z',
  },
  message: {
    id: 'message-1',
    text: 'Is it still there?',
    senderDisplayName: 'Ola N',
    createdAt: '2026-11-07T12:00:00Z',
    isMine: true,
  },
};

@Component({ template: '' })
class MessagesRouteStub {}

const photoBlob = new Blob(['jpeg'], { type: 'image/jpeg' });

describe('SightingDetailPage', () => {
  let get: ReturnType<typeof vi.fn<(id: string) => Observable<Sighting>>>;
  let photo: ReturnType<typeof vi.fn<(id: string) => Observable<Blob>>>;
  let createPreviewUrl: ReturnType<typeof vi.fn<(blob: Blob) => string>>;
  let revokePreviewUrl: ReturnType<typeof vi.fn<(url: string) => void>>;
  let startThread: ReturnType<
    typeof vi.fn<(sightingId: string, text: string) => Observable<SightingThreadStart>>
  >;
  let threads: ReturnType<
    typeof vi.fn<(sightingId: string, page: number) => Observable<SightingThreadPage>>
  >;
  let report: ReturnType<typeof vi.fn>;
  let harness: RouterTestingHarness;

  beforeEach(async () => {
    let previews = 0;

    get = vi.fn<(id: string) => Observable<Sighting>>(() => of(sighting));
    photo = vi.fn<(id: string) => Observable<Blob>>(() => of(photoBlob));
    createPreviewUrl = vi.fn<(blob: Blob) => string>(() => `blob:photo-${++previews}`);
    revokePreviewUrl = vi.fn<(url: string) => void>();
    startThread = vi.fn<(sightingId: string, text: string) => Observable<SightingThreadStart>>(
      () => of(started),
    );
    threads = vi.fn<(sightingId: string, page: number) => Observable<SightingThreadPage>>(() =>
      of({ items: [], totalCount: 0 }),
    );
    report = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([
          { path: 'sightings/:id', component: SightingDetailPage },
          { path: 'messages/:sightingId/:threadId', component: MessagesRouteStub },
        ]),
        {
          provide: SightingsApi,
          useValue: {
            get,
            photo,
            comments: (): Observable<SightingCommentPage> => of({ items: [], totalCount: 0 }),
            threads,
            startThread,
          },
        },
        { provide: PhotoEncoder, useValue: { createPreviewUrl, revokePreviewUrl } },
        { provide: ErrorReportingService, useValue: { report } },
      ],
    });
    TestBed.overrideComponent(SightingDetailPage, {
      remove: { imports: [MapView] },
      add: { imports: [MapViewStub] },
    });
    harness = await RouterTestingHarness.create();
  });

  async function open(id = 'sighting-1'): Promise<HTMLElement> {
    await harness.navigateByUrl(`/sightings/${id}`);
    await settle();

    return harness.routeNativeElement as HTMLElement;
  }

  async function settle(): Promise<void> {
    await harness.fixture.whenStable();
    harness.fixture.detectChanges();
    await harness.fixture.whenStable();
  }

  function backLink(): RouterLink {
    return harness.routeDebugElement!.query(By.css('mat-card-actions a')).injector.get(RouterLink);
  }

  function button(root: HTMLElement, text: string): HTMLButtonElement {
    const match = [...root.querySelectorAll<HTMLButtonElement>('button')].find(
      (candidate) => candidate.textContent?.trim() === text,
    );

    if (!match) throw new Error(`No button "${text}"`);

    return match;
  }

  function pageAlert(root: HTMLElement): Element | null {
    return (
      [...root.querySelectorAll('app-alert')].find(
        (alert) => !alert.closest('app-sighting-comments'),
      ) ?? null
    );
  }

  function pageLinks(root: HTMLElement): HTMLAnchorElement[] {
    return [...root.querySelectorAll('a')].filter((link) => !link.closest('app-sighting-comments'));
  }

  function sightingReports(): unknown[][] {
    return report.mock.calls.filter(([, context]) => !String(context).startsWith('sighting.comments.'));
  }

  function rows(root: HTMLElement): (string | undefined)[][] {
    return [
      ...root.querySelectorAll(
        '.sighting-detail__summary > dl > div, .sighting-detail__details > dl > div',
      ),
    ].map((row) => [
      row.querySelector('dt')?.textContent?.trim(),
      row.querySelector('dd')?.textContent?.trim(),
    ]);
  }

  function mapStub(): MapViewStub | null {
    const element = harness.fixture.debugElement.query(By.directive(MapViewStub));

    return element ? (element.componentInstance as MapViewStub) : null;
  }

  it('loads the sighting by id and shows its data with the coordinates as text', async () => {
    const root = await open();

    expect(get).toHaveBeenCalledExactlyOnceWith('sighting-1');
    expect(root.querySelector('app-page-card h1')?.textContent?.trim()).toBe('Limpy');
    expect(rows(root).filter(([label]) => label !== 'Reported')).toEqual([
      ['Status', 'Caught'],
      ['Criticality', 'Needs help soon'],
      ['Reported by', 'Jan K'],
      ['Description', 'Grey pigeon with a ruffled wing'],
      ['Behaviour', 'Cannot fly'],
      ['Location', '52.22970, 21.01220'],
    ]);
    expect(root.querySelector('time')?.getAttribute('datetime')).toBe(sighting.createdAt);
    expect(root.querySelector('time')?.textContent?.trim()).not.toBe('');
  });

  it('widens the page card while the map is shown', async () => {
    const root = await open();
    const card = root.querySelector('app-page-card');

    expect(card?.classList).not.toContain('page-card-host--wide');

    button(root, 'Show map').click();
    await settle();

    expect(card?.classList).toContain('page-card-host--wide');

    button(root, 'Hide map').click();
    await settle();

    expect(card?.classList).not.toContain('page-card-host--wide');
  });

  it('shows the unnamed label as the title for a sighting without a name', async () => {
    get.mockReturnValue(of({ ...sighting, name: null }));

    const root = await open();

    expect(root.querySelector('app-page-card h1')?.textContent?.trim()).toBe('Unnamed bird');
  });

  it('puts the map toggle in the card header beside the title', async () => {
    const root = await open();

    expect(root.querySelector('app-page-card header button')).toBe(button(root, 'Show map'));
  });

  it('renders no map until "Show map" is pressed, then one map with a single marker', async () => {
    const root = await open();

    expect(root.querySelector('app-map-view')).toBeNull();
    expect(button(root, 'Show map').getAttribute('aria-expanded')).toBe('false');

    button(root, 'Show map').click();
    await settle();

    expect(root.querySelectorAll('app-map-view').length).toBe(1);
    expect(mapStub()?.center()).toEqual({ latitude: 52.2297, longitude: 21.0122 });
    expect(mapStub()?.markers()).toEqual([
      {
        id: 'sighting-1',
        latitude: 52.2297,
        longitude: 21.0122,
        kind: 'needsHelpSoon',
        label: 'Limpy, Needs help soon',
      },
    ]);
    expect(button(root, 'Hide map').getAttribute('aria-expanded')).toBe('true');

    button(root, 'Hide map').click();
    await settle();

    expect(root.querySelector('app-map-view')).toBeNull();
  });

  it('shows the not-found state for a 404', async () => {
    get.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 404 })));

    const root = await open('missing');

    expect(root.querySelector('app-alert')?.textContent?.trim()).toBe(
      "This sighting doesn't exist or is no longer available.",
    );
    expect(root.querySelector('dl')).toBeNull();
    expect(root.querySelector('app-page-card h1')?.textContent?.trim()).toBe('Sighting');
    expect([...root.querySelectorAll('button')].map((b) => b.textContent?.trim())).not.toContain(
      'Try again',
    );
  });

  it('shows a generic error with a retry that loads again', async () => {
    get.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));
    const root = await open();

    expect(root.querySelector('app-alert[role="alert"]')?.textContent?.trim()).toBe(
      "We couldn't reach the sightings right now. Try again.",
    );

    get.mockReturnValue(of(sighting));
    button(root, 'Try again').click();
    await settle();

    expect(get).toHaveBeenCalledTimes(2);
    expect(pageAlert(root)).toBeNull();
    expect(root.querySelector('app-page-card h1')?.textContent?.trim()).toBe('Limpy');
  });

  it('links back to the map from the card actions footer', async () => {
    const root = await open();
    const back = root.querySelector<HTMLAnchorElement>('mat-card-actions a');

    expect(back?.textContent?.trim()).toBe('Back to map');
    expect(back?.getAttribute('href')).toBe('/');
    expect(pageLinks(root)).toHaveLength(1);
    expect(backLink().state).toBeUndefined();
  });

  it('hands the map filters back through Back to map when opened from the map', async () => {
    const mapFilters = { statuses: [SightingStatus.spotted], criticalities: [] };

    await TestBed.inject(Router).navigateByUrl('/sightings/sighting-1', {
      state: { mapFilters },
    });
    await settle();

    const back = (harness.routeNativeElement as HTMLElement).querySelector('mat-card-actions a');

    expect(back?.textContent?.trim()).toBe('Back to map');
    expect(back?.getAttribute('href')).toBe('/');
    expect(backLink().state).toEqual({ mapFilters });
  });

  it('links back to the list with its centre and filters when opened from a list row', async () => {
    const returnToList = {
      centre: { latitude: 52.2297, longitude: 21.0122 },
      filters: { statuses: [SightingStatus.spotted], criticalities: [SightingCriticality.urgent] },
    };

    await TestBed.inject(Router).navigateByUrl('/sightings/sighting-1', {
      state: { returnToList },
    });
    await settle();

    const root = harness.routeNativeElement as HTMLElement;
    const back = root.querySelector<HTMLAnchorElement>('mat-card-actions a');
    const link = harness
      .routeDebugElement!.query(By.css('mat-card-actions a'))
      .injector.get(RouterLink);

    expect(back?.textContent?.trim()).toBe('Back to list');
    expect(back?.getAttribute('href')).toBe('/sightings/list');
    expect(link.state).toEqual(returnToList);
    expect(pageLinks(root)).toHaveLength(1);
  });

  it('keeps the map link when the list state is not a valid hand-over', async () => {
    await TestBed.inject(Router).navigateByUrl('/sightings/sighting-1', {
      state: { returnToList: { centre: { latitude: 999, longitude: 0 }, filters: null } },
    });
    await settle();

    const back = (harness.routeNativeElement as HTMLElement).querySelector<HTMLAnchorElement>(
      'mat-card-actions a',
    );

    expect(back?.textContent?.trim()).toBe('Back to map');
    expect(back?.getAttribute('href')).toBe('/');
  });

  it('shows the Sighting heading and a loading indicator while the sighting loads', async () => {
    get.mockReturnValue(NEVER);

    const root = await open();

    expect(root.querySelector('app-page-card h1')?.textContent?.trim()).toBe('Sighting');
    expect(root.querySelector('app-loading-indicator')).not.toBeNull();
    expect(root.querySelector('dl')).toBeNull();
  });

  it('sets the tab title from the sighting name once it has loaded', async () => {
    await open();

    expect(document.title).toBe('Limpy – PigeonWatch');
  });

  it('uses the Sighting tab title while loading', async () => {
    get.mockReturnValue(NEVER);

    await open();

    expect(document.title).toBe('Sighting – PigeonWatch');
  });

  it('uses the Sighting tab title in the not-found state', async () => {
    get.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 404 })));

    await open('missing');

    expect(document.title).toBe('Sighting – PigeonWatch');
  });

  it('uses the Sighting tab title in the error state', async () => {
    get.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));

    await open();

    expect(document.title).toBe('Sighting – PigeonWatch');
  });

  it('resets the tab title to the app name when the page is destroyed', async () => {
    await open();

    harness.fixture.destroy();

    expect(document.title).toBe('PigeonWatch');
  });

  it('focuses the heading once the sighting has loaded by navigation', async () => {
    const root = await open();

    expect(document.activeElement).toBe(root.querySelector('app-page-card h1'));
  });

  it('focuses the heading in the not-found state', async () => {
    get.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 404 })));

    const root = await open('missing');

    expect(document.activeElement).toBe(root.querySelector('app-page-card h1'));
  });

  it('focuses the heading in the error state', async () => {
    get.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));

    const root = await open();

    expect(document.activeElement).toBe(root.querySelector('app-page-card h1'));
  });

  it('does not focus the heading while the sighting is still loading', async () => {
    get.mockReturnValue(NEVER);

    const root = await open();

    expect(document.activeElement).not.toBe(root.querySelector('app-page-card h1'));
  });

  it('focuses the heading again after a retry that succeeds', async () => {
    get.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));
    const root = await open();

    (document.activeElement as HTMLElement | null)?.blur();

    expect(document.activeElement).not.toBe(root.querySelector('app-page-card h1'));

    get.mockReturnValue(of(sighting));
    button(root, 'Try again').click();
    await settle();

    expect(document.activeElement).toBe(root.querySelector('app-page-card h1'));
  });

  it('does not take focus on first load when the page is not created by a navigation', async () => {
    const fixture = TestBed.createComponent(SightingDetailPage);

    await fixture.whenStable();
    fixture.detectChanges();
    await fixture.whenStable();

    expect(fixture.nativeElement.querySelector('app-page-card h1')?.textContent?.trim()).toBe(
      'Limpy',
    );
    expect(document.activeElement).not.toBe(fixture.nativeElement.querySelector('h1'));
  });

  it('gives the map a one-sighting label, display-only markers and the criticality in the marker label', async () => {
    const root = await open();

    button(root, 'Show map').click();
    await settle();

    expect(mapStub()?.interactiveMarkers()).toBe(false);
    expect(mapStub()?.label()).toBe('Map showing where this sighting was reported');
    const labels = (mapStub()?.markers() ?? []).map((marker) => marker.label);

    expect(labels).toEqual(['Limpy, Needs help soon']);
  });

  it('hides a shown map and follows the title when the sighting reloads after a failure', async () => {
    const first = new Subject<Sighting>();

    get.mockReturnValueOnce(first);

    const root = await open();

    first.next(sighting);
    await settle();
    button(root, 'Show map').click();
    await settle();

    expect(root.querySelectorAll('app-map-view').length).toBe(1);
    expect(document.title).toBe('Limpy – PigeonWatch');

    first.error(new HttpErrorResponse({ status: 500 }));
    await settle();

    expect(document.title).toBe('Sighting – PigeonWatch');

    get.mockReturnValue(of(sighting));
    button(root, 'Try again').click();
    await settle();

    expect(root.querySelector('app-map-view')).toBeNull();
    expect(button(root, 'Show map').getAttribute('aria-expanded')).toBe('false');
  });

  it('starts over when the route moves to another sighting: loading, hidden map, new title, heading focus', async () => {
    const second = new Subject<Sighting>();

    get.mockImplementation((id) =>
      id === 'a' ? of({ ...sighting, id: 'a', name: 'First' }) : second,
    );

    const root = await open('a');

    button(root, 'Show map').click();
    await settle();

    expect(root.querySelectorAll('app-map-view').length).toBe(1);
    expect(document.title).toBe('First – PigeonWatch');

    (document.activeElement as HTMLElement | null)?.blur();

    await harness.navigateByUrl('/sightings/b');
    await settle();

    expect(get).toHaveBeenLastCalledWith('b');
    expect(root.querySelector('app-map-view')).toBeNull();
    expect(root.querySelector('app-page-card h1')?.textContent?.trim()).toBe('Sighting');
    expect(root.querySelector('app-loading-indicator')).not.toBeNull();
    expect(document.title).toBe('Sighting – PigeonWatch');
    expect(document.activeElement).not.toBe(root.querySelector('app-page-card h1'));

    second.next({ ...sighting, id: 'b', name: 'Second' });
    await settle();

    expect(root.querySelector('app-page-card h1')?.textContent?.trim()).toBe('Second');
    expect(document.title).toBe('Second – PigeonWatch');
    expect(document.activeElement).toBe(root.querySelector('app-page-card h1'));
  });

  it('ignores a slow answer for the sighting the route has already left', async () => {
    const slow = new Subject<Sighting>();

    get.mockImplementation((id) =>
      id === 'a' ? slow : of({ ...sighting, id: 'b', name: 'Second' }),
    );

    const root = await open('a');

    await harness.navigateByUrl('/sightings/b');
    await settle();

    slow.next({ ...sighting, id: 'a', name: 'First' });
    await settle();

    expect(root.querySelector('app-page-card h1')?.textContent?.trim()).toBe('Second');
    expect(document.title).toBe('Second – PigeonWatch');
  });

  function contactButton(root: HTMLElement): HTMLButtonElement | null {
    return root.querySelector<HTMLButtonElement>('button.sighting-detail__contact');
  }

  async function sendFromDialog(text: string): Promise<void> {
    const dialog = document.querySelector<HTMLElement>('app-message-owner-dialog')!;
    const field = dialog.querySelector<HTMLTextAreaElement>('textarea')!;

    field.value = text;
    field.dispatchEvent(new Event('input'));
    dialog.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    await settle();
  }

  it('offers "Message {reporter}" on a sighting of someone else', async () => {
    const root = await open();

    expect(contactButton(root)?.getAttribute('aria-label')).toBe('Message Jan K');
    expect(contactButton(root)?.textContent?.trim()).toBe('');
    expect(contactButton(root)?.querySelector('svg')?.getAttribute('aria-hidden')).toBe('true');
    expect(contactButton(root)?.getAttribute('type')).toBe('button');
    expect(contactButton(root)?.closest('.sighting-detail__reporter')?.textContent).toContain(
      'Jan K',
    );
  });

  it('shows no message button on an own sighting', async () => {
    get.mockReturnValue(of({ ...sighting, isMine: true }));

    const root = await open();

    expect(rows(root)).toHaveLength(7);
    expect(contactButton(root)).toBeNull();
    expect(root.textContent).not.toContain('Message Jan K');
  });

  it('opens the message dialog for the reporter without sending anything', async () => {
    const root = await open();

    contactButton(root)!.click();
    await settle();

    const dialog = document.querySelector<HTMLElement>('app-message-owner-dialog');

    expect(dialog?.querySelector('h2')?.textContent?.trim()).toBe('Message Jan K');
    expect(startThread).not.toHaveBeenCalled();

    button(dialog!, 'Cancel').click();
    await settle();
    await vi.waitFor(() =>
      expect(document.querySelector('app-message-owner-dialog')).toBeNull(),
    );
    expect(TestBed.inject(Router).url).toBe('/sightings/sighting-1');
  });

  it('sends from the dialog and opens the conversation with the sighting and returned thread ids', async () => {
    const root = await open();

    contactButton(root)!.click();
    await settle();
    await sendFromDialog('Is it still there?');
    await vi.waitFor(() =>
      expect(TestBed.inject(Router).url).toBe('/messages/sighting-1/thread-9'),
    );

    expect(startThread).toHaveBeenCalledExactlyOnceWith('sighting-1', 'Is it still there?');
    expect(document.querySelector('app-message-owner-dialog')).toBeNull();
  });

  it('stays on the sighting with the dialog open when sending fails', async () => {
    startThread.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));

    const root = await open();

    contactButton(root)!.click();
    await settle();
    await sendFromDialog('Is it still there?');

    expect(document.querySelector('app-message-owner-dialog app-alert')).not.toBeNull();
    expect(TestBed.inject(Router).url).toBe('/sightings/sighting-1');
    expect(report).toHaveBeenCalledExactlyOnceWith(
      'api-failure',
      'sighting.threads.start',
      expect.any(HttpErrorResponse),
    );

    TestBed.inject(MatDialog).closeAll();
    await settle();
  });

  function conversationLink(root: HTMLElement): HTMLAnchorElement | null {
    return root.querySelector<HTMLAnchorElement>('a.sighting-detail__contact');
  }

  it('looks up the own thread of the caller on a sighting of someone else', async () => {
    const root = await open();

    expect(threads).toHaveBeenCalledExactlyOnceWith('sighting-1', 1);
    expect(conversationLink(root)).toBeNull();
    expect(contactButton(root)).not.toBeNull();
  });

  it('sends no thread lookup on an own sighting', async () => {
    get.mockReturnValue(of({ ...sighting, isMine: true }));

    await open();

    expect(threads).not.toHaveBeenCalled();
  });

  it('links the email icon to the existing conversation instead of the dialog', async () => {
    threads.mockReturnValue(of({ items: [started.thread], totalCount: 1 }));

    const root = await open();
    const link = conversationLink(root);

    expect(contactButton(root)).toBeNull();
    expect(link?.getAttribute('href')).toBe('/messages/sighting-1/thread-9');
    expect(link?.getAttribute('aria-label')).toBe('Open conversation with Jan K');
    expect(link?.textContent?.trim()).toBe('');
    expect(link?.querySelector('svg')?.getAttribute('aria-hidden')).toBe('true');

    link!.click();
    await settle();
    await vi.waitFor(() =>
      expect(TestBed.inject(Router).url).toBe('/messages/sighting-1/thread-9'),
    );

    expect(document.querySelector('app-message-owner-dialog')).toBeNull();
    expect(startThread).not.toHaveBeenCalled();
  });

  it('keeps the dialog path and shows the details while the thread lookup is pending', async () => {
    threads.mockReturnValue(NEVER);

    const root = await open();

    expect(rows(root)).toHaveLength(7);
    expect(conversationLink(root)).toBeNull();

    contactButton(root)!.click();
    await settle();

    expect(document.querySelector('app-message-owner-dialog')).not.toBeNull();

    TestBed.inject(MatDialog).closeAll();
    await settle();
  });

  it('keeps the dialog path and reports when the thread lookup fails', async () => {
    const error = new HttpErrorResponse({ status: 500 });

    threads.mockReturnValue(throwError(() => error));

    const root = await open();

    expect(conversationLink(root)).toBeNull();
    expect(sightingReports()).toEqual([['api-failure', 'sighting.threads.load', error]]);

    contactButton(root)!.click();
    await settle();

    expect(document.querySelector('app-message-owner-dialog')).not.toBeNull();

    TestBed.inject(MatDialog).closeAll();
    await settle();
  });

  it.each([401, 404, 429])(
    'keeps the dialog path without reporting when the thread lookup fails with %i',
    async (status) => {
      threads.mockReturnValue(throwError(() => new HttpErrorResponse({ status })));

      const root = await open();

      expect(conversationLink(root)).toBeNull();
      expect(contactButton(root)).not.toBeNull();
      expect(sightingReports()).toEqual([]);
    },
  );

  it('looks the thread up again for the next sighting the route moves to', async () => {
    threads.mockImplementation((sightingId) =>
      of(
        sightingId === 'a'
          ? { items: [{ ...started.thread, sightingId: 'a' }], totalCount: 1 }
          : { items: [], totalCount: 0 },
      ),
    );
    get.mockImplementation((id) => of({ ...sighting, id }));

    const root = await open('a');

    expect(conversationLink(root)?.getAttribute('href')).toBe('/messages/a/thread-9');

    await harness.navigateByUrl('/sightings/b');
    await settle();

    expect(threads).toHaveBeenLastCalledWith('b', 1);
    expect(conversationLink(root)).toBeNull();
    expect(contactButton(root)).not.toBeNull();
  });

  it('sends no photo request and shows no photo when the sighting has none', async () => {
    const root = await open();

    expect(photo).not.toHaveBeenCalled();
    expect(root.querySelector('.sighting-detail__photo')).toBeNull();
    expect(root.querySelector('.sighting-detail__photo-placeholder')).toBeNull();
  });

  it('shows the photo as a button beside the key facts with translated alt text', async () => {
    get.mockReturnValue(of(withPhoto));

    const root = await open();
    const image = root.querySelector<HTMLImageElement>('.sighting-detail__photo img');

    expect(photo).toHaveBeenCalledExactlyOnceWith('sighting-1');
    expect(createPreviewUrl).toHaveBeenCalledExactlyOnceWith(photoBlob);
    expect(image?.getAttribute('src')).toBe('blob:photo-1');
    expect(image?.getAttribute('alt')).toBe('Photo of Limpy');
    expect(root.querySelector('.sighting-detail__photo + dl.sighting-detail__facts')).not.toBeNull();
    expect(sightingReports()).toEqual([]);
  });

  it('opens the full photo in a dialog from the photo button and closes it again', async () => {
    get.mockReturnValue(of(withPhoto));

    const root = await open();
    const trigger = root.querySelector<HTMLButtonElement>('button.sighting-detail__photo');

    expect(trigger?.getAttribute('aria-label')).toBe('Open the full photo');

    const opened = vi.spyOn(TestBed.inject(MatDialog), 'open');

    trigger?.click();
    await settle();

    const dialog = document.querySelector<HTMLElement>('app-photo-dialog');
    const full = dialog?.querySelector<HTMLImageElement>('img');

    expect(dialog?.querySelector('h2')?.textContent?.trim()).toBe('Limpy');
    expect(full?.getAttribute('src')).toBe('blob:photo-1');
    expect(full?.getAttribute('alt')).toBe('Photo of Limpy');
    expect(opened.mock.calls[0]?.[1]?.autoFocus).toBe('dialog');

    button(dialog!, 'Close').click();
    await settle();
    await vi.waitFor(() => expect(document.querySelector('app-photo-dialog')).toBeNull());
    expect(revokePreviewUrl).not.toHaveBeenCalled();
  });

  it('names an unnamed sighting in the photo alt text', async () => {
    get.mockReturnValue(of({ ...withPhoto, name: null }));

    const root = await open();

    expect(root.querySelector('.sighting-detail__photo img')?.getAttribute('alt')).toBe('Photo of Unnamed bird');
  });

  it('shows a placeholder while the photo loads and the details without waiting for it', async () => {
    get.mockReturnValue(of(withPhoto));
    photo.mockReturnValue(NEVER);

    const root = await open();
    const spinner = root.querySelector(
      '.sighting-detail__photo-placeholder app-loading-indicator mat-progress-spinner',
    );

    expect(spinner?.getAttribute('aria-label')).toBe('Loading the photo…');
    expect(root.querySelector('.sighting-detail__photo')).toBeNull();
    expect(rows(root)).toHaveLength(7);
    expect(root.querySelector('app-page-card h1')?.textContent?.trim()).toBe('Limpy');
  });

  it('shows an inline message and reports when the photo fails to load', async () => {
    const error = new HttpErrorResponse({ status: 500 });

    get.mockReturnValue(of(withPhoto));
    photo.mockReturnValue(throwError(() => error));

    const root = await open();

    expect(root.querySelector('.sighting-detail__details > app-alert')?.textContent?.trim()).toBe(
      "We couldn't load the photo right now.",
    );
    expect(root.querySelector('.sighting-detail__photo')).toBeNull();
    expect(rows(root)).toHaveLength(7);
    expect(sightingReports()).toEqual([['api-failure', 'sighting.detail.photo', error]]);
  });

  it.each([400, 401, 429])(
    'shows the inline message without reporting when the photo fails with the expected status %i',
    async (status) => {
      get.mockReturnValue(of(withPhoto));
      photo.mockReturnValue(throwError(() => new HttpErrorResponse({ status })));

      const root = await open();

      expect(root.querySelector('.sighting-detail__details > app-alert')?.textContent?.trim()).toBe(
        "We couldn't load the photo right now.",
      );
      expect(sightingReports()).toEqual([]);
    },
  );

  it('reports a missing photo blob because the sighting says it has a photo', async () => {
    const error = new HttpErrorResponse({ status: 404 });

    get.mockReturnValue(of(withPhoto));
    photo.mockReturnValue(throwError(() => error));

    await open();

    expect(sightingReports()).toEqual([['api-failure', 'sighting.detail.photo', error]]);
  });

  it('revokes the photo URL when the route moves to another sighting', async () => {
    get.mockImplementation((id) => of({ ...withPhoto, id, hasPhoto: id === 'a' }));

    const root = await open('a');

    expect(root.querySelector('.sighting-detail__photo img')?.getAttribute('src')).toBe('blob:photo-1');
    expect(revokePreviewUrl).not.toHaveBeenCalled();

    await harness.navigateByUrl('/sightings/b');
    await settle();

    expect(revokePreviewUrl).toHaveBeenCalledExactlyOnceWith('blob:photo-1');
    expect(photo).toHaveBeenCalledExactlyOnceWith('a');
    expect(root.querySelector('.sighting-detail__photo')).toBeNull();
  });

  it('revokes the photo URL when the page is destroyed', async () => {
    get.mockReturnValue(of(withPhoto));

    await open();

    expect(revokePreviewUrl).not.toHaveBeenCalled();

    harness.fixture.destroy();

    expect(revokePreviewUrl).toHaveBeenCalledExactlyOnceWith('blob:photo-1');
  });
});
