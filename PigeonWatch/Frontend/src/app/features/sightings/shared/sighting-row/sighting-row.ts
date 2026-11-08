import { DatePipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  afterNextRender,
  computed,
  inject,
  input,
  signal,
} from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { Observable, catchError, map, of, startWith, switchMap } from 'rxjs';
import { ErrorReportingService } from '../../../../core/error-reporting/error-reporting.service';
import { TranslatePipe } from '../../../../core/resources/translate.pipe';
import { CriticalityBadge, Tooltip } from '../../../../shared/ui';
import { PhotoEncoder } from '../photo-encoder';
import {
  Sighting,
  sightingCriticalityLabel,
  sightingCriticalityMarker,
  sightingStatusLabel,
} from '../../sighting.types';
import { SightingsApi } from '../../sightings-api';
import { isReportablePhotoFailure } from '../../sighting-errors';
import { sightingAge } from '../sighting-age';

type ThumbnailState =
  | { readonly kind: 'none' }
  | { readonly kind: 'loading' }
  | { readonly kind: 'error' }
  | { readonly kind: 'loaded'; readonly url: string };

const visibilityMargin = '200px';

@Component({
  selector: 'li[app-sighting-row]',
  imports: [DatePipe, RouterLink, TranslatePipe, CriticalityBadge, Tooltip],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './sighting-row.html',
  styleUrl: './sighting-row.scss',
})
export class SightingRow {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly destroyRef = inject(DestroyRef);
  private readonly sightingsApi = inject(SightingsApi);
  private readonly photoUrls = inject(PhotoEncoder);
  private readonly reporting = inject(ErrorReportingService);
  private readonly visible = signal(false);

  readonly sighting = input.required<Sighting>();
  readonly now = input.required<Date>();
  readonly linkState = input<object | undefined>(undefined);

  protected readonly age = computed(() => sightingAge(this.sighting().createdAt, this.now()));
  protected readonly statusLabel = sightingStatusLabel;
  protected readonly criticalityLabel = sightingCriticalityLabel;
  protected readonly criticalityKind = sightingCriticalityMarker;
  protected readonly thumbnail = signal<ThumbnailState>({ kind: 'none' });
  protected readonly thumbnailUrl = computed(() => {
    const thumbnail = this.thumbnail();

    return thumbnail.kind === 'loaded' ? thumbnail.url : null;
  });

  private readonly thumbnailId = computed(() => {
    const sighting = this.sighting();

    return this.visible() && sighting.hasPhoto ? sighting.id : null;
  });

  constructor() {
    afterNextRender(() => this.watchVisibility());

    toObservable(this.thumbnailId)
      .pipe(
        switchMap((id) => this.thumbnailOf(id)),
        takeUntilDestroyed(),
      )
      .subscribe((thumbnail) => this.thumbnail.set(thumbnail));
  }

  private watchVisibility(): void {
    if (typeof IntersectionObserver === 'undefined') {
      this.visible.set(true);

      return;
    }

    const observer = new IntersectionObserver(
      (entries) => {
        if (!entries.some((entry) => entry.isIntersecting)) return;

        this.visible.set(true);
        observer.disconnect();
      },
      { rootMargin: visibilityMargin },
    );

    observer.observe(this.host.nativeElement);
    this.destroyRef.onDestroy(() => observer.disconnect());
  }

  private thumbnailOf(id: string | null): Observable<ThumbnailState> {
    if (id === null) return of<ThumbnailState>({ kind: 'none' });

    return this.sightingsApi.photoThumbnail(id).pipe(
      switchMap((blob) => this.objectUrlOf(blob)),
      map((url): ThumbnailState => ({ kind: 'loaded', url })),
      catchError((error: unknown) => {
        if (isReportablePhotoFailure(error)) {
          this.reporting.report('api-failure', 'sighting.row.photo', error);
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
}
