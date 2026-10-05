import { Component } from '@angular/core';
import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Observable, of, throwError } from 'rxjs';
import { SessionService } from '../../../core/auth/session.service';
import { LoginPage } from './login-page';

@Component({ template: '' })
class BlankPage {}

describe('LoginPage', () => {
  let login: ReturnType<typeof vi.fn<(email: string, password: string) => Observable<void>>>;
  let harness: RouterTestingHarness;

  beforeEach(async () => {
    login = vi.fn<(email: string, password: string) => Observable<void>>(() => of(undefined));
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([
          { path: 'login', component: LoginPage },
          { path: '**', component: BlankPage },
        ]),
        { provide: SessionService, useValue: { login } },
      ],
    });
    harness = await RouterTestingHarness.create();
  });

  async function open(url = '/login'): Promise<HTMLElement> {
    await harness.navigateByUrl(url);
    await harness.fixture.whenStable();

    return harness.routeNativeElement as HTMLElement;
  }

  async function settle(): Promise<void> {
    await harness.fixture.whenStable();
    harness.fixture.detectChanges();
    await harness.fixture.whenStable();
  }

  function type(root: HTMLElement, selector: string, value: string): void {
    const input = root.querySelector<HTMLInputElement>(selector);

    if (!input) throw new Error(`No input matches ${selector}`);

    input.value = value;
    input.dispatchEvent(new Event('input'));
    input.dispatchEvent(new Event('blur'));
  }

  async function submit(root: HTMLElement, email = 'jan@example.com'): Promise<void> {
    type(root, 'input[type="email"]', email);
    type(root, 'input[type="password"]', 'Secret1!');
    root.querySelector('form')?.dispatchEvent(new Event('submit', { cancelable: true }));
    await settle();
  }

  function alertText(root: HTMLElement): string | undefined {
    return root.querySelector('app-alert[role="alert"]')?.textContent?.trim();
  }

  function failWith(status: number, error: unknown = null): void {
    login.mockReturnValue(throwError(() => new HttpErrorResponse({ status, error })));
  }

  it('is composed from the shared page card, field errors and submit button', async () => {
    const root = await open();

    expect(root.querySelector('app-page-card h1')?.textContent?.trim()).toBe('Log in');
    expect(root.querySelector('app-submit-button button[type="submit"]')).not.toBeNull();
    expect(root.querySelectorAll('mat-form-field input[matInput]').length).toBe(2);

    root.querySelector('form')?.dispatchEvent(new Event('submit', { cancelable: true }));
    await settle();

    expect(root.querySelectorAll('mat-error app-field-errors').length).toBe(2);
  });

  it('shows required errors only after submit, not when a field loses focus', async () => {
    const root = await open();

    type(root, 'input[type="email"]', '');
    type(root, 'input[type="password"]', '');
    await settle();

    expect(root.querySelector('mat-error')).toBeNull();
    expect(root.querySelector('mat-form-field')?.classList).not.toContain('mat-form-field-invalid');

    root.querySelector('form')?.dispatchEvent(new Event('submit', { cancelable: true }));
    await settle();

    expect(root.querySelector('mat-error')?.textContent?.trim()).toBe('This field is required.');
    expect(root.querySelector('mat-form-field')?.classList).toContain('mat-form-field-invalid');
  });

  it('does not call the API for an invalid form', async () => {
    const root = await open();

    await submit(root, 'not-an-email');

    expect(login).not.toHaveBeenCalled();
    expect(root.querySelector('mat-error')?.textContent?.trim()).toBe(
      'Enter a valid email address.',
    );
  });

  it('logs in and navigates to the return URL', async () => {
    const root = await open('/login?returnUrl=%2Fsightings%2F1');

    await submit(root);

    expect(login).toHaveBeenCalledExactlyOnceWith('jan@example.com', 'Secret1!');
    expect(TestBed.inject(Router).url).toBe('/sightings/1');
  });

  it('ignores an unsafe return URL', async () => {
    const root = await open('/login?returnUrl=%2F%2Fevil.com');

    await submit(root);

    expect(TestBed.inject(Router).url).toBe('/');
  });

  it.each([
    [401, { detail: 'Failed' }, 'Invalid email or password.'],
    [401, { detail: 'LockedOut' }, 'Too many failed attempts. Try again in a few minutes.'],
    [429, null, 'Too many attempts from this device. Wait a minute and try again.'],
    [503, null, 'PigeonWatch is still waking up. Try again in a moment.'],
    [0, null, 'PigeonWatch is still waking up. Try again in a moment.'],
  ])('maps status %s (%o) to its message', async (status, body, message) => {
    failWith(status, body);
    const root = await open();

    await submit(root);

    expect(alertText(root)).toBe(message);
    expect(TestBed.inject(Router).url).toBe('/login');
    expect(root.querySelector('app-submit-button button')?.hasAttribute('disabled')).toBe(false);
  });

  it('keeps returnUrl on the create-account link', async () => {
    const root = await open('/login?returnUrl=%2Fa');

    const link = root.querySelector<HTMLAnchorElement>('a[href^="/register"]');

    expect(link?.getAttribute('href')).toBe('/register?returnUrl=%2Fa');
    expect(link?.textContent?.trim()).toBe('Create account');
  });

  it('prefills the email and shows the notice from the navigation state', async () => {
    await TestBed.inject(Router).navigate(['/login'], {
      state: { email: 'jan@example.com', notice: 'Account created — please log in.' },
    });
    await settle();
    const root = harness.routeNativeElement as HTMLElement;

    expect(root.querySelector<HTMLInputElement>('input[type="email"]')?.value).toBe(
      'jan@example.com',
    );
    expect(root.querySelector('app-alert[role="status"]')?.textContent?.trim()).toBe(
      'Account created — please log in.',
    );
    expect(TestBed.inject(Router).url).toBe('/login');
  });
});
