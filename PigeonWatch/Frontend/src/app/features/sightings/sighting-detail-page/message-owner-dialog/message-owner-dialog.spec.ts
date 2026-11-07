import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatDialog, MatDialogRef } from '@angular/material/dialog';
import { NEVER, Observable, of, throwError } from 'rxjs';
import {
  ClientConfiguration,
  ConfigurationService,
} from '../../../../core/configuration/configuration.service';
import { ErrorReportingService } from '../../../../core/error-reporting/error-reporting.service';
import { SightingThreadStart } from '../../sighting.types';
import { SightingsApi } from '../../sightings-api';
import { MessageOwnerDialog, MessageOwnerDialogData } from './message-owner-dialog';

const started: SightingThreadStart = {
  thread: {
    id: 'thread-1',
    sightingId: 'sighting-1',
    sightingName: 'Limpy',
    counterpartDisplayName: 'Jan K',
    isOwner: false,
    lastMessageAt: '2026-11-07T12:00:00Z',
  },
  message: {
    id: 'message-1',
    text: 'Hello',
    senderDisplayName: 'Ola N',
    createdAt: '2026-11-07T12:00:00Z',
    isMine: true,
  },
};

describe('MessageOwnerDialog', () => {
  let startThread: ReturnType<
    typeof vi.fn<(sightingId: string, text: string) => Observable<SightingThreadStart>>
  >;
  let ref: MatDialogRef<MessageOwnerDialog, SightingThreadStart>;
  let closedWith: SightingThreadStart | undefined | null;

  beforeEach(() => {
    startThread = vi.fn(() => of(started));
    closedWith = null;
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: SightingsApi, useValue: { startThread } },
        { provide: ErrorReportingService, useValue: { report: vi.fn() } },
      ],
    });
  });

  afterEach(() => {
    TestBed.inject(MatDialog).closeAll();
    document.querySelectorAll('app-message-owner-dialog').forEach((dialog) => dialog.remove());
  });

  async function open(): Promise<HTMLElement> {
    ref = TestBed.inject(MatDialog).open<
      MessageOwnerDialog,
      MessageOwnerDialogData,
      SightingThreadStart
    >(MessageOwnerDialog, { data: { sightingId: 'sighting-1', ownerDisplayName: 'Jan K' } });
    ref.afterClosed().subscribe((result) => (closedWith = result));
    await settle();

    return document.querySelector<HTMLElement>('app-message-owner-dialog')!;
  }

  async function settle(): Promise<void> {
    const app = TestBed.inject(ApplicationRef);

    await app.whenStable();
    app.tick();
    await app.whenStable();
  }

  async function send(root: HTMLElement, text: string): Promise<void> {
    const field = root.querySelector<HTMLTextAreaElement>('textarea')!;

    field.value = text;
    field.dispatchEvent(new Event('input'));
    root.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    await settle();
  }

  function fieldError(root: HTMLElement): string {
    return root.querySelector('mat-form-field mat-error')?.textContent?.trim() ?? '';
  }

  function buttonIn(root: HTMLElement, text: string): HTMLButtonElement | null {
    return (
      [...root.querySelectorAll<HTMLButtonElement>('button')].find(
        (candidate) => candidate.textContent?.trim() === text,
      ) ?? null
    );
  }

  it('names the owner in the title and the privacy note and offers Cancel then Send', async () => {
    const root = await open();

    expect(root.querySelector('[mat-dialog-title]')?.textContent?.trim()).toBe('Message Jan K');
    expect(root.querySelector('p')?.textContent?.trim()).toBe(
      'Only Jan K will see your message. Your email address is never shared.',
    );
    expect(root.querySelector('mat-label')?.textContent?.trim()).toBe('Your message');
    expect(root.querySelector('mat-hint')?.textContent?.trim()).toBe('Up to 1000 characters.');
    expect(
      [...root.querySelectorAll('app-form-actions button')].map((button) =>
        button.textContent?.trim(),
      ),
    ).toEqual(['Cancel', 'Send message']);
    expect(fieldError(root)).toBe('');
  });

  it('requires a message on submit and sends nothing', async () => {
    const root = await open();

    await send(root, '');

    expect(fieldError(root)).toBe('Write a message first.');
    expect(startThread).not.toHaveBeenCalled();
    expect(closedWith).toBeNull();
  });

  it('treats a whitespace-only message as empty', async () => {
    const root = await open();

    await send(root, '   ');

    expect(fieldError(root)).toBe('Write a message first.');
    expect(startThread).not.toHaveBeenCalled();
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

    expect(fieldError(root)).toBe('Use at most 5 characters.');
    expect(startThread).not.toHaveBeenCalled();
  });

  it('sends the text to the sighting and closes with the started thread', async () => {
    const root = await open();

    await send(root, 'Is it still there?');
    await vi.waitFor(() => expect(closedWith).toBe(started));

    expect(startThread).toHaveBeenCalledExactlyOnceWith('sighting-1', 'Is it still there?');
  });

  it('shows the busy submit, disables Cancel and keeps the dialog open while sending', async () => {
    startThread.mockReturnValue(NEVER);

    const root = await open();

    await send(root, 'Hello');

    expect(root.querySelector('app-submit-button [aria-busy="true"]')).not.toBeNull();
    expect(buttonIn(root, 'Cancel')?.disabled).toBe(true);
    expect(ref.disableClose).toBe(true);

    await send(root, 'Hello');

    expect(startThread).toHaveBeenCalledOnce();
  });

  it('keeps the text and shows the error when sending fails, then allows another try', async () => {
    startThread.mockReturnValueOnce(
      throwError(() => new HttpErrorResponse({ status: 500 })),
    );

    const root = await open();

    await send(root, 'Hello');

    expect(root.querySelector('app-alert')?.textContent?.trim()).toBe(
      "We couldn't send your message. Try again.",
    );
    expect(root.querySelector('textarea')?.value).toBe('Hello');
    expect(ref.disableClose).toBe(false);
    expect(closedWith).toBeNull();

    root.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    await settle();
    await vi.waitFor(() => expect(closedWith).toBe(started));

    expect(startThread).toHaveBeenCalledTimes(2);
  });

  it('shows the sighting not-found text for a 404', async () => {
    startThread.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 404 })));

    const root = await open();

    await send(root, 'Hello');

    expect(root.querySelector('app-alert')?.textContent?.trim()).toBe(
      "This sighting doesn't exist or is no longer available.",
    );
  });

  it('shows the own-sighting text when the API refuses contact on an own sighting', async () => {
    startThread.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 400,
            error: { errors: { OwnSightingContact: ['Own sighting'] } },
          }),
      ),
    );

    const root = await open();

    await send(root, 'Hello');

    expect(root.querySelector('app-alert')?.textContent?.trim()).toBe(
      "You can't contact yourself about your own sighting.",
    );
  });

  it('closes without a result on Cancel', async () => {
    const root = await open();

    buttonIn(root, 'Cancel')?.click();
    await settle();
    await vi.waitFor(() => expect(closedWith).toBeUndefined());

    expect(startThread).not.toHaveBeenCalled();
  });
});
