import { ErrorHandler } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { NavigationError } from '@angular/router';
import { AppErrorHandler, reportNavigationError } from './app-error-handler';
import { ErrorReportingService } from './error-reporting.service';

describe('error handlers', () => {
  const report = vi.fn();

  beforeEach(() => {
    report.mockReset();
    TestBed.configureTestingModule({
      providers: [
        { provide: ErrorReportingService, useValue: { report } },
        { provide: ErrorHandler, useClass: AppErrorHandler },
      ],
    });
  });

  afterEach(() => vi.restoreAllMocks());

  it('keeps the console output and reports an uncaught error', () => {
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined);
    const error = new Error('boom');

    TestBed.inject(ErrorHandler).handleError(error);

    expect(consoleError).toHaveBeenCalledWith(error);
    expect(report).toHaveBeenCalledWith('uncaught', 'app.uncaught', error);
  });

  it('reports a navigation error and returns nothing so the default behaviour stays', () => {
    const cause = new Error('chunk');
    const navigationError = new NavigationError(1, '/x', cause);

    const result = TestBed.runInInjectionContext(() => reportNavigationError(navigationError));

    expect(result).toBeUndefined();
    expect(report).toHaveBeenCalledWith('navigation', 'router.navigation', cause);
  });

  it('does not report again an error the navigation handler already reported', () => {
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    const cause = new Error('chunk');

    TestBed.runInInjectionContext(() => reportNavigationError(new NavigationError(1, '/x', cause)));
    TestBed.inject(ErrorHandler).handleError(cause);

    expect(report).toHaveBeenCalledTimes(1);
    expect(report).toHaveBeenCalledWith('navigation', 'router.navigation', cause);
  });
});
