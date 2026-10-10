import { Signal } from '@angular/core';
import {
  FieldValidator,
  PathKind,
  SchemaPath,
  SchemaPathRules,
  ValidationError,
} from '@angular/forms/signals';
import { DisplayNameRules, PasswordRules } from '../../core/configuration/configuration.service';

export interface ServerFieldError {
  kind: string;
  value: string;
}

export type ServerFieldErrors<TField extends string> = Partial<Record<TField, ServerFieldError>>;

export const fallbackPasswordRules: PasswordRules = {
  minLength: 8,
  requireDigit: true,
  requireLowercase: true,
  requireUppercase: true,
  requireNonAlphanumeric: true,
};

export const fallbackDisplayNameRules: DisplayNameRules = { minLength: 3, maxLength: 30 };

export const forbiddenDisplayNameCharacter = '@';

export function displayNameCharacterValidator(): FieldValidator<string> {
  return ({ value }) =>
    value().includes(forbiddenDisplayNameCharacter) ? { kind: 'invalidCharacter' } : undefined;
}

export function passwordClassValidator(rules: () => PasswordRules): FieldValidator<string> {
  return ({ value }) => passwordClassErrors(value(), rules());
}

export function confirmPasswordValidator(
  password: SchemaPath<string, SchemaPathRules.Supported, PathKind.Child>,
): FieldValidator<string> {
  return ({ value, valueOf }) => (value() !== valueOf(password) ? { kind: 'mismatch' } : undefined);
}

export function serverErrorFor<TField extends string>(
  errors: Signal<ServerFieldErrors<TField>>,
  field: TField,
): FieldValidator<string> {
  return ({ value }) => {
    const error = errors()[field];

    return error && error.value === value() ? { kind: error.kind } : undefined;
  };
}

export function passwordClassErrors(value: string, rules: PasswordRules): ValidationError[] {
  const errors: ValidationError[] = [];

  if (rules.requireDigit && !/[0-9]/.test(value)) errors.push({ kind: 'requireDigit' });

  if (rules.requireLowercase && !/[a-z]/.test(value)) errors.push({ kind: 'requireLowercase' });

  if (rules.requireUppercase && !/[A-Z]/.test(value)) errors.push({ kind: 'requireUppercase' });

  if (rules.requireNonAlphanumeric && !/[^a-zA-Z0-9]/.test(value)) {
    errors.push({ kind: 'requireNonAlphanumeric' });
  }

  return errors;
}
