import { Injectable, InjectionToken, inject } from '@angular/core';
import { defaultCulture, supportedCultures } from './generated/snapshots';

export const cultureStorageKey = 'pigeonwatch.culture';

export const SUPPORTED_CULTURES = new InjectionToken<readonly string[]>('SUPPORTED_CULTURES', {
  providedIn: 'root',
  factory: () => supportedCultures,
});

@Injectable({ providedIn: 'root' })
export class CultureStore {
  private readonly supportedCultures = inject(SUPPORTED_CULTURES);

  selected(): string {
    const saved = readStoredCulture();

    if (!saved) return defaultCulture;

    const culture = saved.toLowerCase();

    return this.supportedCultures.includes(culture) ? culture : defaultCulture;
  }

  save(culture: string): void {
    try {
      localStorage.setItem(cultureStorageKey, culture);
    } catch {
      return;
    }
  }
}

function readStoredCulture(): string | null {
  try {
    return localStorage.getItem(cultureStorageKey);
  } catch {
    return null;
  }
}
