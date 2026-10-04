import { Component } from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { WarmupState } from '../warmup/warmup-state';
import { warmupInterceptor } from '../warmup/warmup.interceptor';
import { defaultLabels } from './generated/snapshots';
import { ResourceService } from './resource.service';
import { TranslatePipe } from './translate.pipe';

const resourcesUrl = `${environment.apiUrl}/resources/en`;

@Component({
  imports: [TranslatePipe],
  template: `<h1>{{ 'common.appName' | translate }}</h1>
    <p>{{ 'auth.validation.passwordMinLength' | translate: 8 }}</p>`,
})
class LabelHost {}

describe('ResourceService', () => {
  let httpMock: HttpTestingController;
  let service: ResourceService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([warmupInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    httpMock = TestBed.inject(HttpTestingController);
    service = TestBed.inject(ResourceService);
  });

  afterEach(() => {
    httpMock.verify();
    vi.restoreAllMocks();
  });

  it('is seeded synchronously from the en snapshot', () => {
    expect(service.culture()).toBe('en');
    expect(service.labels()).toBe(defaultLabels);
    expect(service.t('common.appName')).toBe('PigeonWatch');
    expect(document.documentElement.lang).toBe('en');
  });

  it('substitutes positional placeholders', () => {
    expect(service.t('auth.validation.passwordMinLength', 8)).toBe('Use at least 8 characters.');
    expect(service.t('auth.register.displayNameHint', 3, 30)).toBe(
      'Shown to other rescuers. 3–30 characters, no "@".',
    );
  });

  it('returns the key for a missing key and warns once', () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined);

    expect(service.t('missing.key')).toBe('missing.key');
    expect(service.t('missing.key')).toBe('missing.key');
    expect(service.t('constructor')).toBe('constructor');

    expect(warn).toHaveBeenCalledTimes(2);
  });

  it('replaces the labels with the API map on success', async () => {
    const load = firstValueFrom(service.load());
    const request = httpMock.expectOne(resourcesUrl);
    expect(request.request.headers.has('Authorization')).toBe(false);
    request.flush({ culture: 'en', labels: { 'common.appName': 'PigeonWatch API' } });
    await load;

    expect(service.t('common.appName')).toBe('PigeonWatch API');
    expect(document.documentElement.lang).toBe('en');
  });

  it('keeps the snapshot labels and does not warm up when the fetch fails', async () => {
    vi.useFakeTimers();
    const warmup = TestBed.inject(WarmupState);

    try {
      const load = firstValueFrom(service.load());
      vi.advanceTimersByTime(5_000);
      expect(warmup.status()).toBe('idle');
      httpMock.expectOne(resourcesUrl).flush(null, { status: 503, statusText: 'Unavailable' });
      await expect(load).resolves.toBeUndefined();
      vi.advanceTimersByTime(60_000);

      httpMock.expectNone(resourcesUrl);
      expect(warmup.status()).toBe('idle');
      expect(service.labels()).toBe(defaultLabels);
    } finally {
      vi.useRealTimers();
    }
  });

  it('renders labels through the translate pipe', () => {
    const fixture = TestBed.createComponent(LabelHost);
    fixture.detectChanges();
    const element: HTMLElement = fixture.nativeElement;

    expect(element.querySelector('h1')?.textContent).toBe('PigeonWatch');
    expect(element.querySelector('p')?.textContent).toBe('Use at least 8 characters.');
  });
});
