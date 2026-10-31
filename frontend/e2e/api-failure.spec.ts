import { expect, test } from '@playwright/test';
import { openZoomedBoard } from './fixture';
import { execFileSync } from 'node:child_process';

test.describe.configure({ timeout: 90_000 });

test('stale revision receives a real 409 and does not add another X', async ({ page }) => {
  await page.goto('/play?scope=guest');
  await page.getByRole('button', { name: 'Bắt đầu chơi khách' }).click();
  await expect(page.getByRole('heading', { name: 'Ván 1' })).toBeVisible();
  const matchId = await page.evaluate(async () => {
    const page = await fetch('/api/v1/matches/active?scope=guest&limit=20', { credentials: 'same-origin' }).then(response => response.json()) as { items: { matchId: string }[] };
    return page.items[0].matchId;
  });
  const result = await page.evaluate(async id => {
    const csrf = await fetch('/api/v1/auth/csrf', { credentials: 'same-origin' }).then(response => response.json()) as { requestToken: string };
    const response = await fetch(`/api/v1/matches/${id}/moves?scope=guest`, {
      method: 'POST', credentials: 'same-origin', headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': csrf.requestToken },
      body: JSON.stringify({ commandId: crypto.randomUUID(), row: 10, column: 10, expectedRevision: 1 }),
    });
    return { status: response.status, body: await response.json() as { code?: string } };
  }, matchId);
  expect(result.status).toBe(409);
  expect(result.body.code).toBe('stale_revision');
  await page.reload();
  await expect(page.getByRole('gridcell', { name: /quân X/ })).toHaveCount(0);
});

test('one browser connection drop offers retry without duplicate X', async ({ page }) => {
  await page.goto('/play?scope=guest');
  await page.getByRole('button', { name: 'Bắt đầu chơi khách' }).click();
  await expect(page.getByRole('heading', { name: 'Ván 1' })).toBeVisible();
  let dropped = false;
  await page.route('**/api/v1/matches/*/moves?scope=guest', route => {
    if (!dropped) { dropped = true; return route.abort('failed'); }
    return route.continue();
  });
  await openZoomedBoard(page);
  await page.getByRole('gridcell', { name: 'Hàng 11, cột 11, ô trống' }).click();
  await expect(page.getByRole('button', { name: 'Thử lại nước đi' })).toBeVisible();
  await page.getByRole('button', { name: 'Thử lại nước đi' }).click();
  await expect(page.getByRole('gridcell', { name: 'Hàng 11, cột 11, quân X' })).toBeVisible();
  await page.reload();
  await expect(page.getByRole('gridcell', { name: /quân X/ })).toHaveCount(1);
});

test('Redis outage keeps the public leaderboard readable from the API and UI', async ({ page }) => {
  test.skip(process.env.GOMOKU_E2E_REDIS_FAULT !== '1', 'Run only in the isolated Compose fault stage');
  test.setTimeout(120_000);
  await page.goto('/leaderboard');
  await expect(page.getByRole('heading', { name: 'Bảng xếp hạng' })).toBeVisible();
  execFileSync('docker', ['compose', 'stop', 'redis'], { stdio: 'pipe' });
  try {
    const response = await page.request.get('/api/v1/leaderboard?limit=20');
    expect(response.status()).toBe(200);
    await page.reload();
    await expect(page.getByRole('table', { name: 'Bảng xếp hạng' }).or(
      page.getByText('Chưa có tài khoản nào trên bảng xếp hạng.')
    )).toBeVisible();
    await expect(page.getByRole('alert')).toHaveCount(0);
  } finally {
    execFileSync('docker', ['compose', 'start', 'redis'], { stdio: 'pipe' });
  }
});
