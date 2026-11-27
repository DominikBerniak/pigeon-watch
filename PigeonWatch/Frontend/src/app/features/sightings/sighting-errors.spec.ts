import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ResourceService } from '../../core/resources/resource.service';
import {
  isReportableSightingFailure,
  isReportableSightingModerationFailure,
  isReportableSightingUpdateFailure,
  isSightingRemovedFailure,
  isStaleEditFailure,
  sightingErrorMessage,
  sightingFailure,
  sightingModerationErrorMessage,
  sightingModerationFailure,
  sightingModerationFailureTraceId,
  sightingUpdateErrorMessage,
  sightingUpdateFailure,
  sightingUpdateFailureTraceId,
} from './sighting-errors';

describe('sighting errors', () => {
  function badRequest(
    codes: string[],
    extensions: Record<string, unknown> = {},
  ): HttpErrorResponse {
    const errors = Object.fromEntries(codes.map((code) => [code, ['x']]));

    return new HttpErrorResponse({ status: 400, error: { status: 400, errors, ...extensions } });
  }

  describe('sightingFailure', () => {
    it('reads the remaining cooldown seconds', () => {
      expect(sightingFailure(badRequest(['CooldownActive'], { retryAfterSeconds: 42 }))).toEqual({
        kind: 'cooldown',
        retryAfterSeconds: 42,
      });
    });

    it('rounds a fractional remainder up', () => {
      expect(sightingFailure(badRequest(['CooldownActive'], { retryAfterSeconds: 1.2 }))).toEqual({
        kind: 'cooldown',
        retryAfterSeconds: 2,
      });
    });

    it.each([undefined, 0, -3, '42'])(
      'falls back to generic when the cooldown seconds are %j',
      (retryAfterSeconds) => {
        expect(sightingFailure(badRequest(['CooldownActive'], { retryAfterSeconds }))).toEqual({
          kind: 'generic',
        });
      },
    );

    it.each(['InvalidInitialStatus', 'InvalidCriticality', 'InvalidLocation', 'Description'])(
      'maps %s to generic',
      (code) => {
        expect(sightingFailure(badRequest([code]))).toEqual({ kind: 'generic' });
      },
    );

    it.each([
      ['PhotoTooLarge', 'photoTooLarge'],
      ['PhotoUnsupportedType', 'photoUnsupportedType'],
      ['PhotoUnreadable', 'photoUnreadable'],
    ])('maps a 400 carrying %s to the %s photo problem', (code, kind) => {
      const error = badRequest([code], { traceId: '00-abc-def-01' });
      const failure = sightingFailure(error);

      expect(failure).toEqual({ kind });
      expect(isReportableSightingFailure(error, failure)).toBe(false);
    });

    it('maps a 503 carrying PhotoStorageUnavailable to the storage outage and reports it', () => {
      const error = new HttpErrorResponse({
        status: 503,
        error: { status: 503, traceId: '00-abc-def-01', errors: { PhotoStorageUnavailable: ['x'] } },
      });
      const failure = sightingFailure(error);

      expect(failure).toEqual({ kind: 'photoStorageUnavailable', traceId: '00-abc-def-01' });
      expect(isReportableSightingFailure(error, failure)).toBe(true);
    });

    it('keeps a 400 carrying PhotoStorageUnavailable generic', () => {
      expect(sightingFailure(badRequest(['PhotoStorageUnavailable']))).toEqual({ kind: 'generic' });
    });

    it('maps 404 to not found', () => {
      expect(sightingFailure(new HttpErrorResponse({ status: 404 }))).toEqual({
        kind: 'notFound',
      });
    });

    it.each([0, 429, 500, 503])('maps status %i to generic', (status) => {
      expect(sightingFailure(new HttpErrorResponse({ status }))).toEqual({ kind: 'generic' });
    });

    it('maps a non-HTTP error to generic', () => {
      expect(sightingFailure(new Error('boom'))).toEqual({ kind: 'generic' });
    });
  });

  describe('sightingErrorMessage', () => {
    function resources(): ResourceService {
      TestBed.configureTestingModule({
        providers: [provideHttpClient(), provideHttpClientTesting()],
      });

      return TestBed.inject(ResourceService);
    }

    it('puts the seconds into the cooldown message', () => {
      expect(sightingErrorMessage(resources(), { kind: 'cooldown', retryAfterSeconds: 17 })).toBe(
        'You can report another sighting in 17 seconds.',
      );
    });

    it('renders the not found and generic messages', () => {
      const labels = resources();

      expect(sightingErrorMessage(labels, { kind: 'notFound' })).toBe(
        "This sighting doesn't exist or is no longer available.",
      );
      expect(sightingErrorMessage(labels, { kind: 'generic' })).toBe(
        "We couldn't reach the sightings right now. Try again.",
      );
    });

    it('renders the photo problem and storage outage messages from their labels', () => {
      const labels = resources();

      expect(sightingErrorMessage(labels, { kind: 'photoTooLarge' })).toBe(
        labels.translate('sighting.form.photoTooLarge'),
      );
      expect(sightingErrorMessage(labels, { kind: 'photoStorageUnavailable' })).toBe(
        labels.translate('sighting.form.photoStorageUnavailable'),
      );
      expect(labels.translate('sighting.form.photoStorageUnavailable')).toContain('remove the photo');
    });
  });

  describe('sightingUpdateFailure', () => {
    it('maps a 403 to a reported forbidden failure with its reference', () => {
      const error = new HttpErrorResponse({ status: 403, error: { traceId: '00-abc-def-01' } });
      const failure = sightingUpdateFailure(error);

      expect(failure).toEqual({ kind: 'forbidden', traceId: '00-abc-def-01' });
      expect(isReportableSightingUpdateFailure(error, failure)).toBe(true);
      expect(isStaleEditFailure(failure)).toBe(false);
      expect(sightingUpdateFailureTraceId(failure)).toBe('00-abc-def-01');
    });

    it('maps a 404 to a silent not found failure', () => {
      const error = new HttpErrorResponse({ status: 404 });
      const failure = sightingUpdateFailure(error);

      expect(failure).toEqual({ kind: 'notFound' });
      expect(isReportableSightingUpdateFailure(error, failure)).toBe(false);
    });

    it.each([
      ['DescriptionLocked', 'descriptionLocked'],
      ['InvalidStatusTransition', 'invalidStatusTransition'],
    ])('maps a 400 carrying %s to the silent stale-form failure %s', (code, kind) => {
      const error = badRequest([code], { traceId: '00-abc-def-01' });
      const failure = sightingUpdateFailure(error);

      expect(failure).toEqual({ kind });
      expect(isReportableSightingUpdateFailure(error, failure)).toBe(false);
      expect(isStaleEditFailure(failure)).toBe(true);
      expect(sightingUpdateFailureTraceId(failure)).toBeNull();
    });

    it('maps a 400 carrying PhotoConflict to a reported client bug with its reference', () => {
      const error = badRequest(['PhotoConflict'], { traceId: '00-abc-def-01' });
      const failure = sightingUpdateFailure(error);

      expect(failure).toEqual({ kind: 'photoConflict', traceId: '00-abc-def-01' });
      expect(isReportableSightingUpdateFailure(error, failure)).toBe(true);
      expect(isStaleEditFailure(failure)).toBe(false);
      expect(sightingUpdateFailureTraceId(failure)).toBe('00-abc-def-01');
    });

    it('maps a PhotoConflict without a reference to photoConflict alone', () => {
      expect(sightingUpdateFailure(badRequest(['PhotoConflict']))).toEqual({
        kind: 'photoConflict',
      });
    });

    it.each([
      ['PhotoTooLarge', 'photoTooLarge'],
      ['PhotoUnsupportedType', 'photoUnsupportedType'],
      ['PhotoUnreadable', 'photoUnreadable'],
    ])('reuses the %s photo problem as %s without a report', (code, kind) => {
      const error = badRequest([code]);
      const failure = sightingUpdateFailure(error);

      expect(failure).toEqual({ kind });
      expect(isReportableSightingUpdateFailure(error, failure)).toBe(false);
    });

    it('reuses the reported photo storage outage', () => {
      const error = new HttpErrorResponse({
        status: 503,
        error: { status: 503, traceId: '00-abc-def-01', errors: { PhotoStorageUnavailable: ['x'] } },
      });
      const failure = sightingUpdateFailure(error);

      expect(failure).toEqual({ kind: 'photoStorageUnavailable', traceId: '00-abc-def-01' });
      expect(isReportableSightingUpdateFailure(error, failure)).toBe(true);
      expect(sightingUpdateFailureTraceId(failure)).toBe('00-abc-def-01');
    });

    it.each(['InvalidStatus', 'InvalidCriticality', 'DescriptionRequired', 'Description'])(
      'keeps a 400 carrying %s generic and silent',
      (code) => {
        const error = badRequest([code]);
        const failure = sightingUpdateFailure(error);

        expect(failure).toEqual({ kind: 'generic' });
        expect(isReportableSightingUpdateFailure(error, failure)).toBe(false);
      },
    );

    it('maps a 400 carrying SightingRemoved to the silent removed failure without a reference', () => {
      const error = new HttpErrorResponse({
        status: 400,
        error: { status: 400, traceId: '00-abc-def-01', errors: { SightingRemoved: ['x'] } },
      });
      const failure = sightingUpdateFailure(error);

      expect(failure).toEqual({ kind: 'removed' });
      expect(isReportableSightingUpdateFailure(error, failure)).toBe(false);
      expect(isStaleEditFailure(failure)).toBe(false);
      expect(sightingUpdateFailureTraceId(failure)).toBeNull();
      expect(isSightingRemovedFailure(error)).toBe(true);
    });

    it('does not treat SightingRemoved on another status as a removed sighting', () => {
      const error = new HttpErrorResponse({
        status: 500,
        error: { status: 500, errors: { SightingRemoved: ['x'] } },
      });

      expect(isSightingRemovedFailure(error)).toBe(false);
      expect(sightingUpdateFailure(error).kind).toBe('generic');
    });

    it('maps a 429 to the silent too many edits failure without a reference', () => {
      const error = new HttpErrorResponse({
        status: 429,
        error: { status: 429, title: 'Too Many Requests', traceId: '00-abc-def-01' },
      });
      const failure = sightingUpdateFailure(error);

      expect(failure).toEqual({ kind: 'tooManyEdits' });
      expect(isReportableSightingUpdateFailure(error, failure)).toBe(false);
      expect(isStaleEditFailure(failure)).toBe(false);
      expect(sightingUpdateFailureTraceId(failure)).toBeNull();
    });

    it('reports a 500 as generic with its reference', () => {
      const error = new HttpErrorResponse({ status: 500, error: { traceId: '00-abc-def-01' } });
      const failure = sightingUpdateFailure(error);

      expect(failure).toEqual({ kind: 'generic', traceId: '00-abc-def-01' });
      expect(isReportableSightingUpdateFailure(error, failure)).toBe(true);
      expect(sightingUpdateFailureTraceId(failure)).toBe('00-abc-def-01');
    });

    it('reports a non-HTTP error as generic', () => {
      const error = new Error('boom');
      const failure = sightingUpdateFailure(error);

      expect(failure).toEqual({ kind: 'generic' });
      expect(isReportableSightingUpdateFailure(error, failure)).toBe(true);
    });
  });

  describe('sightingUpdateErrorMessage', () => {
    function resources(): ResourceService {
      TestBed.configureTestingModule({
        providers: [provideHttpClient(), provideHttpClientTesting()],
      });

      return TestBed.inject(ResourceService);
    }

    it('renders an edit message for each update failure', () => {
      const labels = resources();

      expect(sightingUpdateErrorMessage(labels, { kind: 'forbidden' })).toBe(
        labels.translate('sighting.edit.errors.forbidden'),
      );
      expect(sightingUpdateErrorMessage(labels, { kind: 'descriptionLocked' })).toBe(
        labels.translate('sighting.edit.errors.descriptionLocked'),
      );
      expect(sightingUpdateErrorMessage(labels, { kind: 'invalidStatusTransition' })).toBe(
        labels.translate('sighting.edit.errors.invalidStatusTransition'),
      );
      expect(sightingUpdateErrorMessage(labels, { kind: 'photoConflict' })).toBe(
        labels.translate('sighting.edit.errors.photoConflict'),
      );
      expect(sightingUpdateErrorMessage(labels, { kind: 'photoStorageUnavailable' })).toBe(
        labels.translate('sighting.edit.errors.photoStorageUnavailable'),
      );
      expect(sightingUpdateErrorMessage(labels, { kind: 'generic' })).toBe(
        labels.translate('sighting.edit.errors.generic'),
      );
      expect(sightingUpdateErrorMessage(labels, { kind: 'tooManyEdits' })).toBe(
        "You've saved a lot of changes in a short time. Wait a minute and try again.",
      );
    });

    it('reuses the not found and photo problem messages', () => {
      const labels = resources();

      expect(sightingUpdateErrorMessage(labels, { kind: 'notFound' })).toBe(
        labels.translate('sighting.errors.notFound'),
      );
      expect(sightingUpdateErrorMessage(labels, { kind: 'photoUnreadable' })).toBe(
        labels.translate('sighting.form.photoUnreadable'),
      );
    });

    it('renders the removed message', () => {
      const labels = resources();

      expect(sightingUpdateErrorMessage(labels, { kind: 'removed' })).toBe(
        "This sighting was removed by an admin and can't be edited.",
      );
    });

    it('has a distinct label behind every edit error key', () => {
      const labels = resources();
      const keys = [
        'sighting.edit.errors.forbidden',
        'sighting.edit.errors.descriptionLocked',
        'sighting.edit.errors.invalidStatusTransition',
        'sighting.edit.errors.photoConflict',
        'sighting.edit.errors.photoStorageUnavailable',
        'sighting.edit.errors.generic',
        'sighting.edit.errors.tooManyEdits',
      ];
      const texts = keys.map((key) => labels.translate(key));

      expect(texts.filter((text, index) => text === keys[index])).toEqual([]);
      expect(new Set(texts).size).toBe(keys.length);
    });
  });

  describe('sightingModerationFailure', () => {
    const traceId = '00-abc-def-01';

    it('maps a 403 to a reported forbidden failure with its reference', () => {
      const error = new HttpErrorResponse({ status: 403, error: { traceId } });
      const failure = sightingModerationFailure(error);

      expect(failure).toEqual({ kind: 'forbidden', traceId });
      expect(isReportableSightingModerationFailure(error, failure)).toBe(true);
      expect(sightingModerationFailureTraceId(failure)).toBe(traceId);
    });

    it('maps a 404 to a silent not found failure without a reference', () => {
      const error = new HttpErrorResponse({ status: 404, error: { traceId } });
      const failure = sightingModerationFailure(error);

      expect(failure).toEqual({ kind: 'notFound' });
      expect(isReportableSightingModerationFailure(error, failure)).toBe(false);
      expect(sightingModerationFailureTraceId(failure)).toBeNull();
    });

    it('reports a 500 as unexpected with its reference', () => {
      const error = new HttpErrorResponse({ status: 500, error: { traceId } });
      const failure = sightingModerationFailure(error);

      expect(failure).toEqual({ kind: 'unexpected', traceId });
      expect(isReportableSightingModerationFailure(error, failure)).toBe(true);
      expect(sightingModerationFailureTraceId(failure)).toBe(traceId);
    });

    it.each([400, 401, 429])('keeps a %i unexpected but silent and without a reference', (status) => {
      const error = new HttpErrorResponse({ status, error: { traceId } });
      const failure = sightingModerationFailure(error);

      expect(failure).toEqual({ kind: 'unexpected' });
      expect(isReportableSightingModerationFailure(error, failure)).toBe(false);
      expect(sightingModerationFailureTraceId(failure)).toBeNull();
    });

    it('reports a non-HTTP error as unexpected', () => {
      const error = new Error('boom');
      const failure = sightingModerationFailure(error);

      expect(failure).toEqual({ kind: 'unexpected' });
      expect(isReportableSightingModerationFailure(error, failure)).toBe(true);
    });

    it('renders a message per action and failure', () => {
      TestBed.configureTestingModule({
        providers: [provideHttpClient(), provideHttpClientTesting()],
      });
      const labels = TestBed.inject(ResourceService);

      expect(sightingModerationErrorMessage(labels, 'remove', { kind: 'notFound' })).toBe(
        labels.translate('sighting.errors.notFound'),
      );
      expect(sightingModerationErrorMessage(labels, 'remove', { kind: 'forbidden' })).toBe(
        'Only an admin can remove a sighting.',
      );
      expect(sightingModerationErrorMessage(labels, 'remove', { kind: 'unexpected' })).toBe(
        labels.translate('sighting.remove.errors.generic'),
      );
      expect(sightingModerationErrorMessage(labels, 'restore', { kind: 'forbidden' })).toBe(
        'Only an admin can restore a sighting.',
      );
      expect(sightingModerationErrorMessage(labels, 'restore', { kind: 'unexpected' })).toBe(
        labels.translate('sighting.restore.errors.generic'),
      );
      expect(labels.translate('sighting.restore.errors.generic')).not.toBe(
        labels.translate('sighting.remove.errors.generic'),
      );
    });
  });
});
