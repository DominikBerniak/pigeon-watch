import { Provider } from '@angular/core';
import { ErrorStateMatcher } from '@angular/material/core';
import {
  MAT_FORM_FIELD_DEFAULT_OPTIONS,
  MatFormFieldDefaultOptions,
} from '@angular/material/form-field';
import { FormSubmitState, SubmitErrorStateMatcher } from './form-submit-state';

export const formFieldDefaults: MatFormFieldDefaultOptions = {
  appearance: 'outline',
  subscriptSizing: 'dynamic',
};

export function provideFormFieldDefaults(): Provider[] {
  return [
    FormSubmitState,
    { provide: ErrorStateMatcher, useClass: SubmitErrorStateMatcher },
    { provide: MAT_FORM_FIELD_DEFAULT_OPTIONS, useValue: formFieldDefaults },
  ];
}
