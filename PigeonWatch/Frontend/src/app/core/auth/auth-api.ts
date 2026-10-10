import { HttpClient, HttpContext } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { SKIP_AUTH } from '../http/http-context-tokens';
import { AccessTokenResponse } from './token-store';

export interface RegisterRequest {
  email: string;
  password: string;
  displayName: string;
}

export interface RegisteredAccount {
  email: string;
  displayName: string;
}

export interface PasswordChanged {
  changed: boolean;
}

@Injectable({ providedIn: 'root' })
export class AuthApi {
  private readonly http = inject(HttpClient);

  login(email: string, password: string): Observable<AccessTokenResponse> {
    return this.http.post<AccessTokenResponse>(
      `${environment.apiUrl}/auth/login`,
      { email, password },
      { context: skipAuth() },
    );
  }

  refresh(refreshToken: string): Observable<AccessTokenResponse> {
    return this.http.post<AccessTokenResponse>(
      `${environment.apiUrl}/auth/refresh`,
      { refreshToken },
      { context: skipAuth() },
    );
  }

  register(request: RegisterRequest): Observable<RegisteredAccount> {
    return this.http.post<RegisteredAccount>(`${environment.apiUrl}/account/register`, request, {
      context: skipAuth(),
    });
  }

  changePassword(currentPassword: string, newPassword: string): Observable<PasswordChanged> {
    return this.http.put<PasswordChanged>(`${environment.apiUrl}/account/password`, {
      currentPassword,
      newPassword,
    });
  }
}

function skipAuth(): HttpContext {
  return new HttpContext().set(SKIP_AUTH, true);
}
