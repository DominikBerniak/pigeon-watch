import { test, expect, type Page, type Response } from '@playwright/test';

test.use({
  geolocation: { latitude: 52.07, longitude: 19.48 },
  permissions: ['geolocation'],
  storageState: { cookies: [], origins: [] },
});

const commentResponse = (page: Page, method: string): Promise<Response> =>
  page.waitForResponse(
    (response) =>
      /\/comments(\/[0-9a-f-]+)?$/.test(response.url()) && response.request().method() === method,
  );

test('a comment is posted at the top, an immediate edit hits the cooldown, and a confirmed delete leaves the placeholder', async ({
  page,
}) => {
  const stamp = Date.now();
  const birdName = `Comment bird ${stamp}`;
  const displayName = `Comment tester ${stamp}`;
  const commentText = `Still under the bench at ${stamp}`;
  const password = `E2e-${stamp}-Aa1!`;

  await page.goto('/register');
  await page.getByLabel('Email').fill(`comment-${stamp}@example.test`);
  await page.getByLabel('Display name').fill(displayName);
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

  const heading = page.getByRole('heading', { name: /^Comments/ });
  const toggle = page.getByRole('button', { name: 'Show comments' });

  await expect(heading).toHaveText('Comments (0)');
  await expect(toggle).toHaveAttribute('aria-expanded', 'false');
  await toggle.click();
  await expect(page.getByRole('button', { name: 'Hide comments' })).toHaveAttribute(
    'aria-expanded',
    'true',
  );
  await expect(page.getByText('No comments yet. Be the first to add one.')).toBeVisible();

  await page.getByRole('textbox', { name: 'Add a comment' }).fill(commentText);

  const posted = commentResponse(page, 'POST');
  await page.getByRole('button', { name: 'Post comment' }).click();

  expect((await posted).status()).toBe(201);
  await expect(heading).toHaveText('Comments (1)');
  await expect(page.getByRole('textbox', { name: 'Add a comment' })).toHaveValue('');

  const list = page.getByRole('region', { name: 'Comments, newest first' });
  const first = list.locator('li').first();

  await expect(first).toContainText(displayName);
  await expect(first).toContainText(commentText);

  await first.getByRole('button', { name: 'Edit' }).click();

  const editField = first.getByRole('textbox', { name: 'Edit your comment' });

  await expect(editField).toBeFocused();
  await editField.fill(`${commentText} (edited)`);

  const edited = commentResponse(page, 'PUT');
  await first.getByRole('button', { name: 'Save' }).click();

  expect((await edited).status()).toBe(400);
  await expect(first.getByRole('alert')).toHaveText(
    /You can post or edit another comment in \d+ seconds\./,
  );
  await expect(editField).toHaveValue(`${commentText} (edited)`);

  await first.getByRole('button', { name: 'Cancel' }).click();
  await expect(first).toContainText(commentText);
  await expect(first).not.toContainText('(edited)');

  await first.getByRole('button', { name: 'Delete' }).click();

  const dialog = page.getByRole('dialog', { name: 'Delete this comment?' });

  await expect(dialog).toBeVisible();

  const deleted = commentResponse(page, 'DELETE');
  await dialog.getByRole('button', { name: 'Delete comment' }).click();

  expect((await deleted).status()).toBe(200);
  await expect(dialog).toBeHidden();
  await expect(first).toHaveText('Comment deleted');
  await expect(first).not.toContainText(displayName);
  await expect(first.getByRole('button')).toHaveCount(0);
  await expect(heading).toHaveText('Comments (1)');
});
