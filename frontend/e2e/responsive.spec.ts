import { test, expect } from '@playwright/test';
import { mockGuestMatch } from './fixture';

for (const width of [360, 390, 430, 1280, 1440]) {
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
