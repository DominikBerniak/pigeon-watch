import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface UpdatedProfile {
  email: string;
  displayName: string;
}

@Injectable({ providedIn: 'root' })
export class ProfileApi {
  private readonly http = inject(HttpClient);

  updateDisplayName(displayName: string): Observable<UpdatedProfile> {
    return this.http.put<UpdatedProfile>(`${environment.apiUrl}/account/profile`, {
      displayName,
    });
  }
}
