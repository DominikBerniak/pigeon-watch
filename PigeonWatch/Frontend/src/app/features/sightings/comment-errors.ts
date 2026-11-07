import { HttpErrorResponse } from '@angular/common/http';
import { ResourceService } from '../../core/resources/resource.service';
import {
  isExpectedClientStatus,
  problemErrorCodes,
  problemExtension,
  unexpectedTraceId,
} from '../../shared/http/problem-details';

export type CommentFailure =
  | { readonly kind: 'cooldown'; readonly retryAfterSeconds: number }
  | { readonly kind: 'textRequired' }
  | { readonly kind: 'notFound' }
  | { readonly kind: 'forbidden'; readonly traceId?: string }
  | { readonly kind: 'unexpected'; readonly traceId?: string };

export type CommentContext =
  | 'sighting.comments.load'
  | 'sighting.comments.add'
  | 'sighting.comments.edit'
  | 'sighting.comments.delete';

const cooldownCode = 'cooldownactive';
const textRequiredCode = 'commenttextrequired';
const retryAfterSecondsExtension = 'retryAfterSeconds';

export function commentFailure(error: unknown): CommentFailure {
  if (!(error instanceof HttpErrorResponse)) return withTraceId('unexpected', error);

  if (error.status === 404) return { kind: 'notFound' };

  if (error.status === 403) return withTraceId('forbidden', error);

  if (error.status !== 400) return withTraceId('unexpected', error);

  if (hasCode(error, cooldownCode)) {
    const retryAfterSeconds = secondsOf(problemExtension(error, retryAfterSecondsExtension));

    return retryAfterSeconds === null
      ? withTraceId('unexpected', error)
      : { kind: 'cooldown', retryAfterSeconds };
  }

  if (hasCode(error, textRequiredCode)) return { kind: 'textRequired' };

  return withTraceId('unexpected', error);
}

export function isReportableCommentFailure(error: unknown, failure: CommentFailure): boolean {
  if (failure.kind === 'forbidden') return true;

  return failure.kind === 'unexpected' && !isExpectedClientStatus(error);
}

export function commentFailureTraceId(failure: CommentFailure | null): string | null {
  if (failure?.kind === 'forbidden' || failure?.kind === 'unexpected') {
    return failure.traceId ?? null;
  }

  return null;
}

export function commentErrorMessage(resources: ResourceService, failure: CommentFailure): string {
  switch (failure.kind) {
    case 'cooldown':
      return resources.translate('sighting.comments.errors.cooldown', failure.retryAfterSeconds);
    case 'textRequired':
      return resources.translate('sighting.comments.validation.required');
    case 'notFound':
      return resources.translate('sighting.comments.errors.notFound');
    case 'forbidden':
      return resources.translate('sighting.comments.errors.forbidden');
    default:
      return resources.translate('sighting.comments.errors.generic');
  }
}

function withTraceId(kind: 'forbidden' | 'unexpected', error: unknown): CommentFailure {
  const traceId = unexpectedTraceId(error);

  return traceId === undefined ? { kind } : { kind, traceId };
}

function hasCode(error: HttpErrorResponse, lowercaseCode: string): boolean {
  return problemErrorCodes(error).some((code) => code.toLowerCase() === lowercaseCode);
}

function secondsOf(value: unknown): number | null {
  if (typeof value !== 'number' || !Number.isFinite(value) || value <= 0) return null;

  return Math.ceil(value);
}
