import { test, expect } from '@playwright/test';
import { mockGuestMatch, showBoardOverview } from './fixture';

test('keyboard opens board, moves focus, and activates one labeled cell', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  const moves = await mockGuestMatch(page);
  await showBoardOverview(page);
  const zoom = page.getByRole('button', { name: 'Phóng bàn cờ' });
  await page.keyboard.press('Tab');
  await page.keyboard.press('Tab');
  await zoom.focus();
  await page.keyboard.press('Enter');
  const center = page.getByRole('gridcell', { name: 'Hàng 11, cột 11, ô trống' });
  await expect(center).toBeFocused();
  await page.keyboard.press('ArrowRight');
  const next = page.getByRole('gridcell', { name: 'Hàng 11, cột 12, ô trống' });
  await expect(next).toBeFocused();
  await page.keyboard.press('Space');
  await expect.poll(() => moves.length).toBe(1);
  expect(moves[0]).toEqual({ row: 10, column: 11 });
  await expect(page.getByRole('status')).toContainText('Bot đang suy nghĩ');
});

test('reduced motion disables decorative motion and focus remains visible', async ({ page, isMobile }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await page.setViewportSize({ width: 390, height: 844 });
  await mockGuestMatch(page);
  await showBoardOverview(page);
  const zoom = page.getByRole('button', { name: 'Phóng bàn cờ' });
  await page.keyboard.press('Tab');
  await zoom.focus();
  expect(await zoom.evaluate(element => getComputedStyle(element).outlineStyle)).not.toBe('none');
  expect(await zoom.evaluate(element => getComputedStyle(element).transitionDuration)).toBe('0s');
  // The overview is read-only on touch screens; a mouse still plays its ~17px cells directly.
  await expect(page.getByRole('grid', { name: 'Bàn cờ 20 nhân 20' })).toHaveAttribute('aria-readonly', isMobile ? 'true' : 'false');
});
