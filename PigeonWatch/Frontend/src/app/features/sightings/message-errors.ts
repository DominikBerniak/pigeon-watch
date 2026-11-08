import { HttpErrorResponse } from '@angular/common/http';
import { ResourceService } from '../../core/resources/resource.service';
import {
  isExpectedClientStatus,
  problemErrorCodes,
  problemTraceId,
  unexpectedTraceId,
} from '../../shared/http/problem-details';

export type MessageFailure =
  | { readonly kind: 'notFound' }
  | { readonly kind: 'ownSighting'; readonly traceId?: string }
  | { readonly kind: 'textRequired' }
  | { readonly kind: 'unexpected'; readonly traceId?: string };

export type MessageContext =
  | 'sighting.threads.load'
  | 'sighting.threads.start'
  | 'sighting.messages.load'
  | 'sighting.messages.send'
  | 'messages.inbox.load'
  | 'messages.inbox.sync'
  | 'messages.thread.read'
  | 'messages.unread.count';

const ownSightingCode = 'ownsightingcontact';
const textRequiredCode = 'messagetextrequired';

export function messageFailure(error: unknown): MessageFailure {
  if (!(error instanceof HttpErrorResponse)) return unexpected(error);

  if (error.status === 404) return { kind: 'notFound' };

  if (error.status !== 400) return unexpected(error);

  if (hasCode(error, ownSightingCode)) {
    const traceId = problemTraceId(error);

    return traceId === null ? { kind: 'ownSighting' } : { kind: 'ownSighting', traceId };
  }

  if (hasCode(error, textRequiredCode)) return { kind: 'textRequired' };

  return unexpected(error);
}

export function isReportableMessageFailure(error: unknown, failure: MessageFailure): boolean {
  if (failure.kind === 'ownSighting') return true;

  return failure.kind === 'unexpected' && !isExpectedClientStatus(error);
}

export function messageFailureTraceId(failure: MessageFailure | null): string | null {
  if (failure?.kind === 'ownSighting' || failure?.kind === 'unexpected') {
    return failure.traceId ?? null;
  }

  return null;
}

export function messageErrorMessage(resources: ResourceService, failure: MessageFailure): string {
  switch (failure.kind) {
    case 'notFound':
      return resources.translate('sighting.messages.errors.notFound');
    case 'ownSighting':
      return resources.translate('sighting.messages.errors.ownSighting');
    case 'textRequired':
      return resources.translate('sighting.messages.validation.required');
    default:
      return resources.translate('sighting.messages.errors.send');
  }
}

function unexpected(error: unknown): MessageFailure {
  const traceId = unexpectedTraceId(error);

  return traceId === undefined ? { kind: 'unexpected' } : { kind: 'unexpected', traceId };
}

function hasCode(error: HttpErrorResponse, lowercaseCode: string): boolean {
  return problemErrorCodes(error).some((code) => code.toLowerCase() === lowercaseCode);
}
