import { expect, type Page } from '@playwright/test';

export const matchId = '821412da-c99d-45be-a7e9-328fb464380b';

export async function openSiteMenu(page: Page) {
  const menu = page.getByRole('button', { name: 'Menu' });
  if (await menu.isVisible() && await menu.getAttribute('aria-expanded') === 'false') {
    await menu.click();
  }
}

// Touch devices open the zoomed board by default; mouse devices start on the overview.
export async function openZoomedBoard(page: Page) {
  const zoom = page.getByRole('button', { name: 'Phóng bàn cờ' });
  const overview = page.getByRole('button', { name: 'Toàn cảnh' });
  await expect(zoom.or(overview)).toBeVisible();
  if (await zoom.isVisible()) await zoom.click();
  await expect(overview).toBeVisible();
}

export async function showBoardOverview(page: Page) {
  const zoom = page.getByRole('button', { name: 'Phóng bàn cờ' });
  const overview = page.getByRole('button', { name: 'Toàn cảnh' });
  await expect(zoom.or(overview)).toBeVisible();
  if (await overview.isVisible()) await overview.click();
  await expect(zoom).toBeVisible();
}

const gameId = '933412da-c99d-45be-a7e9-328fb464380b';
const base = {
  matchId, boardSize: 20, boardColumns: 20, ruleVersion: 'v1', revision: 0, xScore: 0, oScore: 0,
  currentGame: { id: gameId, number: 1, openingStone: 'X', aiVersion: null, status: 'InProgress', nextStone: 'X', turnStatus: 'human', aiErrorCode: null, aiRetryAt: null, moves: [] },
};

export async function mockGuestMatch(page: Page, startAtHome = false, beforeStart?: () => Promise<void>) {
  const moves: { row: number; column: number }[] = [];
  let snapshot: typeof base = base;
  let guestCreated = false;
  await page.route('**/api/v1/**', async route => {
    const url = new URL(route.request().url());
    const path = url.pathname;
    if (path === '/api/v1/auth/me') return route.fulfill({ status: 401, json: { code: 'authentication_required', message: 'Sign in' } });
    if (path === '/api/v1/guest-sessions/current') return route.fulfill(guestCreated
      ? { json: { guestSessionId: 'a1b412da-c99d-45be-a7e9-328fb464380b' } }
      : { status: 401, json: { code: 'guest_session_required', message: 'No guest cookie' } });
    if (path === '/api/v1/auth/csrf') return route.fulfill({ json: { requestToken: 'test-csrf' } });
    if (path === '/api/v1/guest-sessions') { guestCreated = true; return route.fulfill({ json: { guestSessionId: 'a1b412da-c99d-45be-a7e9-328fb464380b' } }); }
    if (path === '/api/v1/matches/active') return route.fulfill({ json: { items: [{ matchId, gameId, revision: 0, updatedAt: '2026-10-27T00:00:00Z' }], nextCursor: null } });
    if (path === '/api/v1/matches' && route.request().method() === 'POST') {
      const command = route.request().postDataJSON() as { boardSize?: number; boardColumns?: number };
      snapshot = { ...base, boardSize: command.boardSize ?? 20, boardColumns: command.boardColumns ?? 20 };
      return route.fulfill({ status: 201, json: snapshot });
    }
    if (path === `/api/v1/matches/${matchId}/moves`) {
      const command = route.request().postDataJSON() as { row: number; column: number };
      moves.push({ row: command.row, column: command.column });
      snapshot = { ...base, revision: 1, currentGame: { ...base.currentGame, turnStatus: 'ai_pending', nextStone: 'O', moves: [{ step: 1, row: command.row, column: command.column, stone: 'X' }] } } as typeof base;
      return route.fulfill({ status: 202, json: snapshot });
    }
    if (path === `/api/v1/matches/${matchId}`) return route.fulfill({ json: snapshot });
    return route.fulfill({ status: 404, json: { code: 'not_found', message: path } });
  });
  await page.goto(startAtHome ? '/' : '/play?scope=guest');
  if (startAtHome) {
    await page.getByRole('button', { name: 'Chơi mới' }).click();
    await page.getByRole('button', { name: 'Chơi khách' }).click();
    await beforeStart?.();
    await page.getByRole('link', { name: 'Bắt đầu trận' }).click();
  } else {
    await beforeStart?.();
    await page.getByRole('button', { name: 'Bắt đầu chơi khách' }).click();
  }
  await page.getByRole('heading', { name: 'Ván 1' }).waitFor();
  await page.locator('.versus-intro').waitFor({ state: 'detached' });
  return moves;
}
