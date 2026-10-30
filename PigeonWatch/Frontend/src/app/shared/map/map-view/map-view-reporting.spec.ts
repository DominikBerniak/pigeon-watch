import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { throwError } from 'rxjs';
import { ErrorReportingService } from '../../../core/error-reporting/error-reporting.service';
import { GeoPoint } from '../../../core/geolocation/geo-point';
import { LEAFLET_LOADER, MapView, leafletStylesheetHref } from './map-view';

const centre: GeoPoint = { latitude: 52.07, longitude: 19.48 };

describe('MapView fallback reporting', () => {
  let report: ReturnType<typeof vi.fn>;
  let fixture: ComponentFixture<MapView>;

  function configure(extra: unknown[] = []): void {
    report = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ErrorReportingService, useValue: { report } },
        ...(extra as never[]),
      ],
    });
    fixture = TestBed.createComponent(MapView);
    fixture.componentRef.setInput('label', 'Map of sightings');
    fixture.componentRef.setInput('center', centre);
    fixture.componentRef.setInput('zoom', 6);
  }

  afterEach(() => {
    document.head
      .querySelectorAll(`link[rel="stylesheet"][href="${leafletStylesheetHref}"]`)
      .forEach((link) => link.remove());
  });

  it('reports a failed Leaflet import as a map fallback and still shows the failure state', async () => {
    const failure = new Error('chunk failed');

    configure([{ provide: LEAFLET_LOADER, useValue: () => throwError(() => failure) }]);
    await fixture.whenStable();

    const root = fixture.nativeElement as HTMLElement;

    expect(root.querySelector('app-error-retry')).not.toBeNull();
    expect(report).toHaveBeenCalledExactlyOnceWith('fallback', 'map.import.failed', failure);
  });

  it('reports the first tile error once and ignores a repeat', async () => {
    configure();
    await fixture.whenStable();

    const root = fixture.nativeElement as HTMLElement;

    await vi.waitFor(() => expect(root.querySelector('img.leaflet-tile')).not.toBeNull());

    const tile = root.querySelector('img.leaflet-tile');

    tile?.dispatchEvent(new Event('error'));
    tile?.dispatchEvent(new Event('error'));

    expect(report).toHaveBeenCalledExactlyOnceWith('fallback', 'map.tile.error');
  });

  it('reports nothing while tiles load fine', async () => {
    configure();
    await fixture.whenStable();

    const root = fixture.nativeElement as HTMLElement;

    await vi.waitFor(() => expect(root.querySelector('img.leaflet-tile')).not.toBeNull());

    expect(report).not.toHaveBeenCalled();
  });
});
