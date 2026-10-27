import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider, useQueryClient } from '@tanstack/react-query';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { afterEach, expect, it, vi } from 'vitest';
import { StrictMode } from 'react';
import App from '../../app/App';
import { queryKeys } from '../../shared/api/queryKeys';
import { SessionProvider } from './SessionProvider';
import { useSession } from './sessionContext';

const guestId = '329d3f45-201e-43e9-b90e-249ea2e780f3';
const accountId = 'c4b94753-4f50-4515-925d-59c43299fe2e';
const otherId = 'e84b8830-57ea-4d53-a7ab-6e39b870dc76';
const matchId = '821412da-c99d-45be-a7e9-328fb464380b';
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status });

function SessionProbe() {
  const { session, requireAccount } = useSession();
  const location = useLocation();
  const query = useQueryClient();
  return <aside aria-label="Kiểm tra phiên">
    <span data-testid="session">{session.scope === 'none' ? 'none' : `${session.scope}:${session.ownerId}`}</span>
    <span data-testid="path">{location.pathname}</span>
    <button onClick={() => void requireAccount()}>Kiểm tra tài khoản</button>
    <button onClick={() => query.setQueryData(queryKeys.match({ scope: 'guest', ownerId: guestId }, matchId), { from: 'guest' })}>Tạo cache khách</button>
    <button onClick={() => query.setQueryData(queryKeys.match({ scope: 'account', ownerId: accountId }, matchId), { from: 'account' })}>Tạo cache tài khoản</button>
    <span data-testid="guest-cache">{query.getQueryData(queryKeys.match({ scope: 'guest', ownerId: guestId }, matchId)) ? 'cached' : 'empty'}</span>
    <span data-testid="account-cache">{query.getQueryData(queryKeys.match({ scope: 'account', ownerId: accountId }, matchId)) ? 'cached' : 'empty'}</span>
  </aside>;
}

function renderApp(path = '/') {
  const query = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(<QueryClientProvider client={query}><MemoryRouter initialEntries={[path]}><SessionProvider><SessionProbe /><App /></SessionProvider></MemoryRouter></QueryClientProvider>);
  return query;
}

afterEach(() => vi.unstubAllGlobals());

it('restores an account after a StrictMode reload without creating a guest', async () => {
  const fetcher = vi.fn(async (url: string) => {
    if (url.endsWith('/auth/me')) return json({ userId: accountId, email: 'a@example.com' });
    throw Error(`Unexpected request: ${url}`);
  });
  vi.stubGlobal('fetch', fetcher);
  const query = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(<StrictMode><QueryClientProvider client={query}><MemoryRouter initialEntries={['/play?scope=account']}><SessionProvider><SessionProbe /><App /></SessionProvider></MemoryRouter></QueryClientProvider></StrictMode>);
  expect(await screen.findByTestId('session')).toHaveTextContent(`account:${accountId}`);
  expect(fetcher.mock.calls.every(([url]) => String(url).endsWith('/auth/me'))).toBe(true);
});

it('retries account bootstrap after StrictMode aborts the first request', async () => {
  let calls = 0;
  vi.stubGlobal('fetch', vi.fn((_url: string, options?: RequestInit) => {
    calls++;
    if (calls > 1) return Promise.resolve(json({ userId: accountId, email: 'a@example.com' }));
    return new Promise<Response>((_resolve, reject) => options?.signal?.addEventListener('abort', () => reject(new DOMException('Aborted', 'AbortError'))));
  }));
  const query = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(<StrictMode><QueryClientProvider client={query}><MemoryRouter initialEntries={['/play?scope=account']}><SessionProvider><SessionProbe /><App /></SessionProvider></MemoryRouter></QueryClientProvider></StrictMode>);
  await waitFor(() => expect(screen.getByTestId('session')).toHaveTextContent(`account:${accountId}`));
  expect(calls).toBeGreaterThan(1);
});

it('does not POST a guest session merely from a direct guest URL', async () => {
  const fetcher = vi.fn(async (url: string) => {
    if (url.endsWith('/auth/me')) return json({ code: 'authentication_required', message: 'Sign in.' }, 401);
    if (url.endsWith('/guest-sessions/current')) return json({ code: 'guest_session_required', message: 'No guest session.' }, 401);
    throw Error(`Unexpected request: ${url}`);
  });
  vi.stubGlobal('fetch', fetcher);
  renderApp('/play?scope=guest');
  expect(await screen.findByRole('button', { name: 'Bắt đầu chơi khách' })).toBeInTheDocument();
  expect(fetcher.mock.calls.some(([url]) => String(url).endsWith('/guest-sessions'))).toBe(false);
});

it('restores an existing guest after reload without creating a new session', async () => {
  const fetcher = vi.fn(async (url: string) => {
    if (url.endsWith('/auth/me')) return json({ code: 'authentication_required', message: 'Sign in.' }, 401);
    if (url.endsWith('/guest-sessions/current')) return json({ guestSessionId: guestId });
    throw Error(`Unexpected request: ${url}`);
  });
  vi.stubGlobal('fetch', fetcher);
  renderApp('/history?scope=guest');
  await waitFor(() => expect(screen.getByTestId('session')).toHaveTextContent(`guest:${guestId}`));
  expect(fetcher.mock.calls.some(([url]) => String(url).endsWith('/guest-sessions'))).toBe(false);
});

