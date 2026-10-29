import { render, screen, within, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { SessionContext, type Session } from '../session/sessionContext';
import { listActive, listHistory } from './historyApi';
import { HistoryPage } from './HistoryPage';
import { ReplayPage } from './ReplayPage';

const ownerId = 'c4b94753-4f50-4515-925d-59c43299fe2e';
const guestId = '329d3f45-201e-43e9-b90e-249ea2e780f3';
const game1 = '821412da-c99d-45be-a7e9-328fb464380b';
const game2 = 'e84b8830-57ea-4d53-a7ab-6e39b870dc76';
const match1 = 'dbca1988-25a4-4a87-92bc-ec6045f8c2a1';
const match2 = '8efab559-cd15-4960-a776-78894f0ae5a1';
const date = '2026-10-26T10:00:00+00:00';
const summary = (gameId: string, matchId: string) => ({ gameId, matchId, number: 1, status: 'XWon', openingStone: 'X', ruleVersion: 'v1', boardSize: 20, aiVersion: null, startedAt: date, finishedAt: date, moveCount: 2 });
const active = (matchId: string, gameId: string) => ({ matchId, gameId, revision: 2, updatedAt: date });
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status });
const unauthorized = () => json({ code: 'authentication_required', message: 'Session expired.' }, 401);

function renderFeature(element: React.ReactNode, session: Session, query = new QueryClient({ defaultOptions: { queries: { retry: false } } })) {
  const value = { session, ready: true, error: null, chooseGuest: vi.fn(), login: vi.fn(), register: vi.fn(), logout: vi.fn(), requireAccount: vi.fn().mockResolvedValue(undefined) };
  const view = render(<QueryClientProvider client={query}><MemoryRouter><SessionContext.Provider value={value}>{element}</SessionContext.Provider></MemoryRouter></QueryClientProvider>);
  return { ...view, query, value };
}
beforeEach(() => vi.stubGlobal('ResizeObserver', class { observe() {} disconnect() {} }));
afterEach(() => vi.unstubAllGlobals());

it('uses each endpoint cursor and never sends offset to active matches', async () => {
  const requests: string[] = [];
  vi.stubGlobal('fetch', vi.fn(async (url: string) => {
    requests.push(url);
    if (url.startsWith('/api/v1/matches/active')) return json({ items: [], nextCursor: 'active-next' });
    if (url.startsWith('/api/v1/history')) return json({ items: [], nextCursor: 'history-next' });
    throw Error(url);
  }));
  expect((await listActive('guest', null, 2)).nextCursor).toBe('active-next');
  expect((await listActive('guest', 'active-next', 2)).nextCursor).toBe('active-next');
  expect((await listHistory('guest', 'history-next', 2)).nextCursor).toBe('history-next');
  expect(requests).toEqual([
    '/api/v1/matches/active?scope=guest&limit=2',
    '/api/v1/matches/active?scope=guest&limit=2&cursor=active-next',
    '/api/v1/history?scope=guest&limit=2&cursor=history-next',
  ]);
});

it('keeps earlier cards when loading more from each independent list', async () => {
  const requests: string[] = [];
  vi.stubGlobal('fetch', vi.fn(async (url: string) => {
    requests.push(url);
    const parsed = new URL(url, 'https://same-origin.test');
    if (parsed.pathname.endsWith('/matches/active')) return json(parsed.searchParams.has('cursor') ? { items: [active(match2, game2)], nextCursor: null } : { items: [active(match1, game1)], nextCursor: 'active-next' });
    if (parsed.pathname.endsWith('/history')) return json(parsed.searchParams.has('cursor') ? { items: [summary(game2, match2)], nextCursor: null } : { items: [summary(game1, match1)], nextCursor: 'history-next' });
    throw Error(url);
  }));
  renderFeature(<HistoryPage scope="account" />, { scope: 'account', ownerId });
  const unfinished = await screen.findByRole('region', { name: 'Ván dở' });
  const completed = await screen.findByRole('region', { name: 'Ván đã xong' });
  await within(unfinished).findByRole('link', { name: /Ván dở 1/ });
  await within(completed).findByRole('link', { name: /Ván 1/ });
  const user = userEvent.setup();
  await user.click(within(unfinished).getByRole('button', { name: 'Xem thêm' }));
  await user.click(within(completed).getByRole('button', { name: 'Xem thêm' }));
  await waitFor(() => expect(within(unfinished).getAllByRole('link')).toHaveLength(2));
  expect(within(completed).getAllByRole('link')).toHaveLength(2);
  expect(requests.some(url => url.includes('cursor=active-next') && url.includes('/matches/active'))).toBe(true);
  expect(requests.some(url => url.includes('cursor=history-next') && url.includes('/history'))).toBe(true);
  expect(requests.every(url => !url.includes('offset='))).toBe(true);
});

it('does not show another owner cached history in the same browser', async () => {
  vi.stubGlobal('fetch', vi.fn(async (url: string) => {
    if (url.includes('/matches/active')) return json({ items: [], nextCursor: null });
    if (url.includes('/history')) return json({ items: [], nextCursor: null });
    throw Error(url);
  }));
  const query = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  query.setQueryData(['owner', 'guest', guestId, 'history', null, 20], { pages: [{ items: [summary(game1, match1)], nextCursor: null }], pageParams: [null] });
  renderFeature(<HistoryPage scope="account" />, { scope: 'account', ownerId }, query);
  await screen.findByText('Chưa có ván đã xong.');
  expect(screen.queryByRole('link', { name: /Ván 1/ })).not.toBeInTheDocument();
});

