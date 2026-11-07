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
  signal,
  viewChild,
  viewChildren,
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
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import {
  Observable,
  Subject,
  Subscription,
  catchError,
  combineLatest,
  filter,
  finalize,
  map,
  of,
  startWith,
  switchMap,
  tap,
} from 'rxjs';
import { ConfigurationService } from '../../../../core/configuration/configuration.service';
import { ErrorReportingService } from '../../../../core/error-reporting/error-reporting.service';
import { ResourceService } from '../../../../core/resources/resource.service';
import { TranslatePipe } from '../../../../core/resources/translate.pipe';
import { unexpectedTraceId } from '../../../../shared/http/problem-details';
import {
  Alert,
  ErrorReference,
  ErrorRetry,
  FieldErrors,
  FormActions,
  FormSubmitScope,
  FormSubmitState,
  LoadingIndicator,
  SubmitButton,
  provideFormFieldDefaults,
} from '../../../../shared/ui';
import {
  CommentContext,
  CommentFailure,
  commentErrorMessage,
  commentFailure,
  commentFailureTraceId,
  isReportableCommentFailure,
} from '../../comment-errors';
import { SightingAge, sightingAge } from '../../shared/sighting-age';
import {
  SightingComment,
  SightingCommentPage,
  fallbackCommentRules,
} from '../../sighting.types';
import { SightingsApi } from '../../sightings-api';
import { DeleteCommentDialog } from './delete-comment-dialog/delete-comment-dialog';

interface CommentText {
  text: string;
}

type ThreadState =
  | { readonly kind: 'loading' }
  | { readonly kind: 'error'; readonly traceId?: string }
  | { readonly kind: 'loaded' };

type OlderState =
  | { readonly kind: 'idle' }
  | { readonly kind: 'loading' }
  | { readonly kind: 'error'; readonly traceId?: string };

interface ItemFailure {
  readonly commentId: string;
  readonly failure: CommentFailure;
}

interface CommentEntry {
  readonly comment: SightingComment;
  readonly age: SightingAge;
}

