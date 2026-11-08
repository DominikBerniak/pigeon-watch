import { TextFieldModule } from '@angular/cdk/text-field';
import { DatePipe } from '@angular/common';
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
  output,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { FormField, FormRoot, form, maxLength } from '@angular/forms/signals';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { RouterLink } from '@angular/router';
import {
  Subject,
  Subscription,
  catchError,
  combineLatest,
  finalize,
  map,
  of,
  startWith,
  switchMap,
  tap,
} from 'rxjs';
import { ConfigurationService } from '../../../core/configuration/configuration.service';
import { ErrorReportingService } from '../../../core/error-reporting/error-reporting.service';
import { UnreadCountService } from '../../../core/messages/unread-count.service';
import { ResourceService } from '../../../core/resources/resource.service';
import { TranslatePipe } from '../../../core/resources/translate.pipe';
import { unexpectedTraceId } from '../../../shared/http/problem-details';
import {
  Alert,
  ErrorReference,
  ErrorRetry,
  FieldErrors,
  FormSubmitState,
  LoadingIndicator,
  Tooltip,
  provideFormFieldDefaults,
} from '../../../shared/ui';
import {
  MessageContext,
  MessageFailure,
  isReportableMessageFailure,
  messageErrorMessage,
  messageFailure,
  messageFailureTraceId,
} from '../../sightings/message-errors';
import { SightingAge, sightingAge } from '../../sightings/shared/sighting-age';
import {
  SightingMessage,
  SightingMessagePage,
  SightingThread,
  fallbackMessageRules,
} from '../../sightings/sighting.types';
import { SightingsApi } from '../../sightings/sightings-api';

interface MessageText {
  text: string;
}

type LoadState =
  | { readonly kind: 'loading' }
  | { readonly kind: 'error'; readonly traceId?: string }
  | { readonly kind: 'loaded' };

type OlderState =
  | { readonly kind: 'idle' }
  | { readonly kind: 'loading' }
  | { readonly kind: 'error'; readonly traceId?: string };

interface MessageEntry {
  readonly message: SightingMessage;
  readonly age: SightingAge;
}

interface ThreadKey {
  readonly sightingId: string;
  readonly threadId: string;
}

let nextTitleId = 0;

