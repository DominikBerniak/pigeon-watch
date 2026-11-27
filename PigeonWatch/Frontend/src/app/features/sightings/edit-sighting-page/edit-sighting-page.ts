import {
  ChangeDetectionStrategy,
  Component,
  Injector,
  afterNextRender,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  FormField,
  FormRoot,
  disabled,
  form,
  maxLength,
  required,
  requiredError,
  validate,
} from '@angular/forms/signals';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import {
  Observable,
  Subject,
  catchError,
  distinctUntilChanged,
  finalize,
  map,
  merge,
  of,
  startWith,
  switchMap,
} from 'rxjs';
import { ConfigurationService } from '../../../core/configuration/configuration.service';
import { ErrorReportingService } from '../../../core/error-reporting/error-reporting.service';
import { ResourceService } from '../../../core/resources/resource.service';
import { TranslatePipe } from '../../../core/resources/translate.pipe';
import {
  Alert,
  ErrorReference,
  ErrorRetry,
  FieldErrors,
  FormActions,
  FormSubmitState,
  LoadingIndicator,
  PageCard,
  SubmitButton,
  provideFormFieldDefaults,
} from '../../../shared/ui';
import { PhotoPicker } from '../report-sighting-page/photo-picker/photo-picker';
import { PhotoEncoder } from '../shared/photo-encoder';
import {
  PhotoProblem,
  SightingFailure,
  SightingUpdateFailure,
  isPhotoProblem,
  isReportablePhotoFailure,
  isReportableSightingFailure,
  isReportableSightingUpdateFailure,
  isStaleEditFailure,
  sightingFailure,
  sightingUpdateErrorMessage,
  sightingUpdateFailure,
  sightingUpdateFailureTraceId,
} from '../sighting-errors';
import {
  Sighting,
  SightingCriticality,
  SightingStatus,
  SightingUpdate,
  editableSightingStatuses,
  fallbackSightingRules,
  selectableSightingCriticalities,
  sightingCriticalityLabels,
  sightingStatusLabel,
} from '../sighting.types';
import { SightingsApi } from '../sightings-api';

type EditState =
  | { readonly kind: 'loading' }
  | { readonly kind: 'notFound' }
  | { readonly kind: 'error'; readonly traceId?: string }
  | { readonly kind: 'notOwner'; readonly sighting: Sighting }
  | { readonly kind: 'removed'; readonly sighting: Sighting }
  | { readonly kind: 'editing'; readonly sighting: Sighting };

type ReloadFailure =
  | { readonly kind: 'notFound' }
  | { readonly kind: 'generic'; readonly traceId?: string };

type LoadResult =
  | { readonly kind: 'loading' }
  | { readonly kind: 'notFound' }
  | { readonly kind: 'error'; readonly traceId?: string }
  | { readonly kind: 'reloadFailed'; readonly failure: ReloadFailure }
  | { readonly kind: 'loaded'; readonly sighting: Sighting; readonly keepEdits: boolean };

type ThumbnailState =
  | { readonly kind: 'none' }
  | { readonly kind: 'loading' }
  | { readonly kind: 'error' }
  | { readonly kind: 'loaded'; readonly url: string };

interface Load {
  readonly id: string;
  readonly keepEdits: boolean;
}

interface EditModel {
  name: string;
  description: string;
  behaviour: string;
  status: SightingStatus;
  criticality: SightingCriticality;
  photo: Blob | null;
}

interface PhotoRejection {
  readonly photo: Blob;
  readonly problem: PhotoProblem;
}

const emptyModel: EditModel = {
  name: '',
  description: '',
  behaviour: '',
  status: SightingStatus.spotted,
  criticality: SightingCriticality.monitor,
  photo: null,
};

