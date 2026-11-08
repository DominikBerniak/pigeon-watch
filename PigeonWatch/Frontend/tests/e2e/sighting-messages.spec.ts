import { test, expect, type Browser, type Page, type Response } from '@playwright/test';

const emptyStorageState = { cookies: [], origins: [] };

const threadResponse = (page: Page, method: string, path: RegExp): Promise<Response> =>
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

test('a non-owner messages the reporter, the reporter reads and replies, and each side sees the unread state', async ({
  browser,
  baseURL,
}) => {
  const stamp = Date.now();
  const ownerName = `Owner ${stamp}`;
  const contacterName = `Contacter ${stamp}`;
  const birdName = `Message bird ${stamp}`;
  const firstMessage = `Is the bird still by the fountain at ${stamp}?`;
  const replyText = `Yes, still there at ${stamp}`;
  const password = `E2e-${stamp}-Aa1!`;

  const owner = await openFreshPage(browser, baseURL);
  const contacter = await openFreshPage(browser, baseURL);

  try {
    await register(owner, `message-owner-${stamp}@example.test`, ownerName, password);
    await register(contacter, `message-contacter-${stamp}@example.test`, contacterName, password);

    await owner.goto('/sightings/new');
    await expect(owner.getByRole('heading', { name: 'Report a sighting' })).toBeVisible();
    await owner.getByRole('textbox', { name: 'Name (optional)' }).fill(birdName);
    await owner.getByRole('textbox', { name: 'Description' }).fill('Grey pigeon with a bent wing');
    await owner.getByRole('textbox', { name: 'Behaviour' }).fill('Walks but cannot fly');
    await owner.getByRole('combobox', { name: 'Status' }).click();
    await owner.getByRole('option', { name: 'Spotted' }).click();
    await owner.getByRole('combobox', { name: 'Criticality' }).click();
    await owner.getByRole('option', { name: 'Urgent' }).click();
    await owner.getByRole('button', { name: 'Use my location' }).click();
    await expect(owner.getByText('No location chosen yet.')).toBeHidden();
    await owner.getByRole('button', { name: 'Report sighting' }).click();
    await owner.waitForURL(/\/sightings\/[0-9a-f-]+$/);
    await expect(owner.getByRole('heading', { name: birdName })).toBeVisible();
    await expect(owner.getByRole('button', { name: `Message ${ownerName}` })).toHaveCount(0);

    const sightingPath = new URL(owner.url()).pathname;

    await contacter.goto(sightingPath);
    await expect(contacter.getByRole('heading', { name: birdName })).toBeVisible();
    await contacter.getByRole('button', { name: `Message ${ownerName}` }).click();

    const dialog = contacter.getByRole('dialog', { name: `Message ${ownerName}` });

    await expect(dialog).toBeVisible();
    await dialog.getByRole('textbox', { name: 'Your message' }).fill(firstMessage);

    const started = threadResponse(contacter, 'POST', /\/sightings\/[0-9a-f-]+\/threads$/);
    await dialog.getByRole('button', { name: 'Send message' }).click();

    expect((await started).status()).toBe(201);
    await contacter.waitForURL(/\/messages\/[0-9a-f-]+\/[0-9a-f-]+$/);
    await expect(contacter.getByRole('region', { name: 'Messages, oldest first' })).toContainText(
      firstMessage,
    );

    await contacter.goto('/messages');
    await expect(contacter.getByRole('link', { name: 'Messages', exact: true })).toBeVisible();

    await owner.goto('/messages');

    const ownerBadge = owner.getByRole('banner').getByRole('link', { name: /^Messages/ });
    const ownerRow = owner
      .getByRole('navigation', { name: 'Conversations' })
      .getByRole('link', { name: new RegExp(contacterName) });

    await expect(ownerBadge).toHaveAccessibleName('Messages, 1 unread');
    await expect(ownerBadge).toContainText('1');
    await expect(ownerRow).toContainText('Unread');
    await expect(ownerRow).toContainText(birdName);

    const markedRead = threadResponse(owner, 'PUT', /\/threads\/[0-9a-f-]+\/read$/);
    await ownerRow.click();

    expect((await markedRead).status()).toBe(204);
    await expect(
      owner.getByRole('heading', { name: `Conversation with ${contacterName}` }),
    ).toBeVisible();

    const ownerThread = owner.getByRole('region', { name: 'Messages, oldest first' });

    await expect(ownerThread).toContainText(firstMessage);
    await expect(ownerThread).toContainText(contacterName);
    await expect(ownerBadge).toHaveAccessibleName('Messages');
    await expect(ownerBadge).not.toContainText('1');
    await expect(ownerRow).not.toContainText('Unread');

    await owner.getByRole('textbox', { name: `Reply to ${contacterName}` }).fill(replyText);

    const replied = threadResponse(owner, 'POST', /\/threads\/[0-9a-f-]+\/messages$/);
    await owner.getByRole('button', { name: 'Send', exact: true }).click();

    expect((await replied).status()).toBe(201);
    await expect(ownerThread).toContainText(replyText);
    await expect(ownerBadge).toHaveAccessibleName('Messages');

    await contacter.reload();

    const contacterBadge = contacter.getByRole('banner').getByRole('link', { name: /^Messages/ });
    const contacterRow = contacter
      .getByRole('navigation', { name: 'Conversations' })
      .getByRole('link', { name: new RegExp(ownerName) });

    await expect(contacterBadge).toHaveAccessibleName('Messages, 1 unread');
    await expect(contacterRow).toContainText('Unread');
    await expect(contacterRow).toContainText(birdName);
  } finally {
    await owner.context().close();
    await contacter.context().close();
  }
});
