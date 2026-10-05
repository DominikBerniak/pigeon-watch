import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { form, minLength, required, validate } from '@angular/forms/signals';
import { FormSubmitState } from '../form-field/form-submit-state';
import { FieldErrors } from './field-errors';

@Component({
  imports: [FieldErrors],
  template: `<app-field-errors [field]="profile.name" [messages]="messages" />`,
})
class FieldErrorsHost {
  readonly serverError = signal<string | null>(null);
  readonly model = signal({ name: '' });
  readonly profile = form(this.model, (path) => {
    required(path.name);
    minLength(path.name, 3);
    validate(path.name, () => {
      const kind = this.serverError();

      return kind ? { kind } : undefined;
    });
  });
  readonly messages = { required: 'Required', minLength: 'Too short', taken: 'Taken' };
}

describe('FieldErrors', () => {
  async function render() {
    const fixture = TestBed.createComponent(FieldErrorsHost);
    await fixture.whenStable();

    const text = async (): Promise<string> => {
      await fixture.whenStable();

      return (fixture.nativeElement as HTMLElement).textContent?.trim() ?? '';
    };

    return { host: fixture.componentInstance, text };
  }

  it('renders nothing for an untouched invalid field', async () => {
    const { text } = await render();

    expect(await text()).toBe('');
  });

  it('renders the message of the first error of a touched invalid field', async () => {
    const { host, text } = await render();

    host.profile.name().markAsTouched();
    expect(await text()).toBe('Required');

    host.model.set({ name: 'ab' });
    expect(await text()).toBe('Too short');
  });

  it('renders nothing for a touched valid field', async () => {
    const { host, text } = await render();

    host.model.set({ name: 'Jan' });
    host.profile.name().markAsTouched();

    expect(await text()).toBe('');
  });

  it('renders a server-set error through the same map', async () => {
    const { host, text } = await render();

    host.model.set({ name: 'Jan' });
    host.profile.name().markAsTouched();
    host.serverError.set('taken');

    expect(await text()).toBe('Taken');
  });

  describe('inside a form that validates on submit', () => {
    beforeEach(() => {
      TestBed.configureTestingModule({ providers: [FormSubmitState] });
    });

    it('ignores touched until the form is submitted', async () => {
      const { host, text } = await render();
      const submitState = TestBed.inject(FormSubmitState);

      host.profile.name().markAsTouched();
      expect(await text()).toBe('');

      submitState.markSubmitted();
      expect(await text()).toBe('Required');

      host.model.set({ name: 'ab' });
      expect(await text()).toBe('Too short');
    });
  });
});
