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

const lockedHint =
  "The description can't change any more because someone else has commented on this sighting or contacted you about it.";

test('another user comment locks the owner description while a status change still saves, and deleting the comment unlocks it', async ({
  browser,
  baseURL,
}) => {
  const stamp = Date.now();
  const ownerName = `Edit owner ${stamp}`;
  const commenterName = `Edit commenter ${stamp}`;
  const birdName = `Edit bird ${stamp}`;
  const editedDescription = `Grey pigeon with a bent left wing, seen at ${stamp}`;
  const commentText = `Saw it by the bins at ${stamp}`;
  const password = `E2e-${stamp}-Aa1!`;
  const updatePath = /\/sightings\/[0-9a-f-]+$/;

  const owner = await openFreshPage(browser, baseURL);
  const commenter = await openFreshPage(browser, baseURL);

  try {
    await register(owner, `edit-owner-${stamp}@example.test`, ownerName, password);
    await register(commenter, `edit-commenter-${stamp}@example.test`, commenterName, password);

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

    const sightingPath = new URL(owner.url()).pathname;
    const editPath = `${sightingPath}/edit`;
    const ownerDescription = owner.getByRole('textbox', { name: 'Description' });
    const ownerFacts = owner.locator('.sighting-detail__facts');

    await owner.getByRole('link', { name: 'Edit', exact: true }).click();
    await owner.waitForURL((url) => url.pathname === editPath);
    await expect(owner.getByRole('heading', { name: 'Edit sighting' })).toBeVisible();
    await expect(ownerDescription).toBeEnabled();
    await ownerDescription.fill(editedDescription);

    const described = apiResponse(owner, 'PUT', updatePath);
    await owner.getByRole('button', { name: 'Save changes' }).click();

    expect((await described).status()).toBe(200);
    await owner.waitForURL((url) => url.pathname === sightingPath);
    await expect(owner.getByText(editedDescription)).toBeVisible();
    await expect(ownerFacts).toContainText('Spotted');

    await commenter.goto(sightingPath);
    await expect(commenter.getByRole('heading', { name: birdName })).toBeVisible();
    await commenter.getByRole('button', { name: 'Show comments' }).click();
    await commenter.getByRole('textbox', { name: 'Add a comment' }).fill(commentText);

    const posted = apiResponse(commenter, 'POST', /\/sightings\/[0-9a-f-]+\/comments$/);
    await commenter.getByRole('button', { name: 'Post comment' }).click();

    expect((await posted).status()).toBe(201);

    const comment = commenter
      .getByRole('region', { name: 'Comments, newest first' })
      .locator('li')
      .first();

    await expect(comment).toContainText(commentText);

    await owner.goto(editPath);
    await expect(owner.getByRole('heading', { name: 'Edit sighting' })).toBeVisible();
    await expect(ownerDescription).toBeDisabled();
    await expect(ownerDescription).toHaveValue(editedDescription);
    await expect(owner.getByText(lockedHint)).toBeVisible();
    await owner.getByRole('combobox', { name: 'Status' }).click();
    await owner.getByRole('option', { name: 'Caught' }).click();

    const caught = apiResponse(owner, 'PUT', updatePath);
    await owner.getByRole('button', { name: 'Save changes' }).click();

    expect((await caught).status()).toBe(200);
    await owner.waitForURL((url) => url.pathname === sightingPath);
    await expect(ownerFacts).toContainText('Caught');
    await expect(owner.getByText(editedDescription)).toBeVisible();

    await comment.getByRole('button', { name: 'Delete' }).click();

    const dialog = commenter.getByRole('dialog', { name: 'Delete this comment?' });

    await expect(dialog).toBeVisible();

    const deleted = apiResponse(
      commenter,
      'DELETE',
      /\/sightings\/[0-9a-f-]+\/comments\/[0-9a-f-]+$/,
    );
    await dialog.getByRole('button', { name: 'Delete comment' }).click();

    expect((await deleted).status()).toBe(200);
    await expect(comment).toHaveText('Comment deleted');

    await owner.goto(editPath);
    await expect(owner.getByRole('heading', { name: 'Edit sighting' })).toBeVisible();
    await expect(ownerDescription).toBeEnabled();
    await expect(ownerDescription).toHaveValue(editedDescription);
    await expect(owner.getByText(lockedHint)).toBeHidden();

    await commenter.reload();
    await expect(commenter.getByRole('heading', { name: birdName })).toBeVisible();
    await expect(commenter.getByRole('button', { name: 'Show map' })).toBeVisible();
    await expect(commenter.getByRole('link', { name: 'Edit', exact: true })).toHaveCount(0);
    await expect(commenter.getByRole('button', { name: 'Edit', exact: true })).toHaveCount(0);
  } finally {
    await owner.context().close();
    await commenter.context().close();
  }
});
