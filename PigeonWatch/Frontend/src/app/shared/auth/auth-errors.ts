import { HttpErrorResponse } from '@angular/common/http';
import { ResourceService } from '../../core/resources/resource.service';
import {
  isExpectedClientStatus,
  problemDetail,
  problemErrorCodes,
  unexpectedTraceId,
} from '../http/problem-details';

export type AuthFormError =
  | 'invalidCredentials'
  | 'lockedOut'
  | 'tooManyRequests'
  | 'warmupFailed'
  | 'registrationFailed'
  | 'registrationUnconfirmed'
  | 'unexpected';

export type RegisterField = 'email' | 'displayName' | 'password';

export interface LoginFailure {
  formError: AuthFormError;
  traceId?: string;
}

export interface RegisterFailure {
  formError: AuthFormError | null;
  traceId?: string;
  fieldErrors: Partial<Record<RegisterField, string>>;
}

export type ProfileField = 'displayName';

export interface ProfileFailure {
  formError: AuthFormError | null;
  traceId?: string;
  fieldErrors: Partial<Record<ProfileField, string>>;
}

export type PasswordChangeField = 'currentPassword' | 'newPassword';

export interface PasswordChangeFailure {
  formError: AuthFormError | null;
  traceId?: string;
  fieldErrors: Partial<Record<PasswordChangeField, string>>;
}

export function loginFailure(error: unknown): AuthFormError {
  if (!(error instanceof HttpErrorResponse)) return 'unexpected';

  if (error.status !== 401) return transportFailure(error);

  return problemDetail(error) === 'LockedOut' ? 'lockedOut' : 'invalidCredentials';
}

export function loginFailureResult(error: unknown): LoginFailure {
  const formError = loginFailure(error);

  return { formError, ...traceOf(error, formError) };
}

export function isReportableAuthFailure(error: unknown, formError: AuthFormError | null): boolean {
  return formError === 'unexpected' && !isExpectedClientStatus(error);
}

export function registerFailure(error: unknown): RegisterFailure {
  if (!(error instanceof HttpErrorResponse)) return { formError: 'unexpected', fieldErrors: {} };

  const codes = problemErrorCodes(error);

  if (error.status !== 400 || codes.length === 0) {
    const formError = isUnanswered(error) ? 'registrationUnconfirmed' : transportFailure(error);

    return { formError, ...traceOf(error, formError), fieldErrors: {} };
  }

  const failure: RegisterFailure = { formError: null, fieldErrors: {} };

  for (const code of codes) applyErrorCode(failure, code);

  return { ...failure, ...traceOf(error, failure.formError) };
}

export function profileFailure(error: unknown): ProfileFailure {
  if (!(error instanceof HttpErrorResponse)) return { formError: 'unexpected', fieldErrors: {} };

  const codes = problemErrorCodes(error);

  if (error.status !== 400 || codes.length === 0) {
    const formError = transportFailure(error);

    return { formError, ...traceOf(error, formError), fieldErrors: {} };
  }

  const failure: ProfileFailure = { formError: null, fieldErrors: {} };

  for (const code of codes) applyProfileCode(failure, code);

  return { ...failure, ...traceOf(error, failure.formError) };
}

export function passwordChangeFailure(error: unknown): PasswordChangeFailure {
  if (!(error instanceof HttpErrorResponse)) return { formError: 'unexpected', fieldErrors: {} };

  const codes = problemErrorCodes(error);

  if (error.status !== 400 || codes.length === 0) {
    const formError = transportFailure(error);

    return { formError, ...traceOf(error, formError), fieldErrors: {} };
  }

  const failure: PasswordChangeFailure = { formError: null, fieldErrors: {} };

  for (const code of codes) applyPasswordChangeCode(failure, code);

  return { ...failure, ...traceOf(error, failure.formError) };
}

export function authErrorMessage(resources: ResourceService, error: AuthFormError): string {
  if (error === 'invalidCredentials') return resources.translate('auth.errors.invalidCredentials');

  if (error === 'lockedOut') return resources.translate('auth.errors.lockedOut');

  if (error === 'tooManyRequests') return resources.translate('auth.errors.tooManyRequests');

  if (error === 'warmupFailed') return resources.translate('auth.errors.warmupFailed');

  if (error === 'registrationFailed') return resources.translate('auth.errors.registrationFailed');

  if (error === 'registrationUnconfirmed') {
    return resources.translate('auth.errors.registrationUnconfirmed');
  }

  return resources.translate('common.unexpectedError');
}

function traceOf(error: unknown, formError: AuthFormError | null): { traceId?: string } {
  if (formError !== 'unexpected') return {};

  const traceId = unexpectedTraceId(error);

  return traceId === undefined ? {} : { traceId };
}

function isUnanswered(error: HttpErrorResponse): boolean {
  return error.status === 503 || error.status === 0;
}

function transportFailure(error: HttpErrorResponse): AuthFormError {
  if (error.status === 429) return 'tooManyRequests';

  if (isUnanswered(error)) return 'warmupFailed';

  return 'unexpected';
}

function applyErrorCode(failure: RegisterFailure, code: string): void {
  const normalized = code.toLowerCase();
  const fields = failure.fieldErrors;

  if (normalized === 'registrationfailed') failure.formError = 'registrationFailed';
  else if (normalized.startsWith('displayname')) fields.displayName ??= 'invalidDisplayName';
  else if (normalized.startsWith('password')) fields.password ??= 'invalidPassword';
  else if (normalized === 'invalidemail') fields.email ??= 'invalidEmail';
  else failure.formError ??= 'unexpected';
}

function applyProfileCode(failure: ProfileFailure, code: string): void {
  const normalized = code.toLowerCase();
  const fields = failure.fieldErrors;

  if (normalized === 'duplicatedisplayname') fields.displayName = 'duplicateDisplayName';
  else if (normalized.startsWith('displayname')) fields.displayName ??= 'invalidDisplayName';
  else failure.formError ??= 'unexpected';
}

function applyPasswordChangeCode(failure: PasswordChangeFailure, code: string): void {
  const normalized = code.toLowerCase();
  const fields = failure.fieldErrors;

  if (normalized === 'passwordmismatch') fields.currentPassword = 'wrongCurrentPassword';
  else if (normalized.startsWith('password')) fields.newPassword ??= 'invalidPassword';
  else failure.formError ??= 'unexpected';
}
