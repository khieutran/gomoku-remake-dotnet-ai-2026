import { test, expect } from '@playwright/test';
import { mockGuestMatch } from './fixture';

test('start menu and board fit one viewport on desktop and mobile', async ({ page, isMobile }) => {
  await page.setViewportSize(isMobile ? { width: 390, height: 844 } : { width: 1440, height: 900 });
  await page.goto('/');
  await expect(page.getByRole('region', { name: 'Chọn cách chơi' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Chơi mới' })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollHeight)).toBeLessThanOrEqual(isMobile ? 844 : 900);
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(isMobile ? 390 : 1440);
});

test('starting on desktop keeps the main frame and shows two panels on its right without reloading', async ({ page, isMobile }) => {
  test.skip(isMobile, 'Desktop frame sizing is tested with a mouse pointer');
  await page.setViewportSize({ width: 1440, height: 900 });
  let before: { x: number; y: number; width: number; height: number } | null = null;
  await mockGuestMatch(page, true, async () => {
    before = await page.locator('.game-workspace-main').boundingBox();
    await page.evaluate(() => { (window as Window & { workspaceMarker?: string }).workspaceMarker = 'same-document'; });
  });
  const after = await page.locator('.game-workspace-main').boundingBox();
  expect(after?.x).toBeCloseTo(before!.x, 0);
  expect(after?.y).toBeCloseTo(before!.y, 0);
  expect(after?.width).toBeCloseTo(before!.width, 0);
  expect(Math.abs(after!.height - before!.height)).toBeLessThanOrEqual(24);
  expect(await page.evaluate(() => (window as Window & { workspaceMarker?: string }).workspaceMarker)).toBe('same-document');
  const status = await page.getByRole('region', { name: 'Trạng thái ván đấu' }).boundingBox();
  const score = await page.getByRole('region', { name: 'Điểm trận' }).boundingBox();
  const board = await page.getByRole('grid', { name: 'Bàn cờ 20 nhân 30' }).boundingBox();
  expect(status && score && board).toBeTruthy();
  expect(status!.x).toBeGreaterThan(board!.x + board!.width);
  expect(score!.x).toBeGreaterThan(board!.x + board!.width);
  expect(status!.y).toBeLessThan(score!.y);
  expect(board!.width).toBeLessThanOrEqual(830);
  expect(await page.evaluate(() => document.documentElement.scrollHeight)).toBeLessThanOrEqual(900);
  await expect(page.getByRole('button', { name: 'Phóng bàn cờ' })).toHaveCount(0);
  await expect(page.getByRole('region', { name: 'Điểm trận' }).getByRole('link', { name: 'Rời bàn cờ' })).toBeVisible();
});

test('mobile keeps status above the board and actions below without page overflow', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await mockGuestMatch(page);
  const status = await page.getByRole('region', { name: 'Trạng thái ván đấu' }).boundingBox();
  const score = await page.getByRole('region', { name: 'Điểm trận' }).boundingBox();
  const board = await page.getByRole('grid', { name: 'Bàn cờ 20 nhân 20' }).boundingBox();
  expect(status && score && board).toBeTruthy();
  expect(status!.y + status!.height).toBeLessThanOrEqual(board!.y);
  expect(score!.y).toBeGreaterThan(board!.y + board!.height);
  expect(board!.y).toBeLessThan(460);
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(390);
  expect(await page.evaluate(() => document.documentElement.scrollHeight)).toBeLessThanOrEqual(844);
});

test('fixed mobile board stays in one short viewport when zoomed', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 667 });
  await mockGuestMatch(page, true);
  await expect(page.getByRole('grid', { name: 'Bàn cờ 20 nhân 20' })).toBeVisible();
  await page.getByRole('button', { name: 'Phóng bàn cờ' }).click();
  expect(await page.evaluate(() => document.documentElement.scrollHeight)).toBeLessThanOrEqual(667);
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(390);
});

test('replay keeps the board frame and controls in one short mobile viewport', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 667 });
  await mockGuestMatch(page);
  const gameId = '933412da-c99d-45be-a7e9-328fb464380b';
  await page.route(`**/api/v1/history/${gameId}**`, route => route.fulfill({ json: {
    gameId, matchId: '821412da-c99d-45be-a7e9-328fb464380b', number: 1,
    status: 'XWon', openingStone: 'X', ruleVersion: 'v1', boardSize: 40, aiVersion: null,
    startedAt: '2026-10-28T00:00:00Z', finishedAt: '2026-10-28T01:00:00Z',
    moves: [{ step: 1, row: 0, column: 0, stone: 'X', actor: 'Human', playedAt: '2026-10-28T00:01:00Z' }],
  } }));
  await page.goto(`/history/${gameId}?scope=guest`);
  await expect(page.getByText('Bước 0 / 1')).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollHeight)).toBeLessThanOrEqual(667);
  await page.getByRole('button', { name: 'Phóng bàn cờ' }).click();
  expect(await page.evaluate(() => document.documentElement.scrollHeight)).toBeLessThanOrEqual(667);
  await page.setViewportSize({ width: 320, height: 568 });
  expect(await page.evaluate(() => document.documentElement.scrollHeight)).toBeLessThanOrEqual(568);
});

for (const width of [360, 390, 430, 1280, 1440, 2048]) {
  test(`board stays within page at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 800 });
    await mockGuestMatch(page);
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
    if (width < 800) {
      await expect(page.getByRole('button', { name: 'Phóng bàn cờ' })).toBeVisible();
      await page.getByRole('button', { name: 'Phóng bàn cờ' }).click();
      const cell = page.getByRole('gridcell', { name: 'Hàng 11, cột 11, ô trống' });
      const box = await cell.boundingBox();
      expect(box?.width).toBeGreaterThanOrEqual(40);
      expect(box?.height).toBeGreaterThanOrEqual(40);
      expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
    }
  });
}

test('landscape and 200% page zoom keep board actions reachable', async ({ page }) => {
  await page.setViewportSize({ width: 740, height: 360 });
  const moves = await mockGuestMatch(page);
  await page.evaluate(() => { document.documentElement.style.zoom = '2'; });
  await page.getByRole('button', { name: 'Phóng bàn cờ' }).click();
  const cell = page.getByRole('gridcell', { name: 'Hàng 11, cột 11, ô trống' });
  await cell.click();
  await expect.poll(() => moves.length).toBe(1);
  expect(moves[0]).toEqual({ row: 10, column: 10 });
  expect(await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)).toBeLessThanOrEqual(0);
});

test('touch selects exactly one zoomed cell', async ({ page, isMobile }) => {
  test.skip(!isMobile, 'Touchscreen is configured in the mobile project');
  await page.setViewportSize({ width: 390, height: 844 });
  const moves = await mockGuestMatch(page);
  await page.getByRole('button', { name: 'Phóng bàn cờ' }).click();
  const cell = page.getByRole('gridcell', { name: 'Hàng 11, cột 11, ô trống' });
  await cell.scrollIntoViewIfNeeded();
  const box = await cell.boundingBox();
  expect(box).not.toBeNull();
  await page.touchscreen.tap(box!.x + box!.width / 2, box!.y + box!.height / 2);
  await expect.poll(() => moves.length).toBe(1);
  expect(moves[0]).toEqual({ row: 10, column: 10 });
});
