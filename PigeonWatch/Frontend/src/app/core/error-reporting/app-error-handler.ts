import { ErrorHandler, Injectable, Injector, inject } from '@angular/core';
import { NavigationError } from '@angular/router';
import { ErrorReportingService } from './error-reporting.service';

const reportedNavigationErrors = new WeakSet<object>();

@Injectable()
export class AppErrorHandler implements ErrorHandler {
  private readonly injector = inject(Injector);

  handleError(error: unknown): void {
    console.error(error);

    if (typeof error === 'object' && error !== null && reportedNavigationErrors.has(error)) return;

    this.injector.get(ErrorReportingService).report('uncaught', 'app.uncaught', error);
  }
}

export function reportNavigationError(navigationError: NavigationError): void {
  if (typeof navigationError.error === 'object' && navigationError.error !== null) {
    reportedNavigationErrors.add(navigationError.error);
  }

  inject(ErrorReportingService).report('navigation', 'router.navigation', navigationError.error);
}
