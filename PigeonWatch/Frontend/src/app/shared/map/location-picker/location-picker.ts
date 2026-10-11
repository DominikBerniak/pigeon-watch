import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  model,
  output,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormField, FormValueControl } from '@angular/forms/signals';
import { MatButtonModule } from '@angular/material/button';
import { finalize } from 'rxjs';
import { GeoPoint } from '../../../core/geolocation/geo-point';
import { GeolocationService } from '../../../core/geolocation/geolocation.service';
import { ResourceService } from '../../../core/resources/resource.service';
import { TranslatePipe } from '../../../core/resources/translate.pipe';
import { Alert } from '../../ui/alert/alert';
import { FieldErrors } from '../../ui/field-errors/field-errors';
import { neutralMapView, pickerMapZoom } from '../map-config';
import { MapView } from '../map-view/map-view';

const coordinateDecimals = 5;

@Component({
  selector: 'app-location-picker',
  imports: [MatButtonModule, TranslatePipe, Alert, FieldErrors, MapView],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './location-picker.html',
  styleUrl: './location-picker.scss',
})
export class LocationPicker implements FormValueControl<GeoPoint | null> {
  private readonly geolocation = inject(GeolocationService);
  private readonly resources = inject(ResourceService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly formField = inject<FormField<GeoPoint | null>>(FormField, {
    self: true,
    optional: true,
  });
  private readonly mapCentre = signal<GeoPoint>(neutralMapView.centre);

  readonly value = model<GeoPoint | null>(null);
  readonly touch = output<void>();

  protected readonly viewCentre = signal<GeoPoint>(neutralMapView.centre);
  protected readonly viewZoom = signal<number>(neutralMapView.zoom);
  protected readonly locating = signal(false);
  protected readonly locationFailed = signal(false);
  protected readonly field = computed(() => this.formField?.field() ?? null);
  protected readonly errorMessages = computed(() => ({
    required: this.resources.translate('sighting.form.locationRequired'),
  }));
  protected readonly coordinatesText = computed(() => {
    const value = this.value();

    if (!value) return this.resources.translate('sighting.form.locationNotSet');

    return this.resources.translate(
      'sighting.form.locationSelected',
      value.latitude.toFixed(coordinateDecimals),
      value.longitude.toFixed(coordinateDecimals),
    );
  });

  constructor() {
    this.geolocation
      .currentPosition(true)
      .pipe(takeUntilDestroyed())
      .subscribe((outcome) => {
        if (outcome.status !== 'ok' || this.value()) return;

        this.showAt(outcome);
      });
  }

  protected placePin(point: GeoPoint): void {
    this.locationFailed.set(false);
    this.select(point);
  }

  protected placePinAtCentre(): void {
    this.placePin(this.mapCentre());
  }

  protected trackCentre(point: GeoPoint): void {
    this.mapCentre.set(point);
  }

  protected useMyLocation(): void {
    if (this.locating()) return;

    this.locating.set(true);
    this.locationFailed.set(false);
    this.geolocation
      .currentPosition(true)
      .pipe(
        finalize(() => this.locating.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((outcome) => {
        if (outcome.status !== 'ok') {
          this.locationFailed.set(true);

          return;
        }

        const point = { latitude: outcome.latitude, longitude: outcome.longitude };

        this.showAt(point);
        this.select(point);
      });
  }

  private showAt(point: GeoPoint): void {
    this.viewCentre.set({ latitude: point.latitude, longitude: point.longitude });
    this.viewZoom.set(pickerMapZoom);
  }

  private select(point: GeoPoint): void {
    this.value.set({ latitude: point.latitude, longitude: point.longitude });
    this.touch.emit();
  }
}
