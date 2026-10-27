import { environment } from '../../../environments/environment';
import { reportBootstrapFailure } from './bootstrap-report';

describe('reportBootstrapFailure', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('beacons an allowlisted bootstrap report to the API', async () => {
    const sendBeacon = vi.fn().mockReturnValue(true);

    vi.stubGlobal('navigator', { sendBeacon });
    reportBootstrapFailure(new TypeError('secret@mail.com'));

    const [url, blob] = sendBeacon.mock.calls[0] as [string, Blob];
    const body = JSON.parse(await blob.text()) as Record<string, unknown>;

    expect(url).toBe(`${environment.apiUrl}/client-errors`);
    expect(blob.type).toBe('application/json');
    expect(body).toMatchObject({
      kind: 'bootstrap',
      context: 'app.bootstrap',
      errorName: 'TypeError',
    });
    expect(body['route']).toMatch(/^\/[A-Za-z0-9/:_.-]*$/);
    expect(body['sessionId']).toMatch(/^[0-9a-f-]{36}$/);
    expect(JSON.stringify(body)).not.toMatch(/secret|@/);
  });

  it('does not throw when sendBeacon is missing', () => {
    vi.stubGlobal('navigator', {});

    expect(() => reportBootstrapFailure(new Error('x'))).not.toThrow();
  });
});
