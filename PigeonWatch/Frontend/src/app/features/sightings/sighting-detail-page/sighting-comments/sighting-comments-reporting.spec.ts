import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Observable, of, throwError } from 'rxjs';
import { ErrorReportingService } from '../../../../core/error-reporting/error-reporting.service';
import { SightingComment, SightingCommentPage } from '../../sighting.types';
import { SightingsApi } from '../../sightings-api';
import { SightingComments } from './sighting-comments';

const traceId = '00-abc123-def456-01';

const mine: SightingComment = {
  id: 'mine',
  text: 'My note',
  authorDisplayName: 'Jan K',
  createdAt: '2026-11-07T12:00:00Z',
  editedAt: null,
  isDeleted: false,
  isMine: true,
};

describe('SightingComments failure reporting', () => {
  let report: ReturnType<typeof vi.fn>;
  let comments: ReturnType<typeof vi.fn<() => Observable<SightingCommentPage>>>;
  let addComment: ReturnType<typeof vi.fn<() => Observable<SightingComment>>>;
  let editComment: ReturnType<typeof vi.fn<() => Observable<SightingComment>>>;
  let fixture: ComponentFixture<SightingComments>;

  beforeEach(() => {
    report = vi.fn();
    comments = vi.fn(() => of({ items: [mine], totalCount: 1 }));
    addComment = vi.fn();
    editComment = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: SightingsApi, useValue: { comments, addComment, editComment } },
        { provide: ErrorReportingService, useValue: { report } },
      ],
    });
  });

  async function open(): Promise<HTMLElement> {
    fixture = TestBed.createComponent(SightingComments);
    fixture.componentRef.setInput('sightingId', 'sighting-1');
    await settle();

    return fixture.nativeElement as HTMLElement;
  }

  async function settle(): Promise<void> {
    await fixture.whenStable();
    fixture.detectChanges();
    await fixture.whenStable();
  }

  async function post(root: HTMLElement): Promise<void> {
    const field = root.querySelector<HTMLTextAreaElement>('form.sighting-comments__add textarea')!;

    field.value = 'Hello';
    field.dispatchEvent(new Event('input'));
    root
      .querySelector('form.sighting-comments__add')!
      .dispatchEvent(new Event('submit', { cancelable: true }));
    await settle();
  }

  async function edit(root: HTMLElement): Promise<void> {
    const button = [...root.querySelectorAll<HTMLButtonElement>('li button')].find(
      (candidate) => candidate.textContent?.trim() === 'Edit',
    );

    button?.click();
    await settle();

    const field = root.querySelector<HTMLTextAreaElement>('li textarea')!;

    field.value = 'Changed';
    field.dispatchEvent(new Event('input'));
    root.querySelector('li form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    await settle();
  }

  it('reports a 500 on load with its context and shows the reference', async () => {
    const error = new HttpErrorResponse({ status: 500, error: { traceId } });

    comments.mockReturnValue(throwError(() => error));

    const root = await open();

    expect(root.querySelector('app-error-retry')?.textContent).toContain(`Reference: ${traceId}`);
    expect(report).toHaveBeenCalledExactlyOnceWith('api-failure', 'sighting.comments.load', error);
  });

  it('reports a 500 on post with its context and shows the reference', async () => {
    const error = new HttpErrorResponse({ status: 500, error: { traceId } });

    addComment.mockReturnValue(throwError(() => error));

    const root = await open();

    await post(root);

    expect(root.querySelector('form.sighting-comments__add app-alert')?.textContent).toContain(
      `Reference: ${traceId}`,
    );
    expect(report).toHaveBeenCalledExactlyOnceWith('api-failure', 'sighting.comments.add', error);
  });

  it('reports a 403 on edit as a client bug with the forbidden message and reference', async () => {
    const error = new HttpErrorResponse({ status: 403, error: { traceId } });

    editComment.mockReturnValue(throwError(() => error));

    const root = await open();

    await edit(root);

    const alert = root.querySelector('li app-alert');

    expect(alert?.textContent).toContain('You can only change your own comments.');
    expect(alert?.textContent).toContain(`Reference: ${traceId}`);
    expect(report).toHaveBeenCalledExactlyOnceWith('api-failure', 'sighting.comments.edit', error);
  });

  it('does not report a cooldown 400 on post', async () => {
    addComment.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 400,
            error: { traceId, errors: { CooldownActive: ['Wait'] }, retryAfterSeconds: 30 },
          }),
      ),
    );

    const root = await open();

    await post(root);

    expect(root.textContent).not.toContain('Reference');
    expect(report).not.toHaveBeenCalled();
  });

  it('does not report a 404 on edit', async () => {
    editComment.mockReturnValue(
      throwError(() => new HttpErrorResponse({ status: 404, error: { traceId } })),
    );

    const root = await open();

    await edit(root);

    expect(root.textContent).not.toContain('Reference');
    expect(report).not.toHaveBeenCalled();
  });

  it('does not report a 404 on load', async () => {
    comments.mockReturnValue(
      throwError(() => new HttpErrorResponse({ status: 404, error: { traceId } })),
    );

    const root = await open();

    expect(root.querySelector('app-error-retry')).not.toBeNull();
    expect(report).not.toHaveBeenCalled();
  });
});
