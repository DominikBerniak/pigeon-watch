import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../environments/environment';
import { TokenStore, refreshTokenStorageKey } from './auth/token-store';
import { ErrorReportingService } from './error-reporting/error-reporting.service';
import { CultureStore, SUPPORTED_CULTURES, cultureStorageKey } from './resources/culture-store';
import { snapshots } from './resources/generated/snapshots';
import { ResourceService } from './resources/resource.service';

const reportUrl = `${environment.apiUrl}/client-errors`;
const tokenValue = 'refresh-secret-value';
const mutableSnapshots = snapshots as Record<string, () => Promise<Record<string, string>>>;

describe('fallback reporting', () => {
  let httpMock: HttpTestingController;
  let report: ReturnType<typeof vi.fn>;

  function configure(useRealReporter: boolean): void {
    report = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: SUPPORTED_CULTURES, useValue: ['en', 'pl'] },
        ...(useRealReporter ? [] : [{ provide: ErrorReportingService, useValue: { report } }]),
      ],
    });
    httpMock = TestBed.inject(HttpTestingController);
  }

  function blockStorage(): void {
    const failure = () => {
      throw new DOMException('Storage is disabled', 'SecurityError');
    };
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(failure);
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(failure);
    vi.spyOn(Storage.prototype, 'removeItem').mockImplementation(failure);
  }

  afterEach(() => {
    vi.restoreAllMocks();
    localStorage.clear();
    delete mutableSnapshots['pl'];
  });

  it('reports a failed labels load as a fallback and keeps the snapshot', async () => {
    configure(false);
    const service = TestBed.inject(ResourceService);
    const labels = service.labels();

    const load = firstValueFrom(service.load());
    httpMock
      .expectOne(`${environment.apiUrl}/resources/en`)
      .flush(null, { status: 500, statusText: 'Server Error' });
    await load;

    expect(report).toHaveBeenCalledTimes(1);
    expect(report).toHaveBeenCalledWith(
      'fallback',
      'labels.load.failed',
      expect.any(HttpErrorResponse),
    );
    expect(service.labels()).toBe(labels);
  });

  it('reports a failed label snapshot import as a fallback', async () => {
    configure(false);
    mutableSnapshots['pl'] = () => Promise.reject(new Error('chunk failed'));
    localStorage.setItem(cultureStorageKey, 'pl');

    const service = TestBed.inject(ResourceService);
    await new Promise((resolve) => setTimeout(resolve));

    expect(report).toHaveBeenCalledTimes(1);
    expect(report).toHaveBeenCalledWith('fallback', 'labels.snapshot.failed', expect.any(Error));
    expect(service.culture()).toBe('en');
  });

  it('reports blocked storage for the token store once, without the token value', () => {
    configure(true);
    blockStorage();
    const store = TestBed.inject(TokenStore);

    store.refreshToken();
    store.setTokens({
      tokenType: 'Bearer',
      accessToken: 'a',
      expiresIn: 1,
      refreshToken: tokenValue,
    });
    store.clear();

    const request = httpMock.expectOne(reportUrl);
    expect(request.request.body).toMatchObject({ kind: 'fallback', context: 'storage.token' });
    expect(JSON.stringify(request.request.body)).not.toContain(tokenValue);
    expect(store.refreshToken()).toBeNull();
    httpMock.verify();
  });

  it('reports blocked storage for the culture store once, without the culture value', () => {
    configure(true);
    blockStorage();
    const store = TestBed.inject(CultureStore);

    store.selected();
    store.save('pl');
    store.selected();

    const request = httpMock.expectOne(reportUrl);
    expect(request.request.body).toMatchObject({ kind: 'fallback', context: 'storage.culture' });
    expect(JSON.stringify(request.request.body)).not.toContain('"pl"');
    expect(store.selected()).toBe('en');
    httpMock.verify();
  });

  it('reports nothing while storage works', () => {
    configure(true);
    localStorage.setItem(refreshTokenStorageKey, tokenValue);

    TestBed.inject(TokenStore).refreshToken();
    TestBed.inject(CultureStore).selected();

    httpMock.expectNone(reportUrl);
  });
});
