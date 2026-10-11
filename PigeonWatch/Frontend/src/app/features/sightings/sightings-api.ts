import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { GeoPoint } from '../../core/geolocation/geo-point';
import { NewSighting, Sighting, SightingList } from './sighting.types';

@Injectable({ providedIn: 'root' })
export class SightingsApi {
  private readonly http = inject(HttpClient);

  create(sighting: NewSighting): Observable<Sighting> {
    return this.http.post<Sighting>(`${environment.apiUrl}/sightings`, sighting);
  }

  get(id: string): Observable<Sighting> {
    return this.http.get<Sighting>(`${environment.apiUrl}/sightings/${encodeURIComponent(id)}`);
  }

  nearby(centre: GeoPoint): Observable<SightingList> {
    return this.http.get<SightingList>(`${environment.apiUrl}/sightings/nearby`, {
      params: { latitude: centre.latitude, longitude: centre.longitude },
    });
  }

  mine(): Observable<SightingList> {
    return this.http.get<SightingList>(`${environment.apiUrl}/sightings/mine`);
  }
}
