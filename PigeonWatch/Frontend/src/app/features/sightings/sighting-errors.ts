import { HttpErrorResponse } from '@angular/common/http';
import { ResourceService } from '../../core/resources/resource.service';
import {
  isExpectedClientStatus,
  problemErrorCodes,
  problemExtension,
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

export function isPhotoProblem(failure: SightingFailure): failure is { readonly kind: PhotoProblem } {
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

function genericFailure(error: unknown): SightingFailure {
  const traceId = unexpectedTraceId(error);

  return traceId === undefined ? { kind: 'generic' } : { kind: 'generic', traceId };
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
