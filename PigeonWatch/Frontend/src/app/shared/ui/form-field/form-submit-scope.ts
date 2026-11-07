import { Directive } from '@angular/core';
import { ErrorStateMatcher } from '@angular/material/core';
import { FormSubmitState, SubmitErrorStateMatcher } from './form-submit-state';

@Directive({
  selector: 'form[appFormSubmitScope]',
  exportAs: 'formSubmitScope',
  providers: [FormSubmitState, { provide: ErrorStateMatcher, useClass: SubmitErrorStateMatcher }],
})
export class FormSubmitScope {}
