import { test, expect } from '@playwright/test';

const allowedKeys = ['kind', 'context', 'httpStatus', 'errorName', 'traceId', 'route', 'sessionId'];

test.use({
  geolocation: { latitude: 52.07, longitude: 19.48 },
  permissions: ['geolocation'],
});

test('an unexpected nearby failure shows a reference and reports it with the same trace id', async ({
  page,
}) => {
  const traceId = `00-${Date.now().toString(16).padStart(32, '0')}-0000000000000001-00`;

  await page.route('**/sightings/nearby**', async (route) => {
    if (route.request().method() === 'OPTIONS') {
      await route.fallback();

      return;
    }

    await route.fulfill({
      status: 500,
      contentType: 'application/problem+json',
      headers: { 'access-control-allow-origin': '*' },
      body: JSON.stringify({
        type: 'https://tools.ietf.org/html/rfc9110#section-15.6.1',
        title: 'An error occurred while processing your request.',
        status: 500,
        traceId,
      }),
    });
  });

  const reportRequest = page.waitForRequest(
    (request) => request.method() === 'POST' && request.url().endsWith('/client-errors'),
  );
  const reportResponse = page.waitForResponse(
    (response) =>
      response.request().method() === 'POST' && response.url().endsWith('/client-errors'),
  );

  await page.goto('/');

  await expect(page.getByText("We couldn't load sightings for this area.")).toBeVisible();
  await expect(page.getByText(`Reference: ${traceId}`)).toBeVisible();

  const body = (await reportRequest).postDataJSON() as Record<string, unknown>;

  expect(Object.keys(body).every((key) => allowedKeys.includes(key))).toBe(true);
  expect(body).toMatchObject({
    kind: 'api-failure',
    context: 'nearby.search',
    httpStatus: 500,
    traceId,
  });
  expect((await reportResponse).status()).toBe(202);
});
