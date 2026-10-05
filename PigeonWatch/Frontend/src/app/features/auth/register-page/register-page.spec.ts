import { Component } from '@angular/core';
import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Observable, of, throwError } from 'rxjs';
import { RegisterRequest } from '../../../core/auth/auth-api';
import { AutoLoginError, SessionService } from '../../../core/auth/session.service';
import { LoginPage } from '../login-page/login-page';
import { RegisterPage } from './register-page';

@Component({ template: '' })
class BlankPage {}

interface RegisterInput {
  email: string;
  displayName: string;
  password: string;
  confirmPassword: string;
}

const validInput: RegisterInput = {
  email: 'jan@example.com',
  displayName: 'Jan K',
  password: 'Secret1!',
  confirmPassword: 'Secret1!',
};

describe('RegisterPage', () => {
  let register: ReturnType<typeof vi.fn<(request: RegisterRequest) => Observable<void>>>;
  let harness: RouterTestingHarness;

  beforeEach(async () => {
    register = vi.fn<(request: RegisterRequest) => Observable<void>>(() => of(undefined));
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([
          { path: 'register', component: RegisterPage },
          { path: 'login', component: LoginPage },
          { path: '**', component: BlankPage },
        ]),
        { provide: SessionService, useValue: { register, login: vi.fn() } },
      ],
    });
    harness = await RouterTestingHarness.create();
  });

  async function open(url = '/register'): Promise<HTMLElement> {
    await harness.navigateByUrl(url);
    await harness.fixture.whenStable();

    return harness.routeNativeElement as HTMLElement;
  }

  async function settle(): Promise<void> {
    await harness.fixture.whenStable();
    harness.fixture.detectChanges();
    await harness.fixture.whenStable();
  }

  function fields(root: HTMLElement): HTMLElement[] {
    return Array.from(root.querySelectorAll<HTMLElement>('mat-form-field'));
  }

  function fill(root: HTMLElement, input: RegisterInput): void {
    const values = [input.email, input.displayName, input.password, input.confirmPassword];

    fields(root).forEach((field, index) => {
      const element = field.querySelector('input');

      if (!element) throw new Error('mat-form-field without an input');

      element.value = values[index];
      element.dispatchEvent(new Event('input'));
      element.dispatchEvent(new Event('blur'));
    });
  }

  async function submit(root: HTMLElement, input: RegisterInput = validInput): Promise<void> {
    fill(root, input);
    root.querySelector('form')?.dispatchEvent(new Event('submit', { cancelable: true }));
    await settle();
  }

  function fieldError(root: HTMLElement, index: number): string | undefined {
    return fields(root)[index].querySelector('mat-error')?.textContent?.trim();
  }

  function failWith(errors: Record<string, string[]>): void {
    register.mockReturnValue(
      throwError(() => new HttpErrorResponse({ status: 400, error: { status: 400, errors } })),
    );
  }

  it('is composed from the shared page card, field errors and submit button', async () => {
    const root = await open();

    expect(root.querySelector('app-page-card h1')?.textContent?.trim()).toBe('Create account');
    expect(root.querySelector('app-submit-button button[type="submit"]')).not.toBeNull();
    expect(root.querySelectorAll('mat-form-field input[matInput]').length).toBe(4);
    expect(root.querySelectorAll('mat-hint').length).toBe(2);

    root.querySelector('form')?.dispatchEvent(new Event('submit', { cancelable: true }));
    await settle();

    expect(root.querySelectorAll('mat-error app-field-errors').length).toBe(4);
  });

  it.each([
    ['a display name that is too short', { displayName: 'ab' }, 1, 'Use at least 3 characters.'],
    ['a display name with @', { displayName: 'a@b' }, 1, 'The name can\'t contain "@".'],
    [
      'a 7-character password',
      { password: 'Secre1!', confirmPassword: 'Secre1!' },
      2,
      'Use at least 8 characters.',
    ],
    [
      'a password without a digit',
      { password: 'Secrets!', confirmPassword: 'Secrets!' },
      2,
      'Include at least one digit.',
    ],
    ['mismatched passwords', { confirmPassword: 'Secret2!' }, 3, "Passwords don't match."],
  ])('blocks submission for %s', async (_case, overrides, fieldIndex, message) => {
    const root = await open();

    await submit(root, { ...validInput, ...overrides });

    expect(register).not.toHaveBeenCalled();
    expect(fieldError(root, fieldIndex)).toBe(message);
  });

  it('registers and navigates to the safe return URL', async () => {
    const root = await open('/register?returnUrl=%2Fsightings%2F1');

    await submit(root);

    expect(register).toHaveBeenCalledExactlyOnceWith({
      email: 'jan@example.com',
      displayName: 'Jan K',
      password: 'Secret1!',
    });
    expect(TestBed.inject(Router).url).toBe('/sightings/1');
  });

  it('maps RegistrationFailed to a generic form-level alert, never to the email field', async () => {
    failWith({ RegistrationFailed: ['The account could not be created.'] });
    const root = await open();

    await submit(root);

    expect(root.querySelector('app-alert[role="alert"]')?.textContent?.trim()).toBe(
      "We couldn't create an account with these details.",
    );
    expect(fieldError(root, 0)).toBeUndefined();
    expect(root.textContent).not.toContain('taken');
  });

  it('maps DuplicateDisplayName to the display-name field until it changes', async () => {
    failWith({ DuplicateDisplayName: ['Display name is already taken.'] });
    const root = await open();

    await submit(root);

    expect(fieldError(root, 1)).toBe('This name is already taken.');
    expect(root.querySelector('app-alert[role="alert"]')).toBeNull();

    register.mockClear();
    root.querySelector('form')?.dispatchEvent(new Event('submit', { cancelable: true }));
    await settle();
    expect(register).not.toHaveBeenCalled();

    register.mockReturnValue(of(undefined));
    await submit(root, { ...validInput, displayName: 'Jan K2' });
    expect(register).toHaveBeenCalledOnce();
  });

  it.each([
    [{ PasswordRequiresUniqueChars: ['x'] }, 2, 'This password does not meet the rules.'],
    [{ DisplayNameLength: ['x'] }, 1, 'This name does not meet the rules.'],
    [{ InvalidEmail: ['x'] }, 0, 'Enter a valid email address.'],
  ])('maps server error %o to its field', async (errors, fieldIndex, message) => {
    failWith(errors);
    const root = await open();

    await submit(root);

    expect(fieldError(root, fieldIndex)).toBe(message);
  });

  it('falls back to /login with navigation state when the auto-login fails', async () => {
    register.mockReturnValue(
      throwError(() => new AutoLoginError(new HttpErrorResponse({ status: 503 }))),
    );
    const root = await open('/register?returnUrl=%2Fa');

    await submit(root);
    await settle();

    const login = harness.routeNativeElement as HTMLElement;
    expect(root.isConnected).toBe(false);
    expect(TestBed.inject(Router).url).toBe('/login?returnUrl=%2Fa');
    expect(login.querySelector<HTMLInputElement>('input[type="email"]')?.value).toBe(
      'jan@example.com',
    );
    expect(login.querySelector('app-alert[role="status"]')?.textContent?.trim()).toBe(
      'Account created — please log in.',
    );
  });
});
