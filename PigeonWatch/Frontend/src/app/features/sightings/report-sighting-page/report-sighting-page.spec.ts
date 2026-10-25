import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { HarnessLoader } from '@angular/cdk/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatSelectHarness } from '@angular/material/select/testing';
import { By } from '@angular/platform-browser';
import { Router, provideRouter } from '@angular/router';
import { Observable, Subject, of, throwError } from 'rxjs';
import { GeoPoint } from '../../../core/geolocation/geo-point';
import {
  GeolocationOutcome,
  GeolocationService,
} from '../../../core/geolocation/geolocation.service';
import { LocationPicker } from '../../../shared/map/location-picker/location-picker';
import { MapView } from '../../../shared/map/map-view/map-view';
import { MapViewStub } from '../../../shared/map/testing/map-view-stub';
import { NewSighting, Sighting, SightingCriticality, SightingStatus } from '../sighting.types';
import { SightingsApi } from '../sightings-api';
import { ReportSightingPage } from './report-sighting-page';

const warsaw: GeoPoint = { latitude: 52.2297, longitude: 21.0122 };

const created: Sighting = {
  id: 'sighting-7',
  name: 'Limpy',
  description: 'Grey pigeon',
  behaviour: 'Cannot fly',
  latitude: warsaw.latitude,
  longitude: warsaw.longitude,
  status: SightingStatus.caught,
  criticality: SightingCriticality.urgent,
  reporterDisplayName: 'Jan K',
  createdAt: '2026-10-11T12:00:00Z',
};

function spyOnNavigate() {
  return vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
}

