import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  computed,
  effect,
  inject,
  model,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormField, FormValueControl } from '@angular/forms/signals';
import { MatButtonModule } from '@angular/material/button';
import { Subscription, finalize } from 'rxjs';
import { ConfigurationService } from '../../../../core/configuration/configuration.service';
import { ErrorReportingService } from '../../../../core/error-reporting/error-reporting.service';
import { ResourceService } from '../../../../core/resources/resource.service';
import { TranslatePipe } from '../../../../core/resources/translate.pipe';
import { FieldErrors } from '../../../../shared/ui/field-errors/field-errors';
import { PhotoProblem, photoProblemLabels } from '../../sighting-errors';
import { fallbackSightingRules } from '../../sighting.types';
import { PhotoDecodeError, PhotoEncoder } from '../../shared/photo-encoder';

@Component({
  selector: 'app-photo-picker',
  imports: [MatButtonModule, TranslatePipe, FieldErrors],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './photo-picker.html',
  styleUrl: './photo-picker.scss',
})
export class PhotoPicker implements FormValueControl<Blob | null> {
  private readonly encoder = inject(PhotoEncoder);
  private readonly configuration = inject(ConfigurationService);
  private readonly resources = inject(ResourceService);
  private readonly reporting = inject(ErrorReportingService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly formField = inject<FormField<Blob | null>>(FormField, {
    self: true,
    optional: true,
  });
  private readonly chooseButton = viewChild.required('chooseButton', {
    read: ElementRef<HTMLButtonElement>,
  });
  private encoding: Subscription | undefined;

  readonly value = model<Blob | null>(null);
  readonly touch = output<void>();
  readonly processingChange = output<boolean>();

  protected readonly rules = computed(
    () => this.configuration.clientConfig()?.sightingRules ?? fallbackSightingRules,
  );
  protected readonly accept = computed(() => this.rules().photoAllowedContentTypes.join(','));
  protected readonly processing = signal(false);
  protected readonly problem = signal<PhotoProblem | null>(null);
  protected readonly previewUrl = signal<string | null>(null);
  protected readonly field = computed(() => this.formField?.field() ?? null);
  protected readonly problemText = computed(() => {
    const problem = this.problem();

    return problem ? this.resources.translate(photoProblemLabels[problem]) : null;
  });
  protected readonly errorMessages = computed(() => ({
    photoTooLarge: this.resources.translate(photoProblemLabels.photoTooLarge),
    photoUnsupportedType: this.resources.translate(photoProblemLabels.photoUnsupportedType),
    photoUnreadable: this.resources.translate(photoProblemLabels.photoUnreadable),
  }));

  constructor() {
    effect((onCleanup) => {
      const photo = this.value();

      if (!photo) {
        this.previewUrl.set(null);

        return;
      }

      const url = this.encoder.createPreviewUrl(photo);

      this.previewUrl.set(url);
      onCleanup(() => this.encoder.revokePreviewUrl(url));
    });
  }

  protected choose(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.item(0) ?? null;

    input.value = '';

    if (!file) return;

    this.cancelEncoding();
    this.problem.set(null);

    const rules = this.rules();

    if (file.type !== '' && !rules.photoAllowedContentTypes.includes(file.type)) {
      this.reject('photoUnsupportedType');

      return;
    }

    this.setProcessing(true);
    this.encoding = this.encoder
      .encode(file, rules.photoMaxDimension)
      .pipe(
        finalize(() => this.setProcessing(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (photo) => {
          if (photo.size > rules.photoMaxBytes) {
            this.reject('photoTooLarge');

            return;
          }

          this.select(photo);
        },
        error: (error: unknown) => this.encodingFailed(error),
      });
  }

  protected remove(): void {
    this.cancelEncoding();
    this.problem.set(null);
    this.select(null);
    this.chooseButton().nativeElement.focus();
  }

  private encodingFailed(error: unknown): void {
    if (error instanceof PhotoDecodeError) {
      this.reject('photoUnsupportedType');

      return;
    }

    this.reporting.report('fallback', 'sighting.photo.encode', error);
    this.reject('photoUnreadable');
  }

  private reject(problem: PhotoProblem): void {
    this.problem.set(problem);
    this.select(null);
  }

  private select(photo: Blob | null): void {
    this.value.set(photo);
    this.touch.emit();
  }

  private cancelEncoding(): void {
    this.encoding?.unsubscribe();
    this.encoding = undefined;
    this.setProcessing(false);
  }

  private setProcessing(processing: boolean): void {
    if (this.processing() === processing) return;

    this.processing.set(processing);
    this.processingChange.emit(processing);
  }
}
