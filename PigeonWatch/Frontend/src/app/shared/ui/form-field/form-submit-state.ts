import { Injectable, inject, signal } from '@angular/core';
import { AbstractControl, FormGroupDirective, NgForm } from '@angular/forms';
import { Field } from '@angular/forms/signals';
import { ErrorStateMatcher } from '@angular/material/core';

@Injectable()
export class FormSubmitState {
  private readonly submittedState = signal(false);

  readonly submitted = this.submittedState.asReadonly();

  markSubmitted(): void {
    this.submittedState.set(true);
  }
}

@Injectable()
export class SubmitErrorStateMatcher implements ErrorStateMatcher {
  private readonly submitState = inject(FormSubmitState);

  isErrorState(control: AbstractControl | null, form: FormGroupDirective | NgForm | null): boolean {
    return !!control?.invalid && (this.submitState.submitted() || !!form?.submitted);
  }

  isSignalErrorState(field: Field<unknown> | null): boolean {
    if (!field) return false;

    return this.submitState.submitted() && field().invalid();
  }
}
