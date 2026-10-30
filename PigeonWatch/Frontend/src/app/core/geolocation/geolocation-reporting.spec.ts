import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { ErrorReportingService } from '../error-reporting/error-reporting.service';
import { GeolocationService } from './geolocation.service';

type GetCurrentPosition = Geolocation['getCurrentPosition'];

describe('GeolocationService exception reporting', () => {
  let report: ReturnType<typeof vi.fn>;

  function stubGeolocation(getCurrentPosition: GetCurrentPosition): void {
    Object.defineProperty(window.navigator, 'geolocation', {
      configurable: true,
      value: { getCurrentPosition },
    });
  }

  beforeEach(() => {
    report = vi.fn();
    TestBed.configureTestingModule({
      providers: [{ provide: ErrorReportingService, useValue: { report } }],
    });
  });

  afterEach(() => {
    Reflect.deleteProperty(window.navigator, 'geolocation');
  });

  it('reports a thrown exception as a fallback and still answers unavailable', async () => {
    const failure = new DOMException('blocked', 'SecurityError');

    stubGeolocation(() => {
      throw failure;
    });

    const outcome = await firstValueFrom(TestBed.inject(GeolocationService).currentPosition(true));

    expect(outcome).toEqual({ status: 'unavailable' });
    expect(report).toHaveBeenCalledExactlyOnceWith('fallback', 'geolocation.exception', failure);
  });

  it('does not report a denied permission, a timeout or a position', async () => {
    const service = TestBed.inject(GeolocationService);

    stubGeolocation((_success, error) => error?.({ code: 1 } as GeolocationPositionError));
    await firstValueFrom(service.currentPosition(true));
    stubGeolocation((_success, error) => error?.({ code: 3 } as GeolocationPositionError));
    await firstValueFrom(service.currentPosition(true));
    stubGeolocation((success) =>
      success({ coords: { latitude: 1, longitude: 2 } } as GeolocationPosition),
    );
    await firstValueFrom(service.currentPosition(true));

    expect(report).not.toHaveBeenCalled();
  });
});
