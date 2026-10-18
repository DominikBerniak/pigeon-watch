import { HttpErrorResponse } from '@angular/common/http';
import { loginFailure, profileFailure, registerFailure } from './auth-errors';

describe('auth errors', () => {
  describe('profileFailure', () => {
    function badRequest(...codes: string[]): HttpErrorResponse {
      const errors = Object.fromEntries(codes.map((code) => [code, ['x']]));

      return new HttpErrorResponse({ status: 400, error: { status: 400, errors } });
    }

    it('maps DuplicateDisplayName to the display name field', () => {
      expect(profileFailure(badRequest('DuplicateDisplayName'))).toEqual({
        formError: null,
        fieldErrors: { displayName: 'duplicateDisplayName' },
      });
    });

    it.each(['DisplayNameLength', 'DisplayNameInvalidCharacter'])(
      'maps %s to an invalid display name',
      (code) => {
        expect(profileFailure(badRequest(code))).toEqual({
          formError: null,
          fieldErrors: { displayName: 'invalidDisplayName' },
        });
      },
    );

    it('reports an unknown code as an unexpected form error', () => {
      expect(profileFailure(badRequest('ConcurrencyFailure'))).toEqual({
        formError: 'unexpected',
        fieldErrors: {},
      });
    });

    it.each([
      [429, 'tooManyRequests'],
      [503, 'warmupFailed'],
      [0, 'warmupFailed'],
      [500, 'unexpected'],
    ])('maps status %i to %s', (status, formError) => {
      expect(profileFailure(new HttpErrorResponse({ status }))).toEqual({
        formError,
        fieldErrors: {},
      });
    });

    it('reports a non-HTTP error as unexpected', () => {
      expect(profileFailure(new Error('boom'))).toEqual({
        formError: 'unexpected',
        fieldErrors: {},
      });
    });
  });

  describe('registerFailure', () => {
    function badRequest(...codes: string[]): HttpErrorResponse {
      const errors = Object.fromEntries(codes.map((code) => [code, ['x']]));

      return new HttpErrorResponse({ status: 400, error: { status: 400, errors } });
    }

    it('maps RegistrationFailed to the generic form error and no field error', () => {
      expect(registerFailure(badRequest('RegistrationFailed'))).toEqual({
        formError: 'registrationFailed',
        fieldErrors: {},
      });
    });

    it('maps DuplicateDisplayName to the display name field', () => {
      expect(registerFailure(badRequest('DuplicateDisplayName'))).toEqual({
        formError: null,
        fieldErrors: { displayName: 'duplicateDisplayName' },
      });
    });

    it.each([
      [0, 'registrationUnconfirmed'],
      [503, 'registrationUnconfirmed'],
      [429, 'tooManyRequests'],
      [500, 'unexpected'],
    ])('maps status %i to %s', (status, formError) => {
      expect(registerFailure(new HttpErrorResponse({ status }))).toEqual({
        formError,
        fieldErrors: {},
      });
    });

    it('reports a non-HTTP error as unexpected', () => {
      expect(registerFailure(new Error('boom'))).toEqual({
        formError: 'unexpected',
        fieldErrors: {},
      });
    });
  });

  describe('loginFailure', () => {
    it.each([
      [0, 'warmupFailed'],
      [503, 'warmupFailed'],
      [429, 'tooManyRequests'],
    ])('maps status %i to %s', (status, formError) => {
      expect(loginFailure(new HttpErrorResponse({ status }))).toBe(formError);
    });
  });
});
