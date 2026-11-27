import { test, expect, type Browser, type Page, type Response } from '@playwright/test';

const emptyStorageState = { cookies: [], origins: [] };

const apiResponse = (page: Page, method: string, path: RegExp): Promise<Response> =>
  page.waitForResponse(
    (response) =>
      path.test(new URL(response.url()).pathname) && response.request().method() === method,
  );

const openFreshPage = async (browser: Browser, baseURL: string | undefined): Promise<Page> => {
  const context = await browser.newContext({
    baseURL,
    storageState: emptyStorageState,
    geolocation: { latitude: 52.07, longitude: 19.48 },
    permissions: ['geolocation'],
  });

  return context.newPage();
};

const register = async (page: Page, email: string, displayName: string, password: string) => {
  await page.goto('/register');
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Display name').fill(displayName);
  await page.getByLabel('Password', { exact: true }).fill(password);
  await page.getByLabel('Confirm password').fill(password);
  await page.getByRole('button', { name: 'Create account' }).click();
  await expect(page.getByRole('banner').getByRole('button')).toBeVisible();
};

const logIn = async (page: Page, email: string, password: string): Promise<string[]> => {
  const general = apiResponse(page, 'GET', /\/configuration\/general$/);

  await page.goto('/login');
  await expect(async () => {
    await page.getByLabel('Email').fill('');
    await page.getByLabel('Email').fill(email);
    await page.getByLabel('Password', { exact: true }).fill('');
    await page.getByLabel('Password', { exact: true }).fill(password);
    await page.getByRole('button', { name: 'Log in' }).click();
    await page.waitForURL((url) => !url.pathname.startsWith('/login'), { timeout: 5_000 });
  }).toPass();
  await expect(page.getByRole('banner').getByRole('button')).toBeVisible();

  const configuration = (await (await general).json()) as { currentUser: { roles: string[] } };

  return configuration.currentUser.roles;
};

const notAvailable = "This sighting doesn't exist or is no longer available.";
const removedNotice = 'This sighting was removed by an admin.';

test('an admin removes a reporter sighting so it leaves the map for a member and shows as Removed to the reporter, and restoring brings it back', async ({
  browser,
  baseURL,
}) => {
  const adminEmail = process.env.E2E_ADMIN_USERNAME;
  const adminPassword = process.env.E2E_ADMIN_PASSWORD;
  if (!adminEmail || !adminPassword) {
    throw new Error(
      'Set E2E_ADMIN_USERNAME and E2E_ADMIN_PASSWORD (see context/foundation/test-stack.md, ## E2E)',
    );
  }

  const stamp = Date.now();
  const reporterName = `Removal reporter ${stamp}`;
  const memberName = `Removal member ${stamp}`;
  const birdName = `Removal bird ${stamp}`;
  const password = `E2e-${stamp}-Aa1!`;
  const markerName = `${birdName}, Urgent`;

  const reporter = await openFreshPage(browser, baseURL);
  const admin = await openFreshPage(browser, baseURL);
  const member = await openFreshPage(browser, baseURL);

  try {
    const adminRoles = await logIn(admin, adminEmail, adminPassword);

    expect(
      adminRoles,
      'E2E_ADMIN_USERNAME must be listed in Moderation:AdminEmails and the API restarted after it registered',
    ).toContain('Admin');

    await register(reporter, `removal-reporter-${stamp}@example.test`, reporterName, password);

    await reporter.goto('/sightings/new');
    await expect(reporter.getByRole('heading', { name: 'Report a sighting' })).toBeVisible();
    await reporter.getByRole('textbox', { name: 'Name (optional)' }).fill(birdName);
    await reporter.getByRole('textbox', { name: 'Description' }).fill('Grey pigeon with a bent wing');
    await reporter.getByRole('textbox', { name: 'Behaviour' }).fill('Walks but cannot fly');
    await reporter.getByRole('combobox', { name: 'Status' }).click();
    await reporter.getByRole('option', { name: 'Spotted' }).click();
    await reporter.getByRole('combobox', { name: 'Criticality' }).click();
    await reporter.getByRole('option', { name: 'Urgent' }).click();
    await reporter.getByRole('button', { name: 'Use my location' }).click();
    await expect(reporter.getByText('No location chosen yet.')).toBeHidden();
    await reporter.getByRole('button', { name: 'Report sighting' }).click();
    await reporter.waitForURL(/\/sightings\/[0-9a-f-]+$/);
    await expect(reporter.getByRole('heading', { name: birdName })).toBeVisible();

    const sightingPath = new URL(reporter.url()).pathname;
    const sightingId = sightingPath.split('/').pop() ?? '';

    await admin.goto(sightingPath);
    await expect(admin.getByRole('heading', { name: birdName })).toBeVisible();
    await expect(admin.getByRole('button', { name: 'Restore', exact: true })).toHaveCount(0);
    await admin.getByRole('button', { name: 'Remove', exact: true }).click();

    const removeDialog = admin.getByRole('dialog', { name: 'Remove this sighting?' });

    await expect(removeDialog).toBeVisible();

    const removed = apiResponse(admin, 'DELETE', /\/sightings\/[0-9a-f-]+$/);
    await removeDialog.getByRole('button', { name: 'Remove sighting' }).click();

    expect((await removed).status()).toBe(200);
    await expect(removeDialog).toBeHidden();
    await expect(admin.getByText(removedNotice)).toBeVisible();
    await expect(admin.getByRole('button', { name: 'Restore', exact: true })).toBeVisible();
    await expect(admin.getByRole('button', { name: 'Remove', exact: true })).toHaveCount(0);

    await reporter.goto('/profile');
    await expect(reporter.getByRole('heading', { name: 'My sightings' })).toBeVisible();

    const reporterRow = reporter.getByRole('link', { name: birdName });

    await expect(reporterRow).toBeVisible();
    await expect(reporterRow).toContainText('Removed');

    await register(member, `removal-member-${stamp}@example.test`, memberName, password);

    await member.goto(sightingPath);
    await expect(member.getByText(notAvailable)).toBeVisible();
    await expect(member.getByRole('heading', { name: birdName })).toHaveCount(0);

    const hiddenNearby = apiResponse(member, 'GET', /\/sightings\/nearby$/);
    await member.goto('/');
    const hiddenItems = ((await (await hiddenNearby).json()) as { items: { id: string }[] }).items;

    expect(hiddenItems.map((item) => item.id)).not.toContain(sightingId);
    await expect(member.getByRole('button', { name: markerName })).toHaveCount(0);

    const restored = apiResponse(admin, 'POST', /\/sightings\/[0-9a-f-]+\/restore$/);
    await admin.getByRole('button', { name: 'Restore', exact: true }).click();

    expect((await restored).status()).toBe(200);
    await expect(admin.getByText(removedNotice)).toBeHidden();
    await expect(admin.getByRole('button', { name: 'Remove', exact: true })).toBeVisible();

    await member.goto('/');
    await expect(member.getByRole('button', { name: markerName })).toBeVisible();
  } finally {
    await reporter.context().close();
    await admin.context().close();
    await member.context().close();
  }
});
