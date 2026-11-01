import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { GeoPoint } from '../../core/geolocation/geo-point';
import { NewSighting, Sighting, SightingFilters, SightingList } from './sighting.types';

@Injectable({ providedIn: 'root' })
export class SightingsApi {
  private readonly http = inject(HttpClient);

  create(sighting: NewSighting): Observable<Sighting> {
    return this.http.post<Sighting>(`${environment.apiUrl}/sightings`, sighting);
  }

  get(id: string): Observable<Sighting> {
    return this.http.get<Sighting>(`${environment.apiUrl}/sightings/${encodeURIComponent(id)}`);
  }

  nearby(centre: GeoPoint, filters?: SightingFilters): Observable<SightingList> {
    let params = new HttpParams()
      .set('latitude', centre.latitude)
      .set('longitude', centre.longitude);

    for (const status of filters?.statuses ?? []) params = params.append('statuses', status);

    for (const criticality of filters?.criticalities ?? []) {
      params = params.append('criticalities', criticality);
    }

    return this.http.get<SightingList>(`${environment.apiUrl}/sightings/nearby`, { params });
  }

  mine(): Observable<SightingList> {
    return this.http.get<SightingList>(`${environment.apiUrl}/sightings/mine`);
  }
}
