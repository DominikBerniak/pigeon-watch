import { HttpErrorResponse } from '@angular/common/http';
import { ResourceService } from '../../core/resources/resource.service';
import {
  isExpectedClientStatus,
  problemErrorCodes,
  problemExtension,
  unexpectedTraceId,
} from '../../shared/http/problem-details';

export type SightingFailure =
  | { readonly kind: 'cooldown'; readonly retryAfterSeconds: number }
  | { readonly kind: 'notFound' }
  | { readonly kind: 'generic'; readonly traceId?: string };

const cooldownCode = 'cooldownactive';
const retryAfterSecondsExtension = 'retryAfterSeconds';

export function sightingFailure(error: unknown): SightingFailure {
  if (!(error instanceof HttpErrorResponse)) return genericFailure(error);

  if (error.status === 404) return { kind: 'notFound' };

  if (error.status !== 400 || !hasCooldownCode(error)) return genericFailure(error);

  const retryAfterSeconds = secondsOf(problemExtension(error, retryAfterSecondsExtension));

  return retryAfterSeconds === null
    ? genericFailure(error)
    : { kind: 'cooldown', retryAfterSeconds };
}

export function isReportableSightingFailure(error: unknown, failure: SightingFailure): boolean {
  return failure.kind === 'generic' && !isExpectedClientStatus(error);
}

export function sightingErrorMessage(resources: ResourceService, failure: SightingFailure): string {
  if (failure.kind === 'cooldown') {
    return resources.translate('sighting.errors.cooldown', failure.retryAfterSeconds);
  }

  if (failure.kind === 'notFound') return resources.translate('sighting.errors.notFound');

  return resources.translate('sighting.errors.generic');
}

function genericFailure(error: unknown): SightingFailure {
  const traceId = unexpectedTraceId(error);

  return traceId === undefined ? { kind: 'generic' } : { kind: 'generic', traceId };
}

function hasCooldownCode(error: HttpErrorResponse): boolean {
  return problemErrorCodes(error).some((code) => code.toLowerCase() === cooldownCode);
}

function secondsOf(value: unknown): number | null {
  if (typeof value !== 'number' || !Number.isFinite(value) || value <= 0) return null;

  return Math.ceil(value);
}
