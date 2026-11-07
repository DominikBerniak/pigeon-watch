import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { GeoPoint } from '../../core/geolocation/geo-point';
import {
  NewSighting,
  Sighting,
  SightingFilters,
  SightingList,
  SightingListRequest,
  SightingPage,
} from './sighting.types';

@Injectable({ providedIn: 'root' })
export class SightingsApi {
  private readonly http = inject(HttpClient);

  create(sighting: NewSighting): Observable<Sighting> {
    return this.http.post<Sighting>(`${environment.apiUrl}/sightings`, sightingForm(sighting));
  }

  get(id: string): Observable<Sighting> {
    return this.http.get<Sighting>(`${environment.apiUrl}/sightings/${encodeURIComponent(id)}`);
  }

  photo(id: string): Observable<Blob> {
    return this.http.get(`${environment.apiUrl}/sightings/${encodeURIComponent(id)}/photo`, {
      responseType: 'blob',
    });
  }

  photoThumbnail(id: string): Observable<Blob> {
    return this.http.get(
      `${environment.apiUrl}/sightings/${encodeURIComponent(id)}/photo/thumbnail`,
      { responseType: 'blob' },
    );
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

  list(request: SightingListRequest): Observable<SightingPage> {
    return this.http.post<SightingPage>(`${environment.apiUrl}/sightings/list`, request);
  }

  mine(): Observable<SightingList> {
    return this.http.get<SightingList>(`${environment.apiUrl}/sightings/mine`);
  }
}

export const sightingPhotoFileName = 'photo.jpg';

function sightingForm(sighting: NewSighting): FormData {
  const form = new FormData();

  if (sighting.name !== null) form.append('name', sighting.name);

  form.append('description', sighting.description);
  form.append('behaviour', sighting.behaviour);
  form.append('latitude', String(sighting.latitude));
  form.append('longitude', String(sighting.longitude));
  form.append('status', String(sighting.status));
  form.append('criticality', String(sighting.criticality));

  if (sighting.photo !== null) form.append('photo', sighting.photo, sightingPhotoFileName);

  return form;
}
