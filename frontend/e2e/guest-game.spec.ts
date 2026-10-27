import { expect, test } from '@playwright/test';

test.describe.configure({ timeout: 90_000 });

test('guest move survives a deep-link reload and remains in active history', async ({ page }) => {
  await page.goto('/play?scope=guest');
  await page.getByRole('button', { name: 'Bắt đầu chơi khách' }).click();
  await expect(page.getByRole('heading', { name: 'Ván 1' })).toBeVisible();
  const zoom = page.getByRole('button', { name: 'Phóng bàn cờ' });
  if (await zoom.isVisible()) await zoom.click();
  await page.getByRole('gridcell', { name: 'Hàng 11, cột 11, ô trống' }).dblclick();
  await expect(page.getByRole('gridcell', { name: 'Hàng 11, cột 11, quân X' })).toBeVisible();
  await expect(page.getByRole('gridcell', { name: /quân O/ }).first()).toBeVisible({ timeout: 30_000 });
  await page.goto('/history?scope=guest');
  const matchUrl = await page.getByRole('region', { name: 'Ván dở' }).getByRole('link', { name: 'Ván dở 1' }).getAttribute('href');
  expect(matchUrl).toBeTruthy();
  await page.goto(matchUrl!);
  await page.reload();
  await expect(page).toHaveURL(new RegExp(`/play/[^/]+\\?scope=guest$`));
  await expect(page.getByRole('gridcell', { name: 'Hàng 11, cột 11, quân X' })).toBeVisible();
  await page.goto('/history?scope=guest');
  await expect(page.getByRole('region', { name: 'Ván dở' }).getByRole('link', { name: 'Ván dở 1' })).toBeVisible();
});