describe('ReportSightingPage', () => {
  let create: ReturnType<typeof vi.fn<(sighting: NewSighting) => Observable<Sighting>>>;
  let navigate: ReturnType<typeof spyOnNavigate>;
  let fixture: ComponentFixture<ReportSightingPage>;
  let loader: HarnessLoader;

  beforeEach(async () => {
    create = vi.fn<(sighting: NewSighting) => Observable<Sighting>>(() => of(created));
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
      ],
    });
    TestBed.overrideComponent(LocationPicker, {
      remove: { imports: [MapView] },
      add: { imports: [MapViewStub] },
    });
    navigate = spyOnNavigate();
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

  function control<T extends HTMLElement>(selector: string): T {
    const element = root().querySelector<T>(selector);

    if (!element) throw new Error(`No control ${selector}`);

    return element;
  }

  function fields(): HTMLElement[] {
    return [...root().querySelectorAll<HTMLElement>('mat-form-field')];
  }

  function setValue(element: HTMLInputElement | HTMLTextAreaElement, value: string): void {
    element.value = value;
    element.dispatchEvent(new Event('input'));
    element.dispatchEvent(new Event('change'));
    element.dispatchEvent(new Event('blur'));
  }

  async function selects(): Promise<MatSelectHarness[]> {
    return loader.getAllHarnesses(MatSelectHarness);
  }

  async function optionLabels(select: MatSelectHarness): Promise<string[]> {
    await select.open();

    const labels = await Promise.all((await select.getOptions()).map((option) => option.getText()));

    await select.close();

    return labels;
  }

  async function choose(select: MatSelectHarness, label: string): Promise<void> {
    await select.open();
    await select.clickOptions({ text: label });
  }

  function textareas(): HTMLTextAreaElement[] {
    return [...root().querySelectorAll<HTMLTextAreaElement>('textarea')];
  }

  function map(): MapViewStub {
    return fixture.debugElement.query(By.directive(MapViewStub)).componentInstance as MapViewStub;
  }

  async function fillValid(name = 'Limpy'): Promise<void> {
    setValue(control<HTMLInputElement>('input[type="text"]'), name);
    setValue(textareas()[0], 'Grey pigeon');
    setValue(textareas()[1], 'Cannot fly');
    const [status, criticality] = await selects();

    await choose(status, 'Caught');
    await choose(criticality, 'Urgent');
    map().mapClick.emit(warsaw);
    await settle();
  }

  async function submit(): Promise<void> {
    control<HTMLFormElement>('form').dispatchEvent(new Event('submit', { cancelable: true }));
    await settle();
  }

  function fieldErrors(): string[] {
    return fields().map((field) => field.querySelector('mat-error')?.textContent?.trim() ?? '');
  }

  function locationError(): string | undefined {
    return root().querySelector('.location-picker__error')?.textContent?.trim();
  }

  function alertText(): string | undefined {
    return root().querySelector('app-alert[role="alert"]')?.textContent?.trim();
  }

  it('is composed from the shared page card, form actions and submit button', () => {
    expect(root().querySelector('app-page-card h1')?.textContent?.trim()).toBe('Report a sighting');
    expect(
      root().querySelector('app-form-actions app-submit-button button')?.textContent?.trim(),
    ).toBe('Report sighting');
    expect(root().querySelector('app-location-picker')).not.toBeNull();
    expect(root().querySelector('app-page-card')?.classList).toContain('page-card-host--wide');
    expect(fields().map((field) => field.querySelector('mat-label')?.textContent?.trim())).toEqual([
      'Name (optional)',
      'Description',
      'Behaviour',
      'Status',
      'Criticality',
    ]);
  });

  it('offers only Spotted and Caught as statuses with an empty first option', async () => {
    const [status] = await selects();

    expect(await optionLabels(status)).toEqual(['', 'Spotted', 'Caught']);
    expect(await status.getValueText()).toBe('Choose a status');
  });

  it('shows the chosen status and returns to the placeholder when the blank option is picked', async () => {
    const [status] = await selects();

    await status.open();
    await status.clickOptions({ text: 'Caught' });

    expect(await status.getValueText()).toBe('Caught');
    expect(await optionLabels(status)).toEqual(['', 'Spotted', 'Caught']);

    await status.open();
    await status.clickOptions({ text: '' });

    expect(await status.getValueText()).toBe('Choose a status');

    await status.open();

    const selected = await Promise.all(
      (await status.getOptions()).map((option) => option.isSelected()),
    );

    expect(selected).toEqual([false, false, false]);
  });

  it('reports a cleared status as required after submit', async () => {
    const [status] = await selects();

    await choose(status, 'Caught');
    await status.open();
    await status.clickOptions({ text: '' });
    await submit();

    expect(create).not.toHaveBeenCalled();
    expect(fieldErrors()[3]).toBe('This field is required.');
  });

  it('offers the three criticalities after an empty first option', async () => {
    const [, criticality] = await selects();

    expect(await optionLabels(criticality)).toEqual(['', 'Monitor', 'Needs help soon', 'Urgent']);
  });

  it('shows required errors only after submit', async () => {
    setValue(textareas()[0], '');
    await settle();

    expect(fieldErrors()).toEqual(['', '', '', '', '']);
    expect(locationError()).toBe('');

    await submit();

    expect(create).not.toHaveBeenCalled();
    expect(fieldErrors()).toEqual([
      '',
      'This field is required.',
      'This field is required.',
      'This field is required.',
      'This field is required.',
    ]);
    expect(locationError()).toBe('Choose a location on the map.');
  });

  it('treats a whitespace-only description and behaviour as missing', async () => {
    await fillValid();
    setValue(textareas()[0], '   ');
    setValue(textareas()[1], '\n ');
    await settle();

    await submit();

    expect(create).not.toHaveBeenCalled();
    expect(fieldErrors()).toEqual([
      '',
      'This field is required.',
      'This field is required.',
      '',
      '',
    ]);
  });

  it('blocks a name over the configured maximum length', async () => {
    await fillValid('a'.repeat(61));

    await submit();

    expect(create).not.toHaveBeenCalled();
    expect(fieldErrors()[0]).toBe('Use at most 60 characters.');
  });

  it('creates the sighting and navigates to its detail page', async () => {
    await fillValid();

    await submit();

    expect(create).toHaveBeenCalledExactlyOnceWith({
      name: 'Limpy',
      description: 'Grey pigeon',
      behaviour: 'Cannot fly',
      latitude: warsaw.latitude,
      longitude: warsaw.longitude,
      status: SightingStatus.caught,
      criticality: SightingCriticality.urgent,
    });
    expect(navigate).toHaveBeenCalledExactlyOnceWith(['/sightings', 'sighting-7']);
  });

  it('sends a blank name as null', async () => {
    await fillValid('   ');

    await submit();

    expect(create.mock.calls[0][0].name).toBeNull();
  });

  it('shows the busy submit button while the request is in flight', async () => {
    const pending = new Subject<Sighting>();
    create.mockReturnValue(pending);
    await fillValid();

    await submit();

    expect(root().querySelector('app-submit-button [aria-busy="true"]')).not.toBeNull();

    pending.next(created);
    pending.complete();
    await settle();

    expect(root().querySelector('app-submit-button [aria-busy="true"]')).toBeNull();
  });

  it('shows the remaining cooldown seconds and stays on the form', async () => {
    create.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 400,
            error: { status: 400, errors: { CooldownActive: ['x'] }, retryAfterSeconds: 42 },
          }),
      ),
    );
    await fillValid();

    await submit();

    expect(alertText()).toBe('You can report another sighting in 42 seconds.');
    expect(navigate).not.toHaveBeenCalled();
    expect(control<HTMLButtonElement>('app-submit-button button').disabled).toBe(false);
  });

  it('shows the generic message for other failures', async () => {
    create.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));
    await fillValid();

    await submit();

    expect(alertText()).toBe("We couldn't reach the sightings right now. Try again.");
  });

  it('clears the error when a new submit starts', async () => {
    create.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));
    await fillValid();
    await submit();

    create.mockReturnValue(new Subject<Sighting>());
    await submit();

    expect(alertText()).toBeUndefined();
  });

  it('returns to the map on Cancel', async () => {
    const cancel = [...root().querySelectorAll<HTMLButtonElement>('app-form-actions button')].find(
      (button) => button.textContent?.trim() === 'Cancel',
    );

    cancel!.click();
    await settle();

    expect(navigate).toHaveBeenCalledExactlyOnceWith(['/']);
    expect(create).not.toHaveBeenCalled();
  });
});
