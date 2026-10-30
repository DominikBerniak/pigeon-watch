import { HttpClient, HttpContext, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Router } from '@angular/router';
import { EMPTY, catchError } from 'rxjs';
import { environment } from '../../../environments/environment';
import { problemTraceId } from '../../shared/http/problem-details';
import { SKIP_AUTH, SKIP_WARMUP } from '../http/http-context-tokens';
import {
  ErrorReportKind,
  ErrorReportPayload,
  errorNameOf,
  routeTemplate,
} from './error-report.types';

const maxReports = 20;
const traceIdPattern = /^[0-9a-fA-F-]{1,64}$/;

@Injectable({ providedIn: 'root' })
export class ErrorReportingService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly sessionId = crypto.randomUUID();
  private readonly seen = new Set<string>();

  report(kind: ErrorReportKind, context: string, error?: unknown): void {
    const status = error instanceof HttpErrorResponse ? error.status : undefined;
    const key = `${kind}|${context}|${status ?? ''}|${errorNameOf(error) ?? ''}`;

    if (this.seen.has(key) || this.seen.size >= maxReports) return;

    this.seen.add(key);
    this.send(this.payload(kind, context, status, error));
  }

  private payload(
    kind: ErrorReportKind,
    context: string,
    status: number | undefined,
    error: unknown,
  ): ErrorReportPayload {
    const traceId = error instanceof HttpErrorResponse ? problemTraceId(error) : null;

    return {
      kind,
      context,
      httpStatus: status !== undefined && status >= 0 && status <= 599 ? status : undefined,
      errorName: errorNameOf(error),
      traceId: traceId !== null && traceIdPattern.test(traceId) ? traceId : undefined,
      route: routeTemplate(this.router.url),
      sessionId: this.sessionId,
    };
  }

  private send(payload: ErrorReportPayload): void {
    const context = new HttpContext().set(SKIP_AUTH, true).set(SKIP_WARMUP, true);

    this.http
      .post(`${environment.apiUrl}/client-errors`, payload, { context })
      .pipe(
        catchError(() => {
          console.warn('Error report could not be sent.');

          return EMPTY;
        }),
      )
      .subscribe();
  }
}
