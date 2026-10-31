import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { StrictMode, useEffect } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter, useLocation, type Location } from 'react-router-dom';
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
type RouteEntry = string | { pathname: string; search?: string; state?: unknown };
let currentLocation: Location | undefined;
function LocationProbe() {
  const location = useLocation();
  useEffect(() => { currentLocation = location; }, [location]);
  return null;
}
function setup(handler: FetchHandler, id?: string, identity: OwnerIdentity = owner, route: RouteEntry = '/') {
  const requests: { path: string; init: RequestInit }[] = [];
  vi.stubGlobal('fetch', vi.fn(async (path: string, init: RequestInit) => {
    requests.push({ path, init });
    if (path === '/api/v1/auth/csrf') return json({ requestToken: 'csrf' });
    return handler(path, init);
  }));
  const query = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(<QueryClientProvider client={query}><MemoryRouter initialEntries={[route]}><MatchPage scope={identity.scope} identity={identity} matchId={id} /><LocationProbe /></MemoryRouter></QueryClientProvider>);
  return requests;
}
beforeEach(() => {
  Object.defineProperty(globalThis, 'ResizeObserver', { configurable: true, writable: true, value: class { observe() {} disconnect() {} } });
  vi.spyOn(HTMLMediaElement.prototype, 'play').mockResolvedValue(undefined);
});
afterEach(() => { vi.restoreAllMocks(); vi.unstubAllGlobals(); vi.useRealTimers(); });

async function skipCutscene() {
  // jsdom never plays media; signal playback so the cutscene opens and offers its skip button.
  fireEvent.playing(await screen.findByLabelText(/Hoạt cảnh kết thúc/));
  fireEvent.click(await screen.findByRole('button', { name: 'Bỏ qua hoạt cảnh' }));
}

it('discovers an active match for this owner before creating a new one', async () => {
  const requests = setup((path) => path.startsWith('/api/v1/matches/active')
    ? json({ items: [{ matchId, gameId, revision: 0, updatedAt: '2026-10-27T00:00:00Z' }], nextCursor: null })
    : path.includes(`/${matchId}`) ? json({ ...base, xScore: 3, oScore: 2 }) : error('unexpected', 500));
  await screen.findByText('Ván 1');
  expect(requests.some(({ path }) => path === '/api/v1/matches/active?scope=guest&limit=20')).toBe(true);
  expect(requests.some(({ init }) => init.method === 'POST')).toBe(false);
  expect(screen.getByRole('region', { name: 'Điểm trận' })).toHaveTextContent('Bạn X3Bot O2');
  expect(screen.queryByRole('button', { name: 'Chơi tiếp' })).not.toBeInTheDocument();
});

it('sends selected O and bot-first setup when no saved match exists', async () => {
  const oStart = { ...base, humanStone: 'O', currentGame: { ...base.currentGame,
    openingStone: 'X', nextStone: 'X', turnStatus: 'ai_pending' } };
  const requests = setup(path => path.includes('/active?')
    ? json({ items: [], nextCursor: null })
    : path === '/api/v1/matches?scope=guest' ? json(oStart, 202) : error('unexpected', 500),
  undefined, owner, '/play?scope=guest&stone=O&first=false');
  await screen.findByText('Bot đang suy nghĩ…');
  const create = requests.find(({ path }) => path === '/api/v1/matches?scope=guest');
  expect(JSON.parse(String(create?.init.body))).toMatchObject({ humanStone: 'O', humanFirst: false });
  expect(screen.getByRole('region', { name: 'Điểm trận' })).toHaveTextContent('Bạn O0Bot X0');
});

it('uses the fixed 20 by 30 desktop board even when an old size is in the URL', async () => {
  const requests = setup(path => path.includes('/active?')
    ? json({ items: [], nextCursor: null })
    : path === '/api/v1/matches?scope=guest' ? json({ ...base, boardSize: 20, boardColumns: 30 }, 201) : error('unexpected', 500),
  undefined, owner, '/play?scope=guest&stone=X&first=true&boardSize=40');
  await screen.findByText('Ván 1');
  const create = requests.find(({ path }) => path === '/api/v1/matches?scope=guest');
  expect(JSON.parse(String(create?.init.body))).toMatchObject({ boardSize: 20, boardColumns: 30 });
  expect(screen.getByRole('grid', { name: 'Bàn cờ 20 nhân 30' })).toBeInTheDocument();
});

