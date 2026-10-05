import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { ReadonlyFieldState } from '@angular/forms/signals';
import { FormSubmitState } from '../form-field/form-submit-state';

export type FieldErrorMessages = Readonly<Record<string, string>>;

@Component({
  selector: 'app-field-errors',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './field-errors.html',
})
export class FieldErrors {
  private readonly submitState = inject(FormSubmitState, { optional: true });

  readonly field = input.required<() => ReadonlyFieldState<unknown>>();
  readonly messages = input.required<FieldErrorMessages>();

  protected readonly message = computed(() => {
    const state = this.field()();

    const shown = this.submitState ? this.submitState.submitted() : state.touched();

    if (!shown || !state.invalid()) return null;

    const messages = this.messages();
    const error = state.errors().find((candidate) => Object.hasOwn(messages, candidate.kind));

    return error ? messages[error.kind] : null;
  });
}
