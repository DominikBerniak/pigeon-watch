import { test, expect } from '@playwright/test';
import { join } from 'node:path';

test.use({
  geolocation: { latitude: 52.07, longitude: 19.48 },
  permissions: ['geolocation'],
  storageState: { cookies: [], origins: [] },
});

const photoFixture = join(__dirname, 'fixtures', 'pigeon.jpg');

test('a sighting reported with a photo shows the photo on its detail page and a camera icon in My sightings', async ({
  page,
}) => {
  const stamp = Date.now();
  const birdName = `Photo bird ${stamp}`;
  const password = `E2e-${stamp}-Aa1!`;

  await page.goto('/register');
  await page.getByLabel('Email').fill(`photo-${stamp}@example.test`);
  await page.getByLabel('Display name').fill(`Photo tester ${stamp}`);
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

  await page.locator('app-photo-picker input[type="file"]').setInputFiles(photoFixture);
  await expect(page.getByRole('img', { name: 'Preview of the chosen photo' })).toBeVisible();

  const created = page.waitForResponse(
    (response) => response.url().endsWith('/sightings') && response.request().method() === 'POST',
  );
  await page.getByRole('button', { name: 'Report sighting' }).click();
  const response = await created;

  expect(response.status()).toBe(201);
  expect(((await response.json()) as { hasPhoto: boolean }).hasPhoto).toBe(true);

  await page.waitForURL(/\/sightings\/[0-9a-f-]+$/);
  await expect(page.getByRole('heading', { name: birdName })).toBeVisible();

  const photo = page.getByRole('img', { name: `Photo of ${birdName}` });

  await expect(photo).toBeVisible();
  await expect(photo).toHaveAttribute('src', /^blob:/);
  await expect
    .poll(() => photo.evaluate((image: HTMLImageElement) => image.naturalWidth))
    .toBeGreaterThan(0);
  await expect(page.getByText("We couldn't load the photo right now.")).toHaveCount(0);

  await page.goto('/profile');

  const row = page.locator('li[app-sighting-row]').filter({ hasText: birdName });

  await expect(row).toBeVisible();
  await expect(row.locator('.sighting-row__photo svg')).toBeVisible();
  await expect(row.locator('.sighting-row__photo svg')).toHaveAttribute('aria-hidden', 'true');
  await expect(row.getByRole('link')).toContainText('Has a photo');
});
