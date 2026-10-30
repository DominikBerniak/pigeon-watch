import { environment } from '../../../environments/environment';
import { ErrorReportPayload, errorNameOf, routeTemplate } from './error-report.types';

export function reportBootstrapFailure(error: unknown): void {
  if (typeof navigator.sendBeacon !== 'function') return;

  const payload: ErrorReportPayload = {
    kind: 'bootstrap',
    context: 'app.bootstrap',
    errorName: errorNameOf(error),
    route: routeTemplate(window.location.pathname),
    sessionId: crypto.randomUUID(),
  };
  const body = new Blob([JSON.stringify(payload)], { type: 'application/json' });

  navigator.sendBeacon(`${environment.apiUrl}/client-errors`, body);
}
