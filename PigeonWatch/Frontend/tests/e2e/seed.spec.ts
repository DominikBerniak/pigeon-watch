import { test, expect } from '@playwright/test';

test.use({
  geolocation: { latitude: 52.07, longitude: 19.48 },
  permissions: ['geolocation'],
});

test('risk #4: a reported sighting appears on the nearby map where it was reported', async ({ page }) => {
  const birdName = `Seed bird ${Date.now()}`;

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
  await expect(page.getByText('52.07000, 19.48000')).toBeVisible();

  await page.getByRole('link', { name: 'Back to map' }).click();

  await expect(page.getByRole('button', { name: `${birdName}, Urgent` })).toBeVisible();
});
