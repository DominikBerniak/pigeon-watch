import { formatDate } from '@angular/common';
import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { HttpErrorResponse } from '@angular/common/http';
import { RouterLink, provideRouter } from '@angular/router';
import { NEVER, Observable, of, throwError } from 'rxjs';
import { ErrorReportingService } from '../../../../core/error-reporting/error-reporting.service';
import { PhotoEncoder } from '../photo-encoder';
import { Sighting, SightingCriticality, SightingStatus } from '../../sighting.types';
import { SightingsApi } from '../../sightings-api';
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

const withPhoto: Sighting = { ...named, hasPhoto: true };

class FakeIntersectionObserver {
  static instances: FakeIntersectionObserver[] = [];

  readonly observed: Element[] = [];
  disconnected = false;

  constructor(private readonly callback: IntersectionObserverCallback) {
    FakeIntersectionObserver.instances.push(this);
  }

  observe(element: Element): void {
    this.observed.push(element);
  }

  disconnect(): void {
    this.disconnected = true;
  }

  intersect(isIntersecting: boolean): void {
    this.callback(
      this.observed.map((target) => ({ target, isIntersecting }) as IntersectionObserverEntry),
      this as unknown as IntersectionObserver,
    );
  }
}

describe('SightingRow', () => {
  let fixture: ComponentFixture<SightingRowHost>;
  let photoThumbnail: ReturnType<typeof vi.fn<(id: string) => Observable<Blob>>>;
  let createPreviewUrl: ReturnType<typeof vi.fn<(blob: Blob) => string>>;
  let revokePreviewUrl: ReturnType<typeof vi.fn<(url: string) => void>>;
  let report: ReturnType<typeof vi.fn>;
  const thumbnailBlob = new Blob(['jpeg'], { type: 'image/jpeg' });

  beforeEach(() => {
    let previews = 0;
    photoThumbnail = vi.fn<(id: string) => Observable<Blob>>(() => of(thumbnailBlob));
    createPreviewUrl = vi.fn<(blob: Blob) => string>(() => `blob:thumb-${++previews}`);
    revokePreviewUrl = vi.fn<(url: string) => void>();
    report = vi.fn();
    FakeIntersectionObserver.instances = [];
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  async function render(sighting: Sighting): Promise<HTMLLIElement> {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: SightingsApi, useValue: { photoThumbnail } },
        { provide: PhotoEncoder, useValue: { createPreviewUrl, revokePreviewUrl } },
        { provide: ErrorReportingService, useValue: { report } },
      ],
    });
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

  it('shows no camera icon for a sighting without a photo', async () => {
    const row = await render(named);

    expect(row.querySelector('svg')).toBeNull();
    expect(row.textContent).not.toContain('Has a photo');
  });

  it('requests no thumbnail and renders no thumbnail square for a sighting without a photo', async () => {
    const row = await render(named);

    expect(photoThumbnail).not.toHaveBeenCalled();
    expect(row.querySelector('.sighting-row__thumb')).toBeNull();
  });

  it('keeps the visually hidden photo label in the name of a sighting with a photo', async () => {
    const row = await render(withPhoto);

    expect(row.querySelector('a .sighting-row__name .pw-visually-hidden')?.textContent?.trim()).toBe(
      'Has a photo',
    );
    expect(row.querySelector('a')?.textContent).toContain('Has a photo');
  });

  it('loads the thumbnail once and shows it as a decorative image in the thumbnail square', async () => {
    const row = await render(withPhoto);
    const image = row.querySelector<HTMLImageElement>('a .sighting-row__thumb img');

    expect(photoThumbnail).toHaveBeenCalledTimes(1);
    expect(photoThumbnail).toHaveBeenCalledWith('sighting-1');
    expect(createPreviewUrl).toHaveBeenCalledWith(thumbnailBlob);
    expect(image?.getAttribute('src')).toBe('blob:thumb-1');
    expect(image?.getAttribute('alt')).toBe('');
    expect(row.querySelector('.sighting-row__thumb svg')).toBeNull();
    expect(report).not.toHaveBeenCalled();
  });

  it('shows the hidden camera glyph in the thumbnail square while the thumbnail loads', async () => {
    photoThumbnail.mockReturnValue(NEVER);

    const row = await render(withPhoto);
    const icon = row.querySelector('a .sighting-row__thumb svg');

    expect(icon?.getAttribute('aria-hidden')).toBe('true');
    expect(icon?.getAttribute('focusable')).toBe('false');
    expect(row.querySelector('.sighting-row__thumb img')).toBeNull();
  });

  it('keeps the camera glyph and reports the failure when the thumbnail does not load', async () => {
    const failure = new HttpErrorResponse({ status: 500 });
    photoThumbnail.mockReturnValue(throwError(() => failure));

    const row = await render(withPhoto);

    expect(row.querySelector('.sighting-row__thumb svg')).not.toBeNull();
    expect(row.querySelector('.sighting-row__thumb img')).toBeNull();
    expect(report).toHaveBeenCalledWith('api-failure', 'sighting.row.photo', failure);
    expect(createPreviewUrl).not.toHaveBeenCalled();
  });

  it.each([400, 401, 429])(
    'keeps the camera glyph without reporting when the thumbnail fails with the expected status %i',
    async (status) => {
      photoThumbnail.mockReturnValue(throwError(() => new HttpErrorResponse({ status })));

      const row = await render(withPhoto);

      expect(row.querySelector('.sighting-row__thumb svg')).not.toBeNull();
      expect(report).not.toHaveBeenCalled();
    },
  );

  it('reports a missing thumbnail because the sighting says it has a photo', async () => {
    const failure = new HttpErrorResponse({ status: 404 });
    photoThumbnail.mockReturnValue(throwError(() => failure));

    await render(withPhoto);

    expect(report).toHaveBeenCalledWith('api-failure', 'sighting.row.photo', failure);
  });

  it('revokes the thumbnail URL when the row is destroyed', async () => {
    await render(withPhoto);

    fixture.destroy();

    expect(revokePreviewUrl).toHaveBeenCalledWith('blob:thumb-1');
  });

  it('revokes the old thumbnail URL and loads the new one when the sighting changes', async () => {
    await render(withPhoto);

    fixture.componentInstance.sighting.set({ ...withPhoto, id: 'sighting-2' });
    await fixture.whenStable();

    expect(revokePreviewUrl).toHaveBeenCalledWith('blob:thumb-1');
    expect(photoThumbnail).toHaveBeenLastCalledWith('sighting-2');
  });

  it('waits until the row scrolls into view before requesting the thumbnail', async () => {
    vi.stubGlobal('IntersectionObserver', FakeIntersectionObserver);

    const row = await render(withPhoto);
    const observer = FakeIntersectionObserver.instances[0];

    expect(observer.observed).toEqual([row]);
    expect(photoThumbnail).not.toHaveBeenCalled();

    observer.intersect(false);
    await fixture.whenStable();

    expect(photoThumbnail).not.toHaveBeenCalled();

    observer.intersect(true);
    await fixture.whenStable();

    expect(photoThumbnail).toHaveBeenCalledTimes(1);
    expect(observer.disconnected).toBe(true);
    expect(row.querySelector('.sighting-row__thumb img')?.getAttribute('src')).toBe('blob:thumb-1');
  });

  it('disconnects the observer when a row that never came into view is destroyed', async () => {
    vi.stubGlobal('IntersectionObserver', FakeIntersectionObserver);

    await render(withPhoto);
    fixture.destroy();

    expect(FakeIntersectionObserver.instances[0].disconnected).toBe(true);
    expect(photoThumbnail).not.toHaveBeenCalled();
  });

  it('follows a new now from the parent', async () => {
    const row = await render(named);

    fixture.componentInstance.now.set(new Date('2026-10-11T13:00:00Z'));
    await fixture.whenStable();

    expect(row.querySelector('time')?.textContent?.trim()).toBe('1 h ago');
  });
});
