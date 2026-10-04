import { Injectable, signal } from '@angular/core';

export const refreshTokenStorageKey = 'pigeonwatch.refreshToken';

export interface AccessTokenResponse {
  tokenType: string;
  accessToken: string;
  expiresIn: number;
  refreshToken: string;
}

@Injectable({ providedIn: 'root' })
export class TokenStore {
  private readonly accessTokenState = signal<string | null>(null);
  private memoryRefreshToken: string | null = null;
  private storageUnavailable = false;

  readonly accessToken = this.accessTokenState.asReadonly();

  refreshToken(): string | null {
    if (this.storageUnavailable) return this.memoryRefreshToken;

    try {
      return localStorage.getItem(refreshTokenStorageKey);
    } catch {
      this.storageUnavailable = true;

      return this.memoryRefreshToken;
    }
  }

  setTokens(response: AccessTokenResponse): void {
    this.accessTokenState.set(response.accessToken);
    this.memoryRefreshToken = response.refreshToken;

    try {
      localStorage.setItem(refreshTokenStorageKey, response.refreshToken);
      this.storageUnavailable = false;
    } catch {
      this.storageUnavailable = true;
    }
  }

  clear(): void {
    this.accessTokenState.set(null);
    this.memoryRefreshToken = null;

    try {
      localStorage.removeItem(refreshTokenStorageKey);
    } catch {
      this.storageUnavailable = true;
    }
  }
}