@Component({
  providers: [provideFormFieldDefaults()],
  selector: 'app-edit-sighting-page',
  imports: [
    FormField,
    FormRoot,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    RouterLink,
    TranslatePipe,
    Alert,
    ErrorReference,
    ErrorRetry,
    FieldErrors,
    FormActions,
    LoadingIndicator,
    PageCard,
    PhotoPicker,
    SubmitButton,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './edit-sighting-page.html',
  styleUrl: './edit-sighting-page.scss',
})
export class EditSightingPage {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly submitState = inject(FormSubmitState);
  private readonly configuration = inject(ConfigurationService);
  private readonly sightingsApi = inject(SightingsApi);
  private readonly resources = inject(ResourceService);
  private readonly reporting = inject(ErrorReportingService);
  private readonly photoUrls = inject(PhotoEncoder);
  private readonly injector = inject(Injector);
  private readonly photoPicker = viewChild(PhotoPicker);
  private readonly reloads = new Subject<Load>();
  private readonly shownSightings = new Subject<Sighting | null>();
  private readonly model = signal<EditModel>({ ...emptyModel });
  private readonly failure = signal<SightingUpdateFailure | null>(null);
  private readonly photoRejection = signal<PhotoRejection | null>(null);

  protected readonly sightingId = signal('');
  protected readonly state = signal<EditState>({ kind: 'loading' });
  protected readonly thumbnail = signal<ThumbnailState>({ kind: 'none' });
  protected readonly removeCurrentPhoto = signal(false);
  protected readonly busy = signal(false);
  protected readonly photoProcessing = signal(false);
  protected readonly criticalities = selectableSightingCriticalities;
  protected readonly criticalityLabels = sightingCriticalityLabels;
  protected readonly statusLabel = sightingStatusLabel;
  protected readonly rules = computed(
    () => this.configuration.clientConfig()?.sightingRules ?? fallbackSightingRules,
  );
  protected readonly sighting = computed(() => {
    const state = this.state();

    return state.kind === 'editing' ? state.sighting : null;
  });
  protected readonly canEditDescription = computed(
    () => this.sighting()?.editPermissions?.canEditDescription ?? false,
  );
  protected readonly statuses = computed(() => {
    const sighting = this.sighting();

    return sighting ? editableSightingStatuses(sighting) : [];
  });
  protected readonly subtitle = computed(() => {
    const state = this.state();

    if (state.kind !== 'editing' && state.kind !== 'notOwner' && state.kind !== 'removed') return '';

    return state.sighting.name ?? this.resources.translate('sighting.unnamed');
  });
  protected readonly currentPhotoShown = computed(
    () =>
      (this.sighting()?.hasPhoto ?? false) &&
      !this.removeCurrentPhoto() &&
      this.model().photo === null,
  );
  protected readonly thumbnailUrl = computed(() => {
    const thumbnail = this.thumbnail();

    return thumbnail.kind === 'loaded' ? thumbnail.url : null;
  });
  protected readonly loadTraceId = computed(() => {
    const state = this.state();

    return state.kind === 'error' ? (state.traceId ?? null) : null;
  });
  protected readonly editForm = form(this.model, (path) => {
    maxLength(path.name, () => this.rules().nameMaxLength);
    required(path.description);
    validate(path.description, ({ value }) => (isBlank(value()) ? requiredError() : undefined));
    maxLength(path.description, () => this.rules().descriptionMaxLength);
    disabled(path.description, () => !this.canEditDescription());
    required(path.behaviour);
    validate(path.behaviour, ({ value }) => (isBlank(value()) ? requiredError() : undefined));
    maxLength(path.behaviour, () => this.rules().behaviourMaxLength);
    validate(path.photo, ({ value }) => {
      const rejection = this.photoRejection();

      return rejection !== null && rejection.photo === value()
        ? { kind: rejection.problem }
        : undefined;
    });
  });
  protected readonly errorText = computed(() => {
    const failure = this.failure();

    return failure ? sightingUpdateErrorMessage(this.resources, failure) : null;
  });
  protected readonly errorTraceId = computed(() => sightingUpdateFailureTraceId(this.failure()));
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

  constructor() {
    this.shownSightings
      .pipe(
        distinctUntilChanged(isSamePhoto),
        switchMap((sighting) => this.thumbnailOf(sighting)),
        takeUntilDestroyed(),
      )
      .subscribe((thumbnail) => this.thumbnail.set(thumbnail));

    merge(
      this.route.paramMap.pipe(
        map((params): Load => ({ id: params.get('id') ?? '', keepEdits: false })),
      ),
      this.reloads,
    )
      .pipe(
        switchMap((load) => this.loadOf(load)),
        takeUntilDestroyed(),
      )
      .subscribe((result) => this.apply(result));
  }

  protected retry(): void {
    this.reloads.next({ id: this.sightingId(), keepEdits: false });
  }

  protected cancel(): void {
    void this.router.navigate(['/sightings', this.sightingId()]);
  }

  protected removePhoto(): void {
    this.removeCurrentPhoto.set(true);
    afterNextRender(() => this.photoPicker()?.focus(), { injector: this.injector });
  }

  protected submit(): void {
    this.submitState.markSubmitted();
    this.failure.set(null);

    const sighting = this.sighting();

    if (!sighting || this.busy() || this.photoProcessing() || this.editForm().invalid()) return;

    const update = this.updateOf(sighting);

    this.busy.set(true);
    this.sightingsApi
      .update(sighting.id, update)
      .pipe(finalize(() => this.busy.set(false)))
      .subscribe({
        next: (updated) => void this.router.navigate(['/sightings', updated.id]),
        error: (error: unknown) => this.handleFailure(error, sighting.id, update.photo),
      });
  }

  private loadOf(load: Load): Observable<LoadResult> {
    const loaded = this.sightingsApi.get(load.id).pipe(
      map((sighting): LoadResult => ({ kind: 'loaded', sighting, keepEdits: load.keepEdits })),
      catchError((error: unknown) => of(this.loadFailure(error, load.keepEdits))),
    );

    this.sightingId.set(load.id);

    return load.keepEdits ? loaded : loaded.pipe(startWith<LoadResult>({ kind: 'loading' }));
  }

  private loadFailure(error: unknown, keepEdits: boolean): LoadResult {
    const failure = sightingFailure(error);

    if (isReportableSightingFailure(error, failure)) {
      this.reporting.report('api-failure', 'sighting.edit.load', error);
    }

    const shown = reloadFailureOf(failure);

    if (keepEdits) return { kind: 'reloadFailed', failure: shown };

    if (shown.kind === 'notFound') return { kind: 'notFound' };

    return shown.traceId !== undefined
      ? { kind: 'error', traceId: shown.traceId }
      : { kind: 'error' };
  }

  private apply(result: LoadResult): void {
    if (result.kind === 'reloadFailed') {
      this.failure.set(result.failure);

      return;
    }

    if (result.kind !== 'loaded') {
      this.state.set(result);
      this.shownSightings.next(null);

      return;
    }

    const sighting = result.sighting;

    if (sighting.removedAt !== null) {
      this.state.set({ kind: 'removed', sighting });
      this.shownSightings.next(null);

      return;
    }

    if (sighting.editPermissions === null) {
      this.state.set({ kind: 'notOwner', sighting });
      this.shownSightings.next(null);

      return;
    }

    if (result.keepEdits) {
      this.mergeReloaded(sighting);
    } else {
      this.model.set(modelOf(sighting));
      this.removeCurrentPhoto.set(false);
      this.photoRejection.set(null);
      this.failure.set(null);
    }

    this.state.set({ kind: 'editing', sighting });
    this.shownSightings.next(sighting);
  }

  private mergeReloaded(sighting: Sighting): void {
    const statuses = editableSightingStatuses(sighting);
    const canEditDescription = sighting.editPermissions?.canEditDescription ?? false;

    this.model.update((current) => ({
      ...current,
      description: canEditDescription ? current.description : sighting.description,
      status: statuses.includes(current.status) ? current.status : sighting.status,
    }));
  }

  private updateOf(sighting: Sighting): SightingUpdate {
    const current = this.model();
    const name = current.name.trim();

    return {
      name: name === '' ? null : name,
      description: this.canEditDescription() ? current.description : sighting.description,
      behaviour: current.behaviour,
      status: current.status,
      criticality: current.criticality,
      photo: current.photo,
      removePhoto: current.photo === null && sighting.hasPhoto && this.removeCurrentPhoto(),
    };
  }

  private handleFailure(error: unknown, id: string, photo: Blob | null): void {
    const failure = sightingUpdateFailure(error);

    if (isPhotoProblem(failure) && photo !== null) {
      this.photoRejection.set({ photo, problem: failure.kind });

      return;
    }

    this.failure.set(failure);

    if (isReportableSightingUpdateFailure(error, failure)) {
      this.reporting.report('api-failure', 'sighting.edit', error);
    }

    if (isStaleEditFailure(failure)) this.reloads.next({ id, keepEdits: true });
  }

  private thumbnailOf(sighting: Sighting | null): Observable<ThumbnailState> {
    if (!sighting?.hasPhoto) return of<ThumbnailState>({ kind: 'none' });

    return this.sightingsApi.photoThumbnail(sighting.id).pipe(
      switchMap((blob) => this.objectUrlOf(blob)),
      map((url): ThumbnailState => ({ kind: 'loaded', url })),
      catchError((error: unknown) => {
        if (isReportablePhotoFailure(error)) {
          this.reporting.report('api-failure', 'sighting.edit.photo', error);
        }

        return of<ThumbnailState>({ kind: 'error' });
      }),
      startWith<ThumbnailState>({ kind: 'loading' }),
    );
  }

  private objectUrlOf(blob: Blob): Observable<string> {
    return new Observable<string>((subscriber) => {
      const url = this.photoUrls.createPreviewUrl(blob);

      subscriber.next(url);

      return () => this.photoUrls.revokePreviewUrl(url);
    });
  }

  private maxLengthMessage(length: number): string {
    return this.resources.translate('sighting.form.maxLength', length);
  }
}

function isBlank(value: string): boolean {
  return value !== '' && value.trim() === '';
}

function isSamePhoto(previous: Sighting | null, current: Sighting | null): boolean {
  return previous?.id === current?.id && previous?.hasPhoto === current?.hasPhoto;
}

function reloadFailureOf(failure: SightingFailure): ReloadFailure {
  if (failure.kind === 'notFound') return { kind: 'notFound' };

  return failure.kind === 'generic' && failure.traceId !== undefined
    ? { kind: 'generic', traceId: failure.traceId }
    : { kind: 'generic' };
}

function modelOf(sighting: Sighting): EditModel {
  return {
    name: sighting.name ?? '',
    description: sighting.description,
    behaviour: sighting.behaviour,
    status: sighting.status,
    criticality: sighting.criticality,
    photo: null,
  };
}
