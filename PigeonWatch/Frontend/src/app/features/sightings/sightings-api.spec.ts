import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { NewSighting, Sighting, SightingCriticality, SightingStatus } from './sighting.types';
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

  it('creates a sighting with a POST of the new sighting', () => {
    const created: NewSighting = {
      name: null,
      description: sighting.description,
      behaviour: sighting.behaviour,
      latitude: sighting.latitude,
      longitude: sighting.longitude,
      status: SightingStatus.spotted,
      criticality: SightingCriticality.urgent,
    };
    let result: Sighting | undefined;

    api.create(created).subscribe((value) => (result = value));
    const request = httpMock.expectOne(sightingsUrl);
    request.flush(sighting);

    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual(created);
    expect(result).toEqual(sighting);
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

  it('lists the current user sightings', () => {
    let items: Sighting[] | undefined;

    api.mine().subscribe((list) => (items = list.items));
    const request = httpMock.expectOne(`${sightingsUrl}/mine`);
    request.flush({ items: [sighting] });

    expect(request.request.method).toBe('GET');
    expect(items).toEqual([sighting]);
  });
});
