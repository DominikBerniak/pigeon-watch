import {
  ApplicationConfig,
  ErrorHandler,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideRouter, withNavigationErrorHandler } from '@angular/router';
import { routes } from './app.routes';
import { authInterceptor } from './core/auth/auth.interceptor';
import { SessionService } from './core/auth/session.service';
import { AppErrorHandler, reportNavigationError } from './core/error-reporting/app-error-handler';
import { ResourceService } from './core/resources/resource.service';
import { warmupInterceptor } from './core/warmup/warmup.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    { provide: ErrorHandler, useClass: AppErrorHandler },
    provideRouter(routes, withNavigationErrorHandler(reportNavigationError)),
    provideHttpClient(withInterceptors([authInterceptor, warmupInterceptor])),
    provideAppInitializer(() => {
      inject(SessionService).initialize();
      inject(ResourceService).load().subscribe();
    }),
  ],
};