it('does not create a guest on entry; a second explicit choice reuses the server guest', async () => {
  const user = userEvent.setup();
  const fetcher = vi.fn(async (url: string) => {
    if (url.endsWith('/auth/csrf')) return json({ requestToken: 'token' });
    if (url.endsWith('/guest-sessions')) return json({ guestSessionId: guestId });
    if (url.endsWith('/auth/me')) return json({ code: 'authentication_required', message: 'Sign in.' }, 401);
    throw Error(`Unexpected request: ${url}`);
  });
  vi.stubGlobal('fetch', fetcher);
  renderApp();
  expect(fetcher.mock.calls.some(([url]) => String(url).endsWith('/guest-sessions'))).toBe(false);
  await user.click(screen.getByRole('button', { name: 'Chơi khách' }));
  expect(await screen.findByTestId('session')).toHaveTextContent(`guest:${guestId}`);
  expect(screen.getByTestId('path')).toHaveTextContent('/play');
  await user.click(screen.getByRole('link', { name: 'Gomoku trang chủ' }));
  await user.click(screen.getByRole('button', { name: 'Chơi khách' }));
  await waitFor(() => expect(fetcher.mock.calls.filter(([url]) => String(url).endsWith('/guest-sessions'))).toHaveLength(2));
  expect(screen.getByTestId('session')).toHaveTextContent(`guest:${guestId}`);
});

it('restores a signed-in account on the home page and offers play under that account', async () => {
  const fetcher = vi.fn(async (url: string) => {
    if (url.endsWith('/auth/me')) return json({ userId: accountId, email: 'a@example.com' });
    throw Error(`Unexpected request: ${url}`);
  });
  vi.stubGlobal('fetch', fetcher);
  renderApp();
  await waitFor(() => expect(screen.getByTestId('session')).toHaveTextContent(`account:${accountId}`));
  expect(screen.getByRole('link', { name: 'Chơi tiếp' })).toHaveAttribute('href', '/play?scope=account');
  expect(fetcher.mock.calls.every(([url]) => String(url).endsWith('/auth/me'))).toBe(true);
});

it('switches from guest to account without keeping guest match data', async () => {
  const user = userEvent.setup();
  vi.stubGlobal('fetch', vi.fn(async (url: string) => {
    if (url.endsWith('/auth/csrf')) return json({ requestToken: 'token' });
    if (url.endsWith('/guest-sessions')) return json({ guestSessionId: guestId });
    if (url.endsWith('/auth/me')) return json({ code: 'authentication_required', message: 'Sign in.' }, 401);
    if (url.endsWith('/auth/login')) return json({ userId: accountId, email: 'a@example.com' });
    throw Error(`Unexpected request: ${url}`);
  }));
  const query = renderApp();
  await user.click(screen.getByRole('button', { name: 'Chơi khách' }));
  await screen.findByText(`guest:${guestId}`);
  query.setQueryData(queryKeys.match({ scope: 'guest', ownerId: guestId }, matchId), { from: 'guest' });
  await user.click(screen.getByRole('link', { name: 'Tài khoản' }));
  await user.type(screen.getByLabelText('Email'), 'a@example.com');
  await user.type(screen.getByLabelText('Mật khẩu'), 'secret123');
  await user.click(screen.getByRole('button', { name: 'Đăng nhập' }));
  expect(await screen.findByTestId('session')).toHaveTextContent(`account:${accountId}`);
  expect(query.getQueryData(queryKeys.match({ scope: 'guest', ownerId: guestId }, matchId))).toBeUndefined();
  expect(screen.getByTestId('path')).toHaveTextContent('/play');
});

it('keeps the match URL but clears account data when the auth cookie is lost', async () => {
  const user = userEvent.setup();
  let authenticated = true;
  vi.stubGlobal('fetch', vi.fn(async (url: string) => {
    if (url.endsWith('/auth/me')) return authenticated ? json({ userId: accountId, email: 'a@example.com' }) : json({ code: 'authentication_required', message: 'Sign in.' }, 401);
    throw Error(`Unexpected request: ${url}`);
  }));
  const query = renderApp(`/play/${matchId}?scope=account`);
  await screen.findByText(`account:${accountId}`);
  query.setQueryData(queryKeys.match({ scope: 'account', ownerId: accountId }, matchId), { secret: 'old snapshot' });
  authenticated = false;
  await user.click(screen.getByRole('button', { name: 'Kiểm tra tài khoản' }));
  await screen.findByText('none');
  expect(query.getQueryData(queryKeys.match({ scope: 'account', ownerId: accountId }, matchId))).toBeUndefined();
  expect(screen.getByTestId('path')).toHaveTextContent(`/play/${matchId}`);
  expect(screen.getByRole('link', { name: 'Đăng nhập để tiếp tục' })).toHaveAttribute('href', expect.stringContaining(encodeURIComponent(`/play/${matchId}?scope=account`)));
});

