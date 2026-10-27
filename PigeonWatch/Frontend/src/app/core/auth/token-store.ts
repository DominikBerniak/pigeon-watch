import { Injectable, Injector, inject, signal } from '@angular/core';
import { ErrorReportingService } from '../error-reporting/error-reporting.service';

export const refreshTokenStorageKey = 'pigeonwatch.refreshToken';

export interface AccessTokenResponse {
  tokenType: string;
  accessToken: string;
  expiresIn: number;
  refreshToken: string;
}

@Injectable({ providedIn: 'root' })
export class TokenStore {
  private readonly injector = inject(Injector);
  private readonly accessTokenState = signal<string | null>(null);
  private memoryRefreshToken: string | null = null;
  private storageUnavailable = false;

  readonly accessToken = this.accessTokenState.asReadonly();

  refreshToken(): string | null {
    if (this.storageUnavailable) return this.memoryRefreshToken;

    try {
      return localStorage.getItem(refreshTokenStorageKey);
    } catch (error) {
      this.storageUnavailable = true;
      this.reportStorage(error);

      return this.memoryRefreshToken;
    }
  }

  setTokens(response: AccessTokenResponse): void {
    this.accessTokenState.set(response.accessToken);
    this.memoryRefreshToken = response.refreshToken;

    try {
      localStorage.setItem(refreshTokenStorageKey, response.refreshToken);
      this.storageUnavailable = false;
    } catch (error) {
      this.storageUnavailable = true;
      this.reportStorage(error);
    }
  }

  clear(): void {
    this.accessTokenState.set(null);
    this.memoryRefreshToken = null;

    try {
      localStorage.removeItem(refreshTokenStorageKey);
    } catch (error) {
      this.storageUnavailable = true;
      this.reportStorage(error);
    }
  }

  private reportStorage(error: unknown): void {
    this.injector.get(ErrorReportingService).report('fallback', 'storage.token', error);
  }
}
