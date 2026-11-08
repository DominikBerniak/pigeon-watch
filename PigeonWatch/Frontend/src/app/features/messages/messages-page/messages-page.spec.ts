import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { Router, UrlSegment, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Observable, Subject, of, throwError } from 'rxjs';
import { ErrorReportingService } from '../../../core/error-reporting/error-reporting.service';
import {
  SightingMessage,
  SightingMessagePage,
  SightingThread,
  SightingThreadPage,
} from '../../sightings/sighting.types';
import { SightingsApi } from '../../sightings/sightings-api';
import { MessageThreadDialog } from '../message-thread-dialog/message-thread-dialog';
import { MessageThread } from '../message-thread/message-thread';
import { MessagesPage } from './messages-page';
import { messagesUrlMatcher } from './messages-url-matcher';

const minute = 60_000;

@Component({ template: '' })
class SightingRouteStub {}

function threadOf(index: number, overrides: Partial<SightingThread> = {}): SightingThread {
  return {
    id: `thread-${index}`,
    sightingId: `sighting-${index}`,
    sightingName: `Pigeon ${index}`,
    counterpartDisplayName: `Person ${index}`,
    isOwner: index % 2 === 0,
    lastMessageAt: new Date(Date.now() - (index * 60 + 5) * minute).toISOString(),
    ...overrides,
  };
}

const message: SightingMessage = {
  id: 'message-1',
  text: 'Is it still there?',
  senderDisplayName: 'Person 1',
  createdAt: new Date(Date.now() - 5 * minute).toISOString(),
  isMine: false,
};

