import { HttpClient, HttpContext } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, tap } from 'rxjs';
import { environment } from '../../../environments/environment';
import { SKIP_AUTH } from '../http/http-context-tokens';

export interface PasswordRules {
  minLength: number;
  requireDigit: boolean;
  requireLowercase: boolean;
  requireUppercase: boolean;
  requireNonAlphanumeric: boolean;
}

export interface DisplayNameRules {
  minLength: number;
  maxLength: number;
}

export interface ClientConfiguration {
  passwordRules: PasswordRules;
  displayNameRules: DisplayNameRules;
}

export interface CurrentUser {
  id: string;
  email: string;
  displayName: string;
  roles: string[];
}

export interface GeneralConfiguration {
  currentUser: CurrentUser;
}

@Injectable({ providedIn: 'root' })
export class ConfigurationService {
  private readonly http = inject(HttpClient);
  private readonly clientConfigState = signal<ClientConfiguration | null>(null);
  private readonly generalConfigState = signal<GeneralConfiguration | null>(null);

  readonly clientConfig = this.clientConfigState.asReadonly();
  readonly generalConfig = this.generalConfigState.asReadonly();

  loadClient(): Observable<ClientConfiguration> {
    return this.http
      .get<ClientConfiguration>(`${environment.apiUrl}/configuration/client`, {
        context: new HttpContext().set(SKIP_AUTH, true),
      })
      .pipe(tap((configuration) => this.clientConfigState.set(configuration)));
  }

  loadGeneral(): Observable<GeneralConfiguration> {
    return this.http
      .get<GeneralConfiguration>(`${environment.apiUrl}/configuration/general`)
      .pipe(tap((configuration) => this.generalConfigState.set(configuration)));
  }

  clearGeneral(): void {
    this.generalConfigState.set(null);
  }
}
