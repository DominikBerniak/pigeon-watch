import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  afterNextRender,
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
import { ResourceService } from '../../core/resources/resource.service';
import { ProfileApi } from '../../features/profile/profile-api';
import { ProfilePage } from '../../features/profile/profile-page/profile-page';
import { SightingsApi } from '../../features/sightings/sightings-api';
import { CellDefinition } from './cell-definitions';
import { CellContext, press, runSteps, until, waitLimitMilliseconds } from './cell-driver';
import { CellStubs } from './cell-stubs';

@Component({
  selector: 'app-state-cell',
  imports: [MatButtonModule, ProfilePage],
  providers: [
    CellStubs,
    { provide: SessionService, useFactory: () => inject(CellStubs).session },
    { provide: ConfigurationService, useFactory: () => inject(CellStubs).configuration },
    { provide: ProfileApi, useFactory: () => inject(CellStubs).profileApi },
    { provide: SightingsApi, useFactory: () => inject(CellStubs).sightingsApi },
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
