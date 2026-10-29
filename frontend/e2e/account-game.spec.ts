import { expect, test } from '@playwright/test';

test.describe.configure({ timeout: 90_000 });

test('account can log out and log back in to its saved match', async ({ page }) => {
  const email = `phase4-${crypto.randomUUID()}@example.test`;
  const password = `P4-${crypto.randomUUID()}!`;
  await page.goto('/register');
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Mật khẩu').fill(password);
  await page.getByRole('button', { name: 'Đăng ký' }).click();
  await expect(page.getByRole('button', { name: 'Đăng xuất' })).toBeVisible();
  await page.goto('/play?scope=account');
  await expect(page.getByRole('heading', { name: 'Ván 1' })).toBeVisible();
  await page.goto('/history?scope=account');
  const matchUrl = await page.getByRole('region', { name: 'Ván dở' }).getByRole('link', { name: 'Ván dở 1' }).getAttribute('href');
  expect(matchUrl).toBeTruthy();
  await page.getByRole('button', { name: 'Đăng xuất' }).click();
  await expect(page.getByRole('button', { name: 'Đăng xuất' })).toHaveCount(0);
  await page.goto(matchUrl!);
  await expect(page.getByRole('link', { name: 'Đăng nhập để tiếp tục' })).toBeVisible();
  await page.goto('/login');
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Mật khẩu').fill(password);
  await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Đăng xuất' })).toBeVisible();
  await page.goto(matchUrl!);
  await expect(page.getByRole('heading', { name: 'Ván 1' })).toBeVisible();
  await page.goto('/leaderboard');
  await expect(page.getByRole('heading', { name: 'Vị trí của bạn' })).toBeVisible();
});
