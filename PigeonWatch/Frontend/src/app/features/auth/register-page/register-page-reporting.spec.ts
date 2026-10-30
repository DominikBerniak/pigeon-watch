import { Component } from '@angular/core';
import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Observable, of, throwError } from 'rxjs';
import { RegisterRequest } from '../../../core/auth/auth-api';
import { SessionService } from '../../../core/auth/session.service';
import { ErrorReportingService } from '../../../core/error-reporting/error-reporting.service';
import { RegisterPage } from './register-page';

@Component({ template: '' })
class BlankPage {}

const traceId = '00-abc123-def456-01';

describe('RegisterPage failure reporting', () => {
  let register: ReturnType<typeof vi.fn<(request: RegisterRequest) => Observable<void>>>;
  let report: ReturnType<typeof vi.fn>;
  let harness: RouterTestingHarness;

  beforeEach(async () => {
    report = vi.fn();
    register = vi.fn<(request: RegisterRequest) => Observable<void>>(() => of(undefined));
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([
          { path: 'register', component: RegisterPage },
          { path: '**', component: BlankPage },
        ]),
        { provide: SessionService, useValue: { register, login: vi.fn() } },
        { provide: ErrorReportingService, useValue: { report } },
      ],
    });
    harness = await RouterTestingHarness.create();
  });

  async function submitWith(error: HttpErrorResponse): Promise<HTMLElement> {
    register.mockReturnValue(throwError(() => error));
    await harness.navigateByUrl('/register');
    await harness.fixture.whenStable();

    const root = harness.routeNativeElement as HTMLElement;
    const values = ['jan@example.com', 'Jan K', 'Secret1!', 'Secret1!'];

    root.querySelectorAll<HTMLElement>('mat-form-field').forEach((field, index) => {
      const input = field.querySelector('input');

      if (!input) throw new Error('mat-form-field without an input');

      input.value = values[index];
      input.dispatchEvent(new Event('input'));
      input.dispatchEvent(new Event('blur'));
    });
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
    expect(report).toHaveBeenCalledExactlyOnceWith('api-failure', 'auth.register', error);
  });

  it('does not report a validation 400 or show a reference', async () => {
    const root = await submitWith(
      new HttpErrorResponse({
        status: 400,
        error: { status: 400, traceId, errors: { RegistrationFailed: ['x'] } },
      }),
    );

    expect(root.querySelector('app-alert[role="alert"]')).not.toBeNull();
    expect(root.textContent).not.toContain('Reference');
    expect(report).not.toHaveBeenCalled();
  });

  it.each([429, 503])('does not report or show a reference for a %s', async (status) => {
    const root = await submitWith(new HttpErrorResponse({ status, error: { traceId } }));

    expect(root.textContent).not.toContain('Reference');
    expect(report).not.toHaveBeenCalled();
  });
});
