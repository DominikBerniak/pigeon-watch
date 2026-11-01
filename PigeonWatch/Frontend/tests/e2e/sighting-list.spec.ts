import { test, expect, type Page, type Response } from '@playwright/test';

test.use({
  geolocation: { latitude: 52.07, longitude: 19.48 },
  permissions: ['geolocation'],
  storageState: { cookies: [], origins: [] },
});

interface SightingPageBody {
  items: { id: string }[];
  totalCount: number;
}

const listResponse = (page: Page): Promise<Response> =>
  page.waitForResponse(
    (response) =>
      response.url().endsWith('/sightings/list') && response.request().method() === 'POST',
  );

const holds = async (response: Response, id: string): Promise<boolean> => {
  const body = (await response.json()) as SightingPageBody;

  return body.items.some((item) => item.id === id);
};

test('the list opened from the map shows the reported sighting and a no-match message for a resolved status', async ({
  page,
}) => {
  const stamp = Date.now();
  const birdName = `List bird ${stamp}`;
  const password = `E2e-${stamp}-Aa1!`;

  await page.goto('/register');
  await page.getByLabel('Email').fill(`list-${stamp}@example.test`);
  await page.getByLabel('Display name').fill(`List tester ${stamp}`);
  await page.getByLabel('Password', { exact: true }).fill(password);
  await page.getByLabel('Confirm password').fill(password);
  await page.getByRole('button', { name: 'Create account' }).click();
  await expect(page.getByRole('banner').getByRole('button')).toBeVisible();

  await page.goto('/sightings/new');
  await expect(page.getByRole('heading', { name: 'Report a sighting' })).toBeVisible();

  await page.getByRole('textbox', { name: 'Name (optional)' }).fill(birdName);
  await page.getByRole('textbox', { name: 'Description' }).fill('Grey pigeon with a bent wing');
  await page.getByRole('textbox', { name: 'Behaviour' }).fill('Walks but cannot fly');
  await page.getByRole('combobox', { name: 'Status' }).click();
  await page.getByRole('option', { name: 'Spotted' }).click();
  await page.getByRole('combobox', { name: 'Criticality' }).click();
  await page.getByRole('option', { name: 'Urgent' }).click();
  await page.getByRole('button', { name: 'Use my location' }).click();
  await expect(page.getByText('No location chosen yet.')).toBeHidden();

  await page.getByRole('button', { name: 'Report sighting' }).click();

  await page.waitForURL(/\/sightings\/[0-9a-f-]+$/);
  await expect(page.getByRole('heading', { name: birdName })).toBeVisible();
  const sightingId = new URL(page.url()).pathname.split('/').pop() ?? '';

  await page.getByRole('link', { name: 'Back to map' }).click();
  await expect(page.getByRole('button', { name: `${birdName}, Urgent` })).toBeVisible();

  const opened = listResponse(page);
  await page.getByRole('link', { name: 'List view' }).click();
  const first = await opened;

  await page.waitForURL(/\/sightings\/list$/);
  expect(first.request().postDataJSON()).toEqual({
    latitude: 52.07,
    longitude: 19.48,
    statuses: [],
    criticalities: [],
    page: 1,
    pageSize: 10,
  });
  const { totalCount } = (await first.json()) as SightingPageBody;
  await expect(
    page.getByRole('heading', { name: `Sightings nearby (${totalCount})` }),
  ).toBeVisible();

  const criticality = page.getByRole('combobox', { name: 'Criticality' });
  const status = page.getByRole('combobox', { name: 'Status' });
  const panel = page.getByRole('listbox');

  await page.getByRole('button', { name: 'Show filters' }).click();
  await criticality.click();
  await expect(panel).toBeVisible();

  const urgentOnly = listResponse(page);
  await page.getByRole('option', { name: 'Urgent' }).click();
  let current = await urgentOnly;
  await page.keyboard.press('Escape');
  await expect(panel).toBeHidden();

  expect(current.request().postDataJSON()).toMatchObject({ criticalities: [3], page: 1 });
  const { totalCount: urgentCount } = (await current.json()) as SightingPageBody;
  await expect(
    page.getByRole('heading', { name: `Sightings nearby (${urgentCount})` }),
  ).toBeVisible();

  const lastPage = page.getByRole('button', { name: 'Last page' });

  if (!(await holds(current, sightingId)) && urgentCount > 10) {
    await expect(lastPage).toBeEnabled();
    const last = listResponse(page);
    await lastPage.click();
    current = await last;
  }

  for (let step = 0; step < 3 && !(await holds(current, sightingId)); step++) {
    const previous = listResponse(page);
    await page.getByRole('button', { name: 'Previous page' }).click();
    current = await previous;
  }

  const row = page.locator('li[app-sighting-row]').filter({ hasText: birdName });

  await expect(row).toBeVisible();
  await expect(row.getByRole('link')).toHaveAttribute('href', `/sightings/${sightingId}`);
  await expect(row.locator('.sighting-row__name')).toHaveText(birdName);
  await expect(row.locator('app-criticality-badge')).toHaveText('Urgent');
  await expect(row.locator('time')).toHaveText(/^(just now|\d+ min ago)$/);

  await status.click();
  await expect(panel).toBeVisible();

  const adoptedOnly = listResponse(page);
  await page.getByRole('option', { name: 'Adopted' }).click();
  const resolved = await adoptedOnly;
  await page.keyboard.press('Escape');
  await expect(panel).toBeHidden();

  expect(resolved.request().postDataJSON()).toMatchObject({ statuses: [6], page: 1 });
  const { totalCount: resolvedCount } = (await resolved.json()) as SightingPageBody;

  if (resolvedCount === 0) {
    await expect(
      page.getByText(/^No sightings within \d+ km match the selected filters\.$/),
    ).toBeVisible();
    await expect(page.locator('.sighting-list__page-number')).toHaveCount(0);
  } else {
    await expect(
      page.getByRole('heading', { name: `Sightings nearby (${resolvedCount})` }),
    ).toBeVisible();
  }
  await expect(row).toHaveCount(0);
});
