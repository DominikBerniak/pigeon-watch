import { HttpErrorResponse } from '@angular/common/http';
import { ResourceService } from '../../core/resources/resource.service';
import {
  isExpectedClientStatus,
  problemErrorCodes,
  problemExtension,
  problemTraceId,
  unexpectedTraceId,
} from '../../shared/http/problem-details';

export type PhotoProblem = 'photoTooLarge' | 'photoUnsupportedType' | 'photoUnreadable';

export type SightingFailure =
  | { readonly kind: 'cooldown'; readonly retryAfterSeconds: number }
  | { readonly kind: 'notFound' }
  | { readonly kind: PhotoProblem }
  | { readonly kind: 'photoStorageUnavailable'; readonly traceId?: string }
  | { readonly kind: 'generic'; readonly traceId?: string };

export const photoProblemLabels: Readonly<Record<PhotoProblem, string>> = {
  photoTooLarge: 'sighting.form.photoTooLarge',
  photoUnsupportedType: 'sighting.form.photoUnsupportedType',
  photoUnreadable: 'sighting.form.photoUnreadable',
};

const cooldownCode = 'cooldownactive';
const retryAfterSecondsExtension = 'retryAfterSeconds';
const photoStorageUnavailableCode = 'photostorageunavailable';
const photoProblemCodes: ReadonlyMap<string, PhotoProblem> = new Map([
  ['phototoolarge', 'photoTooLarge'],
  ['photounsupportedtype', 'photoUnsupportedType'],
  ['photounreadable', 'photoUnreadable'],
]);

export function sightingFailure(error: unknown): SightingFailure {
  if (!(error instanceof HttpErrorResponse)) return genericFailure(error);

  if (error.status === 404) return { kind: 'notFound' };

  if (error.status === 503 && isPhotoStorageOutage(error)) return photoStorageFailure(error);

  if (error.status !== 400) return genericFailure(error);

  const photoProblem = photoProblemOf(error);

  if (photoProblem !== null) return { kind: photoProblem };

  if (!hasCode(error, cooldownCode)) return genericFailure(error);

  const retryAfterSeconds = secondsOf(problemExtension(error, retryAfterSecondsExtension));

  return retryAfterSeconds === null
    ? genericFailure(error)
    : { kind: 'cooldown', retryAfterSeconds };
}

export function isPhotoStorageOutage(error: HttpErrorResponse): boolean {
  return hasCode(error, photoStorageUnavailableCode);
}

export function isPhotoProblem(
  failure: { readonly kind: string },
): failure is { readonly kind: PhotoProblem } {
  return Object.hasOwn(photoProblemLabels, failure.kind);
}

export function isReportableSightingFailure(error: unknown, failure: SightingFailure): boolean {
  if (failure.kind === 'photoStorageUnavailable') return true;

  return failure.kind === 'generic' && !isExpectedClientStatus(error);
}

export function isReportablePhotoFailure(error: unknown): boolean {
  if (error instanceof HttpErrorResponse && error.status === 404) return true;

  return !isExpectedClientStatus(error);
}

export function sightingErrorMessage(resources: ResourceService, failure: SightingFailure): string {
  if (failure.kind === 'cooldown') {
    return resources.translate('sighting.errors.cooldown', failure.retryAfterSeconds);
  }

  if (failure.kind === 'notFound') return resources.translate('sighting.errors.notFound');

  if (failure.kind === 'photoStorageUnavailable') {
    return resources.translate('sighting.form.photoStorageUnavailable');
  }

  if (isPhotoProblem(failure)) return resources.translate(photoProblemLabels[failure.kind]);

  return resources.translate('sighting.errors.generic');
}

export type SightingUpdateFailure =
  | SightingFailure
  | { readonly kind: 'forbidden'; readonly traceId?: string }
  | { readonly kind: 'descriptionLocked' }
  | { readonly kind: 'invalidStatusTransition' }
  | { readonly kind: 'photoConflict'; readonly traceId?: string }
  | { readonly kind: 'tooManyEdits' }
  | { readonly kind: 'removed' };

const sightingRemovedCode = 'sightingremoved';
const descriptionLockedCode = 'descriptionlocked';
const invalidStatusTransitionCode = 'invalidstatustransition';
const photoConflictCode = 'photoconflict';

export function sightingUpdateFailure(error: unknown): SightingUpdateFailure {
  if (!(error instanceof HttpErrorResponse)) return sightingFailure(error);

  if (error.status === 403) return withOptionalTraceId('forbidden', unexpectedTraceId(error));

  if (error.status === 429) return { kind: 'tooManyEdits' };

  if (error.status !== 400) return sightingFailure(error);

  if (isSightingRemovedFailure(error)) return { kind: 'removed' };

  if (hasCode(error, descriptionLockedCode)) return { kind: 'descriptionLocked' };

  if (hasCode(error, invalidStatusTransitionCode)) return { kind: 'invalidStatusTransition' };

  if (hasCode(error, photoConflictCode)) {
    return withOptionalTraceId('photoConflict', problemTraceId(error) ?? undefined);
  }

  return sightingFailure(error);
}