it('starts a selected board instead of resuming an older active match', async () => {
  const requests = setup(path => path.includes('/active?')
    ? json({ items: [{ matchId, gameId, revision: 0, updatedAt: '2026-10-27T00:00:00Z' }], nextCursor: null })
    : path === '/api/v1/matches?scope=guest' ? json({ ...base, boardSize: 20, boardColumns: 30 }, 201)
      : path.includes(`/${matchId}`) ? json(base) : error('unexpected', 500),
  undefined, owner, '/play?scope=guest&stone=X&first=true&boardSize=30');
  await screen.findByText('Ván 1');
  expect(screen.getByRole('grid', { name: 'Bàn cờ 20 nhân 30' })).toBeInTheDocument();
  expect(requests.some(({ path }) => path === '/api/v1/matches?scope=guest')).toBe(true);
});

it('keeps the selected board frame visible while a new match is loading', async () => {
  const pending = new Promise<Response>(() => {});
  setup(path => path === '/api/v1/matches?scope=guest' ? pending : error('unexpected', 500),
    undefined, owner, '/play?scope=guest&stone=O&first=false&boardSize=30');
  expect(screen.getByRole('grid', { name: 'Bàn cờ 20 nhân 30' })).toBeInTheDocument();
  expect(screen.getByText('Đang mở ván…')).toBeInTheDocument();
  expect(document.querySelector('.game-workspace--match')).toBeInTheDocument();
});

it('shows an optimistic O for a human O move', async () => {
  const oStart = { ...base, humanStone: 'O', currentGame: { ...base.currentGame,
    openingStone: 'O', nextStone: 'O' } };
  let resolveMove!: (response: Response) => void;
  const pending = new Promise<Response>(resolve => { resolveMove = resolve; });
  setup(path => path.includes('/moves?') ? pending : json(oStart), matchId);
  await screen.findByText('Lượt của bạn · O');
  fireEvent.click(screen.getByRole('button', { name: 'Phóng bàn cờ' }));
  fireEvent.click(screen.getByRole('gridcell', { name: 'Hàng 1, cột 1, ô trống' }));
  expect(screen.getByRole('gridcell', { name: /Hàng 1, cột 1, quân O/ })).toBeInTheDocument();
  await act(async () => resolveMove(json(oStart)));
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
  await skipCutscene();
  await screen.findByRole('button', { name: 'Chơi tiếp' });
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
  await skipCutscene();
  await screen.findByRole('button', { name: 'Chơi tiếp' });
  fireEvent.click(screen.getByRole('button', { name: 'Chơi tiếp' }));
  await screen.findByText('Ván 2');
  await waitFor(() => expect(screen.getByRole('gridcell', { name: /Hàng 5, cột 5, quân O/ })).toBeInTheDocument());
  expect(screen.queryByRole('region', { name: 'Kết quả ván' })).not.toBeInTheDocument();
  expect(requests.filter(({ path }) => path.includes('/next-game?'))).toHaveLength(1);
});

it('announces a server-confirmed win, highlights its five cells, and offers the next actions', async () => {
  const moves = [4, 5, 6, 7, 8].map((column, index) => ({ step: index + 1, row: 7, column, stone: 'X' as const }));
  const finished = { ...base, revision: 5, xScore: 1, currentGame: { ...base.currentGame,
    status: 'XWon', nextStone: null, turnStatus: 'finished', moves } };
  setup(() => json(finished), matchId);
  await skipCutscene();
  const result = await screen.findByRole('region', { name: 'Kết quả ván' });
  expect(result).toHaveTextContent('Bạn thắng!');
  expect(result).not.toHaveTextContent('5 quân liên tiếp');
  expect(result.querySelectorAll('[data-winning-cell]')).toHaveLength(0);
  expect(document.querySelectorAll('.gomoku-cell[data-winning-cell]')).toHaveLength(5);
  expect(screen.getByRole('button', { name: 'Chơi tiếp' })).toBeInTheDocument();
  expect(screen.getByRole('link', { name: 'Xem lại' })).toHaveAttribute('href', `/history/${gameId}?scope=guest`);
  expect(screen.queryByText('Điểm: X 1 – O 0')).not.toBeInTheDocument();
});

it.each([
  ['OWon', 'Bot thắng!'],
  ['Draw', 'Ván hòa'],
] as const)('shows a clear %s result from the server', async (status, message) => {
  const finished = { ...base, currentGame: { ...base.currentGame, status, nextStone: null, turnStatus: 'finished' } };
  setup(() => json(finished), matchId);
  if (status !== 'Draw') await skipCutscene();
  expect(await screen.findByRole('region', { name: 'Kết quả ván' })).toHaveTextContent(message);
});

