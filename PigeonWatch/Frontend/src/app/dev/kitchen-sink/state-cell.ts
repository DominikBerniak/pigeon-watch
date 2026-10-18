import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  inject,
  input,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import {
  MAT_DIALOG_DEFAULT_OPTIONS,
  MatDialog,
  MatDialogConfig,
  MatDialogRef,
  MatDialogState,
} from '@angular/material/dialog';
import { filter, finalize, ignoreElements, merge, switchMap, take, tap, timeout } from 'rxjs';
import { SessionService } from '../../core/auth/session.service';
import { ConfigurationService } from '../../core/configuration/configuration.service';
import { GeolocationService } from '../../core/geolocation/geolocation.service';
import { ResourceService } from '../../core/resources/resource.service';
import { ProfileApi } from '../../features/profile/profile-api';
import { ProfilePage } from '../../features/profile/profile-page/profile-page';
import { NearbyMapPage } from '../../features/sightings/nearby-map-page/nearby-map-page';
import { SightingDetailPage } from '../../features/sightings/sighting-detail-page/sighting-detail-page';
import { SightingsApi } from '../../features/sightings/sightings-api';
import { LEAFLET_LOADER } from '../../shared/map/map-view/map-view';
import { CellDefinition } from './cell-definitions';
import { CellContext, press, runSteps, until, waitLimitMilliseconds } from './cell-driver';
import { CellStubs, NextLocationMode } from './cell-stubs';

interface NextLocationOption {
  readonly mode: NextLocationMode;
  readonly label: string;
}

@Component({
  selector: 'app-state-cell',
  imports: [MatButtonModule, ProfilePage, NearbyMapPage, SightingDetailPage],
  providers: [
    CellStubs,
    { provide: SessionService, useFactory: () => inject(CellStubs).session },
    { provide: ConfigurationService, useFactory: () => inject(CellStubs).configuration },
    { provide: ProfileApi, useFactory: () => inject(CellStubs).profileApi },
    { provide: SightingsApi, useFactory: () => inject(CellStubs).sightingsApi },
    { provide: GeolocationService, useFactory: () => inject(CellStubs).geolocation },
    {
      provide: LEAFLET_LOADER,
      useFactory: () => inject(CellStubs).leafletLoader(inject(LEAFLET_LOADER, { skipSelf: true })),
    },
    MatDialog,
    {
      provide: MAT_DIALOG_DEFAULT_OPTIONS,
      useFactory: () => ({ ...new MatDialogConfig(), injector: inject(Injector) }),
    },
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './state-cell.html',
  styleUrl: './state-cell.scss',
})
export class StateCell {
  private readonly stubs = inject(CellStubs);
  private readonly dialogs = inject(MatDialog);
  private readonly resources = inject(ResourceService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly destroyRef = inject(DestroyRef);

  readonly definition = input.required<CellDefinition>();

  protected readonly running = signal(false);
  protected readonly nextLocation = this.stubs.nextLocation;
  protected readonly nextLocationOptions: readonly NextLocationOption[] = [
    { mode: 'ok', label: 'Position found' },
    { mode: 'timeout', label: 'Timed out' },
    { mode: 'pending', label: 'Never answers' },
  ];
  protected readonly asksForLocationAgain = computed(() => {
    const definition = this.definition();

    return definition.group === 'nearbyMap' && definition.geolocation === 'timeoutThenNext';
  });

  private openRef: MatDialogRef<unknown> | null = null;

  constructor() {
    this.stubs.follow(this.definition);
    this.destroyRef.onDestroy(() => {
      if (this.openRef?.getState() === MatDialogState.OPEN) this.openRef.close();
    });
    afterNextRender(() =>
      runSteps(this.definition().steps, this.context(this.host.nativeElement))
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({ error: (error: unknown) => this.report(error) }),
    );
  }

  protected chooseNextLocation(event: Event): void {
    this.stubs.chooseNextLocation((event.target as HTMLSelectElement).value as NextLocationMode);
  }

  protected runManual(): void {
    const scenario = this.definition().manual;

    if (!scenario) return;

    this.running.set(true);
    runSteps(scenario.steps, this.context(this.host.nativeElement))
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.running.set(false)),
      )
      .subscribe({ error: (error: unknown) => this.report(error) });
  }

  protected openDialog(): void {
    const scenario = this.definition().dialog;

    if (!scenario) return;

    const opened = this.dialogs.afterOpened.pipe(
      filter((ref) => ref.componentRef?.injector.get(CellStubs, null) === this.stubs),
      take(1),
      timeout(waitLimitMilliseconds),
      tap((ref) => (this.openRef = ref)),
      switchMap((ref) =>
        until<HTMLElement>(() => ref.componentRef?.location.nativeElement ?? null),
      ),
    );
    const trigger = press('profile.password.title')(this.context(this.host.nativeElement));

    this.running.set(true);
    merge(opened, trigger.pipe(ignoreElements()))
      .pipe(
        switchMap((root) => runSteps(scenario.steps, this.context(root))),
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.running.set(false)),
      )
      .subscribe({ error: (error: unknown) => this.report(error) });
  }

  private report(error: unknown): void {
    console.error(`Kitchen-sink cell "${this.definition().id}" stopped`, error);
  }

  private context(root: HTMLElement): CellContext {
    return { root, text: (key) => this.resources.translate(key) };
  }
}
