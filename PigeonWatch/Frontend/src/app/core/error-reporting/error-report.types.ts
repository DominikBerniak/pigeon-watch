export type ErrorReportKind =
  | 'uncaught'
  | 'bootstrap'
  | 'navigation'
  | 'api-failure'
  | 'session-expired'
  | 'warmup-exhausted'
  | 'fallback';

export interface ErrorReportPayload {
  kind: ErrorReportKind;
  context: string;
  httpStatus?: number;
  errorName?: string;
  traceId?: string;
  route: string;
  sessionId: string;
}

const guidSegment = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function routeTemplate(url: string): string {
  const path = url.split(/[?#]/)[0];
  const template = path
    .split('/')
    .map((segment) => (guidSegment.test(segment) ? ':id' : segment.replace(/[^A-Za-z0-9:_.-]/g, '_')))
    .join('/');
  const route = template.startsWith('/') ? template : `/${template}`;

  return route.slice(0, 100);
}

export function errorNameOf(error: unknown): string | undefined {
  if (typeof error !== 'object' || error === null || !('name' in error)) return undefined;

  const name = error.name;

  if (typeof name !== 'string') return undefined;

  const cleaned = name.replace(/[^A-Za-z0-9_]/g, '').slice(0, 80);

  return /^[A-Za-z_]/.test(cleaned) ? cleaned : undefined;
}
