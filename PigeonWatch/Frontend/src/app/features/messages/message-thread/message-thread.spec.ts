import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Observable, of, throwError } from 'rxjs';
import {
  ClientConfiguration,
  ConfigurationService,
} from '../../../core/configuration/configuration.service';
import { ErrorReportingService } from '../../../core/error-reporting/error-reporting.service';
import { SightingMessage, SightingMessagePage } from '../../sightings/sighting.types';
import { SightingsApi } from '../../sightings/sightings-api';
import { MessageThread } from './message-thread';

const minute = 60_000;

function messageOf(id: string, overrides: Partial<SightingMessage> = {}): SightingMessage {
  return {
    id,
    text: `Message ${id}`,
    senderDisplayName: 'Ola N',
    createdAt: new Date(Date.now() - 5 * minute).toISOString(),
    isMine: false,
    ...overrides,
  };
}

const theirs = messageOf('theirs');
const mine = messageOf('mine', { senderDisplayName: 'Jan K', isMine: true });

describe('MessageThread', () => {
  let threadMessages: ReturnType<
    typeof vi.fn<
      (sightingId: string, threadId: string, page: number) => Observable<SightingMessagePage>
    >
  >;
  let sendMessage: ReturnType<
    typeof vi.fn<
      (sightingId: string, threadId: string, text: string) => Observable<SightingMessage>
    >
  >;
  let fixture: ComponentFixture<MessageThread>;

  beforeEach(() => {
    threadMessages = vi.fn(() => of({ items: [theirs, mine], totalCount: 2 }));
    sendMessage = vi.fn((_sightingId: string, _threadId: string, text: string) =>
      of(messageOf('new', { text, senderDisplayName: 'Jan K', isMine: true })),
    );
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: SightingsApi, useValue: { threadMessages, sendMessage } },
        { provide: ErrorReportingService, useValue: { report: vi.fn() } },
      ],
    });
  });

  async function open(): Promise<HTMLElement> {
    fixture = TestBed.createComponent(MessageThread);
    fixture.componentRef.setInput('sightingId', 'sighting-1');
    fixture.componentRef.setInput('threadId', 'thread-1');
    fixture.componentRef.setInput('counterpartDisplayName', 'Ola N');
    await settle();

    return fixture.nativeElement as HTMLElement;
  }

  async function settle(): Promise<void> {
    await fixture.whenStable();
    fixture.detectChanges();
    await fixture.whenStable();
  }

  function items(root: HTMLElement): HTMLElement[] {
    return [...root.querySelectorAll<HTMLElement>('li.message-thread__item')];
  }

  function ids(root: HTMLElement): (string | undefined)[] {
    return items(root).map((element) => element.dataset['messageId']);
  }

  function buttonIn(root: Element, text: string): HTMLButtonElement | null {
    return (
      [...root.querySelectorAll<HTMLButtonElement>('button')].find(
        (candidate) => candidate.textContent?.trim() === text,
      ) ?? null
    );
  }

  function field(root: HTMLElement): HTMLTextAreaElement {
    return root.querySelector<HTMLTextAreaElement>('form.message-thread__reply textarea')!;
  }

  async function send(root: HTMLElement, text: string): Promise<void> {
    field(root).value = text;
    field(root).dispatchEvent(new Event('input'));
    root
      .querySelector('form.message-thread__reply')!
      .dispatchEvent(new Event('submit', { cancelable: true }));
    await settle();
  }

  it('loads the first page newest first under a heading naming the counterpart', async () => {
    const root = await open();

    expect(threadMessages).toHaveBeenCalledExactlyOnceWith('sighting-1', 'thread-1', 1);
    expect(root.querySelector('h3')?.textContent?.trim()).toBe('Conversation with Ola N');
    expect(root.querySelector('h3')?.getAttribute('tabindex')).toBe('-1');
    expect(ids(root)).toEqual(['theirs', 'mine']);
    expect(items(root)[1].classList).toContain('message-thread__item--mine');
    expect(items(root)[0].classList).not.toContain('message-thread__item--mine');
  });

  it('puts the reply box above a focusable labelled list', async () => {
    const root = await open();
    const form = root.querySelector('form.message-thread__reply');
    const region = root.querySelector('.message-thread__list');

    expect(form?.querySelector('mat-label')?.textContent?.trim()).toBe('Reply to Ola N');
    expect(form!.compareDocumentPosition(region!) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(region?.getAttribute('role')).toBe('region');
    expect(region?.getAttribute('tabindex')).toBe('0');
    expect(region?.getAttribute('aria-label')).toBe('Messages, newest first');
  });

  it('shows the sender, a relative time with the absolute date as title, and the text as plain text', async () => {
    threadMessages.mockReturnValue(
      of({ items: [{ ...theirs, text: '<b>bold</b>\nnext' }], totalCount: 1 }),
    );

    const root = await open();
    const item = items(root)[0];
    const time = item.querySelector('time');

    expect(item.querySelector('.message-thread__sender')?.textContent?.trim()).toBe('Ola N');
    expect(time?.getAttribute('datetime')).toBe(theirs.createdAt);
    expect(time?.textContent?.trim()).toBe('5 min ago');
    expect(
      document.getElementById(time?.getAttribute('aria-describedby') ?? '')?.textContent,
    ).toMatch(/\d{4}/);
    expect(item.querySelector('.message-thread__text')?.textContent).toBe('<b>bold</b>\nnext');
    expect(item.querySelector('b')).toBeNull();
  });

  it('loads older messages inside the list and appends without duplicating a repeated item', async () => {
    threadMessages.mockImplementation((_sightingId, _threadId, page) =>
      page === 1
        ? of({ items: [theirs, mine], totalCount: 4 })
        : of({ items: [mine, messageOf('old-1'), messageOf('old-2')], totalCount: 4 }),
    );

    const root = await open();
    const showOlder = buttonIn(root, 'Show older messages');

    expect(root.querySelector('.message-thread__list')?.contains(showOlder)).toBe(true);

    showOlder?.click();
    await settle();

    expect(threadMessages).toHaveBeenLastCalledWith('sighting-1', 'thread-1', 2);
    expect(ids(root)).toEqual(['theirs', 'mine', 'old-1', 'old-2']);
    expect(buttonIn(root, 'Show older messages')).toBeNull();
  });

  it('shows an inline retry when older messages fail', async () => {
    let older: Observable<SightingMessagePage> = throwError(
      () => new HttpErrorResponse({ status: 500 }),
    );

    threadMessages.mockImplementation((_sightingId, _threadId, page) =>
      page === 1 ? of({ items: [theirs], totalCount: 2 }) : older,
    );

    const root = await open();

    buttonIn(root, 'Show older messages')?.click();
    await settle();

    expect(root.querySelector('.message-thread__older app-alert')?.textContent?.trim()).toBe(
      "We couldn't load older messages.",
    );

    older = of({ items: [mine], totalCount: 2 });
    buttonIn(root.querySelector('.message-thread__older')!, 'Try again')?.click();
    await settle();

    expect(ids(root)).toEqual(['theirs', 'mine']);
  });

  it('sends a reply, prepends it, clears the field and shows no error', async () => {
    const root = await open();

    await send(root, 'On my way');

    expect(sendMessage).toHaveBeenCalledExactlyOnceWith('sighting-1', 'thread-1', 'On my way');
    expect(ids(root)).toEqual(['new', 'theirs', 'mine']);
    expect(field(root).value).toBe('');
    expect(root.querySelector('form.message-thread__reply mat-error')).toBeNull();
  });

  it('does not duplicate a sent message the list already holds', async () => {
    sendMessage.mockReturnValue(of({ ...mine, text: 'Again' }));

    const root = await open();

    await send(root, 'Again');

    expect(ids(root)).toEqual(['mine', 'theirs']);
  });

  it('reloads after a successful send when the first page had failed', async () => {
    threadMessages.mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 500 })));

    const root = await open();

    expect(root.querySelector('app-error-retry')).not.toBeNull();

    threadMessages.mockReturnValue(of({ items: [messageOf('new'), theirs], totalCount: 2 }));
    await send(root, 'Hello');

    expect(threadMessages).toHaveBeenCalledTimes(2);
    expect(ids(root)).toEqual(['new', 'theirs']);
  });

  it('shows the required error for whitespace-only text and makes no call', async () => {
    const root = await open();

    await send(root, '   ');

    expect(sendMessage).not.toHaveBeenCalled();
    expect(root.querySelector('form.message-thread__reply mat-error')?.textContent?.trim()).toBe(
      'Write a message first.',
    );
  });

  it('limits the text to the configured message length', async () => {
    TestBed.overrideProvider(ConfigurationService, {
      useValue: {
        clientConfig: signal({
          messageRules: { textMaxLength: 5, pageSize: 20 },
        } as ClientConfiguration),
      },
    });

    const root = await open();

    expect(root.querySelector('mat-hint')?.textContent?.trim()).toBe('Up to 5 characters.');

    await send(root, 'Too long');

    expect(sendMessage).not.toHaveBeenCalled();
    expect(root.querySelector('form.message-thread__reply mat-error')?.textContent?.trim()).toBe(
      'Use at most 5 characters.',
    );
  });

  it('keeps the text and explains when the conversation is gone', async () => {
    sendMessage.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 404 })));

    const root = await open();

    await send(root, 'Hello?');

    expect(root.querySelector('form.message-thread__reply app-alert')?.textContent?.trim()).toBe(
      'This conversation is no longer available. Reload the page.',
    );
    expect(field(root).value).toBe('Hello?');
    expect(items(root)).toHaveLength(2);
  });

  it('shows a load error with a retry that loads again', async () => {
    threadMessages.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));

    const root = await open();

    expect(root.querySelector('app-error-retry app-alert')?.textContent?.trim()).toBe(
      "We couldn't reach the messages right now. Try again.",
    );

    threadMessages.mockReturnValue(of({ items: [theirs], totalCount: 1 }));
    buttonIn(root, 'Try again')?.click();
    await settle();

    expect(threadMessages).toHaveBeenCalledTimes(2);
    expect(root.querySelector('app-error-retry')).toBeNull();
    expect(items(root)).toHaveLength(1);
  });

  it('starts over with page 1 when the thread changes', async () => {
    await open();

    fixture.componentRef.setInput('threadId', 'thread-2');
    await settle();

    expect(threadMessages).toHaveBeenLastCalledWith('sighting-1', 'thread-2', 1);
  });

  it('takes no focus on its own and moves focus to the heading on request', async () => {
    const root = await open();

    expect(root.contains(document.activeElement)).toBe(false);

    fixture.componentInstance.focusHeading();

    expect(document.activeElement).toBe(root.querySelector('h3'));
  });
});
