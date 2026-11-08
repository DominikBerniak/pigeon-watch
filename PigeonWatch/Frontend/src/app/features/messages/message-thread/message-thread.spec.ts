import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';
import {
  ClientConfiguration,
  ConfigurationService,
} from '../../../core/configuration/configuration.service';
import { ErrorReportingService } from '../../../core/error-reporting/error-reporting.service';
import {
  SightingMessage,
  SightingMessagePage,
  SightingThread,
} from '../../sightings/sighting.types';
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

const summary: SightingThread = {
  id: 'thread-1',
  sightingId: 'sighting-1',
  sightingName: 'Limpy',
  counterpartDisplayName: 'Ola N',
  isOwner: true,
  lastMessageAt: new Date().toISOString(),
};

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
        provideRouter([]),
        { provide: SightingsApi, useValue: { threadMessages, sendMessage } },
        { provide: ErrorReportingService, useValue: { report: vi.fn() } },
      ],
    });
  });

  async function open(thread: SightingThread | null = summary): Promise<HTMLElement> {
    fixture = TestBed.createComponent(MessageThread);
    fixture.componentRef.setInput('sightingId', 'sighting-1');
    fixture.componentRef.setInput('threadId', 'thread-1');
    fixture.componentRef.setInput('thread', thread);
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

  it('loads the first page in chat order, oldest at the top, under a heading naming the counterpart', async () => {
    const root = await open();

    expect(threadMessages).toHaveBeenCalledExactlyOnceWith('sighting-1', 'thread-1', 1);
    expect(root.querySelector('h2')?.textContent?.trim()).toBe('Conversation with Ola N');
    expect(root.querySelector('h2')?.getAttribute('tabindex')).toBe('-1');
    expect(ids(root)).toEqual(['mine', 'theirs']);
    expect(items(root)[0].classList).toContain('message-thread__item--mine');
    expect(items(root)[1].classList).not.toContain('message-thread__item--mine');
  });

  it('links the sighting by name in the header', async () => {
    const root = await open();
    const link = root.querySelector<HTMLAnchorElement>('a.message-thread__sighting');

    expect(link?.textContent?.trim()).toBe('Sighting: Limpy');
    expect(link?.getAttribute('href')).toBe('/sightings/sighting-1');
  });

  it('names an unnamed sighting with the unnamed label', async () => {
    const root = await open({ ...summary, sightingName: null });

    expect(root.querySelector('a.message-thread__sighting')?.textContent?.trim()).toBe(
      'Sighting: Unnamed bird',
    );
  });

  it('falls back to generic labels when the thread summary is unknown', async () => {
    const root = await open(null);
    const link = root.querySelector<HTMLAnchorElement>('a.message-thread__sighting');

    expect(root.querySelector('h2')?.textContent?.trim()).toBe('Conversation');
    expect(link?.textContent?.trim()).toBe('Open the sighting');
    expect(link?.getAttribute('href')).toBe('/sightings/sighting-1');
    expect(root.querySelector('form mat-label')?.textContent?.trim()).toBe('Your reply');
  });

  it('puts a focusable labelled list above the reply box', async () => {
    const root = await open();
    const form = root.querySelector('form.message-thread__reply');
    const region = root.querySelector('.message-thread__list');

    expect(form?.querySelector('mat-label')?.textContent?.trim()).toBe('Reply to Ola N');
    expect(region!.compareDocumentPosition(form!) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(region?.getAttribute('role')).toBe('region');
    expect(region?.getAttribute('tabindex')).toBe('0');
    expect(region?.getAttribute('aria-label')).toBe('Messages, oldest first');
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

  it('loads older messages above the shown ones without duplicating a repeated item', async () => {
    threadMessages.mockImplementation((_sightingId, _threadId, page) =>
      page === 1
        ? of({ items: [theirs, mine], totalCount: 4 })
        : of({ items: [mine, messageOf('old-1'), messageOf('old-2')], totalCount: 4 }),
    );

    const root = await open();
    const showOlder = buttonIn(root, 'Show older messages');

    expect(root.querySelector('.message-thread__list')?.contains(showOlder)).toBe(true);
    expect(
      showOlder!.compareDocumentPosition(items(root)[0]) & Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy();

    showOlder?.click();
    await settle();

    expect(threadMessages).toHaveBeenLastCalledWith('sighting-1', 'thread-1', 2);
    expect(ids(root)).toEqual(['old-2', 'old-1', 'mine', 'theirs']);
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

    expect(ids(root)).toEqual(['mine', 'theirs']);
  });

  it('sends a reply, shows it last next to the reply box, clears the field and shows no error', async () => {
    const root = await open();

    await send(root, 'On my way');

    expect(sendMessage).toHaveBeenCalledExactlyOnceWith('sighting-1', 'thread-1', 'On my way');
    expect(ids(root)).toEqual(['mine', 'theirs', 'new']);
    expect(field(root).value).toBe('');
    expect(root.querySelector('form.message-thread__reply mat-error')).toBeNull();
  });

  it('sends on Enter and keeps the newline out of the field', async () => {
    const root = await open();

    field(root).value = 'On my way';
    field(root).dispatchEvent(new Event('input'));
    const enter = new KeyboardEvent('keydown', { key: 'Enter', cancelable: true });
    field(root).dispatchEvent(enter);
    await settle();

    expect(enter.defaultPrevented).toBe(true);
    expect(sendMessage).toHaveBeenCalledExactlyOnceWith('sighting-1', 'thread-1', 'On my way');
  });

  it('does not send on Shift+Enter or while composing text', async () => {
    const root = await open();

    field(root).value = 'First line';
    field(root).dispatchEvent(new Event('input'));
    const shiftEnter = new KeyboardEvent('keydown', {
      key: 'Enter',
      shiftKey: true,
      cancelable: true,
    });
    field(root).dispatchEvent(shiftEnter);
    field(root).dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Enter', isComposing: true, cancelable: true }),
    );
    await settle();

    expect(shiftEnter.defaultPrevented).toBe(false);
    expect(sendMessage).not.toHaveBeenCalled();
  });

  it('does not duplicate a sent message the list already holds', async () => {
    sendMessage.mockReturnValue(of({ ...mine, text: 'Again' }));

    const root = await open();

    await send(root, 'Again');

    expect(ids(root)).toEqual(['theirs', 'mine']);
  });

  it('reloads after a successful send when the first page had failed', async () => {
    threadMessages.mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 500 })));

    const root = await open();

    expect(root.querySelector('app-error-retry')).not.toBeNull();

    threadMessages.mockReturnValue(of({ items: [messageOf('new'), theirs], totalCount: 2 }));
    await send(root, 'Hello');

    expect(threadMessages).toHaveBeenCalledTimes(2);
    expect(ids(root)).toEqual(['theirs', 'new']);
  });

  it('disables the send button while the field is blank and enables it once text is typed', async () => {
    const root = await open();
    const button = root.querySelector<HTMLButtonElement>('.message-thread__send')!;

    expect(button.getAttribute('aria-disabled')).toBe('true');

    button.dispatchEvent(new MouseEvent('mouseenter'));

    expect(document.body.querySelector('.pw-tooltip')).toBeNull();

    button.dispatchEvent(new MouseEvent('mouseleave'));

    field(root).value = '   ';
    field(root).dispatchEvent(new Event('input'));
    await settle();

    expect(button.getAttribute('aria-disabled')).toBe('true');

    field(root).value = 'Hi';
    field(root).dispatchEvent(new Event('input'));
    await settle();

    expect(button.getAttribute('aria-disabled')).not.toBe('true');

    button.dispatchEvent(new MouseEvent('mouseenter'));

    expect(document.body.querySelector('.pw-tooltip')?.textContent).toBe('Send');

    button.dispatchEvent(new MouseEvent('mouseleave'));
  });

  it('does nothing for empty or whitespace-only text and shows no error or required marker', async () => {
    const root = await open();

    await send(root, '');
    await send(root, '   ');

    expect(sendMessage).not.toHaveBeenCalled();
    expect(root.querySelector('form.message-thread__reply mat-error')).toBeNull();
    expect(field(root).getAttribute('aria-invalid')).not.toBe('true');
    expect(
      root.querySelector('form.message-thread__reply .mat-mdc-form-field-required-marker'),
    ).toBeNull();
    expect(field(root).required).toBe(false);
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

    const hint = root.querySelector('.message-thread__hint')!;

    expect(hint.textContent?.trim()).toBe('Up to 5 characters.');
    expect(field(root).getAttribute('aria-describedby')).toContain(hint.id);

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

  function expandButton(root: HTMLElement): HTMLButtonElement {
    return root.querySelector<HTMLButtonElement>('header button.message-thread__expand')!;
  }

  it('offers full screen beside the heading with the maximize glyph and emits on click', async () => {
    const root = await open();
    const button = expandButton(root);
    const emitted = vi.fn();

    fixture.componentInstance.expandToggle.subscribe(emitted);

    expect(button.getAttribute('aria-label')).toBe('Open in full screen');
    expect(button.querySelector('.message-thread__maximize-glyph')).not.toBeNull();
    expect(button.querySelector('.message-thread__close-glyph')).toBeNull();
    expect(button.querySelector('svg')?.getAttribute('aria-hidden')).toBe('true');
    expect(button.querySelector('svg')?.getAttribute('focusable')).toBe('false');
    expect(
      root.querySelector('h2')!.compareDocumentPosition(button) & Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy();

    button.click();

    expect(emitted).toHaveBeenCalledOnce();
  });

  it('offers Close with the close glyph while expanded', async () => {
    fixture = TestBed.createComponent(MessageThread);
    fixture.componentRef.setInput('sightingId', 'sighting-1');
    fixture.componentRef.setInput('threadId', 'thread-1');
    fixture.componentRef.setInput('expanded', true);
    await settle();

    const root = fixture.nativeElement as HTMLElement;
    const button = expandButton(root);
    const emitted = vi.fn();

    fixture.componentInstance.expandToggle.subscribe(emitted);

    expect(button.getAttribute('aria-label')).toBe('Close');
    expect(button.querySelector('.message-thread__close-glyph')).not.toBeNull();
    expect(button.querySelector('.message-thread__maximize-glyph')).toBeNull();

    button.click();

    expect(emitted).toHaveBeenCalledOnce();
  });

  it('labels the section by its own heading id', async () => {
    const root = await open();
    const heading = root.querySelector('h2')!;

    expect(heading.id).not.toBe('');
    expect(root.querySelector('section')?.getAttribute('aria-labelledby')).toBe(heading.id);
  });

  it('takes no focus on its own and moves focus to the heading on request', async () => {
    const root = await open();

    expect(root.contains(document.activeElement)).toBe(false);

    fixture.componentInstance.focusHeading();

    expect(document.activeElement).toBe(root.querySelector('h2'));
  });
});
