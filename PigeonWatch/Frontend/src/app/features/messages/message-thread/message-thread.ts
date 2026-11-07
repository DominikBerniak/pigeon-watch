import { DatePipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  computed,
  inject,
  input,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import {
  FormField,
  FormRoot,
  form,
  maxLength,
  required,
  requiredError,
  validate,
} from '@angular/forms/signals';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatTooltipModule } from '@angular/material/tooltip';
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
  SubmitButton,
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
    MatTooltipModule,
    TranslatePipe,
    Alert,
    ErrorReference,
    ErrorRetry,
    FieldErrors,
    LoadingIndicator,
    SubmitButton,
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
  private readonly submitState = inject(FormSubmitState);
  private readonly destroyRef = inject(DestroyRef);
  private readonly reloads = new Subject<void>();
  private readonly replyModel = signal<MessageText>({ text: '' });
  private readonly heading = viewChild<ElementRef<HTMLElement>>('heading');
  private loadedPages = 0;
  private olderRequest: Subscription | null = null;

  readonly sightingId = input.required<string>();
  readonly threadId = input.required<string>();
  readonly counterpartDisplayName = input.required<string>();

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
    required(path.text);
    validate(path.text, ({ value }) => (isBlank(value()) ? requiredError() : undefined));
    maxLength(path.text, () => this.rules().textMaxLength);
  });
  protected readonly textMessages = computed(() => ({
    required: this.resources.translate('sighting.messages.validation.required'),
    maxLength: this.resources.translate(
      'sighting.messages.validation.maxLength',
      this.rules().textMaxLength,
    ),
  }));
  protected readonly entries = computed<readonly MessageEntry[]>(() => {
    const now = this.now();

    return this.items().map((message) => ({ message, age: sightingAge(message.createdAt, now) }));
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

          return;
        }

        this.state.set(result);
      });
    this.destroyRef.onDestroy(() => this.olderRequest?.unsubscribe());
  }

  focusHeading(): void {
    this.heading()?.nativeElement.focus();
  }

  protected retry(): void {
    this.reloads.next();
  }

  protected send(): void {
    this.submitState.markSubmitted();
    this.sendFailure.set(null);

    if (this.sending() || this.replyForm().invalid()) return;

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
  return value !== '' && value.trim() === '';
}

function appendNew(
  items: readonly SightingMessage[],
  incoming: readonly SightingMessage[],
): readonly SightingMessage[] {
  const known = new Set(items.map((item) => item.id));

  return [...items, ...incoming.filter((item) => !known.has(item.id))];
}
