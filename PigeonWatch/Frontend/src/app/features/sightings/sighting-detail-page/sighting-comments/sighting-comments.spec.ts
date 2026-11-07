import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Observable, of, throwError } from 'rxjs';
import { ErrorReportingService } from '../../../../core/error-reporting/error-reporting.service';
import { SightingComment, SightingCommentPage } from '../../sighting.types';
import { SightingsApi } from '../../sightings-api';
import { SightingComments } from './sighting-comments';

const minute = 60_000;

function commentOf(id: string, overrides: Partial<SightingComment> = {}): SightingComment {
  return {
    id,
    text: `Comment ${id}`,
    authorDisplayName: 'Ola',
    createdAt: new Date(Date.now() - 5 * minute).toISOString(),
    editedAt: null,
    isDeleted: false,
    isMine: false,
    ...overrides,
  };
}

const mine = commentOf('mine', { text: 'My note\nsecond line', authorDisplayName: 'Jan K', isMine: true });
const other = commentOf('other');
const placeholder: SightingComment = {
  ...mine,
  text: null,
  authorDisplayName: null,
  editedAt: null,
  isDeleted: true,
  isMine: false,
};

describe('SightingComments', () => {
  let comments: ReturnType<
    typeof vi.fn<(sightingId: string, page: number) => Observable<SightingCommentPage>>
  >;
  let addComment: ReturnType<
    typeof vi.fn<(sightingId: string, text: string) => Observable<SightingComment>>
  >;
  let editComment: ReturnType<
    typeof vi.fn<(sightingId: string, commentId: string, text: string) => Observable<SightingComment>>
  >;
  let deleteComment: ReturnType<
    typeof vi.fn<(sightingId: string, commentId: string) => Observable<SightingComment>>
  >;
  let fixture: ComponentFixture<SightingComments>;

  beforeEach(() => {
    comments = vi.fn(() => of({ items: [mine, other], totalCount: 2 }));
    addComment = vi.fn((_sightingId: string, text: string) =>
      of(commentOf('new', { text, authorDisplayName: 'Jan K', isMine: true })),
    );
    editComment = vi.fn((_sightingId: string, commentId: string, text: string) =>
      of({ ...mine, id: commentId, text, editedAt: new Date().toISOString() }),
    );
    deleteComment = vi.fn(() => of(placeholder));
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: SightingsApi, useValue: { comments, addComment, editComment, deleteComment } },
        { provide: ErrorReportingService, useValue: { report: vi.fn() } },
      ],
    });
  });

  afterEach(() => {
    document.querySelectorAll('app-delete-comment-dialog').forEach((dialog) => dialog.remove());
  });

  async function open(expand = true): Promise<HTMLElement> {
    fixture = TestBed.createComponent(SightingComments);
    fixture.componentRef.setInput('sightingId', 'sighting-1');
    await settle();

    const root = fixture.nativeElement as HTMLElement;

    if (expand) {
      root.querySelector<HTMLButtonElement>('button.sighting-comments__toggle')!.click();
      await settle();
    }

    return root;
  }

  async function settle(): Promise<void> {
    await fixture.whenStable();
    fixture.detectChanges();
    await fixture.whenStable();
  }

  function items(root: HTMLElement): HTMLElement[] {
    return [...root.querySelectorAll<HTMLElement>('li.sighting-comments__item')];
  }

  function item(root: HTMLElement, id: string): HTMLElement {
    const match = root.querySelector<HTMLElement>(`li[data-comment-id="${id}"]`);

    if (!match) throw new Error(`No comment ${id}`);

    return match;
  }

  function buttonIn(root: Element, text: string): HTMLButtonElement | null {
    return (
      [...root.querySelectorAll<HTMLButtonElement>('button')].find(
        (candidate) => candidate.textContent?.trim() === text,
      ) ?? null
    );
  }

  function type(field: HTMLTextAreaElement, value: string): void {
    field.value = value;
    field.dispatchEvent(new Event('input'));
  }

  function addField(root: HTMLElement): HTMLTextAreaElement {
    return root.querySelector<HTMLTextAreaElement>('form.sighting-comments__add textarea')!;
  }

  async function post(root: HTMLElement, text: string): Promise<void> {
    type(addField(root), text);
    root
      .querySelector('form.sighting-comments__add')!
      .dispatchEvent(new Event('submit', { cancelable: true }));
    await settle();
  }

  it('loads the first page and shows the count in the heading', async () => {
    const root = await open();

    expect(comments).toHaveBeenCalledExactlyOnceWith('sighting-1', 1);
    expect(root.querySelector('h2')?.textContent?.trim()).toBe('Comments (2)');
    expect(items(root)).toHaveLength(2);
  });

  it('starts collapsed with the count, expands and collapses again, keeping a typed draft', async () => {
    const root = await open(false);
    const toggle = root.querySelector<HTMLButtonElement>('button.sighting-comments__toggle')!;
    const body = root.querySelector<HTMLElement>('.sighting-comments__body')!;

    expect(toggle.getAttribute('aria-controls')).toBe(body.id);
    expect(toggle.getAttribute('aria-expanded')).toBe('false');
    expect(toggle.textContent?.trim()).toBe('Show comments');
    expect(body.hidden).toBe(true);
    expect(root.querySelector('h2')?.textContent?.trim()).toBe('Comments (2)');
    expect(comments).toHaveBeenCalledExactlyOnceWith('sighting-1', 1);

    toggle.click();
    await settle();

    expect(toggle.getAttribute('aria-expanded')).toBe('true');
    expect(toggle.textContent?.trim()).toBe('Hide comments');
    expect(body.hidden).toBe(false);

    type(addField(root), 'Draft');
    toggle.click();
    await settle();

    expect(body.hidden).toBe(true);

    toggle.click();
    await settle();

    expect(body.hidden).toBe(false);
    expect(addField(root).value).toBe('Draft');
    expect(comments).toHaveBeenCalledOnce();
  });

  it('renders the list as a focusable labelled region that keeps line breaks as plain text', async () => {
    comments.mockReturnValue(
      of({ items: [{ ...mine, text: '<b>bold</b>\nnext' }], totalCount: 1 }),
    );

    const root = await open();
    const region = root.querySelector('.sighting-comments__list');
    const text = item(root, 'mine').querySelector('.sighting-comments__text');

    expect(region?.getAttribute('role')).toBe('region');
    expect(region?.getAttribute('tabindex')).toBe('0');
    expect(region?.getAttribute('aria-label')).toBe('Comments, newest first');
    expect(text?.textContent).toBe('<b>bold</b>\nnext');
    expect(text?.querySelector('b')).toBeNull();
  });

  it('shows the author, a relative time with the absolute date as title, and the edited marker', async () => {
    comments.mockReturnValue(
      of({ items: [{ ...other, editedAt: new Date().toISOString() }], totalCount: 1 }),
    );

    const root = await open();
    const time = item(root, 'other').querySelector('time');

    expect(item(root, 'other').querySelector('.sighting-comments__author')?.textContent?.trim()).toBe(
      'Ola',
    );
    expect(time?.getAttribute('datetime')).toBe(other.createdAt);
    expect(time?.textContent?.trim()).toBe('5 min ago');
    expect(time?.getAttribute('title')).not.toBe('');
    expect(item(root, 'other').querySelector('.sighting-comments__edited')?.textContent?.trim()).toBe(
      'edited',
    );
  });

  it('shows the muted empty text without an alert when there are no comments', async () => {
    comments.mockReturnValue(of({ items: [], totalCount: 0 }));

    const root = await open();

    expect(root.querySelector('p.pw-text-muted')?.textContent?.trim()).toBe(
      'No comments yet. Be the first to add one.',
    );
    expect(root.querySelector('app-alert')).toBeNull();
    expect(root.querySelector('dl')).toBeNull();
    expect(root.querySelector('a')).toBeNull();
  });

  it('shows the loading indicator while the first page loads', async () => {
    comments.mockReturnValue(new Observable<SightingCommentPage>());

    const root = await open();

    expect(root.querySelector('app-loading-indicator')).not.toBeNull();
    expect(root.querySelector('h2')?.textContent?.trim()).toBe('Comments');
  });

  it('shows a load error with a retry that loads again', async () => {
    comments.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));

    const root = await open();

    expect(root.querySelector('app-error-retry app-alert')?.textContent?.trim()).toBe(
      "We couldn't reach the comments right now. Try again.",
    );

    comments.mockReturnValue(of({ items: [other], totalCount: 1 }));
    buttonIn(root, 'Try again')?.click();
    await settle();

    expect(comments).toHaveBeenCalledTimes(2);
    expect(root.querySelector('app-error-retry')).toBeNull();
    expect(items(root)).toHaveLength(1);
  });

  it('loads page 2 inside the list and appends without duplicating a repeated item', async () => {
    comments.mockImplementation((_id, page) =>
      page === 1
        ? of({ items: [mine, other], totalCount: 4 })
        : of({ items: [other, commentOf('old-1'), commentOf('old-2')], totalCount: 4 }),
    );

    const root = await open();
    const showOlder = buttonIn(root, 'Show older comments');

    expect(root.querySelector('.sighting-comments__list')?.contains(showOlder)).toBe(true);

    showOlder?.click();
    await settle();

    expect(comments).toHaveBeenLastCalledWith('sighting-1', 2);
    expect(items(root).map((element) => element.dataset['commentId'])).toEqual([
      'mine',
      'other',
      'old-1',
      'old-2',
    ]);
    expect(buttonIn(root, 'Show older comments')).toBeNull();
  });

  it('shows a busy state while older comments load and an inline retry when they fail', async () => {
    let older: Observable<SightingCommentPage> = new Observable<SightingCommentPage>();

    comments.mockImplementation((_id, page) =>
      page === 1 ? of({ items: [mine], totalCount: 3 }) : older,
    );

    const root = await open();

    buttonIn(root, 'Show older comments')?.click();
    await settle();

    expect(root.querySelector('.sighting-comments__older app-loading-indicator')).not.toBeNull();

    older = throwError(() => new HttpErrorResponse({ status: 500 }));
    fixture.destroy();

    const again = await open();

    buttonIn(again, 'Show older comments')?.click();
    await settle();

    expect(again.querySelector('.sighting-comments__older app-alert')?.textContent?.trim()).toBe(
      "We couldn't load older comments.",
    );

    older = of({ items: [other], totalCount: 2 });
    buttonIn(again.querySelector('.sighting-comments__older')!, 'Try again')?.click();
    await settle();

    expect(items(again)).toHaveLength(2);
  });

  it('posts a comment, prepends it, clears the field and counts it', async () => {
    const root = await open();

    await post(root, 'Seen again by the bench');

    expect(addComment).toHaveBeenCalledExactlyOnceWith('sighting-1', 'Seen again by the bench');
    expect(items(root)[0].dataset['commentId']).toBe('new');
    expect(addField(root).value).toBe('');
    expect(root.querySelector('h2')?.textContent?.trim()).toBe('Comments (3)');
    expect(root.querySelector('form.sighting-comments__add mat-error')).toBeNull();
  });

  it('reloads the thread after a successful post when the first page had failed', async () => {
    comments.mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 500 })));
    const root = await open();

    expect(root.querySelector('app-error-retry')).not.toBeNull();

    comments.mockReturnValue(
      of({ items: [commentOf('new', { text: 'Seen again by the bench' }), mine, other], totalCount: 3 }),
    );
    await post(root, 'Seen again by the bench');

    expect(comments).toHaveBeenCalledTimes(2);
    expect(root.querySelector('app-error-retry')).toBeNull();
    expect(items(root).map((element) => element.dataset['commentId'])).toEqual(['new', 'mine', 'other']);
    expect(root.querySelector('h2')?.textContent?.trim()).toBe('Comments (3)');
    expect(addField(root).value).toBe('');
  });

  it('shows the cooldown seconds and keeps the text', async () => {
    addComment.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 400,
            error: { errors: { CooldownActive: ['Wait'] }, retryAfterSeconds: 42 },
          }),
      ),
    );

    const root = await open();

    await post(root, 'Too soon');

    expect(root.querySelector('form.sighting-comments__add app-alert')?.textContent?.trim()).toBe(
      'You can post or edit another comment in 42 seconds.',
    );
    expect(addField(root).value).toBe('Too soon');
    expect(items(root)).toHaveLength(2);
  });

  it('shows the required error for whitespace-only text and makes no call', async () => {
    const root = await open();

    await post(root, '   ');

    expect(addComment).not.toHaveBeenCalled();
    expect(root.querySelector('form.sighting-comments__add mat-error')?.textContent?.trim()).toBe(
      'Write a comment first.',
    );
  });

  it('offers Edit and Delete only on the caller own comments', async () => {
    const root = await open();

    expect(buttonIn(item(root, 'mine'), 'Edit')).not.toBeNull();
    expect(buttonIn(item(root, 'mine'), 'Delete')).not.toBeNull();
    expect(buttonIn(item(root, 'other'), 'Edit')).toBeNull();
    expect(buttonIn(item(root, 'other'), 'Delete')).toBeNull();
  });

  it('edits inline, saves and shows the edited marker', async () => {
    const root = await open();

    buttonIn(item(root, 'mine'), 'Edit')?.click();
    await settle();

    const field = item(root, 'mine').querySelector<HTMLTextAreaElement>('textarea')!;

    expect(field.value).toBe(mine.text);
    expect(document.activeElement).toBe(field);

    type(field, 'Updated note');
    item(root, 'mine')
      .querySelector('form')!
      .dispatchEvent(new Event('submit', { cancelable: true }));
    await settle();

    expect(editComment).toHaveBeenCalledExactlyOnceWith('sighting-1', 'mine', 'Updated note');
    expect(item(root, 'mine').querySelector('textarea')).toBeNull();
    expect(item(root, 'mine').querySelector('.sighting-comments__text')?.textContent).toBe(
      'Updated note',
    );
    expect(item(root, 'mine').querySelector('.sighting-comments__edited')).not.toBeNull();
    expect(document.activeElement).toBe(buttonIn(item(root, 'mine'), 'Edit'));
  });

  it('saving an edit does not validate the empty add field, and posting does not validate the edit field', async () => {
    const root = await open();
    buttonIn(item(root, 'mine'), 'Edit')!.click();
    await settle();
    const editField = item(root, 'mine').querySelector<HTMLTextAreaElement>('textarea')!;
    type(editField, '   ');
    buttonIn(item(root, 'mine'), 'Save')!.click();
    await settle();

    expect(item(root, 'mine').querySelector('app-field-errors')?.textContent?.trim()).toBeTruthy();
    expect(root.querySelector('form.sighting-comments__add app-field-errors')?.textContent?.trim() ?? '').toBe('');
    expect(root.querySelector('form.sighting-comments__add .mat-form-field-invalid')).toBeNull();

    type(editField, 'Fixed');
    await post(root, '   ');

    expect(root.querySelector('form.sighting-comments__add app-field-errors')?.textContent?.trim()).toBeTruthy();
    expect(item(root, 'mine').querySelector('.mat-form-field-invalid')).toBeNull();
  });

  it('cancels an edit without a call', async () => {
    const root = await open();

    buttonIn(item(root, 'mine'), 'Edit')?.click();
    await settle();
    buttonIn(item(root, 'mine'), 'Cancel')?.click();
    await settle();

    expect(editComment).not.toHaveBeenCalled();
    expect(item(root, 'mine').querySelector('textarea')).toBeNull();
  });

  it('keeps the edit open with the cooldown seconds when the edit is throttled', async () => {
    editComment.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 400,
            error: { errors: { CooldownActive: ['Wait'] }, retryAfterSeconds: 7 },
          }),
      ),
    );

    const root = await open();

    buttonIn(item(root, 'mine'), 'Edit')?.click();
    await settle();
    type(item(root, 'mine').querySelector('textarea')!, 'Changed');
    item(root, 'mine')
      .querySelector('form')!
      .dispatchEvent(new Event('submit', { cancelable: true }));
    await settle();

    expect(item(root, 'mine').querySelector('app-alert')?.textContent?.trim()).toBe(
      'You can post or edit another comment in 7 seconds.',
    );
    expect(item(root, 'mine').querySelector('textarea')?.value).toBe('Changed');
  });

  it('shows the not-found message when the edited comment is gone', async () => {
    editComment.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 404 })));

    const root = await open();

    buttonIn(item(root, 'mine'), 'Edit')?.click();
    await settle();
    type(item(root, 'mine').querySelector('textarea')!, 'Changed');
    item(root, 'mine')
      .querySelector('form')!
      .dispatchEvent(new Event('submit', { cancelable: true }));
    await settle();

    expect(item(root, 'mine').querySelector('app-alert')?.textContent?.trim()).toBe(
      'This comment no longer exists. Refresh the comments.',
    );
  });

  async function answerDeleteDialog(root: HTMLElement, choice: string): Promise<void> {
    buttonIn(item(root, 'mine'), 'Delete')?.click();
    await settle();

    const dialog = document.querySelector<HTMLElement>('app-delete-comment-dialog');

    expect(dialog?.querySelector('h2')?.textContent?.trim()).toBe('Delete this comment?');

    buttonIn(dialog!, choice)?.click();
    await settle();
    await vi.waitFor(() =>
      expect(document.querySelector('app-delete-comment-dialog')).toBeNull(),
    );
    await settle();
  }

  it('replaces a confirmed delete in place with the placeholder', async () => {
    const root = await open();

    await answerDeleteDialog(root, 'Delete comment');

    expect(deleteComment).toHaveBeenCalledExactlyOnceWith('sighting-1', 'mine');
    expect(items(root)[0].dataset['commentId']).toBe('mine');
    expect(item(root, 'mine').textContent?.trim()).toBe('Comment deleted');
    expect(item(root, 'mine').querySelector('.sighting-comments__author')).toBeNull();
    expect(item(root, 'mine').querySelector('button')).toBeNull();
    expect(root.querySelector('h2')?.textContent?.trim()).toBe('Comments (2)');
  });

  it('makes no call when the delete is cancelled', async () => {
    const root = await open();

    await answerDeleteDialog(root, 'Cancel');

    expect(deleteComment).not.toHaveBeenCalled();
    expect(item(root, 'mine').querySelector('.sighting-comments__text')).not.toBeNull();
  });

  it('shows the not-found message when the deleted comment is gone', async () => {
    deleteComment.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 404 })));

    const root = await open();

    await answerDeleteDialog(root, 'Delete comment');

    expect(item(root, 'mine').querySelector('app-alert')?.textContent?.trim()).toBe(
      'This comment no longer exists. Refresh the comments.',
    );
  });

  it('starts over with page 1 when the sighting changes', async () => {
    await open();

    fixture.componentRef.setInput('sightingId', 'sighting-2');
    await settle();

    expect(comments).toHaveBeenLastCalledWith('sighting-2', 1);
  });
});
