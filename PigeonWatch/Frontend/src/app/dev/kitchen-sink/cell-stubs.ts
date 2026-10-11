import { HttpErrorResponse } from '@angular/common/http';
import { DestroyRef, Injectable, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NEVER, Observable, map, of, throwError, timer } from 'rxjs';
import {
  ClientConfiguration,
  CurrentUser,
  GeneralConfiguration,
} from '../../core/configuration/configuration.service';
import { UpdatedProfile } from '../../features/profile/profile-api';
import { SightingList } from '../../features/sightings/sighting.types';

export type SaveMode = 'success' | 'duplicate' | 'failure' | 'pending';
export type PasswordMode = 'success' | 'wrongCurrent' | 'pending';

export interface StubModes {
  readonly save: SaveMode;
  readonly password: PasswordMode;
}

export const sinkUser: CurrentUser = {
  id: '1',
  email: 'jan@example.com',
  displayName: 'Jan K',
  roles: [],
};

const busyHoldMilliseconds = 30000;

function badRequest(errors: Record<string, string[]>): HttpErrorResponse {
  return new HttpErrorResponse({ status: 400, error: { status: 400, errors } });
}

@Injectable()
export class CellStubs {
  private readonly destroyRef = inject(DestroyRef);
  private modes: () => StubModes = () => ({ save: 'success', password: 'success' });

  readonly session = {
    currentUser: signal<CurrentUser | null>(sinkUser).asReadonly(),
    changePassword: (): Observable<void> => this.passwordResult(),
  };
  readonly configuration = {
    clientConfig: signal<ClientConfiguration | null>(null),
    loadGeneral: (): Observable<GeneralConfiguration> => of({ currentUser: sinkUser }),
  };
  readonly profileApi = {
    updateDisplayName: (displayName: string): Observable<UpdatedProfile> =>
      this.saveResult(displayName),
  };
  readonly sightingsApi = {
    mine: (): Observable<SightingList> => of({ items: [] }),
  };

  follow(modes: () => StubModes): void {
    this.modes = modes;
  }

  private saveResult(displayName: string): Observable<UpdatedProfile> {
    switch (this.modes().save) {
      case 'duplicate':
        return throwError(() => badRequest({ DuplicateDisplayName: ['Display name is taken.'] }));
      case 'failure':
        return throwError(() => new HttpErrorResponse({ status: 500 }));
      case 'pending':
        return NEVER;
      default:
        return of({ email: sinkUser.email, displayName });
    }
  }

  private passwordResult(): Observable<void> {
    switch (this.modes().password) {
      case 'wrongCurrent':
        return throwError(() => badRequest({ PasswordMismatch: ['Incorrect password.'] }));
      case 'pending':
        return timer(busyHoldMilliseconds).pipe(
          map((): void => undefined),
          takeUntilDestroyed(this.destroyRef),
        );
      default:
        return of(undefined);
    }
  }
}
