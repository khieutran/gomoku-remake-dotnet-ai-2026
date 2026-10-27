import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { StrictMode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import type { OwnerIdentity } from '../../shared/api/contracts';
import { MatchPage } from './MatchPage';

const matchId = '821412da-c99d-45be-a7e9-328fb464380b';
const gameId = '933412da-c99d-45be-a7e9-328fb464380b';
const owner: OwnerIdentity = { scope: 'guest', ownerId: 'a1b412da-c99d-45be-a7e9-328fb464380b' };
const base = { matchId, boardSize: 20, ruleVersion: 'v1', revision: 0, xScore: 0, oScore: 0,
  currentGame: { id: gameId, number: 1, openingStone: 'X', aiVersion: null, status: 'InProgress', nextStone: 'X', turnStatus: 'human', aiErrorCode: null, aiRetryAt: null, moves: [] as { step: number; row: number; column: number; stone: 'X' | 'O' }[] } };
const xMove = { ...base, revision: 1, currentGame: { ...base.currentGame, nextStone: 'O', turnStatus: 'ai_pending', moves: [{ step: 1, row: 0, column: 0, stone: 'X' }] } };
const oMove = { ...xMove, revision: 2, currentGame: { ...xMove.currentGame, nextStone: 'X', turnStatus: 'human', moves: [...xMove.currentGame.moves, { step: 2, row: 0, column: 1, stone: 'O' }] } };
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
const error = (code: string, status = 409) => json({ code, message: code }, status);
type FetchHandler = (path: string, init: RequestInit) => Response | Promise<Response>;
function setup(handler: FetchHandler, id?: string, identity: OwnerIdentity = owner) {
  const requests: { path: string; init: RequestInit }[] = [];
  vi.stubGlobal('fetch', vi.fn(async (path: string, init: RequestInit) => {
    requests.push({ path, init });
    if (path === '/api/v1/auth/csrf') return json({ requestToken: 'csrf' });
    return handler(path, init);
  }));
  const query = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(<QueryClientProvider client={query}><MemoryRouter><MatchPage scope={identity.scope} identity={identity} matchId={id} /></MemoryRouter></QueryClientProvider>);
  return requests;
}
beforeEach(() => { Object.defineProperty(globalThis, 'ResizeObserver', { configurable: true, writable: true, value: class { observe() {} disconnect() {} } }); });
afterEach(() => { vi.unstubAllGlobals(); vi.useRealTimers(); });

it('discovers an active match for this owner before creating a new one', async () => {
  const requests = setup((path) => path.startsWith('/api/v1/matches/active')
    ? json({ items: [{ matchId, gameId, revision: 0, updatedAt: '2026-10-27T00:00:00Z' }], nextCursor: null })
    : path.includes(`/${matchId}`) ? json({ ...base, xScore: 3, oScore: 2 }) : error('unexpected', 500));
  await screen.findByText('Ván 1');
  expect(requests.some(({ path }) => path === '/api/v1/matches/active?scope=guest&limit=20')).toBe(true);
  expect(requests.some(({ init }) => init.method === 'POST')).toBe(false);
  expect(screen.getByRole('region', { name: 'Điểm trận' })).toHaveTextContent('Bạn X3Bot O2');
  expect(screen.queryByRole('button', { name: 'Ván tiếp theo' })).not.toBeInTheDocument();
});

it('shows X immediately, blocks a double click, then replaces it with the server snapshot and polls for O', async () => {
  let resolveMove!: (response: Response) => void;
  const post = new Promise<Response>(resolve => { resolveMove = resolve; });
  let getCount = 0;
  const requests = setup((path) => {
    if (path.includes('/moves?')) return post;
    if (path.includes(`/${matchId}`)) return json(getCount++ === 0 ? base : oMove);
    return error('unexpected', 500);
  }, matchId);
  await screen.findByText('Ván 1');
  fireEvent.click(screen.getByRole('button', { name: 'Phóng bàn cờ' }));
  const cell = screen.getByRole('gridcell', { name: 'Hàng 1, cột 1, ô trống' });
  fireEvent.click(cell);
  fireEvent.click(cell);
  expect(screen.getByRole('gridcell', { name: /Hàng 1, cột 1, quân X/ })).toBeInTheDocument();
  await waitFor(() => expect(requests.filter(({ path }) => path.includes('/moves?'))).toHaveLength(1));
  await act(async () => resolveMove(json(xMove, 202)));
  await waitFor(() => expect(screen.getByRole('gridcell', { name: /Hàng 1, cột 2, quân O/ })).toBeInTheDocument(), { timeout: 2500 });
});

it('reconciles a timeout by GET and retries the identical command after the server did not commit', async () => {
  let postCount = 0;
  const requests = setup((path) => {
    if (path.includes('/moves?')) return ++postCount === 1 ? Promise.reject(new TypeError('network')) : json(xMove, 202);
    if (path.includes(`/${matchId}`)) return json(base);
    return error('unexpected', 500);
  }, matchId);
  await screen.findByText('Ván 1');
  fireEvent.click(screen.getByRole('button', { name: 'Phóng bàn cờ' }));
  fireEvent.click(screen.getByRole('gridcell', { name: 'Hàng 1, cột 1, ô trống' }));
  await screen.findByRole('button', { name: 'Thử lại nước đi' });
  fireEvent.click(screen.getByRole('button', { name: 'Thử lại nước đi' }));
  await waitFor(() => expect(postCount).toBe(2));
  const bodies = requests.filter(({ path }) => path.includes('/moves?')).map(({ init }) => init.body);
  expect(bodies[0]).toBe(bodies[1]);
  expect(JSON.parse(String(bodies[0]))).toMatchObject({ row: 0, column: 0, expectedRevision: 0 });
});

it('refreshes after a stale revision and offers next game only after a finished snapshot', async () => {
  const finished = { ...base, revision: 5, currentGame: { ...base.currentGame, status: 'XWon', nextStone: null, turnStatus: 'finished' } };
  let reads = 0;
  setup(path => {
    if (path.includes('/moves?')) return error('stale_revision');
    if (path.includes(`/${matchId}`)) return json(++reads === 1 ? base : finished);
    return error('unexpected', 500);
  }, matchId);
  await screen.findByText('Ván 1');
  fireEvent.click(screen.getByRole('button', { name: 'Phóng bàn cờ' }));
  fireEvent.click(screen.getByRole('gridcell', { name: 'Hàng 1, cột 1, ô trống' }));
  await screen.findByRole('button', { name: 'Ván tiếp theo' });
  expect(screen.getByRole('alert')).toHaveTextContent(/đã thay đổi/i);
});

it('starts the next game only after finish and renders an AI opening move from GET', async () => {
  const finished = { ...base, revision: 5, currentGame: { ...base.currentGame, status: 'XWon', nextStone: null, turnStatus: 'finished' } };
  const opener = { ...base, revision: 6, currentGame: { ...base.currentGame, id: 'a33412da-c99d-45be-a7e9-328fb464380b', number: 2, openingStone: 'O', nextStone: 'O', turnStatus: 'ai_pending' } };
  const opened = { ...opener, revision: 7, currentGame: { ...opener.currentGame, nextStone: 'X', turnStatus: 'human', moves: [{ step: 1, row: 4, column: 4, stone: 'O' }] } };
  let didNext = false;
  const requests = setup(path => {
    if (path.includes('/next-game?')) { didNext = true; return json(opener, 202); }
    if (path.includes(`/${matchId}`)) return json(didNext ? opened : finished);
    return error('unexpected', 500);
  }, matchId);
  await screen.findByRole('button', { name: 'Ván tiếp theo' });
  fireEvent.click(screen.getByRole('button', { name: 'Ván tiếp theo' }));
  await screen.findByText('Ván 2');
  await waitFor(() => expect(screen.getByRole('gridcell', { name: /Hàng 5, cột 5, quân O/ })).toBeInTheDocument());
  expect(requests.filter(({ path }) => path.includes('/next-game?'))).toHaveLength(1);
});

it('clears the owner snapshot and offers a session action after 401', async () => {
  setup(path => path.includes('/moves?') ? error('authentication_required', 401) : json(base), matchId);
  await screen.findByText('Ván 1');
  fireEvent.click(screen.getByRole('button', { name: 'Phóng bàn cờ' }));
  fireEvent.click(screen.getByRole('gridcell', { name: 'Hàng 1, cột 1, ô trống' }));
  await screen.findByRole('link', { name: 'Mở lại phiên' });
  expect(screen.queryByRole('grid')).not.toBeInTheDocument();
});

it('retries a failed initial load without generating a replacement match', async () => {
  let reads = 0;
  const requests = setup(path => {
    if (path.startsWith('/api/v1/matches/active')) return ++reads === 1 ? Promise.reject(new TypeError('offline')) : json({ items: [{ matchId, gameId, revision: 0, updatedAt: '2026-10-27T00:00:00Z' }], nextCursor: null });
    if (path.includes(`/${matchId}`)) return json(base);
    return error('unexpected', 500);
  });
  await screen.findByRole('button', { name: 'Thử lại' });
  fireEvent.click(screen.getByRole('button', { name: 'Thử lại' }));
  await screen.findByText('Ván 1');
  expect(requests.some(({ init }) => init.method === 'POST')).toBe(false);
});

it('discards a late response from the previous owner after switching identity', async () => {
  let resolveOld!: (response: Response) => void;
  const oldResponse = new Promise<Response>(resolve => { resolveOld = resolve; });
  const account: OwnerIdentity = { scope: 'account', ownerId: 'b1b412da-c99d-45be-a7e9-328fb464380b' };
  const second = { ...base, currentGame: { ...base.currentGame, number: 2 } };
  vi.stubGlobal('fetch', vi.fn((path: string) => {
    if (path === '/api/v1/auth/csrf') return Promise.resolve(json({ requestToken: 'csrf' }));
    if (path.includes('/moves?') && path.includes('scope=guest')) return oldResponse;
    return Promise.resolve(json(path.includes('scope=guest') ? base : second));
  }));
  const query = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const view = render(<QueryClientProvider client={query}><MemoryRouter><MatchPage scope="guest" identity={owner} matchId={matchId} /></MemoryRouter></QueryClientProvider>);
  await screen.findByText('Ván 1');
  fireEvent.click(screen.getByRole('button', { name: 'Phóng bàn cờ' }));
  fireEvent.click(screen.getByRole('gridcell', { name: 'Hàng 1, cột 1, ô trống' }));
  view.rerender(<QueryClientProvider client={query}><MemoryRouter><MatchPage scope="account" identity={account} matchId={matchId} /></MemoryRouter></QueryClientProvider>);
  await screen.findByText('Ván 2');
  query.removeQueries({ queryKey: ['owner', 'guest', owner.ownerId] });
  await act(async () => resolveOld(json(xMove, 202)));
  expect(screen.getByText('Ván 2')).toBeInTheDocument();
  expect(query.getQueryData(['owner', 'guest', owner.ownerId, 'match', matchId])).toBeUndefined();
});

it('stops AI polling and hides the board when a poll returns 401', async () => {
  let reads = 0;
  setup(path => {
    if (path.includes(`/${matchId}`)) return ++reads === 1 ? json(xMove) : error('authentication_required', 401);
    return error('unexpected', 500);
  }, matchId);
  await screen.findByRole('link', { name: 'Mở lại phiên' });
  expect(screen.queryByRole('grid')).not.toBeInTheDocument();
  const settledReads = reads;
  await new Promise(resolve => setTimeout(resolve, 1200));
  expect(reads).toBe(settledReads);
});

it('preserves the account match URL in the sign-in return link after 401', async () => {
  const account: OwnerIdentity = { scope: 'account', ownerId: 'b1b412da-c99d-45be-a7e9-328fb464380b' };
  setup(path => path.includes('/moves?') ? error('authentication_required', 401) : json(base), matchId, account);
  await screen.findByText('Ván 1');
  fireEvent.click(screen.getByRole('button', { name: 'Phóng bàn cờ' }));
  fireEvent.click(screen.getByRole('gridcell', { name: 'Hàng 1, cột 1, ô trống' }));
  const link = await screen.findByRole('link', { name: 'Mở lại phiên' });
  expect(link).toHaveAttribute('href', `/login?returnTo=${encodeURIComponent(`/play/${matchId}?scope=account`)}`);
});

it('creates one match when StrictMode replays an empty active-match lookup', async () => {
  vi.stubGlobal('crypto', { randomUUID: vi.fn().mockReturnValueOnce('first-command').mockReturnValue('second-command') });
  let resolveActive!: () => void;
  const active = new Promise<void>(resolve => { resolveActive = resolve; });
  let creates = 0;
  vi.stubGlobal('fetch', vi.fn((path: string) => {
    if (path === '/api/v1/auth/csrf') return Promise.resolve(json({ requestToken: 'csrf' }));
    if (path.startsWith('/api/v1/matches/active')) return active.then(() => json({ items: [], nextCursor: null }));
    if (path === '/api/v1/matches?scope=guest') { creates++; return Promise.resolve(json(base, 201)); }
    return Promise.resolve(error('unexpected', 500));
  }));
  const query = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(<StrictMode><QueryClientProvider client={query}><MemoryRouter><MatchPage scope="guest" identity={owner} /></MemoryRouter></QueryClientProvider></StrictMode>);
  await act(async () => resolveActive());
  await screen.findByText('Ván 1');
  expect(creates).toBe(1);
});

it('checks active matches after an uncertain create and retries the same command ID', async () => {
  vi.stubGlobal('crypto', { randomUUID: vi.fn().mockReturnValue('stable-create-command') });
  let creates = 0;
  const requests = setup(path => {
    if (path.startsWith('/api/v1/matches/active')) return json({ items: [], nextCursor: null });
    if (path === '/api/v1/matches?scope=guest') return ++creates === 1 ? Promise.reject(new TypeError('timeout')) : json(base, 201);
    return error('unexpected', 500);
  });
  await screen.findByRole('button', { name: 'Thử lại' });
  fireEvent.click(screen.getByRole('button', { name: 'Thử lại' }));
  await screen.findByText('Ván 1');
  const posts = requests.filter(({ path }) => path === '/api/v1/matches?scope=guest');
  expect(posts).toHaveLength(2);
  expect(posts.map(({ init }) => init.body)).toEqual(['{"commandId":"stable-create-command"}', '{"commandId":"stable-create-command"}']);
  expect(requests.filter(({ path }) => path.startsWith('/api/v1/matches/active'))).toHaveLength(2);
});
