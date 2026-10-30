import { Component } from '@angular/core';
import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Observable, of, throwError } from 'rxjs';
import { SessionService } from '../../../core/auth/session.service';
import { ErrorReportingService } from '../../../core/error-reporting/error-reporting.service';
import { LoginPage } from './login-page';

@Component({ template: '' })
class BlankPage {}

const traceId = '00-abc123-def456-01';

describe('LoginPage failure reporting', () => {
  let login: ReturnType<typeof vi.fn<(email: string, password: string) => Observable<void>>>;
  let report: ReturnType<typeof vi.fn>;
  let harness: RouterTestingHarness;

  beforeEach(async () => {
    report = vi.fn();
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
        { provide: ErrorReportingService, useValue: { report } },
      ],
    });
    harness = await RouterTestingHarness.create();
  });

  async function submitWith(error: HttpErrorResponse): Promise<HTMLElement> {
    login.mockReturnValue(throwError(() => error));
    await harness.navigateByUrl('/login');
    await harness.fixture.whenStable();

    const root = harness.routeNativeElement as HTMLElement;

    for (const [selector, value] of [
      ['input[type="email"]', 'jan@example.com'],
      ['input[type="password"]', 'Secret1!'],
    ]) {
      const input = root.querySelector<HTMLInputElement>(selector);

      if (!input) throw new Error(`No input ${selector}`);

      input.value = value;
      input.dispatchEvent(new Event('input'));
      input.dispatchEvent(new Event('blur'));
    }

    root.querySelector('form')?.dispatchEvent(new Event('submit', { cancelable: true }));
    await harness.fixture.whenStable();
    harness.fixture.detectChanges();
    await harness.fixture.whenStable();

    return root;
  }

  it('shows the reference and reports api-failure for a 500 with a trace id', async () => {
    const error = new HttpErrorResponse({ status: 500, error: { traceId } });
    const root = await submitWith(error);

    expect(root.querySelector('app-alert[role="alert"]')?.textContent).toContain(
      `Reference: ${traceId}`,
    );
    expect(report).toHaveBeenCalledExactlyOnceWith('api-failure', 'auth.login', error);
  });

  it('reports a 500 without a trace id and shows no reference', async () => {
    const error = new HttpErrorResponse({ status: 500 });
    const root = await submitWith(error);

    expect(root.querySelector('app-alert[role="alert"]')).not.toBeNull();
    expect(root.textContent).not.toContain('Reference');
    expect(report).toHaveBeenCalledExactlyOnceWith('api-failure', 'auth.login', error);
  });

  it.each([401, 429, 503])('does not report or show a reference for a %s', async (status) => {
    const root = await submitWith(new HttpErrorResponse({ status, error: { traceId } }));

    expect(root.querySelector('app-alert[role="alert"]')).not.toBeNull();
    expect(root.textContent).not.toContain('Reference');
    expect(report).not.toHaveBeenCalled();
  });
});
