import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { GeolocationService, geolocationTimeoutMs } from './geolocation.service';

type GetCurrentPosition = Geolocation['getCurrentPosition'];

describe('GeolocationService', () => {
  function stubGeolocation(getCurrentPosition: GetCurrentPosition | null): void {
    Object.defineProperty(window.navigator, 'geolocation', {
      configurable: true,
      value: getCurrentPosition ? { getCurrentPosition } : undefined,
    });
  }

  function positionAt(latitude: number, longitude: number): GeolocationPosition {
    return { coords: { latitude, longitude } } as GeolocationPosition;
  }

  function failingWith(code: number): GetCurrentPosition {
    return (_success, error) => error?.({ code } as GeolocationPositionError);
  }

  afterEach(() => {
    Reflect.deleteProperty(window.navigator, 'geolocation');
  });

  it('reports unsupported when the browser has no geolocation', async () => {
    stubGeolocation(null);

    const outcome = await firstValueFrom(TestBed.inject(GeolocationService).currentPosition(true));

    expect(outcome).toEqual({ status: 'unsupported' });
  });

  it('emits the position and asks with a timeout and the requested accuracy', async () => {
    const getCurrentPosition = vi.fn<GetCurrentPosition>((success) =>
      success(positionAt(52.23, 21.01)),
    );
    stubGeolocation(getCurrentPosition);

    const outcome = await firstValueFrom(TestBed.inject(GeolocationService).currentPosition(true));

    expect(outcome).toEqual({ status: 'ok', latitude: 52.23, longitude: 21.01 });
    expect(getCurrentPosition.mock.calls[0][2]).toEqual({
      enableHighAccuracy: true,
      timeout: geolocationTimeoutMs,
    });
    expect(geolocationTimeoutMs).toBe(10000);
  });

  it('passes low accuracy through', async () => {
    const getCurrentPosition = vi.fn<GetCurrentPosition>((success) =>
      success(positionAt(50.06, 19.94)),
    );
    stubGeolocation(getCurrentPosition);

    await firstValueFrom(TestBed.inject(GeolocationService).currentPosition(false));

    expect(getCurrentPosition.mock.calls[0][2]?.enableHighAccuracy).toBe(false);
  });

  it.each([
    [1, 'denied'],
    [2, 'unavailable'],
    [3, 'timeout'],
  ])('maps error code %i to %s', async (code, status) => {
    stubGeolocation(failingWith(code));

    const outcome = await firstValueFrom(TestBed.inject(GeolocationService).currentPosition(false));

    expect(outcome).toEqual({ status });
  });

  it('reports unavailable when the browser throws', async () => {
    stubGeolocation(() => {
      throw new Error('blocked');
    });

    const outcome = await firstValueFrom(TestBed.inject(GeolocationService).currentPosition(true));

    expect(outcome).toEqual({ status: 'unavailable' });
  });
});