it('hides old account queries as soon as a protected request reports 401', async () => {
  const user = userEvent.setup();
  let checks = 0;
  vi.stubGlobal('fetch', vi.fn((url: string) => {
    if (!url.endsWith('/auth/me')) throw Error(`Unexpected request: ${url}`);
    checks++;
    if (checks === 1) return Promise.resolve(json({ userId: accountId, email: 'a@example.com' }));
    return new Promise<Response>(() => {});
  }));
  const query = renderApp(`/play/${matchId}?scope=account`);
  await waitFor(() => expect(screen.getByTestId('session')).toHaveTextContent(`account:${accountId}`));
  query.setQueryData(queryKeys.match({ scope: 'account', ownerId: accountId }, matchId), { secret: 'old snapshot' });
  await user.click(screen.getByRole('button', { name: 'Kiểm tra tài khoản' }));
  await waitFor(() => expect(query.getQueryData(queryKeys.match({ scope: 'account', ownerId: accountId }, matchId))).toBeUndefined());
  expect(screen.getByRole('link', { name: 'Đăng nhập để tiếp tục' })).toBeInTheDocument();
});

it('clears previous account data before a different account signs in at the preserved match URL', async () => {
  const user = userEvent.setup();
  let authenticated = true;
  vi.stubGlobal('fetch', vi.fn(async (url: string) => {
    if (url.endsWith('/auth/me')) return authenticated ? json({ userId: accountId, email: 'a@example.com' }) : json({ code: 'authentication_required', message: 'Sign in.' }, 401);
    if (url.endsWith('/auth/csrf')) return json({ requestToken: 'token' });
    if (url.endsWith('/auth/login')) return json({ userId: otherId, email: 'b@example.com' });
    throw Error(`Unexpected request: ${url}`);
  }));
  const query = renderApp(`/play/${matchId}?scope=account`);
  await screen.findByText(`account:${accountId}`);
  query.setQueryData(queryKeys.match({ scope: 'account', ownerId: accountId }, matchId), { secret: 'old snapshot' });
  authenticated = false;
  await user.click(screen.getByRole('button', { name: 'Kiểm tra tài khoản' }));
  await user.click(await screen.findByRole('link', { name: 'Đăng nhập để tiếp tục' }));
  await user.type(screen.getByLabelText('Email'), 'b@example.com');
  await user.type(screen.getByLabelText('Mật khẩu'), 'secret123');
  await user.click(screen.getByRole('button', { name: 'Đăng nhập' }));
  expect(await screen.findByTestId('session')).toHaveTextContent(`account:${otherId}`);
  await waitFor(() => expect(screen.getByTestId('path').textContent).toBe(`/play/${matchId}`));
  expect(query.getQueryData(queryKeys.match({ scope: 'account', ownerId: accountId }, matchId))).toBeUndefined();
});

it('logs out to the choice screen and discards account queries', async () => {
  const user = userEvent.setup();
  vi.stubGlobal('fetch', vi.fn(async (url: string) => {
    if (url.endsWith('/auth/me')) return json({ userId: accountId, email: 'a@example.com' });
    if (url.endsWith('/auth/csrf')) return json({ requestToken: 'token' });
    if (url.endsWith('/auth/logout')) return new Response(null, { status: 204 });
    throw Error(`Unexpected request: ${url}`);
  }));
  const query = renderApp('/play?scope=account');
  await screen.findByText(`account:${accountId}`);
  query.setQueryData(queryKeys.match({ scope: 'account', ownerId: accountId }, matchId), { secret: 'old snapshot' });
  await user.click(screen.getByRole('button', { name: 'Đăng xuất' }));
  await screen.findByText('none');
  await waitFor(() => expect(screen.getByTestId('path').textContent).toBe('/'));
  expect(query.getQueryData(queryKeys.match({ scope: 'account', ownerId: accountId }, matchId))).toBeUndefined();
});

it('keeps the account visible and explains a failed logout request', async () => {
  const user = userEvent.setup();
  vi.stubGlobal('fetch', vi.fn(async (url: string) => {
    if (url.endsWith('/auth/me')) return json({ userId: accountId, email: 'a@example.com' });
    if (url.endsWith('/auth/csrf')) return json({ requestToken: 'token' });
    if (url.endsWith('/auth/logout')) throw Error('network unavailable');
    throw Error(`Unexpected request: ${url}`);
  }));
  renderApp('/play?scope=account');
  await screen.findByText(`account:${accountId}`);
  await user.click(screen.getByRole('button', { name: 'Đăng xuất' }));
  expect(await screen.findByText('Chưa đăng xuất được. Hãy thử lại.')).toHaveAttribute('role', 'alert');
  expect(screen.getByTestId('session')).toHaveTextContent(`account:${accountId}`);
  expect(screen.getByTestId('path').textContent).toBe('/play');
});
