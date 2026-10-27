import { expect, test } from '@playwright/test';

test.describe.configure({ timeout: 90_000 });

test('lost account cookie hides its match until the same account signs in again', async ({ page, context }) => {
  const email = `phase4-recovery-${crypto.randomUUID()}@example.test`;
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
  await page.goto(matchUrl!);
  await context.clearCookies();
  await page.reload();
  await expect(page.getByRole('link', { name: 'Đăng nhập để tiếp tục' })).toBeVisible();
  await expect(page.getByRole('grid', { name: /Bàn cờ/ })).toHaveCount(0);
  await page.goto('/login');
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Mật khẩu').fill(password);
  await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Đăng xuất' })).toBeVisible();
  await page.goto(matchUrl!);
  await expect(page.getByRole('heading', { name: 'Ván 1' })).toBeVisible();
});

test('account scope cannot read a guest-owned match', async ({ page }) => {
  await page.goto('/play?scope=guest');
  await page.getByRole('button', { name: 'Bắt đầu chơi khách' }).click();
  await expect(page.getByRole('heading', { name: 'Ván 1' })).toBeVisible();
  await page.getByRole('link', { name: 'Lịch sử' }).click();
  const guestHref = await page.getByRole('region', { name: 'Ván dở' }).getByRole('link', { name: 'Ván dở 1' }).getAttribute('href');
  const guestId = guestHref?.split('/play/')[1]?.split('?')[0];
  expect(guestId).toBeTruthy();
  const email = `phase4-owner-${crypto.randomUUID()}@example.test`;
  await page.goto('/register');
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Mật khẩu').fill(`P4-${crypto.randomUUID()}!`);
  await page.getByRole('button', { name: 'Đăng ký' }).click();
  await expect(page.getByRole('button', { name: 'Đăng xuất' })).toBeVisible();
  await page.goto(`/play/${guestId}?scope=account`);
  await expect(page.getByRole('alert')).toBeVisible();
  await expect(page.getByRole('grid', { name: /Bàn cờ/ })).toHaveCount(0);
});

test('lost guest cookie cannot recover private active history', async ({ page, context }) => {
  await page.goto('/play?scope=guest');
  await page.getByRole('button', { name: 'Bắt đầu chơi khách' }).click();
  await expect(page.getByRole('heading', { name: 'Ván 1' })).toBeVisible();
  await context.clearCookies();
  await page.getByRole('link', { name: 'Lịch sử' }).click();
  await expect(page.getByRole('alert')).toContainText('Không thể truy cập phiên khách cũ');
  await expect(page.getByRole('link', { name: 'Ván dở 1' })).toHaveCount(0);
});