describe('MessagesPage', () => {
  let inbox: ReturnType<typeof vi.fn<(page: number) => Observable<SightingThreadPage>>>;
  let threadMessages: ReturnType<
    typeof vi.fn<
      (sightingId: string, threadId: string, page: number) => Observable<SightingMessagePage>
    >
  >;
  let report: ReturnType<typeof vi.fn>;
  let harness: RouterTestingHarness;

  const first = threadOf(1);
  const unnamed = threadOf(2, { sightingName: null });

  beforeEach(async () => {
    inbox = vi.fn<(page: number) => Observable<SightingThreadPage>>(() =>
      of({ items: [first, unnamed], totalCount: 2 }),
    );
    threadMessages = vi.fn(() => of({ items: [message], totalCount: 1 }));
    report = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([
          { matcher: messagesUrlMatcher, component: MessagesPage },
          { path: 'sightings/:id', component: SightingRouteStub },
        ]),
        {
          provide: SightingsApi,
          useValue: {
            inbox,
            threadMessages,
            sendMessage: (): Observable<SightingMessage> => of(message),
          },
        },
        { provide: ErrorReportingService, useValue: { report } },
      ],
    });
    harness = await RouterTestingHarness.create();
  });

  async function open(url = '/messages'): Promise<HTMLElement> {
    await harness.navigateByUrl(url);
    await settle();

    return harness.routeNativeElement as HTMLElement;
  }

  async function settle(): Promise<void> {
    await harness.fixture.whenStable();
    harness.fixture.detectChanges();
    await harness.fixture.whenStable();
  }

  function rows(root: HTMLElement): HTMLAnchorElement[] {
    return [...root.querySelectorAll<HTMLAnchorElement>('a.messages__thread')];
  }

  function textOf(element: Element | null | undefined): string {
    return element?.textContent?.trim() ?? '';
  }

  function button(root: Element, text: string): HTMLButtonElement | null {
    return (
      [...root.querySelectorAll<HTMLButtonElement>('button')].find(
        (candidate) => textOf(candidate) === text,
      ) ?? null
    );
  }

  function threadView(): MessageThread | null {
    return (harness.routeDebugElement?.query(
      (element) => element.componentInstance instanceof MessageThread,
    )?.componentInstance ?? null) as MessageThread | null;
  }

  it('lists the threads most recent first with counterpart, sighting and last activity', async () => {
    const root = await open();
    const [firstRow, second] = rows(root);

    expect(inbox).toHaveBeenCalledExactlyOnceWith(1);
    expect(textOf(root.querySelector('h1'))).toBe('Messages');
    expect(root.querySelector('nav')?.getAttribute('aria-label')).toBe('Conversations');
    expect(rows(root)).toHaveLength(2);
    expect(textOf(firstRow.querySelector('.messages__counterpart'))).toBe('Person 1');
    expect(textOf(firstRow.querySelector('.messages__sighting'))).toBe('Pigeon 1');
    expect(textOf(firstRow.querySelector('time'))).toBe('1 h ago');
    expect(firstRow.querySelector('time')?.getAttribute('datetime')).toBe(first.lastMessageAt);
    expect(firstRow.querySelector('time')?.hasAttribute('title')).toBe(false);
    expect(
      document.getElementById(
        firstRow.querySelector('time')?.getAttribute('aria-describedby') ?? '',
      )?.textContent,
    ).toMatch(/\d{4}/);
    expect(firstRow.getAttribute('href')).toBe('/messages/sighting-1/thread-1');
    expect(textOf(second.querySelector('.messages__sighting'))).toBe('Unnamed bird');
  });

  it('shows the empty-pane prompt and no conversation while no thread is chosen', async () => {
    const root = await open();

    expect(textOf(root.querySelector('.messages__prompt'))).toBe(
      'Choose a conversation to read it and reply.',
    );
    expect(root.querySelector('app-message-thread')).toBeNull();
    expect(threadMessages).not.toHaveBeenCalled();
    expect(root.querySelector('.messages')?.classList).not.toContain('messages--selected');
  });

  it('shows the empty state when there are no threads', async () => {
    inbox.mockReturnValue(of({ items: [], totalCount: 0 }));

    const root = await open();

    expect(textOf(root.querySelector('nav p'))).toBe(
      "No conversations yet. Open someone else's sighting and message its reporter to start one.",
    );
    expect(root.querySelector('.messages__prompt')).toBeNull();
    expect(button(root, 'Show more')).toBeNull();
  });

  it('shows a load error with its reference, reports it and loads again on retry', async () => {
    inbox.mockReturnValue(
      throwError(
        () => new HttpErrorResponse({ status: 500, error: { traceId: '00-inbox-trace-01' } }),
      ),
    );

    const root = await open();
    const alert = root.querySelector('app-error-retry app-alert');

    expect(alert?.textContent).toContain("We couldn't load your conversations. Try again.");
    expect(alert?.textContent).toContain('00-inbox-trace-01');
    expect(report).toHaveBeenCalledExactlyOnceWith(
      'api-failure',
      'messages.inbox.load',
      expect.any(HttpErrorResponse),
    );

    inbox.mockReturnValue(of({ items: [threadOf(1)], totalCount: 1 }));
    button(root, 'Try again')?.click();
    await settle();

    expect(inbox).toHaveBeenCalledTimes(2);
    expect(root.querySelector('app-error-retry')).toBeNull();
    expect(rows(root)).toHaveLength(1);
  });

  it('does not report an expected status', async () => {
    inbox.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 429 })));

    const root = await open();

    expect(root.querySelector('app-error-retry')).not.toBeNull();
    expect(report).not.toHaveBeenCalled();
  });

  it('shows more threads and appends them without duplicating a repeated one', async () => {
    inbox.mockImplementation((page) =>
      page === 1
        ? of({ items: [threadOf(1), threadOf(2)], totalCount: 4 })
        : of({ items: [threadOf(2), threadOf(3), threadOf(4)], totalCount: 4 }),
    );

    const root = await open();

    button(root, 'Show more')?.click();
    await settle();

    expect(inbox).toHaveBeenLastCalledWith(2);
    expect(rows(root).map((row) => row.dataset['threadId'])).toEqual([
      'thread-1',
      'thread-2',
      'thread-3',
      'thread-4',
    ]);
    expect(button(root, 'Show more')).toBeNull();
  });

  it('shows an inline retry when more threads fail to load', async () => {
    inbox.mockImplementation((page) =>
      page === 1
        ? of({ items: [threadOf(1)], totalCount: 2 })
        : throwError(() => new HttpErrorResponse({ status: 500 })),
    );

    const root = await open();

    button(root, 'Show more')?.click();
    await settle();

    expect(textOf(root.querySelector('.messages__more app-alert'))).toBe(
      "We couldn't load more conversations.",
    );

    inbox.mockReturnValue(of({ items: [threadOf(2)], totalCount: 2 }));
    button(root.querySelector('.messages__more')!, 'Try again')?.click();
    await settle();

    expect(rows(root)).toHaveLength(2);
  });

  it('opens a deep-linked thread in the pane with its sidebar summary', async () => {
    const root = await open('/messages/sighting-1/thread-1');
    const selected = rows(root)[0];

    expect(threadMessages).toHaveBeenCalledExactlyOnceWith('sighting-1', 'thread-1', 1);
    expect(textOf(root.querySelector('app-message-thread h2'))).toBe('Conversation with Person 1');
    expect(textOf(root.querySelector('a.message-thread__sighting'))).toBe('Sighting: Pigeon 1');
    expect(selected.getAttribute('aria-current')).toBe('page');
    expect(selected.classList).toContain('messages__thread--selected');
    expect(rows(root)[1].getAttribute('aria-current')).toBeNull();
    expect(root.querySelector('.messages')?.classList).toContain('messages--selected');
    expect(root.querySelector('.messages__prompt')).toBeNull();
    expect(root.querySelector('a.messages__back')?.getAttribute('href')).toBe('/messages');
  });

  it('opens a thread that is not in the loaded threads with generic labels', async () => {
    const root = await open('/messages/sighting-9/thread-9');

    expect(threadMessages).toHaveBeenCalledExactlyOnceWith('sighting-9', 'thread-9', 1);
    expect(textOf(root.querySelector('app-message-thread h2'))).toBe('Conversation');
    expect(rows(root).some((row) => row.getAttribute('aria-current') === 'page')).toBe(false);
  });

  it('keeps the page and its threads when a thread is chosen and moves focus to the conversation', async () => {
    const root = await open();
    const page = harness.routeDebugElement?.componentInstance;

    await harness.navigateByUrl('/messages/sighting-2/thread-2');
    await settle();

    expect(harness.routeDebugElement?.componentInstance).toBe(page);
    expect(inbox).toHaveBeenCalledTimes(1);
    expect(threadMessages).toHaveBeenCalledExactlyOnceWith('sighting-2', 'thread-2', 1);
    expect(document.activeElement).toBe(root.querySelector('app-message-thread h2'));
  });

  it('switches the pane when another thread is chosen and empties it on the way back', async () => {
    const root = await open('/messages/sighting-1/thread-1');

    await harness.navigateByUrl('/messages/sighting-2/thread-2');
    await settle();

    expect(threadMessages).toHaveBeenLastCalledWith('sighting-2', 'thread-2', 1);
    expect(threadView()).not.toBeNull();

    await harness.navigateByUrl('/messages');
    await settle();

    expect(root.querySelector('app-message-thread')).toBeNull();
    expect(inbox).toHaveBeenCalledTimes(1);
  });

  it('puts a Refresh button in the card header', async () => {
    const root = await open();

    expect(root.querySelector('app-page-card header button')).toBe(button(root, 'Refresh'));
  });

  it('refreshes the threads from page 1 and drops the later pages without navigating', async () => {
    inbox.mockImplementation((page) =>
      page === 1
        ? of({ items: [threadOf(1), threadOf(2)], totalCount: 4 })
        : of({ items: [threadOf(3), threadOf(4)], totalCount: 4 }),
    );

    const root = await open();
    const page = harness.routeDebugElement?.componentInstance;

    button(root, 'Show more')?.click();
    await settle();

    expect(rows(root)).toHaveLength(4);

    const refreshed = new Subject<SightingThreadPage>();

    inbox.mockReturnValueOnce(refreshed);
    button(root, 'Refresh')?.click();
    await settle();

    expect(inbox).toHaveBeenLastCalledWith(1);
    expect(root.querySelector('nav app-loading-indicator')).not.toBeNull();
    expect(rows(root)).toHaveLength(0);

    refreshed.next({ items: [threadOf(5), threadOf(1)], totalCount: 3 });
    await settle();

    expect(rows(root).map((row) => row.dataset['threadId'])).toEqual(['thread-5', 'thread-1']);
    expect(button(root, 'Show more')).not.toBeNull();
    expect(TestBed.inject(Router).url).toBe('/messages');
    expect(harness.routeDebugElement?.componentInstance).toBe(page);
    expect(threadMessages).not.toHaveBeenCalled();
  });

  it('refreshes the open thread from its first page and keeps the selection', async () => {
    const root = await open('/messages/sighting-1/thread-1');
    const view = threadView();
    const refreshed = new Subject<SightingThreadPage>();

    inbox.mockReturnValueOnce(refreshed);
    button(root, 'Refresh')?.click();
    await settle();

    expect(inbox).toHaveBeenCalledTimes(2);
    expect(threadMessages).toHaveBeenCalledTimes(2);
    expect(threadMessages).toHaveBeenLastCalledWith('sighting-1', 'thread-1', 1);
    expect(threadView()).toBe(view);
    expect(textOf(root.querySelector('app-message-thread h2'))).toBe('Conversation with Person 1');

    refreshed.next({ items: [first, unnamed], totalCount: 2 });
    await settle();

    expect(rows(root)[0].getAttribute('aria-current')).toBe('page');
    expect(TestBed.inject(Router).url).toBe('/messages/sighting-1/thread-1');
    expect(root.querySelectorAll('li.message-thread__item')).toHaveLength(1);
  });

  it('refreshes after a failed load like Try again', async () => {
    inbox.mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 500 })));

    const root = await open();

    button(root, 'Refresh')?.click();
    await settle();

    expect(root.querySelector('app-error-retry')).toBeNull();
    expect(rows(root)).toHaveLength(2);
  });

  function dialogElement(): HTMLElement | null {
    return document.querySelector<HTMLElement>('app-message-thread-dialog');
  }

  afterEach(() => {
    TestBed.inject(MatDialog).closeAll();
    document.querySelectorAll('app-message-thread-dialog').forEach((dialog) => dialog.remove());
  });

  it('opens the chosen thread in a full screen dialog from the maximize button', async () => {
    const root = await open('/messages/sighting-1/thread-1');
    const dialog = TestBed.inject(MatDialog);
    const openSpy = vi.spyOn(dialog, 'open');
    const maximize = root.querySelector<HTMLButtonElement>('button.message-thread__expand')!;

    expect(maximize.getAttribute('aria-label')).toBe('Open in full screen');

    maximize.click();
    await settle();

    expect(openSpy).toHaveBeenCalledExactlyOnceWith(
      MessageThreadDialog,
      expect.objectContaining({
        autoFocus: 'dialog',
        width: 'var(--pw-dialog-fullscreen-width)',
        height: 'var(--pw-dialog-fullscreen-height)',
        maxWidth: 'var(--pw-dialog-fullscreen-width)',
        maxHeight: 'var(--pw-dialog-fullscreen-height)',
        data: { sightingId: 'sighting-1', threadId: 'thread-1', thread: first },
      }),
    );
    expect(textOf(dialogElement()?.querySelector('h2'))).toBe('Conversation with Person 1');
    expect(
      dialogElement()?.querySelector('button.message-thread__expand')?.getAttribute('aria-label'),
    ).toBe('Close');
  });

  it('reloads the pane thread and returns focus to the maximize button once the dialog closes', async () => {
    const root = await open('/messages/sighting-1/thread-1');
    const maximize = root.querySelector<HTMLButtonElement>('button.message-thread__expand')!;

    maximize.focus();
    maximize.click();
    await settle();

    expect(threadMessages).toHaveBeenCalledTimes(2);

    dialogElement()!.querySelector<HTMLButtonElement>('button.message-thread__expand')!.click();
    await vi.waitFor(() => expect(threadMessages).toHaveBeenCalledTimes(3));

    expect(threadMessages).toHaveBeenLastCalledWith('sighting-1', 'thread-1', 1);
    expect(TestBed.inject(Router).url).toBe('/messages/sighting-1/thread-1');
    await vi.waitFor(() => expect(document.activeElement).toBe(maximize));
  });

  it('takes no focus when it opens', async () => {
    const root = await open('/messages/sighting-1/thread-1');

    expect(root.contains(document.activeElement)).toBe(false);
  });
});

describe('messagesUrlMatcher', () => {
  function segments(...paths: string[]): UrlSegment[] {
    return paths.map((path) => new UrlSegment(path, {}));
  }

  it('matches the inbox and a thread with its ids', () => {
    expect(messagesUrlMatcher(segments('messages'))?.posParams).toBeUndefined();
    expect(messagesUrlMatcher(segments('messages'))?.consumed).toHaveLength(1);

    const thread = messagesUrlMatcher(segments('messages', 's-1', 't-1'));

    expect(thread?.posParams?.['sightingId'].path).toBe('s-1');
    expect(thread?.posParams?.['threadId'].path).toBe('t-1');
  });

  it('rejects other paths', () => {
    expect(messagesUrlMatcher(segments('messages', 's-1'))).toBeNull();
    expect(messagesUrlMatcher(segments('messages', 's-1', 't-1', 'x'))).toBeNull();
    expect(messagesUrlMatcher(segments('sightings'))).toBeNull();
    expect(messagesUrlMatcher([])).toBeNull();
  });
});
