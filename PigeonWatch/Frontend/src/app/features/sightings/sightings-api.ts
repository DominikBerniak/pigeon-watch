import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { GeoPoint } from '../../core/geolocation/geo-point';
import {
  NewSighting,
  Sighting,
  SightingComment,
  SightingCommentPage,
  SightingFilters,
  SightingList,
  SightingListRequest,
  SightingMessage,
  SightingMessagePage,
  SightingPage,
  SightingThreadPage,
  SightingThreadStart,
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

  comments(sightingId: string, page: number): Observable<SightingCommentPage> {
    return this.http.get<SightingCommentPage>(commentsUrl(sightingId), {
      params: new HttpParams().set('page', page),
    });
  }

  addComment(sightingId: string, text: string): Observable<SightingComment> {
    return this.http.post<SightingComment>(commentsUrl(sightingId), { text });
  }

  editComment(sightingId: string, commentId: string, text: string): Observable<SightingComment> {
    return this.http.put<SightingComment>(commentUrl(sightingId, commentId), { text });
  }

  deleteComment(sightingId: string, commentId: string): Observable<SightingComment> {
    return this.http.delete<SightingComment>(commentUrl(sightingId, commentId));
  }

  threads(sightingId: string, page: number): Observable<SightingThreadPage> {
    return this.http.get<SightingThreadPage>(threadsUrl(sightingId), {
      params: new HttpParams().set('page', page),
    });
  }

  startThread(sightingId: string, text: string): Observable<SightingThreadStart> {
    return this.http.post<SightingThreadStart>(threadsUrl(sightingId), { text });
  }

  threadMessages(
    sightingId: string,
    threadId: string,
    page: number,
  ): Observable<SightingMessagePage> {
    return this.http.get<SightingMessagePage>(messagesUrl(sightingId, threadId), {
      params: new HttpParams().set('page', page),
    });
  }

  sendMessage(sightingId: string, threadId: string, text: string): Observable<SightingMessage> {
    return this.http.post<SightingMessage>(messagesUrl(sightingId, threadId), { text });
  }
}

export const sightingPhotoFileName = 'photo.jpg';

function commentsUrl(sightingId: string): string {
  return `${environment.apiUrl}/sightings/${encodeURIComponent(sightingId)}/comments`;
}

function commentUrl(sightingId: string, commentId: string): string {
  return `${commentsUrl(sightingId)}/${encodeURIComponent(commentId)}`;
}

function threadsUrl(sightingId: string): string {
  return `${environment.apiUrl}/sightings/${encodeURIComponent(sightingId)}/threads`;
}

function messagesUrl(sightingId: string, threadId: string): string {
  return `${threadsUrl(sightingId)}/${encodeURIComponent(threadId)}/messages`;
}

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
