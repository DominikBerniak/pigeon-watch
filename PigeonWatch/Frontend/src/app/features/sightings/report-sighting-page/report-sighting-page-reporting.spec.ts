import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { HarnessLoader } from '@angular/cdk/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatSelectHarness } from '@angular/material/select/testing';
import { By } from '@angular/platform-browser';
import { Router, provideRouter } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';
import { ErrorReportingService } from '../../../core/error-reporting/error-reporting.service';
import { GeoPoint } from '../../../core/geolocation/geo-point';
import {
  GeolocationOutcome,
  GeolocationService,
} from '../../../core/geolocation/geolocation.service';
import { LocationPicker } from '../../../shared/map/location-picker/location-picker';
import { MapView } from '../../../shared/map/map-view/map-view';
import { MapViewStub } from '../../../shared/map/testing/map-view-stub';
import { NewSighting, Sighting } from '../sighting.types';
import { SightingsApi } from '../sightings-api';
import { PhotoEncoder } from '../shared/photo-encoder';
import { ReportSightingPage } from './report-sighting-page';

const warsaw: GeoPoint = { latitude: 52.2297, longitude: 21.0122 };
const traceId = '00-abc123-def456-01';
const encodedPhoto = new Blob(['encoded-jpeg'], { type: 'image/jpeg' });

