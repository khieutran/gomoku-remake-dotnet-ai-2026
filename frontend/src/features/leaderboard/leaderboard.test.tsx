import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, expect, it, vi } from 'vitest';
import { SessionProvider } from '../session/SessionProvider';
import { useSession } from '../session/sessionContext';
import { LeaderboardPage } from './LeaderboardPage';
import { MatchScorePanel } from './MatchScorePanel';

const accountId = 'c4b94753-4f50-4515-925d-59c43299fe2e';
const otherId = 'e84b8830-57ea-4d53-a7ab-6e39b870dc76';
const guestId = '329d3f45-201e-43e9-b90e-249ea2e780f3';
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status });
const error401 = () => json({ code: 'authentication_required', message: 'Sign in.' }, 401);

function Controls() {
  const { session, chooseGuest, logout } = useSession();
  return <aside>
    <span data-testid="scope">{session.scope}</span>
    <button onClick={() => void chooseGuest()}>Chọn khách</button>
    <button onClick={() => void logout()}>Đăng xuất</button>
  </aside>;
}

function renderPage() {
  const query = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(<QueryClientProvider client={query}><MemoryRouter><SessionProvider><Controls /><LeaderboardPage /></SessionProvider></MemoryRouter></QueryClientProvider>);
  return query;
}

afterEach(() => vi.unstubAllGlobals());

it('shows public top to a guest without requesting a private rank', async () => {
  const calls: string[] = [];
  vi.stubGlobal('fetch', vi.fn(async (url: string) => {
    calls.push(url);
    if (url.endsWith('/auth/me')) return error401();
    if (url.endsWith('/auth/csrf')) return json({ requestToken: 'token' });
    if (url.endsWith('/guest-sessions')) return json({ guestSessionId: guestId });
    if (url.endsWith('/leaderboard?limit=20')) return json({ items: [{ accountId, points: 9, rank: 1 }] });
    throw Error(`Unexpected request: ${url}`);
  }));
  renderPage();
  await userEvent.setup().click(screen.getByRole('button', { name: 'Chọn khách' }));
  await screen.findByText(accountId);
  expect(screen.getByTestId('scope')).toHaveTextContent('guest');
  expect(screen.getByRole('table', { name: 'Bảng xếp hạng' })).toHaveTextContent('9');
  expect(calls).not.toContain('/api/v1/leaderboard/me');
  expect(screen.getByText(/Chỉ tài khoản có vị trí riêng/)).toBeInTheDocument();
});

it('shows the account position from /me even when it is outside the top list', async () => {
  vi.stubGlobal('fetch', vi.fn(async (url: string) => {
    if (url.endsWith('/auth/me')) return json({ userId: accountId, email: 'a@example.com' });
    if (url.endsWith('/leaderboard?limit=20')) return json({ items: [{ accountId: otherId, points: 12, rank: 1 }] });
    if (url.endsWith('/leaderboard/me')) return json({ accountId, points: 4, rank: 27 });
    throw Error(`Unexpected request: ${url}`);
  }));
  renderPage();
  const mine = await screen.findByRole('region', { name: 'Vị trí của bạn' });
  await waitFor(() => expect(within(mine).getByText('27')).toBeInTheDocument());
  expect(within(mine).getByText('4')).toBeInTheDocument();
  expect(screen.getByRole('table', { name: 'Bảng xếp hạng' })).toHaveTextContent('12');
});

