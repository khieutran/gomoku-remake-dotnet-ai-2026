import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { vi } from 'vitest';
import App from './App';

it('offers entry choices without opening a server session', () => {
  const fetchSpy = vi.spyOn(globalThis, 'fetch');
  render(<MemoryRouter><App /></MemoryRouter>);
  expect(screen.getByRole('heading', { name: /Một nước nữa nhé/ })).toBeInTheDocument();
  expect(screen.getByRole('link', { name: 'Chơi khách' })).toHaveAttribute('href', '/play?scope=guest');
  expect(screen.getByRole('link', { name: 'Đăng ký' })).toBeInTheDocument();
  expect(fetchSpy).not.toHaveBeenCalled();
  fetchSpy.mockRestore();
});
it('supports keyboard navigation and moves focus to the destination heading', async () => {
  const user = userEvent.setup();
  render(<MemoryRouter><App /></MemoryRouter>);
  await user.tab();
  expect(screen.getByRole('link', { name: 'Đến nội dung' })).toHaveFocus();
  screen.getByRole('link', { name: 'Lịch sử' }).focus();
  await user.keyboard('{Enter}');
  expect(screen.getByRole('heading', { name: 'Lịch sử ván đấu' })).toHaveFocus();
});
it.each([['/play', 'Bắt đầu chơi'], ['/play/example', 'Ván đang chơi'], ['/login', 'Đăng nhập'], ['/register', 'Đăng ký'], ['/history/example', 'Phát lại ván đấu'], ['/leaderboard', 'Bảng xếp hạng']])('renders the %s route', (path, heading) => {
  render(<MemoryRouter initialEntries={[path]}><App /></MemoryRouter>);
  expect(screen.getByRole('heading', { name: heading })).toBeInTheDocument();
});