@Component({
  providers: [provideFormFieldDefaults()],
  selector: 'app-sighting-comments',
  imports: [
    DatePipe,
    FormField,
    FormRoot,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    TranslatePipe,
    Alert,
    ErrorReference,
    ErrorRetry,
    FieldErrors,
    FormActions,
    FormSubmitScope,
    LoadingIndicator,
    SubmitButton,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './sighting-comments.html',
  styleUrl: './sighting-comments.scss',
})
export class SightingComments {
  private readonly sightingsApi = inject(SightingsApi);
  private readonly configuration = inject(ConfigurationService);
  private readonly resources = inject(ResourceService);
  private readonly reporting = inject(ErrorReportingService);
  private readonly submitState = inject(FormSubmitState);
  private readonly dialog = inject(MatDialog);
  private readonly injector = inject(Injector);
  private readonly destroyRef = inject(DestroyRef);
  private readonly reloads = new Subject<void>();
  private readonly addModel = signal<CommentText>({ text: '' });
  private readonly editModel = signal<CommentText>({ text: '' });
  private readonly editButtons = viewChildren('editButton', {
    read: ElementRef<HTMLButtonElement>,
  });
  private readonly editField = viewChild<ElementRef<HTMLTextAreaElement>>('editField');
  private readonly editSubmitState = viewChild('editFormElement', { read: FormSubmitState });
  private readonly listRegion = viewChild<ElementRef<HTMLElement>>('listRegion');
  private loadedPages = 0;
  private olderRequest: Subscription | null = null;

  readonly sightingId = input.required<string>();

  protected readonly rules = computed(
    () => this.configuration.clientConfig()?.commentRules ?? fallbackCommentRules,
  );
  protected readonly expanded = signal(false);
  protected readonly state = signal<ThreadState>({ kind: 'loading' });
  protected readonly items = signal<readonly SightingComment[]>([]);
  protected readonly totalCount = signal(0);
  protected readonly now = signal(new Date());
  protected readonly older = signal<OlderState>({ kind: 'idle' });
  protected readonly adding = signal(false);
  protected readonly addFailure = signal<CommentFailure | null>(null);
  protected readonly editingId = signal<string | null>(null);
  protected readonly saving = signal(false);
  protected readonly deletingId = signal<string | null>(null);
  protected readonly itemFailure = signal<ItemFailure | null>(null);
  protected readonly addForm = form(this.addModel, (path) => {
    required(path.text);
    validate(path.text, ({ value }) => (isBlank(value()) ? requiredError() : undefined));
    maxLength(path.text, () => this.rules().textMaxLength);
  });
  protected readonly editForm = form(this.editModel, (path) => {
    required(path.text);
    validate(path.text, ({ value }) => (isBlank(value()) ? requiredError() : undefined));
    maxLength(path.text, () => this.rules().textMaxLength);
  });
  protected readonly textMessages = computed(() => ({
    required: this.resources.translate('sighting.comments.validation.required'),
    maxLength: this.resources.translate(
      'sighting.comments.validation.maxLength',
      this.rules().textMaxLength,
    ),
  }));
  protected readonly heading = computed(() =>
    this.state().kind === 'loaded'
      ? this.resources.translate('sighting.comments.titleWithCount', this.totalCount())
      : this.resources.translate('sighting.comments.title'),
  );
  protected readonly entries = computed<readonly CommentEntry[]>(() => {
    const now = this.now();

    return this.items().map((comment) => ({ comment, age: sightingAge(comment.createdAt, now) }));
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
  protected readonly addErrorText = computed(() => this.messageOf(this.addFailure()));
  protected readonly addErrorTraceId = computed(() => commentFailureTraceId(this.addFailure()));

  constructor() {
    combineLatest([toObservable(this.sightingId), this.reloads.pipe(startWith(undefined))])
      .pipe(
        tap(() => this.resetThread()),
        switchMap(([id]) =>
          this.sightingsApi.comments(id, 1).pipe(
            map((page): SightingCommentPage | ThreadState => page),
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

  protected toggleExpanded(): void {
    this.expanded.update((expanded) => !expanded);
  }

  protected retry(): void {
    this.reloads.next();
  }

  protected itemErrorText(commentId: string): string | null {
    const failure = this.itemFailure();

    return failure?.commentId === commentId ? this.messageOf(failure.failure) : null;
  }

  protected itemErrorTraceId(commentId: string): string | null {
    const failure = this.itemFailure();

    return failure?.commentId === commentId ? commentFailureTraceId(failure.failure) : null;
  }

  protected submitAdd(): void {
    this.submitState.markSubmitted();
    this.addFailure.set(null);

    if (this.adding() || this.addForm().invalid()) return;

    this.adding.set(true);
    this.sightingsApi
      .addComment(this.sightingId(), this.addModel().text)
      .pipe(
        finalize(() => this.adding.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (comment) => {
          this.items.update((items) => [comment, ...items.filter((item) => item.id !== comment.id)]);
          this.totalCount.update((count) => count + 1);
          this.now.set(new Date());
          this.addModel.set({ text: '' });
          this.addForm().reset();
          this.submitState.reset();
        },
        error: (error: unknown) =>
          this.addFailure.set(this.failureOf(error, 'sighting.comments.add')),
      });
  }

  protected showOlder(): void {
    if (this.older().kind === 'loading') return;

    const page = this.loadedPages + 1;

    this.older.set({ kind: 'loading' });
    this.olderRequest = this.sightingsApi.comments(this.sightingId(), page).subscribe({
      next: (result) => {
        this.loadedPages = page;
        this.items.update((items) => appendNew(items, result.items));
        this.totalCount.set(result.totalCount);
        this.older.set({ kind: 'idle' });
      },
      error: (error: unknown) => this.older.set(this.loadFailure(error)),
    });
  }

  protected startEdit(comment: SightingComment): void {
    this.itemFailure.set(null);
    this.editModel.set({ text: comment.text ?? '' });
    this.editForm().reset();
    this.editingId.set(comment.id);
    afterNextRender(() => this.editField()?.nativeElement.focus(), { injector: this.injector });
  }

  protected cancelEdit(): void {
    const id = this.editingId();

    this.editingId.set(null);
    this.itemFailure.set(null);

    if (id !== null) this.focusEditButton(id);
  }

  protected saveEdit(comment: SightingComment): void {
    this.editSubmitState()?.markSubmitted();
    this.itemFailure.set(null);

    if (this.saving() || this.editForm().invalid()) return;

    this.saving.set(true);
    this.sightingsApi
      .editComment(this.sightingId(), comment.id, this.editModel().text)
      .pipe(
        finalize(() => this.saving.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (updated) => {
          this.replace(updated);
          this.editingId.set(null);
          this.focusEditButton(updated.id);
        },
        error: (error: unknown) =>
          this.itemFailure.set({
            commentId: comment.id,
            failure: this.failureOf(error, 'sighting.comments.edit'),
          }),
      });
  }

  protected confirmDelete(comment: SightingComment): void {
    this.itemFailure.set(null);
    this.dialog
      .open<DeleteCommentDialog, void, boolean>(DeleteCommentDialog)
      .afterClosed()
      .pipe(
        filter((confirmed) => confirmed === true),
        switchMap(() => this.deleteComment(comment)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe();
  }

  private deleteComment(comment: SightingComment): Observable<unknown> {
    this.deletingId.set(comment.id);

    return this.sightingsApi.deleteComment(this.sightingId(), comment.id).pipe(
      tap((placeholder) => {
        this.replace(placeholder);
        afterNextRender(() => this.listRegion()?.nativeElement.focus(), {
          injector: this.injector,
        });
      }),
      catchError((error: unknown) => {
        this.itemFailure.set({
          commentId: comment.id,
          failure: this.failureOf(error, 'sighting.comments.delete'),
        });

        return of(null);
      }),
      finalize(() => this.deletingId.set(null)),
    );
  }

  private resetThread(): void {
    this.olderRequest?.unsubscribe();
    this.olderRequest = null;
    this.loadedPages = 0;
    this.state.set({ kind: 'loading' });
    this.items.set([]);
    this.totalCount.set(0);
    this.older.set({ kind: 'idle' });
    this.editingId.set(null);
    this.itemFailure.set(null);
  }

  private applyFirstPage(page: SightingCommentPage): void {
    this.loadedPages = 1;
    this.now.set(new Date());
    this.items.set(appendNew([], page.items));
    this.totalCount.set(page.totalCount);
    this.state.set({ kind: 'loaded' });
  }

  private replace(updated: SightingComment): void {
    this.items.update((items) => items.map((item) => (item.id === updated.id ? updated : item)));
  }

  private focusEditButton(commentId: string): void {
    afterNextRender(
      () =>
        this.editButtons()
          .find((button) => button.nativeElement.dataset['commentId'] === commentId)
          ?.nativeElement.focus(),
      { injector: this.injector },
    );
  }

  private loadFailure(error: unknown): { readonly kind: 'error'; readonly traceId?: string } {
    this.failureOf(error, 'sighting.comments.load');

    const traceId = unexpectedTraceId(error);

    return traceId === undefined ? { kind: 'error' } : { kind: 'error', traceId };
  }

  private failureOf(error: unknown, context: CommentContext): CommentFailure {
    const failure = commentFailure(error);

    if (isReportableCommentFailure(error, failure)) {
      this.reporting.report('api-failure', context, error);
    }

    return failure;
  }

  private messageOf(failure: CommentFailure | null): string | null {
    return failure ? commentErrorMessage(this.resources, failure) : null;
  }
}

function isBlank(value: string): boolean {
  return value !== '' && value.trim() === '';
}

function appendNew(
  items: readonly SightingComment[],
  incoming: readonly SightingComment[],
): readonly SightingComment[] {
  const known = new Set(items.map((item) => item.id));

  return [...items, ...incoming.filter((item) => !known.has(item.id))];
}