it('hides cached guest history after the guest cookie is rejected', async () => {
  vi.stubGlobal('fetch', vi.fn(async (url: string) => url.includes('/matches/active') ? json({ items: [], nextCursor: null }) : unauthorized()));
  const query = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  query.setQueryData(['owner', 'guest', guestId, 'active', null, 20], { pages: [{ items: [], nextCursor: null }], pageParams: [null] });
  query.setQueryData(['owner', 'guest', guestId, 'history', null, 20], { pages: [{ items: [summary(game1, match1)], nextCursor: null }], pageParams: [null] });
  renderFeature(<HistoryPage scope="guest" />, { scope: 'guest', ownerId: guestId }, query);
  expect(await screen.findByRole('alert')).toHaveTextContent('Không thể truy cập phiên khách cũ');
  expect(screen.queryByRole('link', { name: /Ván 1/ })).not.toBeInTheDocument();
});

it('does not reveal cached guest history while access is being revalidated', async () => {
  let rejectHistory!: (response: Response) => void;
  const pending = new Promise<Response>(resolve => { rejectHistory = resolve; });
  vi.stubGlobal('fetch', vi.fn(async (url: string) => url.includes('/matches/active') ? json({ items: [], nextCursor: null }) : pending));
  const query = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  query.setQueryData(['owner', 'guest', guestId, 'active', null, 20], { pages: [{ items: [], nextCursor: null }], pageParams: [null] });
  query.setQueryData(['owner', 'guest', guestId, 'history', null, 20], { pages: [{ items: [summary(game1, match1)], nextCursor: null }], pageParams: [null] });
  renderFeature(<HistoryPage scope="guest" />, { scope: 'guest', ownerId: guestId }, query);
  expect(screen.queryByRole('link', { name: /Ván 1/ })).not.toBeInTheDocument();
  rejectHistory(unauthorized());
  expect(await screen.findByRole('alert')).toHaveTextContent('Không thể truy cập phiên khách cũ');
});

it('does not reveal a cached guest replay board while access is being revalidated', async () => {
  let rejectGame!: (response: Response) => void;
  const pending = new Promise<Response>(resolve => { rejectGame = resolve; });
  vi.stubGlobal('fetch', vi.fn(async () => pending));
  const query = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  query.setQueryData(['owner', 'guest', guestId, 'history-game', game1], { ...summary(game1, match1), moves: [{ step: 1, row: 0, column: 0, stone: 'X', actor: 'Human', playedAt: date }] });
  renderFeature(<ReplayPage scope="guest" gameId={game1} />, { scope: 'guest', ownerId: guestId }, query);
  expect(screen.queryByRole('grid')).not.toBeInTheDocument();
  expect(document.querySelector('.game-workspace--match')).toBeInTheDocument();
  expect(screen.queryByText('Bước 0 / 1')).not.toBeInTheDocument();
  rejectGame(unauthorized());
  expect(await screen.findByRole('alert')).toHaveTextContent('Không thể truy cập phiên khách cũ');
});

it('requests account login when the account cookie is rejected', async () => {
  vi.stubGlobal('fetch', vi.fn(async () => unauthorized()));
  const { value } = renderFeature(<HistoryPage scope="account" />, { scope: 'account', ownerId });
  await waitFor(() => expect(value.requireAccount).toHaveBeenCalled());
  expect(screen.queryByRole('link', { name: /Ván 1/ })).not.toBeInTheDocument();
});

it('replays sorted steps without mutating the received game or sending moves', async () => {
  const moves = [
    { step: 2, row: 0, column: 1, stone: 'O', actor: 'AI', playedAt: date },
    { step: 1, row: 0, column: 0, stone: 'X', actor: 'Human', playedAt: date },
  ];
  const requests: string[] = [];
  vi.stubGlobal('fetch', vi.fn(async (url: string) => { requests.push(url); return json({ ...summary(game1, match1), moves }); }));
  renderFeature(<ReplayPage scope="account" gameId={game1} />, { scope: 'account', ownerId });
  expect(await screen.findByText('Bước 0 / 2')).toBeInTheDocument();
  expect(document.querySelector('.game-workspace--match')).toBeInTheDocument();
  expect(document.querySelector('.game-workspace-main .gomoku-board')).toBeInTheDocument();
  expect(document.querySelectorAll('.game-workspace-side .window')).toHaveLength(2);
  const user = userEvent.setup();
  await user.click(screen.getByRole('button', { name: 'Bước sau' }));
  expect(screen.getByText('Bước 1 / 2')).toBeInTheDocument();
  expect(screen.getByRole('gridcell', { name: /Hàng 1, cột 1, quân X/ })).toBeInTheDocument();
  await user.click(screen.getByRole('button', { name: 'Bước sau' }));
  expect(screen.getByRole('gridcell', { name: /Hàng 1, cột 2, quân O/ })).toBeInTheDocument();
  await user.click(screen.getByRole('button', { name: 'Bước trước' }));
  expect(screen.getByText('Bước 1 / 2')).toBeInTheDocument();
  expect(screen.getByRole('gridcell', { name: /Hàng 1, cột 2, ô trống/ })).toBeInTheDocument();
  expect(moves.map(move => move.step)).toEqual([2, 1]);
  expect(requests).toEqual([`/api/v1/history/${game1}?scope=account`]);
});
