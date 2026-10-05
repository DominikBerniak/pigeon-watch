import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { form, required } from '@angular/forms/signals';
import { FormSubmitState, SubmitErrorStateMatcher } from './form-submit-state';

describe('SubmitErrorStateMatcher', () => {
  function setup() {
    TestBed.configureTestingModule({ providers: [FormSubmitState, SubmitErrorStateMatcher] });
    const model = signal({ name: '' });
    const profile = TestBed.runInInjectionContext(() => form(model, (path) => required(path.name)));

    return {
      model,
      profile,
      matcher: TestBed.inject(SubmitErrorStateMatcher),
      submitState: TestBed.inject(FormSubmitState),
    };
  }

  it('reports no error for a touched invalid field before submit', () => {
    const { profile, matcher } = setup();

    profile.name().markAsTouched();

    expect(matcher.isSignalErrorState(profile.name)).toBe(false);
  });

  it('reports invalid fields after submit and follows later edits', () => {
    const { model, profile, matcher, submitState } = setup();

    submitState.markSubmitted();
    expect(matcher.isSignalErrorState(profile.name)).toBe(true);

    model.set({ name: 'Jan' });
    expect(matcher.isSignalErrorState(profile.name)).toBe(false);
  });

  it('reports no error without a field', () => {
    const { matcher, submitState } = setup();

    submitState.markSubmitted();

    expect(matcher.isSignalErrorState(null)).toBe(false);
  });
});
