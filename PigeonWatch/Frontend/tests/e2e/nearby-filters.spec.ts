import { test, expect } from '@playwright/test';

test.use({
  geolocation: { latitude: 52.07, longitude: 19.48 },
  permissions: ['geolocation'],
  storageState: { cookies: [], origins: [] },
});

test('a criticality filter keeps a matching marker and removes a non-matching one', async ({
  page,
}) => {
  const stamp = Date.now();
  const birdName = `Filter bird ${stamp}`;
  const password = `E2e-${stamp}-Aa1!`;

  await page.goto('/register');
  await page.getByLabel('Email').fill(`filter-${stamp}@example.test`);
  await page.getByLabel('Display name').fill(`Filter tester ${stamp}`);
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

  await page.getByRole('link', { name: 'Back to map' }).click();

  const marker = page.getByRole('button', { name: `${birdName}, Urgent` });
  const criticality = page.getByRole('combobox', { name: 'Criticality' });
  const panel = page.getByRole('listbox');
  const nearbyWith = (query: string) =>
    page.waitForResponse(
      (response) =>
        response.url().includes('/sightings/nearby') && response.url().endsWith(query),
    );

  await expect(marker).toBeVisible();

  await page.getByRole('button', { name: 'Show filters' }).click();
  await criticality.click();
  await expect(panel).toBeVisible();

  const urgentOnly = nearbyWith('criticalities=3');
  await page.getByRole('option', { name: 'Urgent' }).click();
  await urgentOnly;
  await page.keyboard.press('Escape');
  await expect(panel).toBeHidden();

  await expect(marker).toBeVisible();

  await criticality.click();
  await expect(panel).toBeVisible();
  await page.getByRole('option', { name: 'Monitor' }).click();

  const monitorOnly = nearbyWith('criticalities=1');
  await page.getByRole('option', { name: 'Urgent' }).click();
  await monitorOnly;
  await page.keyboard.press('Escape');
  await expect(panel).toBeHidden();

  await expect(marker).toHaveCount(0);
});
