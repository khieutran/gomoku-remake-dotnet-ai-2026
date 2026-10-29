import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { afterEach, beforeEach, vi } from 'vitest';
import App from './App';
import { SessionProvider } from '../features/session/SessionProvider';

function renderApp(path = '/') {
  const query = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(<QueryClientProvider client={query}><MemoryRouter initialEntries={[path]}><SessionProvider><App /></SessionProvider></MemoryRouter></QueryClientProvider>);
}
beforeEach(() => vi.stubGlobal('ResizeObserver', class { observe() {} disconnect() {} }));
afterEach(() => vi.unstubAllGlobals());

it('offers guest choice without creating a guest on page load', () => {
  const fetchSpy = vi.fn().mockResolvedValue(new Response(JSON.stringify({ code: 'authentication_required', message: 'Sign in.' }), { status: 401 }));
  vi.stubGlobal('fetch', fetchSpy);
  renderApp();
  expect(screen.getByRole('heading', { name: /Thử sức nếu bạn dám, Let's go!/ })).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Chơi mới' })).toBeInTheDocument();
  expect(fetchSpy.mock.calls.some(([url]) => String(url).endsWith('/guest-sessions'))).toBe(false);
});

it('keeps the board behind a start menu with independent stone and turn choices', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ code: 'authentication_required', message: 'Sign in.' }), { status: 401 })));
  renderApp();
  const menu = screen.getByRole('region', { name: 'Chọn cách chơi' });
  expect(menu).toBeInTheDocument();
  expect(screen.getByRole('grid', { name: 'Bàn cờ 20 nhân 30' })).toBeInTheDocument();
  await userEvent.click(screen.getByRole('button', { name: 'Chơi mới' }));
  expect(screen.getByRole('button', { name: 'Chơi khách' })).toBeInTheDocument();
  await userEvent.click(screen.getByRole('link', { name: 'Đăng nhập' }));
  expect(screen.getByRole('heading', { name: 'Đăng nhập' })).toBeInTheDocument();
});

it('keeps the board size fixed while choosing stone and turn', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ code: 'authentication_required', message: 'Sign in.' }), { status: 401 })));
  renderApp('/?start=setup');
  const menu = screen.getByRole('region', { name: 'Chọn cách chơi' });
  expect(screen.getByRole('grid', { name: 'Bàn cờ 20 nhân 30' })).toBeInTheDocument();
  expect(screen.getByText('Bàn cờ 20 × 30')).toBeInTheDocument();
  expect(screen.getByRole('region', { name: 'Chọn cách chơi' })).toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Phóng bàn cờ' })).not.toBeInTheDocument();
  expect(menu).toContainElement(screen.getByRole('radio', { name: 'X' }));
  expect(menu).toContainElement(screen.getByRole('radio', { name: 'O' }));
  expect(menu).toContainElement(screen.getByRole('radio', { name: 'Đánh trước' }));
  expect(menu).toContainElement(screen.getByRole('radio', { name: 'Đánh sau' }));
  expect(screen.getByRole('radio', { name: 'X' })).toBeChecked();
  expect(screen.getByRole('radio', { name: 'Đánh trước' })).toBeChecked();
  await userEvent.click(screen.getByRole('radio', { name: 'O' }));
  await userEvent.click(screen.getByRole('radio', { name: 'Đánh sau' }));
  expect(screen.getByRole('radio', { name: 'O' })).toBeChecked();
  expect(screen.getByRole('radio', { name: 'Đánh sau' })).toBeChecked();
});
it('supports keyboard navigation and moves focus to the destination heading', async () => {
  const user = userEvent.setup();
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ code: 'authentication_required', message: 'Sign in.' }), { status: 401 })));
  renderApp();
  await user.tab();
  expect(screen.getByRole('link', { name: 'Đến nội dung' })).toHaveFocus();
  screen.getByRole('link', { name: 'Lịch sử' }).focus();
  await user.keyboard('{Enter}');
  expect(await screen.findByRole('heading', { name: 'Lịch sử ván đấu' })).toHaveFocus();
});
it.each([['/play', 'Đấu với bot'], ['/play/example', 'Đấu với bot'], ['/login', 'Đăng nhập'], ['/register', 'Đăng ký'], ['/history/example', 'Phát lại ván đấu'], ['/leaderboard', 'Bảng xếp hạng']])('renders the %s route', async (path, heading) => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ code: 'authentication_required', message: 'Sign in.' }), { status: 401 })));
  renderApp(path);
  expect(await screen.findByRole('heading', { name: heading })).toBeInTheDocument();
});

it('connects the leaderboard route to public server points', async () => {
  const accountId = 'c4b94753-4f50-4515-925d-59c43299fe2e';
  const fetcher = vi.fn(async (url: string) => url.endsWith('/auth/me')
    ? new Response(JSON.stringify({ code: 'authentication_required', message: 'Sign in.' }), { status: 401 })
    : new Response(JSON.stringify({ items: [{ accountId, rank: 1, points: 3 }] }), { status: 200 }));
  vi.stubGlobal('fetch', fetcher);
  renderApp('/leaderboard');
  expect(await screen.findByRole('table', { name: 'Bảng xếp hạng' })).toHaveTextContent('3');
  await waitFor(() => expect(fetcher.mock.calls.some(([url]) => String(url).includes('/leaderboard?limit=20'))).toBe(true));
  expect(fetcher.mock.calls.some(([url]) => String(url).endsWith('/leaderboard/me'))).toBe(false);
});

it('connects the history route to the signed-in owner lists', async () => {
  const accountId = 'c4b94753-4f50-4515-925d-59c43299fe2e';
  const fetcher = vi.fn(async (url: string) => url.endsWith('/auth/me')
    ? new Response(JSON.stringify({ userId: accountId, email: 'a@example.com' }))
    : new Response(JSON.stringify({ items: [], nextCursor: null })));
  vi.stubGlobal('fetch', fetcher);
  renderApp('/history?scope=account');
  expect(await screen.findByRole('region', { name: 'Ván đã xong' })).toHaveTextContent('Chưa có ván đã xong.');
  expect(fetcher.mock.calls.some(([url]) => String(url).includes('/matches/active?scope=account'))).toBe(true);
});

it('connects a saved match URL to the server board for the signed-in owner', async () => {
  const accountId = 'c4b94753-4f50-4515-925d-59c43299fe2e';
  const matchId = '821412da-c99d-45be-a7e9-328fb464380b';
  const fetcher = vi.fn(async (url: string) => url.endsWith('/auth/me')
    ? new Response(JSON.stringify({ userId: accountId, email: 'a@example.com' }))
    : new Response(JSON.stringify({ matchId, ruleVersion: 'v1', boardSize: 20, xScore: 0, oScore: 0, revision: 0,
      currentGame: { id: matchId, number: 1, openingStone: 'X', aiVersion: null, status: 'InProgress', nextStone: 'X', turnStatus: 'human', aiErrorCode: null, aiRetryAt: null, moves: [] } })));
  vi.stubGlobal('fetch', fetcher);
  renderApp(`/play/${matchId}?scope=account`);
  expect(await screen.findByRole('grid', { name: 'Bàn cờ 20 nhân 20' })).toBeInTheDocument();
  expect(fetcher.mock.calls.some(([url]) => String(url).includes(`/matches/${matchId}?scope=account`))).toBe(true);
});
