import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { form, validate } from '@angular/forms/signals';
import {
  ServerFieldErrors,
  confirmPasswordValidator,
  displayNameCharacterValidator,
  passwordClassErrors,
  passwordClassValidator,
  fallbackPasswordRules,
  serverErrorFor,
} from './account-validation';

describe('account validation', () => {
  describe('displayNameCharacterValidator', () => {
    function errorsFor(displayName: string): string[] {
      return TestBed.runInInjectionContext(() => {
        const model = signal({ displayName });
        const nameForm = form(model, (path) => {
          validate(path.displayName, displayNameCharacterValidator());
        });

        return nameForm
          .displayName()
          .errors()
          .map((error) => error.kind);
      });
    }

    it('flags a name containing @', () => {
      expect(errorsFor('a@b')).toEqual(['invalidCharacter']);
    });

    it('accepts a name without @', () => {
      expect(errorsFor('Jan K')).toEqual([]);
    });
  });

  describe('passwordClassErrors', () => {
    it('reports every missing class', () => {
      expect(passwordClassErrors('abc', fallbackPasswordRules).map((e) => e.kind)).toEqual([
        'requireDigit',
        'requireUppercase',
        'requireNonAlphanumeric',
      ]);
    });

    it('accepts a password that meets every class', () => {
      expect(passwordClassErrors('Secret1!', fallbackPasswordRules)).toEqual([]);
    });

    it('skips classes the rules do not require', () => {
      const rules = {
        ...fallbackPasswordRules,
        requireDigit: false,
        requireUppercase: false,
        requireNonAlphanumeric: false,
      };

      expect(passwordClassErrors('abc', rules)).toEqual([]);
    });
  });

  describe('passwordClassValidator and confirmPasswordValidator', () => {
    function build(password: string, confirmPassword: string) {
      return TestBed.runInInjectionContext(() => {
        const model = signal({ password, confirmPassword });

        return form(model, (path) => {
          validate(
            path.password,
            passwordClassValidator(() => fallbackPasswordRules),
          );
          validate(path.confirmPassword, confirmPasswordValidator(path.password));
        });
      });
    }

    it('reads the rules lazily', () => {
      expect(
        build('abc', 'abc')
          .password()
          .errors()
          .map((e) => e.kind),
      ).toEqual(['requireDigit', 'requireUppercase', 'requireNonAlphanumeric']);
    });

    it('flags a confirmation that differs from the password', () => {
      expect(
        build('Secret1!', 'Secret2!')
          .confirmPassword()
          .errors()
          .map((e) => e.kind),
      ).toEqual(['mismatch']);
    });

    it('accepts a matching confirmation', () => {
      expect(build('Secret1!', 'Secret1!').confirmPassword().errors()).toEqual([]);
    });
  });

  describe('serverErrorFor', () => {
    function build(errors: ServerFieldErrors<'displayName'>, displayName: string) {
      return TestBed.runInInjectionContext(() => {
        const model = signal({ displayName });
        const state = signal(errors);
        const nameForm = form(model, (path) => {
          validate(path.displayName, serverErrorFor(state, 'displayName'));
        });

        return { model, nameForm };
      });
    }

    it('reports the server error while the value is the submitted one', () => {
      const { nameForm } = build({ displayName: { kind: 'taken', value: 'Jan' } }, 'Jan');

      expect(nameForm.displayName().errors()[0].kind).toBe('taken');
    });

    it('clears the error once the value changes', () => {
      const { model, nameForm } = build({ displayName: { kind: 'taken', value: 'Jan' } }, 'Jan');

      model.set({ displayName: 'Jan2' });

      expect(nameForm.displayName().errors()).toEqual([]);
    });

    it('reports nothing for a field without a server error', () => {
      const { nameForm } = build({}, 'Jan');

      expect(nameForm.displayName().errors()).toEqual([]);
    });
  });
});