describe('ReportSightingPage failure reporting', () => {
  let create: ReturnType<typeof vi.fn<(sighting: NewSighting) => Observable<Sighting>>>;
  let report: ReturnType<typeof vi.fn>;
  let fixture: ComponentFixture<ReportSightingPage>;
  let loader: HarnessLoader;

  beforeEach(async () => {
    report = vi.fn();
    create = vi.fn<(sighting: NewSighting) => Observable<Sighting>>();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: GeolocationService,
          useValue: {
            currentPosition: (): Observable<GeolocationOutcome> => of({ status: 'denied' }),
          },
        },
        { provide: SightingsApi, useValue: { create } },
        {
          provide: PhotoEncoder,
          useValue: {
            encode: (): Observable<Blob> => of(encodedPhoto),
            createPreviewUrl: (): string => 'blob:preview',
            revokePreviewUrl: vi.fn(),
          },
        },
        { provide: ErrorReportingService, useValue: { report } },
      ],
    });
    TestBed.overrideComponent(LocationPicker, {
      remove: { imports: [MapView] },
      add: { imports: [MapViewStub] },
    });
    vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    fixture = TestBed.createComponent(ReportSightingPage);
    loader = TestbedHarnessEnvironment.loader(fixture);
    await fixture.whenStable();
  });

  async function settle(): Promise<void> {
    await fixture.whenStable();
    fixture.detectChanges();
    await fixture.whenStable();
  }

  function root(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function setValue(element: HTMLInputElement | HTMLTextAreaElement, value: string): void {
    element.value = value;
    element.dispatchEvent(new Event('input'));
    element.dispatchEvent(new Event('change'));
    element.dispatchEvent(new Event('blur'));
  }

  async function attachPhoto(): Promise<void> {
    const input = root().querySelector<HTMLInputElement>('app-photo-picker input[type="file"]');

    if (!input) throw new Error('No photo input');

    const file = new File(['original'], 'IMG_0001.jpg', { type: 'image/jpeg' });

    Object.defineProperty(input, 'files', {
      configurable: true,
      value: { length: 1, item: () => file },
    });
    input.dispatchEvent(new Event('change'));
    await settle();
  }

  function photoError(): string | undefined {
    return root().querySelector('.photo-picker__error')?.textContent?.trim();
  }

  function submitButton(): HTMLButtonElement {
    const button = root().querySelector<HTMLButtonElement>('app-submit-button button');

    if (!button) throw new Error('No submit button');

    return button;
  }

  async function resubmit(): Promise<void> {
    root().querySelector('form')?.dispatchEvent(new Event('submit', { cancelable: true }));
    await settle();
  }

  async function submitWithPhoto(error: HttpErrorResponse): Promise<void> {
    await attachPhoto();
    await submitWith(error);
  }

  async function submitWith(error: HttpErrorResponse): Promise<void> {
    create.mockReturnValue(throwError(() => error));

    const textareas = [...root().querySelectorAll<HTMLTextAreaElement>('textarea')];
    const name = root().querySelector<HTMLInputElement>('input[type="text"]');

    if (!name) throw new Error('No name input');

    setValue(name, 'Limpy');
    setValue(textareas[0], 'Grey pigeon');
    setValue(textareas[1], 'Cannot fly');

    const [status, criticality] = await loader.getAllHarnesses(MatSelectHarness);

    await status.open();
    await status.clickOptions({ text: 'Caught' });
    await criticality.open();
    await criticality.clickOptions({ text: 'Urgent' });
    (fixture.debugElement.query(By.directive(MapViewStub)).componentInstance as MapViewStub).mapClick.emit(
      warsaw,
    );
    await settle();
    root().querySelector('form')?.dispatchEvent(new Event('submit', { cancelable: true }));
    await settle();
  }

  function alertText(): string | undefined {
    return root().querySelector('app-alert[role="alert"]')?.textContent?.trim();
  }

  it('shows the reference and reports api-failure for a 500 with a trace id', async () => {
    const error = new HttpErrorResponse({ status: 500, error: { traceId } });

    await submitWith(error);

    expect(alertText()).toContain(`Reference: ${traceId}`);
    expect(report).toHaveBeenCalledExactlyOnceWith('api-failure', 'sighting.report', error);
  });

  it('reports a 500 without a trace id and shows no reference', async () => {
    const error = new HttpErrorResponse({ status: 500 });

    await submitWith(error);

    expect(alertText()).not.toBeUndefined();
    expect(alertText()).not.toContain('Reference');
    expect(report).toHaveBeenCalledExactlyOnceWith('api-failure', 'sighting.report', error);
  });

  it('does not report a validation 400 or show a reference', async () => {
    await submitWith(
      new HttpErrorResponse({
        status: 400,
        error: { status: 400, traceId, errors: { InvalidLocation: ['x'] } },
      }),
    );

    expect(alertText()).not.toBeUndefined();
    expect(alertText()).not.toContain('Reference');
    expect(report).not.toHaveBeenCalled();
  });

  it('does not report a cooldown or show a reference', async () => {
    await submitWith(
      new HttpErrorResponse({
        status: 400,
        error: {
          status: 400,
          traceId,
          errors: { CooldownActive: ['x'] },
          retryAfterSeconds: 30,
        },
      }),
    );

    expect(alertText()).toContain('30');
    expect(alertText()).not.toContain('Reference');
    expect(report).not.toHaveBeenCalled();
  });

  it('does not report a 429 or show a reference', async () => {
    await submitWith(new HttpErrorResponse({ status: 429, error: { traceId } }));

    expect(alertText()).not.toContain('Reference');
    expect(report).not.toHaveBeenCalled();
  });

  it.each([
    ['PhotoTooLarge', 'This photo is too large. Choose a smaller one or remove it.'],
    ['PhotoUnsupportedType', "This file type isn't supported. Choose a JPEG, PNG or WebP photo."],
    ['PhotoUnreadable', "We couldn't read this photo. Choose another one or remove it."],
  ])('shows %s as a photo field error without reporting it', async (code, message) => {
    await submitWithPhoto(
      new HttpErrorResponse({
        status: 400,
        error: { status: 400, traceId, errors: { [code]: ['x'] } },
      }),
    );

    expect(create.mock.calls[0][0].photo).toBe(encodedPhoto);
    expect(photoError()).toBe(message);
    expect(alertText()).toBeUndefined();
    expect(report).not.toHaveBeenCalled();
  });

  it('blocks a resend of the rejected photo and submits again once it is removed', async () => {
    await submitWithPhoto(
      new HttpErrorResponse({
        status: 400,
        error: { status: 400, errors: { PhotoTooLarge: ['x'] } },
      }),
    );

    await resubmit();

    expect(create).toHaveBeenCalledOnce();

    const remove = [...root().querySelectorAll<HTMLButtonElement>('app-photo-picker button')].find(
      (button) => button.textContent?.trim() === 'Remove photo',
    );

    remove!.click();
    await settle();
    await resubmit();

    expect(photoError()).toBe('');
    expect(create).toHaveBeenCalledTimes(2);
    expect(create.mock.calls[1][0].photo).toBeNull();
  });

  it('keeps the form filled, says retry or remove and reports a photo storage outage', async () => {
    const error = new HttpErrorResponse({
      status: 503,
      error: { status: 503, traceId, errors: { PhotoStorageUnavailable: ['x'] } },
    });

    await submitWithPhoto(error);

    expect(alertText()).toContain(
      "We couldn't save the photo right now. Try again, or remove the photo and report without it.",
    );
    expect(alertText()).toContain(`Reference: ${traceId}`);
    expect(report).toHaveBeenCalledExactlyOnceWith('api-failure', 'sighting.report', error);
    expect(root().querySelector<HTMLInputElement>('input[type="text"]')?.value).toBe('Limpy');
    expect(root().querySelectorAll<HTMLTextAreaElement>('textarea')[0].value).toBe('Grey pigeon');
    expect(root().querySelector('app-photo-picker img')).not.toBeNull();
    expect(photoError()).toBe('');
    expect(submitButton().disabled).toBe(false);

    create.mockReturnValue(throwError(() => error));
    await resubmit();

    expect(create).toHaveBeenCalledTimes(2);
    expect(create.mock.calls[1][0].photo).toBe(encodedPhoto);
  });
});
