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
import { ProfileApi, UpdatedProfile } from '../profile-api';
import { ProfilePage } from './profile-page';

const user: CurrentUser = { id: '1', email: 'jan@example.com', displayName: 'Jan K', roles: [] };

describe('ProfilePage', () => {
  let updateDisplayName: ReturnType<
    typeof vi.fn<(displayName: string) => Observable<UpdatedProfile>>
  >;
  let loadGeneral: ReturnType<typeof vi.fn<() => Observable<GeneralConfiguration>>>;
  let harness: RouterTestingHarness;

  beforeEach(async () => {
    updateDisplayName = vi.fn<(displayName: string) => Observable<UpdatedProfile>>((displayName) =>
      of({ email: user.email, displayName }),
    );
    loadGeneral = vi.fn<() => Observable<GeneralConfiguration>>(() => of({ currentUser: user }));
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([{ path: 'profile', component: ProfilePage }]),
        { provide: SessionService, useValue: { currentUser: signal(user).asReadonly() } },
        {
          provide: ConfigurationService,
          useValue: { clientConfig: signal<ClientConfiguration | null>(null), loadGeneral },
        },
        { provide: ProfileApi, useValue: { updateDisplayName } },
      ],
    });
    harness = await RouterTestingHarness.create();
  });

  async function openReadOnly(): Promise<HTMLElement> {
    await harness.navigateByUrl('/profile');
    await harness.fixture.whenStable();

    return harness.routeNativeElement as HTMLElement;
  }

  function buttonByText(root: HTMLElement, text: string): HTMLButtonElement | null {
    return (
      [...root.querySelectorAll<HTMLButtonElement>('button[type="button"]')].find(
        (button) => button.textContent?.trim() === text,
      ) ?? null
    );
  }

  function editButton(root: HTMLElement): HTMLButtonElement | null {
    return buttonByText(root, 'Edit');
  }

  function cancelButton(root: HTMLElement): HTMLButtonElement | null {
    return buttonByText(root, 'Cancel');
  }

  function saveButton(root: HTMLElement): HTMLButtonElement {
    const button = root.querySelector<HTMLButtonElement>('app-submit-button button');

    if (!button) throw new Error('No save button');

    return button;
  }

  async function startEditing(root: HTMLElement): Promise<void> {
    const button = editButton(root);

    if (!button) throw new Error('No edit button');

    button.click();
    await settle();
  }

  async function open(): Promise<HTMLElement> {
    const root = await openReadOnly();
    await startEditing(root);

    return root;
  }

  async function settle(): Promise<void> {
    await harness.fixture.whenStable();
    harness.fixture.detectChanges();
    await harness.fixture.whenStable();
  }

  function nameInput(root: HTMLElement): HTMLInputElement {
    const input = root.querySelector<HTMLInputElement>('mat-form-field input');

    if (!input) throw new Error('No display name input');

    return input;
  }

  function type(root: HTMLElement, value: string): void {
    const input = nameInput(root);

    input.value = value;
    input.dispatchEvent(new Event('input'));
    input.dispatchEvent(new Event('blur'));
  }

  async function submit(root: HTMLElement, value?: string): Promise<void> {
    if (value !== undefined) type(root, value);

    root.querySelector('form')?.dispatchEvent(new Event('submit', { cancelable: true }));
    await settle();
  }

  function fieldError(root: HTMLElement): string | undefined {
    return root.querySelector('mat-form-field mat-error')?.textContent?.trim();
  }

  function alertText(root: HTMLElement, role: 'alert' | 'status'): string | undefined {
    return root.querySelector(`app-alert[role="${role}"]`)?.textContent?.trim();
  }

  function failWith(status: number, error: unknown = null): void {
    updateDisplayName.mockReturnValue(throwError(() => new HttpErrorResponse({ status, error })));
  }

  function failWithCodes(errors: Record<string, string[]>): void {
    failWith(400, { status: 400, errors });
  }

  it('is composed from the shared page card, field errors and submit button', async () => {
    const root = await open();

    expect(root.querySelector('app-page-card h1')?.textContent?.trim()).toBe('Profile');
    expect(root.querySelector('app-submit-button button[type="submit"]')?.textContent?.trim()).toBe(
      'Save',
    );
    expect(root.querySelectorAll('mat-form-field input[matInput]').length).toBe(1);
    expect(editButton(root)).toBeNull();

    await submit(root, '');

    expect(root.querySelector('mat-error app-field-errors')).not.toBeNull();
  });

  it('shows the email and the display name as read-only text with an Edit button', async () => {
    const root = await openReadOnly();

    expect(root.textContent).toContain('jan@example.com');
    expect(root.textContent).toContain('Jan K');
    expect(root.textContent).not.toContain("Your email can't be changed here.");
    expect(root.textContent).toContain('Email');
    expect(root.textContent).toContain('Display name');
    expect(root.querySelectorAll('.profile-field').length).toBe(2);
    expect(root.querySelectorAll('input').length).toBe(0);
    expect(root.querySelector('mat-form-field')).toBeNull();
    expect(root.querySelector('app-submit-button')).toBeNull();
    expect(editButton(root)?.textContent?.trim()).toBe('Edit');
  });

  it('turns the name into a prefilled input with a label and a Save button in edit mode', async () => {
    const root = await open();

    expect(nameInput(root).value).toBe('Jan K');
    expect(root.querySelector('mat-form-field mat-label')?.textContent?.trim()).toBe(
      'Display name',
    );
    expect(root.querySelectorAll('input').length).toBe(1);
    expect(root.querySelector('input[type="email"]')).toBeNull();
    expect(root.textContent).toContain('jan@example.com');
    expect(editButton(root)).toBeNull();
    expect(root.querySelector('app-submit-button button')?.textContent?.trim()).toBe('Save');
  });

  it('places Cancel to the left of Save in edit mode', async () => {
    const root = await open();
    const cancel = cancelButton(root);
    const save = saveButton(root);

    expect(cancel).not.toBeNull();
    expect(cancel!.compareDocumentPosition(save) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it('keeps Save disabled until the name differs from the saved one', async () => {
    const root = await open();

    expect(saveButton(root).disabled).toBe(true);

    type(root, 'Jan Kx');
    await settle();
    expect(saveButton(root).disabled).toBe(false);

    type(root, 'Jan K');
    await settle();
    expect(saveButton(root).disabled).toBe(true);
  });

  it('treats surrounding whitespace as no change', async () => {
    const root = await open();

    type(root, '  Jan K ');
    await settle();

    expect(saveButton(root).disabled).toBe(true);
  });

  it('compares against the newly saved name after a successful save', async () => {
    const root = await open();
    await submit(root, 'Jan K2');
    await startEditing(root);

    expect(saveButton(root).disabled).toBe(true);

    type(root, 'Jan K');
    await settle();
    expect(saveButton(root).disabled).toBe(false);
  });

  it('discards the edit and returns to read-only mode on Cancel', async () => {
    const root = await open();
    type(root, 'Something else');
    await settle();

    cancelButton(root)!.click();
    await settle();

    expect(updateDisplayName).not.toHaveBeenCalled();
    expect(root.querySelectorAll('input').length).toBe(0);
    expect(root.textContent).toContain('Jan K');
    expect(root.textContent).not.toContain('Something else');
    expect(editButton(root)).not.toBeNull();

    await startEditing(root);

    expect(nameInput(root).value).toBe('Jan K');
    expect(saveButton(root).disabled).toBe(true);
  });

  it('clears a server error and the alert on Cancel', async () => {
    failWithCodes({ DuplicateDisplayName: ['Display name is already taken.'] });
    const root = await open();
    await submit(root, 'Taken');
    expect(fieldError(root)).toBe('This name is already taken.');

    cancelButton(root)!.click();
    await settle();
    await startEditing(root);

    expect(fieldError(root)).toBeUndefined();
    expect(alertText(root, 'alert')).toBeUndefined();
    expect(nameInput(root).value).toBe('Jan K');
  });

  it.each([
    ['an empty name', '', 'This field is required.'],
    ['a 2-character name', 'ab', 'Use at least 3 characters.'],
    ['a 31-character name', 'a'.repeat(31), 'Use at most 30 characters.'],
    ['a name with @', 'a@b', `The name can't contain "@".`],
  ])('blocks submission for %s', async (_case, value, message) => {
    const root = await open();

    await submit(root, value);

    expect(updateDisplayName).not.toHaveBeenCalled();
    expect(fieldError(root)).toBe(message);
  });

  it('accepts the boundary lengths of 3 and 30 characters', async () => {
    const root = await open();

    await submit(root, 'abc');
    await startEditing(root);
    await submit(root, 'a'.repeat(30));

    expect(updateDisplayName).toHaveBeenNthCalledWith(1, 'abc');
    expect(updateDisplayName).toHaveBeenNthCalledWith(2, 'a'.repeat(30));
  });

  it('saves the name, refreshes the general configuration and shows the success notice', async () => {
    const root = await open();

    await submit(root, 'Jan K2');

    expect(updateDisplayName).toHaveBeenCalledExactlyOnceWith('Jan K2');
    expect(loadGeneral).toHaveBeenCalledOnce();
    expect(alertText(root, 'status')).toBe('Your name has been updated.');
    expect(alertText(root, 'alert')).toBeUndefined();
    expect(root.querySelectorAll('input').length).toBe(0);
    expect(root.textContent).toContain('Jan K2');
    expect(editButton(root)?.textContent?.trim()).toBe('Edit');
  });

  it('hides the success notice and reopens the input when Edit is clicked again', async () => {
    const root = await open();
    await submit(root, 'Jan K2');

    await startEditing(root);

    expect(alertText(root, 'status')).toBeUndefined();
    expect(nameInput(root).value).toBe('Jan K2');
  });

  it('still reports success when refreshing the general configuration fails', async () => {
    loadGeneral.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 503 })));
    const root = await open();

    await submit(root, 'Jan K2');

    expect(alertText(root, 'status')).toBe('Your name has been updated.');
    expect(alertText(root, 'alert')).toBeUndefined();
  });

  it('maps DuplicateDisplayName to the field until the value changes', async () => {
    failWithCodes({ DuplicateDisplayName: ['Display name is already taken.'] });
    const root = await open();

    await submit(root, 'Taken');

    expect(fieldError(root)).toBe('This name is already taken.');
    expect(alertText(root, 'alert')).toBeUndefined();
    expect(alertText(root, 'status')).toBeUndefined();
    expect(loadGeneral).not.toHaveBeenCalled();

    updateDisplayName.mockClear();
    await submit(root);
    expect(updateDisplayName).not.toHaveBeenCalled();

    updateDisplayName.mockReturnValue(of({ email: user.email, displayName: 'Free' }));
    await submit(root, 'Free');

    expect(fieldError(root)).toBeUndefined();
    expect(updateDisplayName).toHaveBeenCalledExactlyOnceWith('Free');
    expect(alertText(root, 'status')).toBe('Your name has been updated.');
  });

  it.each([
    [{ DisplayNameLength: ['x'] }, 'This name does not meet the rules.'],
    [{ DisplayNameInvalidCharacter: ['x'] }, 'This name does not meet the rules.'],
  ])('maps server error %o to the field', async (errors, message) => {
    failWithCodes(errors);
    const root = await open();

    await submit(root, 'Valid name');

    expect(fieldError(root)).toBe(message);
  });

  it.each([
    [429, 'Too many attempts from this device. Wait a minute and try again.'],
    [503, 'PigeonWatch is still waking up. Try again in a moment.'],
    [0, 'PigeonWatch is still waking up. Try again in a moment.'],
    [500, 'Something went wrong. Try again.'],
  ])('maps status %s to a form-level alert', async (status, message) => {
    failWith(status);
    const root = await open();

    await submit(root, 'Valid name');

    expect(alertText(root, 'alert')).toBe(message);
    expect(fieldError(root)).toBeUndefined();
    expect(alertText(root, 'status')).toBeUndefined();
    expect(loadGeneral).not.toHaveBeenCalled();
    expect(nameInput(root).value).toBe('Valid name');
    expect(root.querySelector('app-submit-button button')?.hasAttribute('disabled')).toBe(false);
  });
});
