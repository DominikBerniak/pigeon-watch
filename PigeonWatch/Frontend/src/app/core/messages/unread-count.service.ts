import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  EMPTY,
  Observable,
  Subject,
  asyncScheduler,
  catchError,
  filter,
  map,
  merge,
  of,
  switchMap,
  throttleTime,
} from 'rxjs';
import { environment } from '../../../environments/environment';
import { isExpectedClientStatus } from '../../shared/http/problem-details';
import { ErrorReportingService } from '../error-reporting/error-reporting.service';

export interface UnreadMessageCount {
  count: number;
}

export const unreadCountNavigationThrottleMilliseconds = 5_000;

type CountTrigger = 'load' | 'clear';

@Injectable({ providedIn: 'root' })
export class UnreadCountService {
  private readonly http = inject(HttpClient);
  private readonly reporting = inject(ErrorReportingService);
  private readonly triggers = new Subject<CountTrigger>();
  private readonly navigations = new Subject<void>();
  private readonly countState = signal(0);
  private active = false;

  readonly count = this.countState.asReadonly();

  constructor() {
    merge(
      this.triggers,
      this.navigations.pipe(
        throttleTime(unreadCountNavigationThrottleMilliseconds, asyncScheduler, {
          leading: true,
          trailing: true,
        }),
        map((): CountTrigger => 'load'),
      ),
    )
      .pipe(
        filter((trigger) => trigger === 'clear' || this.active),
        switchMap((trigger) => (trigger === 'clear' ? of(0) : this.load())),
        takeUntilDestroyed(),
      )
      .subscribe((count) => this.countState.set(count));
  }

  start(): void {
    this.active = true;
    this.triggers.next('load');
  }

  clear(): void {
    this.active = false;
    this.triggers.next('clear');
  }

  refresh(): void {
    if (this.active) this.triggers.next('load');
  }

  navigated(): void {
    if (this.active) this.navigations.next();
  }

  private load(): Observable<number> {
    return this.http.get<UnreadMessageCount>(`${environment.apiUrl}/threads/unread-count`).pipe(
      map((result) => result.count),
      catchError((error: unknown) => {
        if (!isExpectedClientStatus(error)) {
          this.reporting.report('api-failure', 'messages.unread.count', error);
        }

        return EMPTY;
      }),
    );
  }
}
