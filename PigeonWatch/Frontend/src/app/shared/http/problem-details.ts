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

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
