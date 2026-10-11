import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormField, form, required } from '@angular/forms/signals';
import { By } from '@angular/platform-browser';
import { Observable, Subject, of } from 'rxjs';
import { GeoPoint } from '../../../core/geolocation/geo-point';
import {
  GeolocationOutcome,
  GeolocationService,
} from '../../../core/geolocation/geolocation.service';
import { FormSubmitState } from '../../ui/form-field/form-submit-state';
import { neutralMapView, pickerMapZoom } from '../map-config';
import { MapView } from '../map-view/map-view';
import { MapViewStub } from '../testing/map-view-stub';
import { LocationPicker } from './location-picker';

interface ReportModel {
  location: GeoPoint | null;
}

@Component({
  imports: [FormField, LocationPicker],
  template: `<app-location-picker [formField]="report.location" />`,
})
class PickerHost {
  readonly model = signal<ReportModel>({ location: null });
  readonly report = form(this.model, (path) => required(path.location));
}

const warsaw: GeoPoint = { latitude: 52.2297, longitude: 21.0122 };
const krakow: GeoPoint = { latitude: 50.0614, longitude: 19.9366 };

describe('LocationPicker', () => {
  let currentPosition: ReturnType<
    typeof vi.fn<(highAccuracy: boolean) => Observable<GeolocationOutcome>>
  >;
  let fixture: ComponentFixture<PickerHost>;

  function configure(firstOutcome: GeolocationOutcome): void {
    currentPosition = vi.fn<(highAccuracy: boolean) => Observable<GeolocationOutcome>>(() =>
      of(firstOutcome),
    );
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        FormSubmitState,
        { provide: GeolocationService, useValue: { currentPosition } },
      ],
    });
    TestBed.overrideComponent(LocationPicker, {
      remove: { imports: [MapView] },
      add: { imports: [MapViewStub] },
    });
  }

  async function render(firstOutcome: GeolocationOutcome = { status: 'denied' }): Promise<void> {
    configure(firstOutcome);
    fixture = TestBed.createComponent(PickerHost);
    await fixture.whenStable();
  }

  async function settle(): Promise<void> {
    await fixture.whenStable();
    fixture.detectChanges();
    await fixture.whenStable();
  }

  function map(): MapViewStub {
    return fixture.debugElement.query(By.directive(MapViewStub)).componentInstance as MapViewStub;
  }

  function root(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function button(text: string): HTMLButtonElement {
    const match = [...root().querySelectorAll<HTMLButtonElement>('button')].find(
      (candidate) => candidate.textContent?.trim() === text,
    );

    if (!match) throw new Error(`No button "${text}"`);

    return match;
  }

  function location(): GeoPoint | null {
    return fixture.componentInstance.model().location;
  }

  function coordinatesText(): string | undefined {
    return root().querySelector('.location-picker__coordinates')?.textContent?.trim();
  }

  function errorText(): string | undefined {
    return root().querySelector('.location-picker__error')?.textContent?.trim();
  }

  it('opens on the neutral view with no pin when the position is unknown', async () => {
    await render({ status: 'denied' });

    expect(currentPosition).toHaveBeenCalledWith(true);
    expect(map().center()).toEqual(neutralMapView.centre);
    expect(map().zoom()).toBe(neutralMapView.zoom);
    expect(map().pin()).toBeNull();
    expect(location()).toBeNull();
    expect(coordinatesText()).toBe('No location chosen yet.');
  });

  it('opens centred on the position when geolocation succeeds, still without a pin', async () => {
    await render({ status: 'ok', ...warsaw });

    expect(map().center()).toEqual(warsaw);
    expect(map().zoom()).toBe(pickerMapZoom);
    expect(map().pin()).toBeNull();
    expect(location()).toBeNull();
  });

  it('places the pin where the map is tapped and moves it on a second tap', async () => {
    await render();

    map().mapClick.emit(warsaw);
    await settle();

    expect(location()).toEqual(warsaw);
    expect(map().pin()).toEqual(warsaw);
    expect(coordinatesText()).toBe('Pin at 52.22970, 21.01220');

    map().mapClick.emit(krakow);
    await settle();

    expect(location()).toEqual(krakow);
  });

  it('moves the value when the pin is dragged', async () => {
    await render();
    map().mapClick.emit(warsaw);
    await settle();

    map().pinMove.emit(krakow);
    await settle();

    expect(location()).toEqual(krakow);
    expect(map().pin()).toEqual(krakow);
  });

  it('fills the value and centres the map from "Use my location"', async () => {
    await render();
    currentPosition.mockReturnValue(of({ status: 'ok', ...krakow }));

    button('Use my location').click();
    await settle();

    expect(currentPosition).toHaveBeenLastCalledWith(true);
    expect(location()).toEqual(krakow);
    expect(map().center()).toEqual(krakow);
    expect(map().zoom()).toBe(pickerMapZoom);
  });

  it('shows the fallback hint and keeps the value when "Use my location" fails', async () => {
    await render();
    map().mapClick.emit(warsaw);
    await settle();
    currentPosition.mockReturnValue(of({ status: 'timeout' }));

    button('Use my location').click();
    await settle();

    expect(location()).toEqual(warsaw);
    expect(root().querySelector('app-alert')?.textContent?.trim()).toBe(
      "We couldn't get your location. Place the pin on the map instead.",
    );
  });

  it('disables "Use my location" while it is locating', async () => {
    await render();
    const pending = new Subject<GeolocationOutcome>();
    currentPosition.mockReturnValue(pending);

    button('Use my location').click();
    await settle();

    expect(button('Use my location').disabled).toBe(true);
    expect(root().querySelector('[role="status"]')?.textContent?.trim()).toBe(
      'Finding your location…',
    );

    pending.next({ status: 'ok', ...warsaw });
    pending.complete();
    await settle();

    expect(button('Use my location').disabled).toBe(false);
    expect(location()).toEqual(warsaw);
  });

  it('keeps "Place pin at map centre" disabled until the map is ready', async () => {
    await render();

    expect(button('Place pin at map centre').disabled).toBe(true);

    map().mapReady.emit();
    await settle();

    expect(button('Place pin at map centre').disabled).toBe(false);
  });

  it('places the pin at the current map centre without a pointer', async () => {
    await render();
    map().mapReady.emit();
    map().centerChange.emit(krakow);
    await settle();

    button('Place pin at map centre').click();
    await settle();

    expect(location()).toEqual(krakow);
  });

  it('places the pin at the opening centre before the map moves', async () => {
    await render();
    map().mapReady.emit();
    await settle();

    button('Place pin at map centre').click();
    await settle();

    expect(location()).toEqual(neutralMapView.centre);
  });

  it('shows the required error only after submit and clears it once a pin is placed', async () => {
    await render();
    fixture.componentInstance.report.location().markAsTouched();
    await settle();

    expect(errorText()).toBe('');

    TestBed.inject(FormSubmitState).markSubmitted();
    await settle();

    expect(errorText()).toBe('Choose a location on the map.');

    map().mapClick.emit(warsaw);
    await settle();

    expect(errorText()).toBe('');
    expect(fixture.componentInstance.report().valid()).toBe(true);
  });
});