export function isStaleEditFailure(failure: SightingUpdateFailure): boolean {
  return failure.kind === 'descriptionLocked' || failure.kind === 'invalidStatusTransition';
}

export function isReportableSightingUpdateFailure(
  error: unknown,
  failure: SightingUpdateFailure,
): boolean {
  switch (failure.kind) {
    case 'forbidden':
    case 'photoConflict':
      return true;
    case 'descriptionLocked':
    case 'invalidStatusTransition':
    case 'tooManyEdits':
    case 'removed':
      return false;
    default:
      return isReportableSightingFailure(error, failure);
  }
}

export function sightingUpdateFailureTraceId(failure: SightingUpdateFailure | null): string | null {
  switch (failure?.kind) {
    case 'forbidden':
    case 'photoConflict':
    case 'photoStorageUnavailable':
    case 'generic':
      return failure.traceId ?? null;
    default:
      return null;
  }
}

export function sightingUpdateErrorMessage(
  resources: ResourceService,
  failure: SightingUpdateFailure,
): string {
  switch (failure.kind) {
    case 'forbidden':
      return resources.translate('sighting.edit.errors.forbidden');
    case 'descriptionLocked':
      return resources.translate('sighting.edit.errors.descriptionLocked');
    case 'invalidStatusTransition':
      return resources.translate('sighting.edit.errors.invalidStatusTransition');
    case 'photoConflict':
      return resources.translate('sighting.edit.errors.photoConflict');
    case 'photoStorageUnavailable':
      return resources.translate('sighting.edit.errors.photoStorageUnavailable');
    case 'generic':
      return resources.translate('sighting.edit.errors.generic');
    case 'tooManyEdits':
      return resources.translate('sighting.edit.errors.tooManyEdits');
    case 'removed':
      return resources.translate('sighting.edit.errors.removed');
    default:
      return sightingErrorMessage(resources, failure);
  }
}

export function isSightingRemovedFailure(error: unknown): boolean {
  return (
    error instanceof HttpErrorResponse &&
    error.status === 400 &&
    hasCode(error, sightingRemovedCode)
  );
}

export type SightingModerationAction = 'remove' | 'restore';

export type SightingModerationFailure =
  | { readonly kind: 'notFound' }
  | { readonly kind: 'forbidden'; readonly traceId?: string }
  | { readonly kind: 'unexpected'; readonly traceId?: string };

export function sightingModerationFailure(error: unknown): SightingModerationFailure {
  if (error instanceof HttpErrorResponse && error.status === 404) return { kind: 'notFound' };

  const traceId = unexpectedTraceId(error);
  const kind =
    error instanceof HttpErrorResponse && error.status === 403 ? 'forbidden' : 'unexpected';

  return traceId === undefined ? { kind } : { kind, traceId };
}

export function isReportableSightingModerationFailure(
  error: unknown,
  failure: SightingModerationFailure,
): boolean {
  if (failure.kind === 'forbidden') return true;

  return failure.kind === 'unexpected' && !isExpectedClientStatus(error);
}

export function sightingModerationFailureTraceId(
  failure: SightingModerationFailure | null,
): string | null {
  if (failure?.kind === 'forbidden' || failure?.kind === 'unexpected') {
    return failure.traceId ?? null;
  }

  return null;
}

export function sightingModerationErrorMessage(
  resources: ResourceService,
  action: SightingModerationAction,
  failure: SightingModerationFailure,
): string {
  switch (failure.kind) {
    case 'notFound':
      return resources.translate('sighting.errors.notFound');
    case 'forbidden':
      return action === 'remove'
        ? resources.translate('sighting.remove.errors.forbidden')
        : resources.translate('sighting.restore.errors.forbidden');
    default:
      return action === 'remove'
        ? resources.translate('sighting.remove.errors.generic')
        : resources.translate('sighting.restore.errors.generic');
  }
}

function genericFailure(error: unknown): SightingFailure {
  const traceId = unexpectedTraceId(error);

  return traceId === undefined ? { kind: 'generic' } : { kind: 'generic', traceId };
}

function withOptionalTraceId<K extends 'forbidden' | 'photoConflict'>(
  kind: K,
  traceId: string | undefined,
): { readonly kind: K; readonly traceId?: string } {
  return traceId === undefined ? { kind } : { kind, traceId };
}

function photoStorageFailure(error: HttpErrorResponse): SightingFailure {
  const traceId = unexpectedTraceId(error);

  return traceId === undefined
    ? { kind: 'photoStorageUnavailable' }
    : { kind: 'photoStorageUnavailable', traceId };
}

function photoProblemOf(error: HttpErrorResponse): PhotoProblem | null {
  for (const code of problemErrorCodes(error)) {
    const problem = photoProblemCodes.get(code.toLowerCase());

    if (problem !== undefined) return problem;
  }

  return null;
}

function hasCode(error: HttpErrorResponse, lowercaseCode: string): boolean {
  return problemErrorCodes(error).some((code) => code.toLowerCase() === lowercaseCode);
}

function secondsOf(value: unknown): number | null {
  if (typeof value !== 'number' || !Number.isFinite(value) || value <= 0) return null;

  return Math.ceil(value);
}
