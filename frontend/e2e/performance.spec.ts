import { test, expect } from '@playwright/test';

test('entry route defers history and leaderboard code until navigation', async ({ page }) => {
  const modules: string[] = [];
  page.on('request', request => {
    if (/\.(js|tsx)(\?|$)/.test(request.url())) modules.push(request.url());
  });
  await page.route('**/api/v1/auth/me', route => route.fulfill({ status: 401, json: { code: 'authentication_required', message: 'Sign in' } }));
  await page.route('**/api/v1/leaderboard?*', route => route.fulfill({ json: { items: [] } }));
  await page.goto('/');
  await page.getByRole('heading', { name: 'Thử sức nếu bạn dám!' }).waitFor();
  expect(modules.filter(url => /HistoryPage|LeaderboardPage|ReplayPage/.test(url))).toEqual([]);
  if (await page.getByRole('button', { name: 'Menu' }).isVisible()) {
    await page.getByRole('button', { name: 'Menu' }).click();
  }
  await page.getByRole('link', { name: 'Xếp hạng' }).click();
  const heading = page.getByRole('heading', { name: /xếp hạng/i });
  await heading.waitFor();
  await expect(heading).toBeFocused();
  expect(modules.some(url => /LeaderboardPage/.test(url))).toBe(true);
});
