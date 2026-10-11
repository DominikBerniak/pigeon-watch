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
import { MAT_DIALOG_DEFAULT_OPTIONS, MatDialog, MatDialogConfig } from '@angular/material/dialog';
import { finalize, ignoreElements, merge, switchMap, take } from 'rxjs';
import { SessionService } from '../../core/auth/session.service';
import { ConfigurationService } from '../../core/configuration/configuration.service';
import { ResourceService } from '../../core/resources/resource.service';
import { ProfileApi } from '../../features/profile/profile-api';
import { ProfilePage } from '../../features/profile/profile-page/profile-page';
import { CellDefinition } from './cell-definitions';
import { CellContext, press, runSteps, until } from './cell-driver';
import { CellStubs } from './cell-stubs';

@Component({
  selector: 'app-state-cell',
  imports: [MatButtonModule, ProfilePage],
  providers: [
    CellStubs,
    { provide: SessionService, useFactory: () => inject(CellStubs).session },
    { provide: ConfigurationService, useFactory: () => inject(CellStubs).configuration },
    { provide: ProfileApi, useFactory: () => inject(CellStubs).profileApi },
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

  constructor() {
    this.stubs.follow(this.definition);
    afterNextRender(() =>
      runSteps(this.definition().steps, this.context(this.host.nativeElement))
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe(),
    );
  }

  protected openDialog(): void {
    const scenario = this.definition().dialog;

    if (!scenario) return;

    const opened = this.dialogs.afterOpened.pipe(
      take(1),
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
      .subscribe();
  }

  private context(root: HTMLElement): CellContext {
    return { root, text: (key) => this.resources.translate(key) };
  }
}
