import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { expect, it, vi } from 'vitest';
import { ApiError } from '../../shared/api/errors';
import { AuthForms } from './AuthForms';
import { SessionContext, type SessionContextValue } from './sessionContext';

function renderForm(mode: 'login' | 'register', overrides: Partial<SessionContextValue> = {}) {
  const value: SessionContextValue = {
    session: { scope: 'none' }, ready: true, error: null,
    chooseGuest: vi.fn(), login: vi.fn(), register: vi.fn(), logout: vi.fn(), requireAccount: vi.fn(),
    ...overrides,
  };
  render(<SessionContext.Provider value={value}><MemoryRouter><AuthForms mode={mode} /></MemoryRouter></SessionContext.Provider>);
  return value;
}

it('asks for a display name and only needs an 8-character password to register', async () => {
  const user = userEvent.setup();
  const session = renderForm('register');
  expect(screen.getByText('Tối thiểu 8 ký tự.')).toBeInTheDocument();
  await user.type(screen.getByLabelText('Tên hiển thị'), 'Kiô Trần');
  await user.type(screen.getByLabelText('Email'), 'kio@example.com');
  await user.type(screen.getByLabelText('Mật khẩu'), 'matkhau8');
  await user.click(screen.getByRole('button', { name: 'Đăng ký' }));
  expect(session.register).toHaveBeenCalledWith('kio@example.com', 'matkhau8', 'Kiô Trần');
});

it('marks each invalid field next to it, focuses the first one, and sends nothing', async () => {
  const user = userEvent.setup();
  const session = renderForm('register');
  await user.type(screen.getByLabelText('Tên hiển thị'), 'K');
  await user.type(screen.getByLabelText('Email'), 'kio@example.com');
  await user.type(screen.getByLabelText('Mật khẩu'), 'ngan');
  await user.click(screen.getByRole('button', { name: 'Đăng ký' }));
  const name = screen.getByLabelText('Tên hiển thị');
  expect(name).toHaveAttribute('aria-invalid', 'true');
  expect(name).toHaveAccessibleDescription(/2 đến 24 ký tự/);
  expect(name).toHaveFocus();
  expect(screen.getByLabelText('Mật khẩu')).toHaveAccessibleDescription(/ít nhất 8 ký tự/);
  expect(screen.getByLabelText('Email')).not.toHaveAttribute('aria-invalid', 'true');
  expect(session.register).not.toHaveBeenCalled();
});

it('puts a taken email error under the email field', async () => {
  const user = userEvent.setup();
  renderForm('register', { register: vi.fn().mockRejectedValue(new ApiError(409, 'account_exists', 'exists')) });
  await user.type(screen.getByLabelText('Tên hiển thị'), 'Kiô');
  await user.type(screen.getByLabelText('Email'), 'kio@example.com');
  await user.type(screen.getByLabelText('Mật khẩu'), 'matkhau8');
  await user.click(screen.getByRole('button', { name: 'Đăng ký' }));
  const email = screen.getByLabelText('Email');
  expect(await screen.findByText(/Email này đã có tài khoản/)).toBeInTheDocument();
  expect(email).toHaveAttribute('aria-invalid', 'true');
  expect(email).toHaveFocus();
});

it('explains wrong credentials and a temporary lock in plain words', async () => {
  const user = userEvent.setup();
  const login = vi.fn()
    .mockRejectedValueOnce(new ApiError(401, 'invalid_credentials', 'bad'))
    .mockRejectedValueOnce(new ApiError(423, 'account_locked', 'locked'));
  renderForm('login', { login });
  await user.type(screen.getByLabelText('Email'), 'kio@example.com');
  await user.type(screen.getByLabelText('Mật khẩu'), 'matkhau8');
  await user.click(screen.getByRole('button', { name: 'Đăng nhập' }));
  expect(await screen.findByRole('alert')).toHaveTextContent('Email hoặc mật khẩu chưa đúng.');
  await user.click(screen.getByRole('button', { name: 'Đăng nhập' }));
  expect(await screen.findByRole('alert')).toHaveTextContent(/tạm khoá.*vài phút/);
});
