import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { Observable, throwError } from 'rxjs';
import { ErrorReportingService } from '../../../../core/error-reporting/error-reporting.service';
import { SightingThreadStart } from '../../sighting.types';
import { SightingsApi } from '../../sightings-api';
import { MessageOwnerDialog, MessageOwnerDialogData } from './message-owner-dialog';

const traceId = '00-abc123-def456-01';
const typed = 'Call me on 555 123 456';

describe('MessageOwnerDialog failure reporting', () => {
  let report: ReturnType<typeof vi.fn>;
  let startThread: ReturnType<typeof vi.fn<() => Observable<SightingThreadStart>>>;

  beforeEach(() => {
    report = vi.fn();
    startThread = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: SightingsApi, useValue: { startThread } },
        { provide: ErrorReportingService, useValue: { report } },
      ],
    });
  });

  afterEach(() => {
    TestBed.inject(MatDialog).closeAll();
    document.querySelectorAll('app-message-owner-dialog').forEach((dialog) => dialog.remove());
  });

  async function settle(): Promise<void> {
    const app = TestBed.inject(ApplicationRef);

    await app.whenStable();
    app.tick();
    await app.whenStable();
  }

  async function sendFailing(error: HttpErrorResponse): Promise<HTMLElement> {
    startThread.mockReturnValue(throwError(() => error));
    TestBed.inject(MatDialog).open<MessageOwnerDialog, MessageOwnerDialogData>(
      MessageOwnerDialog,
      { data: { sightingId: 'sighting-1', ownerDisplayName: 'Jan K' } },
    );
    await settle();

    const root = document.querySelector<HTMLElement>('app-message-owner-dialog')!;
    const field = root.querySelector<HTMLTextAreaElement>('textarea')!;

    field.value = typed;
    field.dispatchEvent(new Event('input'));
    root.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    await settle();

    return root;
  }

  function reportedArguments(): string {
    return JSON.stringify(report.mock.calls.map(([kind, context]) => [kind, context]));
  }

  it('reports a 500 under sighting.threads.start and shows the reference', async () => {
    const error = new HttpErrorResponse({ status: 500, error: { traceId } });

    const root = await sendFailing(error);

    expect(root.querySelector('app-alert')?.textContent).toContain(`Reference: ${traceId}`);
    expect(report).toHaveBeenCalledExactlyOnceWith('api-failure', 'sighting.threads.start', error);
    expect(reportedArguments()).not.toContain(typed);
    expect(reportedArguments()).not.toContain('sighting-1');
  });

  it('reports an own-sighting refusal as a client bug and shows the reference', async () => {
    const error = new HttpErrorResponse({
      status: 400,
      error: { traceId, errors: { OwnSightingContact: ['Own sighting'] } },
    });

    const root = await sendFailing(error);

    expect(root.querySelector('app-alert')?.textContent).toContain(`Reference: ${traceId}`);
    expect(report).toHaveBeenCalledExactlyOnceWith('api-failure', 'sighting.threads.start', error);
  });

  it('does not report a 404', async () => {
    const root = await sendFailing(new HttpErrorResponse({ status: 404, error: { traceId } }));

    expect(root.textContent).not.toContain('Reference');
    expect(report).not.toHaveBeenCalled();
  });

  it('does not report a text-required 400', async () => {
    const root = await sendFailing(
      new HttpErrorResponse({
        status: 400,
        error: { traceId, errors: { MessageTextRequired: ['Required'] } },
      }),
    );

    expect(root.querySelector('app-alert')?.textContent?.trim()).toBe('Write a message first.');
    expect(root.textContent).not.toContain('Reference');
    expect(report).not.toHaveBeenCalled();
  });

  it.each([400, 401, 429])('does not report an expected %i', async (status) => {
    const root = await sendFailing(new HttpErrorResponse({ status, error: { traceId } }));

    expect(root.textContent).not.toContain('Reference');
    expect(report).not.toHaveBeenCalled();
  });
});