@Component({
  providers: [provideFormFieldDefaults()],
  selector: 'app-message-thread',
  imports: [
    DatePipe,
    FormField,
    FormRoot,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    RouterLink,
    TextFieldModule,
    TranslatePipe,
    Alert,
    ErrorReference,
    ErrorRetry,
    FieldErrors,
    LoadingIndicator,
    Tooltip,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './message-thread.html',
  styleUrl: './message-thread.scss',
})
export class MessageThread {
  private readonly sightingsApi = inject(SightingsApi);
  private readonly configuration = inject(ConfigurationService);
  private readonly resources = inject(ResourceService);
  private readonly reporting = inject(ErrorReportingService);
  private readonly unreadCount = inject(UnreadCountService);
  private readonly submitState = inject(FormSubmitState);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly reloads = new Subject<void>();
  private readonly replyModel = signal<MessageText>({ text: '' });
  private readonly heading = viewChild<ElementRef<HTMLElement>>('heading');
  private readonly list = viewChild<ElementRef<HTMLElement>>('list');
  private loadedPages = 0;
  private olderRequest: Subscription | null = null;
  private readRequest: Subscription | null = null;

  readonly sightingId = input.required<string>();
  readonly threadId = input.required<string>();
  readonly thread = input<SightingThread | null>(null);
  readonly expanded = input(false);
  readonly expandToggle = output<void>();
  readonly markedRead = output<string>();

  private readonly instanceId = nextTitleId++;
  protected readonly titleId = `message-thread-title-${this.instanceId}`;
  protected readonly hintId = `message-thread-hint-${this.instanceId}`;
  protected readonly expandLabel = computed(() =>
    this.expanded() ? 'messages.chat.close' : 'messages.chat.maximize',
  );

  protected readonly rules = computed(
    () => this.configuration.clientConfig()?.messageRules ?? fallbackMessageRules,
  );
  protected readonly state = signal<LoadState>({ kind: 'loading' });
  protected readonly items = signal<readonly SightingMessage[]>([]);
  protected readonly totalCount = signal(0);
  protected readonly now = signal(new Date());
  protected readonly older = signal<OlderState>({ kind: 'idle' });
  protected readonly sending = signal(false);
  protected readonly sendFailure = signal<MessageFailure | null>(null);
  protected readonly replyForm = form(this.replyModel, (path) => {
    maxLength(path.text, () => this.rules().textMaxLength);
  });
  protected readonly blank = computed(() => isBlank(this.replyModel().text));
  protected readonly title = computed(() => {
    const thread = this.thread();

    return thread
      ? this.resources.translate('sighting.messages.thread.title', thread.counterpartDisplayName)
      : this.resources.translate('messages.chat.titleUnknown');
  });
  protected readonly sightingLinkText = computed(() => {
    const thread = this.thread();

    if (!thread) return this.resources.translate('messages.chat.openSighting');

    return this.resources.translate(
      'messages.chat.sightingLink',
      thread.sightingName ?? this.resources.translate('sighting.unnamed'),
    );
  });
  protected readonly replyLabel = computed(() => {
    const thread = this.thread();

    return thread
      ? this.resources.translate(
          'sighting.messages.thread.replyLabel',
          thread.counterpartDisplayName,
        )
      : this.resources.translate('messages.chat.replyLabelUnknown');
  });
  protected readonly textMessages = computed(() => ({
    maxLength: this.resources.translate(
      'sighting.messages.validation.maxLength',
      this.rules().textMaxLength,
    ),
  }));
  protected readonly entries = computed<readonly MessageEntry[]>(() => {
    const now = this.now();

    return this.items()
      .map((message) => ({ message, age: sightingAge(message.createdAt, now) }))
      .reverse();
  });
  protected readonly hasOlder = computed(() => this.items().length < this.totalCount());
  protected readonly loadErrorTraceId = computed(() => {
    const state = this.state();

    return state.kind === 'error' ? (state.traceId ?? null) : null;
  });
  protected readonly olderErrorTraceId = computed(() => {
    const older = this.older();

    return older.kind === 'error' ? (older.traceId ?? null) : null;
  });
  protected readonly sendErrorText = computed(() => {
    const failure = this.sendFailure();

    return failure ? messageErrorMessage(this.resources, failure) : null;
  });
  protected readonly sendErrorTraceId = computed(() => messageFailureTraceId(this.sendFailure()));

  private readonly key = computed<ThreadKey>(() => ({
    sightingId: this.sightingId(),
    threadId: this.threadId(),
  }));

  constructor() {
    combineLatest([toObservable(this.key), this.reloads.pipe(startWith(undefined))])
      .pipe(
        tap(() => this.resetThread()),
        switchMap(([key]) =>
          this.sightingsApi.threadMessages(key.sightingId, key.threadId, 1).pipe(
            map((page): SightingMessagePage | LoadState => page),
            catchError((error: unknown) => of(this.loadFailure(error))),
          ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe((result) => {
        if ('items' in result) {
          this.applyFirstPage(result);
          this.markRead();

          return;
        }

        this.state.set(result);
      });
    this.destroyRef.onDestroy(() => {
      this.olderRequest?.unsubscribe();
      this.readRequest?.unsubscribe();
    });
  }

  focusHeading(): void {
    this.heading()?.nativeElement.focus();
  }

  reload(): void {
    this.reloads.next();
  }

  protected retry(): void {
    this.reloads.next();
  }

  protected sendOnEnter(event: Event): void {
    const key = event as KeyboardEvent;

    if (key.shiftKey || key.isComposing) return;

    key.preventDefault();
    this.send();
  }

  protected send(): void {
    if (this.sending() || isBlank(this.replyModel().text)) return;

    this.submitState.markSubmitted();
    this.sendFailure.set(null);

    if (this.replyForm().invalid()) return;

    this.sending.set(true);
    this.sightingsApi
      .sendMessage(this.sightingId(), this.threadId(), this.replyModel().text)
      .pipe(
        finalize(() => this.sending.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (message) => {
          if (this.state().kind === 'loaded') {
            this.items.update((items) => [
              message,
              ...items.filter((item) => item.id !== message.id),
            ]);
            this.totalCount.update((count) => count + 1);
            this.now.set(new Date());
            this.scrollToNewest();
          } else {
            this.reloads.next();
          }

          this.replyModel.set({ text: '' });
          this.replyForm().reset();
          this.submitState.reset();
        },
        error: (error: unknown) =>
          this.sendFailure.set(this.failureOf(error, 'sighting.messages.send')),
      });
  }

  protected showOlder(): void {
    if (this.older().kind === 'loading') return;

    const page = this.loadedPages + 1;

    this.older.set({ kind: 'loading' });
    this.olderRequest = this.sightingsApi
      .threadMessages(this.sightingId(), this.threadId(), page)
      .subscribe({
        next: (result) => {
          this.keepScrollPosition();
          this.loadedPages = page;
          this.items.update((items) => appendNew(items, result.items));
          this.totalCount.set(result.totalCount);
          this.older.set({ kind: 'idle' });
        },
        error: (error: unknown) => this.older.set(this.loadFailure(error)),
      });
  }

  private resetThread(): void {
    this.olderRequest?.unsubscribe();
    this.olderRequest = null;
    this.readRequest?.unsubscribe();
    this.readRequest = null;
    this.loadedPages = 0;
    this.state.set({ kind: 'loading' });
    this.items.set([]);
    this.totalCount.set(0);
    this.older.set({ kind: 'idle' });
    this.sendFailure.set(null);
  }

  private applyFirstPage(page: SightingMessagePage): void {
    this.loadedPages = 1;
    this.now.set(new Date());
    this.items.set(appendNew([], page.items));
    this.totalCount.set(page.totalCount);
    this.state.set({ kind: 'loaded' });
    this.scrollToNewest();
  }

  private markRead(): void {
    const threadId = this.threadId();

    this.readRequest = this.sightingsApi.markThreadRead(this.sightingId(), threadId).subscribe({
      next: () => {
        this.markedRead.emit(threadId);
        this.unreadCount.refresh();
      },
      error: (error: unknown) => this.failureOf(error, 'messages.thread.read'),
    });
  }

  private scrollToNewest(): void {
    afterNextRender(
      {
        write: () => {
          const list = this.list()?.nativeElement;

          if (list) list.scrollTop = list.scrollHeight;
        },
      },
      { injector: this.injector },
    );
  }

  private keepScrollPosition(): void {
    const list = this.list()?.nativeElement;

    if (!list) return;

    const fromBottom = list.scrollHeight - list.scrollTop;

    afterNextRender(
      {
        write: () => {
          list.scrollTop = list.scrollHeight - fromBottom;
        },
      },
      { injector: this.injector },
    );
  }

  private loadFailure(error: unknown): { readonly kind: 'error'; readonly traceId?: string } {
    this.failureOf(error, 'sighting.messages.load');

    const traceId = unexpectedTraceId(error);

    return traceId === undefined ? { kind: 'error' } : { kind: 'error', traceId };
  }

  private failureOf(error: unknown, context: MessageContext): MessageFailure {
    const failure = messageFailure(error);

    if (isReportableMessageFailure(error, failure)) {
      this.reporting.report('api-failure', context, error);
    }

    return failure;
  }
}

function isBlank(value: string): boolean {
  return value.trim() === '';
}

function appendNew(
  items: readonly SightingMessage[],
  incoming: readonly SightingMessage[],
): readonly SightingMessage[] {
  const known = new Set(items.map((item) => item.id));

  return [...items, ...incoming.filter((item) => !known.has(item.id))];
}
