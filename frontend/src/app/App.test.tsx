import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { afterEach, vi } from 'vitest';
import App from './App';
import { SessionProvider } from '../features/session/SessionProvider';

function renderApp(path = '/') {
  const query = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(<QueryClientProvider client={query}><MemoryRouter initialEntries={[path]}><SessionProvider><App /></SessionProvider></MemoryRouter></QueryClientProvider>);
}
afterEach(() => vi.unstubAllGlobals());

it('offers guest choice without creating a guest on page load', () => {
  const fetchSpy = vi.fn().mockResolvedValue(new Response(JSON.stringify({ code: 'authentication_required', message: 'Sign in.' }), { status: 401 }));
  vi.stubGlobal('fetch', fetchSpy);
  renderApp();
  expect(screen.getByRole('heading', { name: /Một nước nữa nhé/ })).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Chơi khách' })).toBeInTheDocument();
  expect(screen.getByRole('link', { name: 'Đăng ký' })).toBeInTheDocument();
  expect(fetchSpy.mock.calls.some(([url]) => String(url).endsWith('/guest-sessions'))).toBe(false);
});
it('supports keyboard navigation and moves focus to the destination heading', async () => {
  const user = userEvent.setup();
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ code: 'authentication_required', message: 'Sign in.' }), { status: 401 })));
  renderApp();
  await user.tab();
  expect(screen.getByRole('link', { name: 'Đến nội dung' })).toHaveFocus();
  screen.getByRole('link', { name: 'Lịch sử' }).focus();
  await user.keyboard('{Enter}');
  expect(screen.getByRole('heading', { name: 'Lịch sử ván đấu' })).toHaveFocus();
});
it.each([['/play', 'Bắt đầu chơi'], ['/play/example', 'Ván đang chơi'], ['/login', 'Đăng nhập'], ['/register', 'Đăng ký'], ['/history/example', 'Phát lại ván đấu'], ['/leaderboard', 'Bảng xếp hạng']])('renders the %s route', (path, heading) => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ code: 'authentication_required', message: 'Sign in.' }), { status: 401 })));
  renderApp(path);
  expect(screen.getByRole('heading', { name: heading })).toBeInTheDocument();
});