it('removes the old account position after logout', async () => {
  vi.stubGlobal('fetch', vi.fn(async (url: string) => {
    if (url.endsWith('/auth/me')) return json({ userId: accountId, email: 'a@example.com' });
    if (url.endsWith('/auth/csrf')) return json({ requestToken: 'token' });
    if (url.endsWith('/auth/logout')) return new Response(null, { status: 204 });
    if (url.endsWith('/leaderboard?limit=20')) return json({ items: [] });
    if (url.endsWith('/leaderboard/me')) return json({ accountId, points: 4, rank: 27 });
    throw Error(`Unexpected request: ${url}`);
  }));
  const query = renderPage();
  await waitFor(() => expect(screen.getByRole('region', { name: 'Vị trí của bạn' })).toHaveTextContent('27'));
  await userEvent.setup().click(screen.getByRole('button', { name: 'Đăng xuất' }));
  await waitFor(() => expect(screen.getByRole('region', { name: 'Vị trí của bạn' })).not.toHaveTextContent('27'));
  expect(query.getQueryData(['owner', 'account', accountId, 'ranking-me'])).toBeUndefined();
});

it('hides stale account rank and prompts sign-in when /me returns 401', async () => {
  let accountChecks = 0;
  vi.stubGlobal('fetch', vi.fn(async (url: string) => {
    if (url.endsWith('/auth/me')) {
      accountChecks++;
      return accountChecks === 1 ? json({ userId: accountId, email: 'a@example.com' }) : error401();
    }
    if (url.endsWith('/leaderboard?limit=20')) return json({ items: [] });
    if (url.endsWith('/leaderboard/me')) return error401();
    throw Error(`Unexpected request: ${url}`);
  }));
  const query = renderPage();
  expect(await screen.findByRole('link', { name: 'Đăng nhập để xem vị trí' })).toHaveAttribute('href', '/login');
  expect(screen.getByRole('region', { name: 'Vị trí của bạn' })).not.toHaveTextContent('27');
  await waitFor(() => expect(query.getQueryData(['owner', 'account', accountId, 'ranking-me'])).toBeUndefined());
});

it('hides a cached rank during revalidation and keeps it hidden after a 401', async () => {
  let privateCalls = 0;
  let resolvePrivate!: (response: Response) => void;
  const revalidation = new Promise<Response>(resolve => { resolvePrivate = resolve; });
  vi.stubGlobal('fetch', vi.fn(async (url: string) => {
    if (url.endsWith('/auth/me')) return privateCalls === 0 ? json({ userId: accountId, email: 'a@example.com' }) : error401();
    if (url.endsWith('/leaderboard?limit=20')) return json({ items: [] });
    if (url.endsWith('/leaderboard/me')) {
      privateCalls++;
      return privateCalls === 1 ? json({ accountId, points: 4, rank: 27 }) : revalidation;
    }
    throw Error(`Unexpected request: ${url}`);
  }));
  const query = renderPage();
  const personal = await screen.findByRole('region', { name: 'Vị trí của bạn' });
  await waitFor(() => expect(personal).toHaveTextContent('27'));

  void query.invalidateQueries({ queryKey: ['owner', 'account', accountId, 'ranking-me'] });
  await waitFor(() => expect(privateCalls).toBe(2));
  expect(personal).not.toHaveTextContent('27');

  resolvePrivate(error401());
  expect(await screen.findByRole('link', { name: 'Đăng nhập để xem vị trí' })).toBeInTheDocument();
  expect(personal).not.toHaveTextContent('27');
});

it('shows server match points for X and O without deriving outcome counts', () => {
  const match = { matchId: accountId, boardSize: 20, ruleVersion: 'v1', revision: 2, xScore: 4, oScore: 1, currentGame: { id: otherId, number: 3, openingStone: 'X' as const, aiVersion: null, status: 'InProgress' as const, nextStone: 'X' as const, turnStatus: 'human' as const, aiErrorCode: null, aiRetryAt: null, moves: [] } };
  render(<MatchScorePanel xScore={match.xScore} oScore={match.oScore} />);
  const panel = screen.getByRole('region', { name: 'Điểm trận' });
  expect(within(panel).getByText('4')).toBeInTheDocument();
  expect(within(panel).getByText('1')).toBeInTheDocument();
  expect(panel).toHaveTextContent('Bạn');
  expect(panel).toHaveTextContent('Bot');
  expect(panel).not.toHaveTextContent(/Thắng|Hòa|Thua/);
});
