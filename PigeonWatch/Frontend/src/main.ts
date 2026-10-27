import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { App } from './app/app';
import { reportBootstrapFailure } from './app/core/error-reporting/bootstrap-report';

bootstrapApplication(App, appConfig)
  .catch((err) => {
    console.error(err);
    reportBootstrapFailure(err);
  });
