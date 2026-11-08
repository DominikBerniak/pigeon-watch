import { DatePipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  Injector,
  afterNextRender,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { ActivatedRoute, ParamMap, RouterLink } from '@angular/router';
import { Subject, Subscription, catchError, map, of, skip, startWith, switchMap, tap } from 'rxjs';
import { ErrorReportingService } from '../../../core/error-reporting/error-reporting.service';
import { TranslatePipe } from '../../../core/resources/translate.pipe';
import { unexpectedTraceId } from '../../../shared/http/problem-details';
import {
  ErrorReference,
  ErrorRetry,
  LoadingIndicator,
  PageCard,
  Tooltip,
} from '../../../shared/ui';
import { isReportableMessageFailure, messageFailure } from '../../sightings/message-errors';
import { SightingAge, sightingAge } from '../../sightings/shared/sighting-age';
import { SightingThread, SightingThreadPage } from '../../sightings/sighting.types';
import { SightingsApi } from '../../sightings/sightings-api';
import {
  MessageThreadDialog,
  MessageThreadDialogData,
} from '../message-thread-dialog/message-thread-dialog';
import { MessageThread } from '../message-thread/message-thread';

type InboxState =
  | { readonly kind: 'loading' }
  | { readonly kind: 'error'; readonly traceId?: string }
  | { readonly kind: 'loaded' };

type MoreState =
  | { readonly kind: 'idle' }
  | { readonly kind: 'loading' }
  | { readonly kind: 'error'; readonly traceId?: string };

interface ThreadSelection {
  readonly sightingId: string;
  readonly threadId: string;
}

interface ThreadRow {
  readonly thread: SightingThread;
  readonly age: SightingAge;
  readonly selected: boolean;
}

@Component({
  selector: 'app-messages-page',
  imports: [
    DatePipe,
    MatButtonModule,
    RouterLink,
    TranslatePipe,
    ErrorReference,
    ErrorRetry,
    LoadingIndicator,
    MessageThread,
    PageCard,
    Tooltip,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './messages-page.html',
  styleUrl: './messages-page.scss',
})
export class MessagesPage {
  private readonly route = inject(ActivatedRoute);
  private readonly sightingsApi = inject(SightingsApi);
  private readonly reporting = inject(ErrorReportingService);
  private readonly dialog = inject(MatDialog);
  private readonly injector = inject(Injector);
  private readonly destroyRef = inject(DestroyRef);
  private readonly reloads = new Subject<void>();
  private readonly threadView = viewChild(MessageThread);
  private loadedPages = 0;
  private moreRequest: Subscription | null = null;

  protected readonly state = signal<InboxState>({ kind: 'loading' });
  protected readonly threads = signal<readonly SightingThread[]>([]);
  protected readonly totalCount = signal(0);
  protected readonly now = signal(new Date());
  protected readonly more = signal<MoreState>({ kind: 'idle' });
  protected readonly selection = toSignal(this.route.paramMap.pipe(map(selectionOf)), {
    initialValue: null,
  });
  protected readonly selectedThread = computed(() => {
    const selection = this.selection();

    if (!selection) return null;

    return (
      this.threads().find(
        (thread) => thread.id === selection.threadId && thread.sightingId === selection.sightingId,
      ) ?? null
    );
  });
  protected readonly rows = computed<readonly ThreadRow[]>(() => {
    const now = this.now();
    const selection = this.selection();

    return this.threads().map((thread) => ({
      thread,
      age: sightingAge(thread.lastMessageAt, now),
      selected:
        selection !== null &&
        thread.id === selection.threadId &&
        thread.sightingId === selection.sightingId,
    }));
  });
  protected readonly hasMore = computed(() => this.threads().length < this.totalCount());
  protected readonly promptShown = computed(
    () => this.state().kind === 'loaded' && this.threads().length > 0,
  );
  protected readonly loadErrorTraceId = computed(() => {
    const state = this.state();

    return state.kind === 'error' ? (state.traceId ?? null) : null;
  });
  protected readonly moreErrorTraceId = computed(() => {
    const more = this.more();

    return more.kind === 'error' ? (more.traceId ?? null) : null;
  });

  constructor() {
    this.reloads
      .pipe(
        startWith(undefined),
        tap(() => this.resetInbox()),
        switchMap(() =>
          this.sightingsApi.inbox(1).pipe(
            map((page): SightingThreadPage | InboxState => page),
            catchError((error: unknown) => of(this.loadFailure(error))),
          ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe((result) => {
        if ('items' in result) {
          this.applyFirstPage(result);

          return;
        }

        this.state.set(result);
      });

    this.route.paramMap
      .pipe(map(selectionOf), skip(1), takeUntilDestroyed())
      .subscribe((selection) => {
        if (selection) {
          afterNextRender(() => this.threadView()?.focusHeading(), { injector: this.injector });
        }
      });

    this.destroyRef.onDestroy(() => this.moreRequest?.unsubscribe());
  }

  protected retry(): void {
    this.reloads.next();
  }

  protected refresh(): void {
    this.reloads.next();
    this.threadView()?.reload();
  }

  protected openFullScreen(selection: ThreadSelection): void {
    this.dialog
      .open<MessageThreadDialog, MessageThreadDialogData>(MessageThreadDialog, {
        autoFocus: 'dialog',
        width: 'var(--pw-dialog-fullscreen-width)',
        height: 'var(--pw-dialog-fullscreen-height)',
        maxWidth: 'var(--pw-dialog-fullscreen-width)',
        maxHeight: 'var(--pw-dialog-fullscreen-height)',
        data: {
          sightingId: selection.sightingId,
          threadId: selection.threadId,
          thread: this.selectedThread(),
        },
      })
      .afterClosed()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.threadView()?.reload());
  }

  protected showMore(): void {
    if (this.more().kind === 'loading') return;

    const page = this.loadedPages + 1;

    this.more.set({ kind: 'loading' });
    this.moreRequest = this.sightingsApi.inbox(page).subscribe({
      next: (result) => {
        this.loadedPages = page;
        this.threads.update((threads) => appendNew(threads, result.items));
        this.totalCount.set(result.totalCount);
        this.more.set({ kind: 'idle' });
      },
      error: (error: unknown) => this.more.set(this.loadFailure(error)),
    });
  }

  private resetInbox(): void {
    this.moreRequest?.unsubscribe();
    this.moreRequest = null;
    this.loadedPages = 0;
    this.state.set({ kind: 'loading' });
    this.more.set({ kind: 'idle' });
  }

  private applyFirstPage(page: SightingThreadPage): void {
    this.loadedPages = 1;
    this.now.set(new Date());
    this.threads.set(appendNew([], page.items));
    this.totalCount.set(page.totalCount);
    this.state.set({ kind: 'loaded' });
  }

  private loadFailure(error: unknown): { readonly kind: 'error'; readonly traceId?: string } {
    if (isReportableMessageFailure(error, messageFailure(error))) {
      this.reporting.report('api-failure', 'messages.inbox.load', error);
    }

    const traceId = unexpectedTraceId(error);

    return traceId === undefined ? { kind: 'error' } : { kind: 'error', traceId };
  }
}

function selectionOf(params: ParamMap): ThreadSelection | null {
  const sightingId = params.get('sightingId');
  const threadId = params.get('threadId');

  if (!sightingId || !threadId) return null;

  return { sightingId, threadId };
}

function appendNew(
  threads: readonly SightingThread[],
  incoming: readonly SightingThread[],
): readonly SightingThread[] {
  const known = new Set(threads.map((thread) => thread.id));

  return [...threads, ...incoming.filter((thread) => !known.has(thread.id))];
}
