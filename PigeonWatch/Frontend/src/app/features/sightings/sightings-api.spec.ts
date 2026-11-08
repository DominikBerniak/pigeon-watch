import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import {
  NewSighting,
  Sighting,
  SightingComment,
  SightingCommentPage,
  SightingCriticality,
  SightingListRequest,
  SightingMessage,
  SightingMessagePage,
  SightingPage,
  SightingStatus,
  SightingThread,
  SightingThreadPage,
  SightingThreadStart,
} from './sighting.types';
import { SightingsApi } from './sightings-api';

const sightingsUrl = `${environment.apiUrl}/sightings`;

const sighting: Sighting = {
  id: '6f1c2a40-0000-0000-0000-000000000001',
  name: null,
  description: 'Grey pigeon with a ruffled wing',
  behaviour: 'Cannot fly',
  latitude: 52.2297,
  longitude: 21.0122,
  status: SightingStatus.spotted,
  criticality: SightingCriticality.urgent,
  reporterDisplayName: 'Jan K',
  createdAt: '2026-10-11T12:00:00Z',
  hasPhoto: false,
  isMine: false,
};

describe('SightingsApi', () => {
  let api: SightingsApi;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    api = TestBed.inject(SightingsApi);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  const newSighting: NewSighting = {
    name: 'Limpy',
    description: sighting.description,
    behaviour: sighting.behaviour,
    latitude: 52.2297,
    longitude: -21.0122,
    status: SightingStatus.spotted,
    criticality: SightingCriticality.urgent,
    photo: null,
  };

  function sentForm(created: NewSighting): FormData {
    let result: Sighting | undefined;

    api.create(created).subscribe((value) => (result = value));
    const request = httpMock.expectOne(sightingsUrl);
    request.flush(sighting);

    expect(request.request.method).toBe('POST');
    expect(request.request.headers.has('Content-Type')).toBe(false);
    expect(request.request.body).toBeInstanceOf(FormData);
    expect(result).toEqual(sighting);

    return request.request.body as FormData;
  }

  it('creates a sighting with a multipart POST of the fields and no photo part', () => {
    const form = sentForm(newSighting);

    expect([...form.keys()]).toEqual([
      'name',
      'description',
      'behaviour',
      'latitude',
      'longitude',
      'status',
      'criticality',
    ]);
    expect(form.get('name')).toBe('Limpy');
    expect(form.get('description')).toBe(sighting.description);
    expect(form.get('behaviour')).toBe(sighting.behaviour);
    expect(form.get('latitude')).toBe('52.2297');
    expect(form.get('longitude')).toBe('-21.0122');
    expect(form.get('status')).toBe('1');
    expect(form.get('criticality')).toBe('3');
    expect(form.has('photo')).toBe(false);
  });

  it('leaves out the name part when the name is null', () => {
    const form = sentForm({ ...newSighting, name: null });

    expect(form.has('name')).toBe(false);
  });

  it('sends the photo as a part named photo.jpg', () => {
    const photo = new Blob(['jpeg-bytes'], { type: 'image/jpeg' });
    const form = sentForm({ ...newSighting, photo });
    const part = form.get('photo');

    expect(part).toBeInstanceOf(File);
    expect((part as File).name).toBe('photo.jpg');
    expect((part as File).type).toBe('image/jpeg');
    expect((part as File).size).toBe(photo.size);
  });

  it('downloads the photo of a sighting as a blob', () => {
    const photo = new Blob(['jpeg-bytes'], { type: 'image/jpeg' });
    let result: Blob | undefined;

    api.photo('a/b').subscribe((value) => (result = value));
    const request = httpMock.expectOne(`${sightingsUrl}/a%2Fb/photo`);
    request.flush(photo);

    expect(request.request.method).toBe('GET');
    expect(request.request.responseType).toBe('blob');
    expect(result).toBe(photo);
  });

  it('downloads the thumbnail of a sighting as a blob', () => {
    const thumbnail = new Blob(['jpeg-bytes'], { type: 'image/jpeg' });
    let result: Blob | undefined;

    api.photoThumbnail('a/b').subscribe((value) => (result = value));
    const request = httpMock.expectOne(`${sightingsUrl}/a%2Fb/photo/thumbnail`);
    request.flush(thumbnail);

    expect(request.request.method).toBe('GET');
    expect(request.request.responseType).toBe('blob');
    expect(result).toBe(thumbnail);
  });

  it('gets one sighting by id', () => {
    api.get(sighting.id).subscribe();
    const request = httpMock.expectOne(`${sightingsUrl}/${sighting.id}`);
    request.flush(sighting);

    expect(request.request.method).toBe('GET');
  });

  it('encodes the id into the path', () => {
    api.get('a/b').subscribe();

    httpMock.expectOne(`${sightingsUrl}/a%2Fb`).flush(sighting);
  });

  it('asks for nearby sightings around a centre', () => {
    api.nearby({ latitude: 50.0614, longitude: 19.9366 }).subscribe();
    const request = httpMock.expectOne((candidate) => candidate.url === `${sightingsUrl}/nearby`);
    request.flush({ items: [] });

    expect(request.request.method).toBe('GET');
    expect(request.request.params.get('latitude')).toBe('50.0614');
    expect(request.request.params.get('longitude')).toBe('19.9366');
    expect(request.request.urlWithParams).toBe(
      `${sightingsUrl}/nearby?latitude=50.0614&longitude=19.9366`,
    );
  });

  it('sends status and criticality filters as repeated query parameters', () => {
    api
      .nearby(
        { latitude: 50.0614, longitude: 19.9366 },
        {
          statuses: [SightingStatus.spotted, SightingStatus.caught],
          criticalities: [SightingCriticality.urgent],
        },
      )
      .subscribe();
    const request = httpMock.expectOne((candidate) => candidate.url === `${sightingsUrl}/nearby`);
    request.flush({ items: [] });

    expect(request.request.params.getAll('statuses')).toEqual(['1', '2']);
    expect(request.request.params.getAll('criticalities')).toEqual(['3']);
    expect(request.request.urlWithParams).toBe(
      `${sightingsUrl}/nearby?latitude=50.0614&longitude=19.9366&statuses=1&statuses=2&criticalities=3`,
    );
  });

  it('adds no filter parameters when the filter lists are empty', () => {
    api
      .nearby({ latitude: 50.0614, longitude: 19.9366 }, { statuses: [], criticalities: [] })
      .subscribe();
    const request = httpMock.expectOne((candidate) => candidate.url === `${sightingsUrl}/nearby`);
    request.flush({ items: [] });

    expect(request.request.urlWithParams).toBe(
      `${sightingsUrl}/nearby?latitude=50.0614&longitude=19.9366`,
    );
  });

  it('posts the list request as JSON and returns the page', () => {
    const listRequest: SightingListRequest = {
      latitude: 50.0614,
      longitude: 19.9366,
      statuses: [SightingStatus.adopted],
      criticalities: [SightingCriticality.urgent],
      page: 2,
      pageSize: 20,
    };
    let page: SightingPage | undefined;

    api.list(listRequest).subscribe((value) => (page = value));
    const request = httpMock.expectOne(`${sightingsUrl}/list`);
    request.flush({ items: [sighting], totalCount: 21 });

    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual(listRequest);
    expect(page).toEqual({ items: [sighting], totalCount: 21 });
  });

  it('lists the current user sightings', () => {
    let items: Sighting[] | undefined;

    api.mine().subscribe((list) => (items = list.items));
    const request = httpMock.expectOne(`${sightingsUrl}/mine`);
    request.flush({ items: [sighting] });

    expect(request.request.method).toBe('GET');
    expect(items).toEqual([sighting]);
  });

  const comment: SightingComment = {
    id: 'c/1',
    text: 'Still there',
    authorDisplayName: 'Jan K',
    createdAt: '2026-11-07T12:00:00Z',
    editedAt: null,
    isDeleted: false,
    isMine: true,
  };

  it('asks for a page of comments with encoded ids', () => {
    let page: SightingCommentPage | undefined;

    api.comments('a/b', 2).subscribe((value) => (page = value));
    const request = httpMock.expectOne(
      (candidate) => candidate.url === `${sightingsUrl}/a%2Fb/comments`,
    );
    request.flush({ items: [comment], totalCount: 21 });

    expect(request.request.method).toBe('GET');
    expect(request.request.params.get('page')).toBe('2');
    expect(page).toEqual({ items: [comment], totalCount: 21 });
  });

  it('posts a new comment as JSON', () => {
    api.addComment('a/b', 'Still there').subscribe();
    const request = httpMock.expectOne(`${sightingsUrl}/a%2Fb/comments`);
    request.flush(comment);

    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ text: 'Still there' });
  });

  it('puts the edited text to the comment', () => {
    api.editComment('a/b', 'c/1', 'Gone now').subscribe();
    const request = httpMock.expectOne(`${sightingsUrl}/a%2Fb/comments/c%2F1`);
    request.flush(comment);

    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ text: 'Gone now' });
  });

  it('deletes the comment and returns the placeholder', () => {
    let deleted: SightingComment | undefined;
    const placeholder = { ...comment, text: null, authorDisplayName: null, isDeleted: true };

    api.deleteComment('a/b', 'c/1').subscribe((value) => (deleted = value));
    const request = httpMock.expectOne(`${sightingsUrl}/a%2Fb/comments/c%2F1`);
    request.flush(placeholder);

    expect(request.request.method).toBe('DELETE');
    expect(deleted).toEqual(placeholder);
  });

  const thread: SightingThread = {
    id: 't/1',
    sightingId: 'a/b',
    sightingName: 'Limpy',
    counterpartDisplayName: 'Ola N',
    isOwner: false,
    lastMessageAt: '2026-11-07T12:00:00Z',
    hasUnread: true,
  };

  const message: SightingMessage = {
    id: 'm1',
    text: 'Is it still there?',
    senderDisplayName: 'Jan K',
    createdAt: '2026-11-07T12:00:00Z',
    isMine: true,
  };

  it('asks for a page of the threads on a sighting with encoded ids', () => {
    let page: SightingThreadPage | undefined;

    api.threads('a/b', 2).subscribe((value) => (page = value));
    const request = httpMock.expectOne(
      (candidate) => candidate.url === `${sightingsUrl}/a%2Fb/threads`,
    );
    request.flush({ items: [thread], totalCount: 1 });

    expect(request.request.method).toBe('GET');
    expect(request.request.params.get('page')).toBe('2');
    expect(page).toEqual({ items: [thread], totalCount: 1 });
  });

  it('starts a thread with the first message as JSON', () => {
    let started: SightingThreadStart | undefined;

    api.startThread('a/b', 'Is it still there?').subscribe((value) => (started = value));
    const request = httpMock.expectOne(`${sightingsUrl}/a%2Fb/threads`);
    request.flush({ thread, message });

    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ text: 'Is it still there?' });
    expect(started).toEqual({ thread, message });
  });

  it('asks for a page of messages in a thread with encoded ids', () => {
    let page: SightingMessagePage | undefined;

    api.threadMessages('a/b', 't/1', 3).subscribe((value) => (page = value));
    const request = httpMock.expectOne(
      (candidate) => candidate.url === `${sightingsUrl}/a%2Fb/threads/t%2F1/messages`,
    );
    request.flush({ items: [message], totalCount: 41 });

    expect(request.request.method).toBe('GET');
    expect(request.request.params.get('page')).toBe('3');
    expect(page).toEqual({ items: [message], totalCount: 41 });
  });

  it('posts a reply to the thread as JSON', () => {
    let sent: SightingMessage | undefined;

    api.sendMessage('a/b', 't/1', 'On my way').subscribe((value) => (sent = value));
    const request = httpMock.expectOne(`${sightingsUrl}/a%2Fb/threads/t%2F1/messages`);
    request.flush(message);

    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ text: 'On my way' });
    expect(sent).toEqual(message);
  });

  it('marks a thread read with an empty PUT', () => {
    let completed = false;

    api.markThreadRead('a/b', 't/1').subscribe({ complete: () => (completed = true) });
    const request = httpMock.expectOne(`${sightingsUrl}/a%2Fb/threads/t%2F1/read`);
    request.flush(null, { status: 204, statusText: 'No Content' });

    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toBeNull();
    expect(completed).toBe(true);
  });

  it('asks for a page of the caller threads across sightings', () => {
    let page: SightingThreadPage | undefined;

    api.inbox(2).subscribe((value) => (page = value));
    const request = httpMock.expectOne(
      (candidate) => candidate.url === `${environment.apiUrl}/threads`,
    );
    request.flush({ items: [thread], totalCount: 21 });

    expect(request.request.method).toBe('GET');
    expect(request.request.params.get('page')).toBe('2');
    expect(page).toEqual({ items: [thread], totalCount: 21 });
  });
});
