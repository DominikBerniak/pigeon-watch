import { HttpErrorResponse } from '@angular/common/http';

export function problemErrorCodes(error: HttpErrorResponse): string[] {
  const body: unknown = error.error;

  if (!isRecord(body) || !isRecord(body['errors'])) return [];

  return Object.keys(body['errors']);
}

export function problemDetail(error: HttpErrorResponse): string | null {
  const body: unknown = error.error;

  if (!isRecord(body) || typeof body['detail'] !== 'string') return null;

  return body['detail'];
}

export function problemExtension(error: HttpErrorResponse, name: string): unknown {
  const body: unknown = error.error;

  return isRecord(body) ? body[name] : undefined;
}

export function problemTraceId(error: HttpErrorResponse): string | null {
  const value = problemExtension(error, 'traceId');

  return typeof value === 'string' && value.length > 0 ? value : null;
}

const expectedClientStatuses: ReadonlySet<number> = new Set([400, 401, 404, 429]);

export function isExpectedClientStatus(error: unknown): boolean {
  return error instanceof HttpErrorResponse && expectedClientStatuses.has(error.status);
}

export function unexpectedTraceId(error: unknown): string | undefined {
  if (!(error instanceof HttpErrorResponse) || isExpectedClientStatus(error)) return undefined;

  return problemTraceId(error) ?? undefined;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
