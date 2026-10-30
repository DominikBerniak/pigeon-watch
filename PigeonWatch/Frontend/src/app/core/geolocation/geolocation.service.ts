import { DOCUMENT } from '@angular/common';
import { Injectable, Injector, inject } from '@angular/core';
import { Observable, of, timeout } from 'rxjs';
import { ErrorReportingService } from '../error-reporting/error-reporting.service';

export type GeolocationFailure = 'denied' | 'unavailable' | 'timeout' | 'unsupported';

export type GeolocationOutcome =
  | { readonly status: 'ok'; readonly latitude: number; readonly longitude: number }
  | { readonly status: GeolocationFailure };

export const geolocationTimeoutMs = 10000;
export const geolocationBackstopMs = geolocationTimeoutMs + 5000;

const permissionDeniedCode = 1;
const timeoutCode = 3;

@Injectable({ providedIn: 'root' })
export class GeolocationService {
  private readonly document = inject(DOCUMENT);
  private readonly injector = inject(Injector);

  currentPosition(highAccuracy: boolean): Observable<GeolocationOutcome> {
    return new Observable<GeolocationOutcome>((subscriber) => {
      const emit = (outcome: GeolocationOutcome): void => {
        subscriber.next(outcome);
        subscriber.complete();
      };
      const geolocation = this.document.defaultView?.navigator?.geolocation;

      if (!geolocation) {
        emit({ status: 'unsupported' });

        return;
      }

      try {
        geolocation.getCurrentPosition(
          (position) =>
            emit({
              status: 'ok',
              latitude: position.coords.latitude,
              longitude: position.coords.longitude,
            }),
          (error) => emit({ status: failureOf(error.code) }),
          { enableHighAccuracy: highAccuracy, timeout: geolocationTimeoutMs },
        );
      } catch (error) {
        this.injector.get(ErrorReportingService).report('fallback', 'geolocation.exception', error);
        emit({ status: 'unavailable' });
      }
    }).pipe(
      timeout({
        first: geolocationBackstopMs,
        with: () => of<GeolocationOutcome>({ status: 'timeout' }),
      }),
    );
  }
}

function failureOf(code: number): GeolocationFailure {
  if (code === permissionDeniedCode) return 'denied';

  return code === timeoutCode ? 'timeout' : 'unavailable';
}
