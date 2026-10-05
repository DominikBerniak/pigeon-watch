import { HttpErrorResponse } from '@angular/common/http';
import { ResourceService } from '../../core/resources/resource.service';

export type AuthFormError =
  | 'invalidCredentials'
  | 'lockedOut'
  | 'tooManyRequests'
  | 'warmupFailed'
  | 'registrationFailed'
  | 'unexpected';

export type RegisterField = 'email' | 'displayName' | 'password';

export interface RegisterFailure {
  formError: AuthFormError | null;
  fieldErrors: Partial<Record<RegisterField, string>>;
}

export function loginFailure(error: unknown): AuthFormError {
  if (!(error instanceof HttpErrorResponse)) return 'unexpected';

  if (error.status !== 401) return transportFailure(error);

  return problemDetail(error) === 'LockedOut' ? 'lockedOut' : 'invalidCredentials';
}

export function registerFailure(error: unknown): RegisterFailure {
  if (!(error instanceof HttpErrorResponse)) return { formError: 'unexpected', fieldErrors: {} };

  const codes = errorCodes(error);

  if (error.status !== 400 || codes.length === 0) {
    return { formError: transportFailure(error), fieldErrors: {} };
  }

  const failure: RegisterFailure = { formError: null, fieldErrors: {} };

  for (const code of codes) applyErrorCode(failure, code);

  return failure;
}

export function authErrorMessage(resources: ResourceService, error: AuthFormError): string {
  if (error === 'invalidCredentials') return resources.translate('auth.errors.invalidCredentials');

  if (error === 'lockedOut') return resources.translate('auth.errors.lockedOut');

  if (error === 'tooManyRequests') return resources.translate('auth.errors.tooManyRequests');

  if (error === 'warmupFailed') return resources.translate('auth.errors.warmupFailed');

  if (error === 'registrationFailed') return resources.translate('auth.errors.registrationFailed');

  return resources.translate('common.unexpectedError');
}

function transportFailure(error: HttpErrorResponse): AuthFormError {
  if (error.status === 429) return 'tooManyRequests';

  if (error.status === 503 || error.status === 0) return 'warmupFailed';

  return 'unexpected';
}

function applyErrorCode(failure: RegisterFailure, code: string): void {
  const normalized = code.toLowerCase();
  const fields = failure.fieldErrors;

  if (normalized === 'registrationfailed') failure.formError = 'registrationFailed';
  else if (normalized === 'duplicatedisplayname') fields.displayName = 'duplicateDisplayName';
  else if (normalized.startsWith('displayname')) fields.displayName ??= 'invalidDisplayName';
  else if (normalized.startsWith('password')) fields.password ??= 'invalidPassword';
  else if (normalized === 'invalidemail') fields.email ??= 'invalidEmail';
  else failure.formError ??= 'unexpected';
}

function problemDetail(error: HttpErrorResponse): string | null {
  const body: unknown = error.error;

  if (!isRecord(body) || typeof body['detail'] !== 'string') return null;

  return body['detail'];
}

function errorCodes(error: HttpErrorResponse): string[] {
  const body: unknown = error.error;

  if (!isRecord(body) || !isRecord(body['errors'])) return [];

  return Object.keys(body['errors']);
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