it('announces human O as winner when the server reports OWon', async () => {
  const finished = { ...base, humanStone: 'O', currentGame: { ...base.currentGame,
    openingStone: 'O', status: 'OWon', nextStone: null, turnStatus: 'finished' } };
  setup(() => json(finished), matchId);
  await skipCutscene();
  expect(await screen.findByRole('region', { name: 'Kết quả ván' })).toHaveTextContent('Bạn thắng!');
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

it('retries an uncertain create with the same command ID', async () => {
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
  expect(posts.map(({ init }) => JSON.parse(String(init.body)))).toEqual([
    { commandId: 'stable-create-command', humanStone: 'X', humanFirst: true, boardSize: 20, boardColumns: 30 },
    { commandId: 'stable-create-command', humanStone: 'X', humanFirst: true, boardSize: 20, boardColumns: 30 },
  ]);
  expect(requests.filter(({ path }) => path.startsWith('/api/v1/matches/active'))).toHaveLength(1);
});

it('remembers a newly created match in history state so a reload resumes it', async () => {
  setup(path => path === '/api/v1/matches?scope=guest' ? json(base, 201) : error('unexpected', 500),
    undefined, owner, '/play?scope=guest&stone=X&first=true');
  await screen.findByText('Ván 1');
  await waitFor(() => expect(currentLocation?.state).toMatchObject({ createdMatchId: matchId }));
  expect(currentLocation?.search).toBe('?scope=guest&stone=X&first=true');
});

it('resumes the remembered match after a reload instead of creating another one', async () => {
  const requests = setup(path => path === `/api/v1/matches/${matchId}?scope=guest` ? json({ ...base, xScore: 1 }) : error('unexpected', 500),
    undefined, owner, { pathname: '/play', search: '?scope=guest&stone=X&first=true', state: { createdMatchId: matchId } });
  await screen.findByText('Ván 1');
  expect(requests.some(({ init }) => init.method === 'POST')).toBe(false);
  expect(document.querySelector('.versus-intro')).not.toBeInTheDocument();
});

it('keeps one match when rotating the phone changes the narrow-screen breakpoint', async () => {
  let narrow = true;
  vi.stubGlobal('matchMedia', (query: string) => ({ matches: query.includes('reduced-motion') || (query.includes('max-width') && narrow), media: query,
    addEventListener() {}, removeEventListener() {}, addListener() {}, removeListener() {} }));
  const requests = setup(path => path === '/api/v1/matches?scope=guest' ? json({ ...base, boardColumns: 20 }, 201)
    : path.includes('/moves?') ? json(xMove, 202)
      : path.includes(`/${matchId}`) ? json(oMove) : error('unexpected', 500),
  undefined, owner, '/play?scope=guest&stone=X&first=true');
  await screen.findByText('Ván 1');
  await waitFor(() => expect(document.querySelector('.versus-intro')).not.toBeInTheDocument());
  narrow = false;
  fireEvent.click(screen.getByRole('button', { name: 'Phóng bàn cờ' }));
  fireEvent.click(screen.getByRole('gridcell', { name: 'Hàng 1, cột 1, ô trống' }));
  await waitFor(() => expect(requests.filter(({ path }) => path.includes('/moves?'))).toHaveLength(1));
  await act(async () => { await new Promise(resolve => setTimeout(resolve, 50)); });
  expect(requests.filter(({ path }) => path === '/api/v1/matches?scope=guest')).toHaveLength(1);
});

it('does not start a second polling loop when focus and visibility fire during a poll', async () => {
  let gets = 0;
  setup(path => {
    if (!path.includes(`/${matchId}`)) return error('unexpected', 500);
    gets++;
    return gets === 1 ? json(xMove) : new Promise<Response>(() => {});
  }, matchId);
  await screen.findByText('Bot đang suy nghĩ…');
  await waitFor(() => expect(gets).toBe(2));
  act(() => {
    window.dispatchEvent(new Event('focus'));
    document.dispatchEvent(new Event('visibilitychange'));
  });
  await act(async () => { await new Promise(resolve => setTimeout(resolve, 50)); });
  expect(gets).toBe(2);
});

it('creates and plays a match on an insecure LAN origin where crypto.randomUUID is unavailable', async () => {
  const realCrypto = globalThis.crypto;
  vi.stubGlobal('crypto', { getRandomValues: <T extends ArrayBufferView>(array: T) => realCrypto.getRandomValues(array) });
  const requests = setup(path => path === '/api/v1/matches?scope=guest' ? json(base, 201)
    : path.includes('/moves?') ? json(oMove) : error('unexpected', 500),
  undefined, owner, '/play?scope=guest&stone=X&first=true');
  await screen.findByText('Ván 1');
  const create = requests.find(({ path }) => path === '/api/v1/matches?scope=guest');
  expect(JSON.parse(String(create?.init.body)).commandId).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/);
});

it('shows an error instead of loading forever when match creation fails before any request', async () => {
  vi.stubGlobal('crypto', {});
  setup(() => error('unexpected', 500), undefined, owner, '/play?scope=guest&stone=X&first=true');
  expect(await screen.findByRole('button', { name: 'Thử lại' })).toBeInTheDocument();
  expect(screen.queryByText('Đang mở ván…')).not.toBeInTheDocument();
});
