import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef, Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { Router, provideRouter } from '@angular/router';
import { Observable, of } from 'rxjs';
import { ErrorReportingService } from '../../../core/error-reporting/error-reporting.service';
import {
  SightingMessage,
  SightingMessagePage,
  SightingThread,
} from '../../sightings/sighting.types';
import { SightingsApi } from '../../sightings/sightings-api';
import { MessageThreadDialog, MessageThreadDialogData } from './message-thread-dialog';

@Component({ template: '' })
class SightingRouteStub {}

const thread: SightingThread = {
  id: 'thread-1',
  sightingId: 'sighting-1',
  sightingName: 'Limpy',
  counterpartDisplayName: 'Ola N',
  isOwner: true,
  lastMessageAt: new Date().toISOString(),
};

const message: SightingMessage = {
  id: 'message-1',
  text: 'Is it still there?',
  senderDisplayName: 'Ola N',
  createdAt: new Date().toISOString(),
  isMine: false,
};

describe('MessageThreadDialog', () => {
  let threadMessages: ReturnType<
    typeof vi.fn<
      (sightingId: string, threadId: string, page: number) => Observable<SightingMessagePage>
    >
  >;
  let closed: boolean;

  beforeEach(() => {
    threadMessages = vi.fn(() => of({ items: [message], totalCount: 1 }));
    closed = false;
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([{ path: 'sightings/:id', component: SightingRouteStub }]),
        {
          provide: SightingsApi,
          useValue: { threadMessages, sendMessage: (): Observable<SightingMessage> => of(message) },
        },
        { provide: ErrorReportingService, useValue: { report: vi.fn() } },
      ],
    });
  });

  afterEach(() => {
    TestBed.inject(MatDialog).closeAll();
    document.querySelectorAll('app-message-thread-dialog').forEach((dialog) => dialog.remove());
  });

  async function open(): Promise<HTMLElement> {
    TestBed.inject(MatDialog)
      .open<MessageThreadDialog, MessageThreadDialogData>(MessageThreadDialog, {
        data: { sightingId: 'sighting-1', threadId: 'thread-1', thread },
      })
      .afterClosed()
      .subscribe(() => (closed = true));
    await settle();

    return document.querySelector<HTMLElement>('app-message-thread-dialog')!;
  }

  async function settle(): Promise<void> {
    const app = TestBed.inject(ApplicationRef);

    await app.whenStable();
    app.tick();
    await app.whenStable();
  }

  it('renders the chosen conversation with its summary', async () => {
    const root = await open();

    expect(threadMessages).toHaveBeenCalledExactlyOnceWith('sighting-1', 'thread-1', 1);
    expect(root.querySelector('app-message-thread h2')?.textContent?.trim()).toBe(
      'Conversation with Ola N',
    );
    expect(root.querySelectorAll('li.message-thread__item')).toHaveLength(1);
    expect(root.querySelector('form.message-thread__reply textarea')).not.toBeNull();
  });

  it('closes through the Close button in the header', async () => {
    const root = await open();
    const button = root.querySelector<HTMLButtonElement>('button.message-thread__expand')!;

    expect(button.getAttribute('aria-label')).toBe('Close');

    button.click();
    await vi.waitFor(() => expect(closed).toBe(true));
  });

  it('closes when a link inside it navigates away', async () => {
    await open();

    await TestBed.inject(Router).navigateByUrl('/sightings/sighting-1');
    await vi.waitFor(() => expect(closed).toBe(true));
  });
});
