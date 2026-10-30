import { Injectable, InjectionToken, Injector, inject } from '@angular/core';
import { ErrorReportingService } from '../error-reporting/error-reporting.service';
import { defaultCulture, supportedCultures } from './generated/snapshots';

export const cultureStorageKey = 'pigeonwatch.culture';

export const SUPPORTED_CULTURES = new InjectionToken<readonly string[]>('SUPPORTED_CULTURES', {
  providedIn: 'root',
  factory: () => supportedCultures,
});

@Injectable({ providedIn: 'root' })
export class CultureStore {
  private readonly supportedCultures = inject(SUPPORTED_CULTURES);
  private readonly injector = inject(Injector);

  selected(): string {
    const saved = this.readStoredCulture();

    if (!saved) return defaultCulture;

    const culture = saved.toLowerCase();

    return this.supportedCultures.includes(culture) ? culture : defaultCulture;
  }

  save(culture: string): void {
    try {
      localStorage.setItem(cultureStorageKey, culture);
    } catch (error) {
      this.reportStorage(error);
    }
  }

  private readStoredCulture(): string | null {
    try {
      return localStorage.getItem(cultureStorageKey);
    } catch (error) {
      this.reportStorage(error);

      return null;
    }
  }

  private reportStorage(error: unknown): void {
    this.injector.get(ErrorReportingService).report('fallback', 'storage.culture', error);
  }
}
