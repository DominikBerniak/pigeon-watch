import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormField, FormRoot, form, maxLength, required } from '@angular/forms/signals';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { Router } from '@angular/router';
import { finalize } from 'rxjs';
import { ConfigurationService } from '../../../core/configuration/configuration.service';
import { GeoPoint } from '../../../core/geolocation/geo-point';
import { ResourceService } from '../../../core/resources/resource.service';
import { TranslatePipe } from '../../../core/resources/translate.pipe';
import { LocationPicker } from '../../../shared/map/location-picker/location-picker';
import {
  Alert,
  FieldErrors,
  FormActions,
  FormSubmitState,
  PageCard,
  SubmitButton,
  provideFormFieldDefaults,
} from '../../../shared/ui';
import { SightingFailure, sightingErrorMessage, sightingFailure } from '../sighting-errors';
import {
  NewSighting,
  SightingCriticality,
  SightingStatus,
  fallbackSightingRules,
  initialSightingStatuses,
  selectableSightingCriticalities,
  sightingCriticalityLabels,
  sightingStatusLabels,
} from '../sighting.types';
import { SightingsApi } from '../sightings-api';

interface ReportModel {
  name: string;
  description: string;
  behaviour: string;
  status: string;
  criticality: string;
  location: GeoPoint | null;
}

const emptyReport: ReportModel = {
  name: '',
  description: '',
  behaviour: '',
  status: '',
  criticality: '',
  location: null,
};

@Component({
  providers: [provideFormFieldDefaults()],
  selector: 'app-report-sighting-page',
  imports: [
    FormField,
    FormRoot,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    TranslatePipe,
    Alert,
    FieldErrors,
    FormActions,
    LocationPicker,
    PageCard,
    SubmitButton,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './report-sighting-page.html',
})
export class ReportSightingPage {
  private readonly submitState = inject(FormSubmitState);
  private readonly configuration = inject(ConfigurationService);
  private readonly sightingsApi = inject(SightingsApi);
  private readonly resources = inject(ResourceService);
  private readonly router = inject(Router);
  private readonly model = signal<ReportModel>({ ...emptyReport });
  private readonly failure = signal<SightingFailure | null>(null);

  protected readonly statuses = initialSightingStatuses;
  protected readonly criticalities = selectableSightingCriticalities;
  protected readonly statusLabels = sightingStatusLabels;
  protected readonly criticalityLabels = sightingCriticalityLabels;
  protected readonly rules = computed(
    () => this.configuration.clientConfig()?.sightingRules ?? fallbackSightingRules,
  );
  protected readonly reportForm = form(this.model, (path) => {
    maxLength(path.name, () => this.rules().nameMaxLength);
    required(path.description);
    maxLength(path.description, () => this.rules().descriptionMaxLength);
    required(path.behaviour);
    maxLength(path.behaviour, () => this.rules().behaviourMaxLength);
    required(path.status);
    required(path.criticality);
    required(path.location);
  });
  protected readonly busy = signal(false);
  protected readonly errorText = computed(() => {
    const failure = this.failure();

    return failure ? sightingErrorMessage(this.resources, failure) : null;
  });
  protected readonly nameMessages = computed(() => ({
    maxLength: this.maxLengthMessage(this.rules().nameMaxLength),
  }));
  protected readonly descriptionMessages = computed(() => ({
    required: this.resources.translate('auth.validation.required'),
    maxLength: this.maxLengthMessage(this.rules().descriptionMaxLength),
  }));
  protected readonly behaviourMessages = computed(() => ({
    required: this.resources.translate('auth.validation.required'),
    maxLength: this.maxLengthMessage(this.rules().behaviourMaxLength),
  }));
  protected readonly choiceMessages = computed(() => ({
    required: this.resources.translate('auth.validation.required'),
  }));

  protected cancel(): void {
    void this.router.navigate(['/']);
  }

  protected submit(): void {
    this.submitState.markSubmitted();
    this.failure.set(null);

    if (this.busy() || this.reportForm().invalid()) return;

    const sighting = newSightingOf(this.model());

    if (!sighting) return;

    this.busy.set(true);
    this.sightingsApi
      .create(sighting)
      .pipe(finalize(() => this.busy.set(false)))
      .subscribe({
        next: (created) => void this.router.navigate(['/sightings', created.id]),
        error: (error: unknown) => this.failure.set(sightingFailure(error)),
      });
  }

  private maxLengthMessage(length: number): string {
    return this.resources.translate('sighting.form.maxLength', length);
  }
}

function newSightingOf(report: ReportModel): NewSighting | null {
  const location = report.location;

  if (!location) return null;

  const name = report.name.trim();

  return {
    name: name === '' ? null : name,
    description: report.description,
    behaviour: report.behaviour,
    latitude: location.latitude,
    longitude: location.longitude,
    status: Number(report.status) as SightingStatus,
    criticality: Number(report.criticality) as SightingCriticality,
  };
}
