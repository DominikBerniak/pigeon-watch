import {
  ApplicationConfig,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { routes } from './app.routes';
import { authInterceptor } from './core/auth/auth.interceptor';
import { SessionService } from './core/auth/session.service';
import { ResourceService } from './core/resources/resource.service';
import { warmupInterceptor } from './core/warmup/warmup.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    provideHttpClient(withInterceptors([authInterceptor, warmupInterceptor])),
    provideAppInitializer(() => {
      inject(SessionService).initialize();
      inject(ResourceService).load().subscribe();
    }),
  ],
};
