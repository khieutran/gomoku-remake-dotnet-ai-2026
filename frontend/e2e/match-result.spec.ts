import { expect, test } from '@playwright/test';
import { matchId, mockGuestMatch } from './fixture';

test('finished game highlights the five stones and shows replay and next game over the board', async ({ page }) => {
  await mockGuestMatch(page);
  const gameId = '933412da-c99d-45be-a7e9-328fb464380b';
  const moves = [4, 5, 6, 7, 8].map((column, index) => ({
    step: index + 1, row: 7, column, stone: 'X',
  }));
  await page.route(`**/api/v1/matches/${matchId}?**`, route => route.fulfill({ json: {
    matchId, boardSize: 20, ruleVersion: 'v1', revision: 5, xScore: 1, oScore: 0,
    currentGame: {
      id: gameId, number: 1, openingStone: 'X', aiVersion: null, status: 'XWon',
      nextStone: null, turnStatus: 'finished', aiErrorCode: null, aiRetryAt: null, moves,
    },
  } }));
  await page.reload();
  await page.getByRole('button', { name: 'Bắt đầu chơi khách' }).click();

  const cutscene = page.locator('.match-result-overlay--cutscene video');
  await expect(cutscene).toBeVisible();
  await expect(cutscene).toHaveAttribute('src', /\/videos\/finish\/frame-(sword-v2|arrow)\.webm$/);
  await page.getByRole('button', { name: 'Bỏ qua hoạt cảnh' }).click();
  const result = page.getByRole('region', { name: 'Kết quả ván' });
  await expect(result).toContainText('Bạn thắng');
  await expect(result).toContainText('5 quân liên tiếp theo hàng ngang');
  await expect(page.locator('.gomoku-cell[data-winning-cell]')).toHaveCount(5);
  await expect(result.getByRole('button', { name: 'Chơi tiếp' })).toBeVisible();
  await expect(result.getByRole('link', { name: 'Xem lại' })).toHaveAttribute('href', `/history/${gameId}?scope=guest`);
});
