import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Observable, of, throwError } from 'rxjs';
import { SessionService } from '../../../core/auth/session.service';
import {
  ClientConfiguration,
  ConfigurationService,
  CurrentUser,
  GeneralConfiguration,
} from '../../../core/configuration/configuration.service';
import { ErrorReportingService } from '../../../core/error-reporting/error-reporting.service';
import { SightingsApi } from '../../sightings/sightings-api';
import { ProfileApi, UpdatedProfile } from '../profile-api';
import { ProfilePage } from './profile-page';

const user: CurrentUser = { id: '1', email: 'jan@example.com', displayName: 'Jan K', roles: [] };
const traceId = '00-abc123-def456-01';

describe('ProfilePage failure reporting', () => {
  let updateDisplayName: ReturnType<
    typeof vi.fn<(displayName: string) => Observable<UpdatedProfile>>
  >;
  let loadGeneral: ReturnType<typeof vi.fn<() => Observable<GeneralConfiguration>>>;
  let changePassword: ReturnType<
    typeof vi.fn<(currentPassword: string, newPassword: string) => Observable<void>>
  >;
  let mine: ReturnType<typeof vi.fn<() => Observable<{ items: never[] }>>>;
  let report: ReturnType<typeof vi.fn>;
  let harness: RouterTestingHarness;
  let root: HTMLElement;

  beforeEach(async () => {
    report = vi.fn();
    changePassword = vi.fn(() => of(undefined));
    updateDisplayName = vi.fn((displayName: string) => of({ email: user.email, displayName }));
    loadGeneral = vi.fn(() => of({ currentUser: user }));
    mine = vi.fn(() => of({ items: [] }));
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([{ path: 'profile', component: ProfilePage }]),
        {
          provide: SessionService,
          useValue: { currentUser: signal(user).asReadonly(), changePassword },
        },
        {
          provide: ConfigurationService,
          useValue: { clientConfig: signal<ClientConfiguration | null>(null), loadGeneral },
        },
        { provide: ProfileApi, useValue: { updateDisplayName } },
        { provide: SightingsApi, useValue: { mine } },
        { provide: ErrorReportingService, useValue: { report } },
      ],
    });
    harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/profile');
    await harness.fixture.whenStable();
    root = harness.routeNativeElement as HTMLElement;
  });

  afterEach(() => {
    document.querySelectorAll('.cdk-overlay-container').forEach((overlay) => {
      overlay.innerHTML = '';
    });
  });

  async function settle(): Promise<void> {
    await harness.fixture.whenStable();
    harness.fixture.detectChanges();
    await harness.fixture.whenStable();
  }

  function buttonByText(text: string): HTMLButtonElement {
    const match = [...document.querySelectorAll<HTMLButtonElement>('button[type="button"]')].find(
      (button) => button.textContent?.trim() === text,
    );

    if (!match) throw new Error(`No button "${text}"`);

    return match;
  }

  function profileCard(): HTMLElement {
    const card = root.querySelector<HTMLElement>('app-page-card');

    if (!card) throw new Error('No profile card');

    return card;
  }

  async function submitProfile(): Promise<void> {
    buttonByText('Edit').click();
    await settle();

    const input = profileCard().querySelector<HTMLInputElement>('mat-form-field input');

    if (!input) throw new Error('No display name input');

    input.value = 'Jan Kowalski';
    input.dispatchEvent(new Event('input'));
    input.dispatchEvent(new Event('blur'));
    profileCard().querySelector('form')?.dispatchEvent(new Event('submit', { cancelable: true }));
    await settle();
  }

  function profileAlert(): string | undefined {
    return profileCard().querySelector('app-alert[role="alert"]')?.textContent?.trim();
  }

  function failProfile(status: number, error: unknown = null): HttpErrorResponse {
    const failure = new HttpErrorResponse({ status, error });

    updateDisplayName.mockReturnValue(throwError(() => failure));

    return failure;
  }

  it('shows the reference and reports api-failure for a profile 500 with a trace id', async () => {
    const failure = failProfile(500, { traceId });

    await submitProfile();

    expect(profileAlert()).toContain(`Reference: ${traceId}`);
    expect(report).toHaveBeenCalledExactlyOnceWith('api-failure', 'profile.update', failure);
  });

  it('reports a profile 500 without a trace id and shows no reference', async () => {
    const failure = failProfile(500);

    await submitProfile();

    expect(profileAlert()).not.toContain('Reference');
    expect(report).toHaveBeenCalledExactlyOnceWith('api-failure', 'profile.update', failure);
  });

  it('does not report a validation 400 or show a reference', async () => {
    failProfile(400, { status: 400, traceId, errors: { DisplayNameTooShort: ['x'] } });

    await submitProfile();

    expect(root.textContent).not.toContain('Reference');
    expect(report).not.toHaveBeenCalled();
  });

  it('does not report a 429 or show a reference', async () => {
    failProfile(429, { traceId });

    await submitProfile();

    expect(root.textContent).not.toContain('Reference');
    expect(report).not.toHaveBeenCalled();
  });

  it('reports a failed reload after a successful save and still shows the saved notice', async () => {
    const failure = new HttpErrorResponse({ status: 500, error: { traceId } });

    loadGeneral.mockReturnValue(throwError(() => failure));

    await submitProfile();

    expect(profileCard().querySelector('app-alert[role="status"]')?.textContent?.trim()).toBe(
      'Your name has been updated.',
    );
    expect(report).toHaveBeenCalledExactlyOnceWith('fallback', 'profile.reload.failed', failure);
  });

  describe('password dialog', () => {
    async function submitPassword(): Promise<void> {
      buttonByText('Change password').click();
      await settle();

      const dialog = document.querySelector<HTMLElement>('app-password-dialog');

      if (!dialog) throw new Error('No password dialog');

      dialog.querySelectorAll<HTMLInputElement>('input[type="password"]').forEach((input) => {
        input.value = 'Old1!passw';
        input.dispatchEvent(new Event('input'));
        input.dispatchEvent(new Event('blur'));
      });
      dialog.querySelector('form')?.dispatchEvent(new Event('submit', { cancelable: true }));
      await settle();
    }

    function dialogText(): string {
      return document.querySelector('app-password-dialog')?.textContent ?? '';
    }

    it('shows the reference and reports api-failure for a 500 with a trace id', async () => {
      const failure = new HttpErrorResponse({ status: 500, error: { traceId } });

      changePassword.mockReturnValue(throwError(() => failure));

      await submitPassword();

      expect(dialogText()).toContain(`Reference: ${traceId}`);
      expect(report).toHaveBeenCalledExactlyOnceWith('api-failure', 'profile.password', failure);
    });

    it('does not report a wrong current password or show a reference', async () => {
      changePassword.mockReturnValue(
        throwError(
          () =>
            new HttpErrorResponse({
              status: 400,
              error: { status: 400, traceId, errors: { PasswordMismatch: ['x'] } },
            }),
        ),
      );

      await submitPassword();

      expect(dialogText()).not.toContain('Reference');
      expect(report).not.toHaveBeenCalled();
    });
  });
});
