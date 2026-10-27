import { useState, type FormEvent } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { ApiError } from '../../shared/api/errors';
import { useSession } from './sessionContext';

function safeReturnTo(raw: string | null): string {
  if (!raw || !raw.startsWith('/') || raw.startsWith('//') || raw.includes('\\')) return '/play?scope=account';
  return raw;
}

export function AuthForms({ mode }: { mode: 'login' | 'register' }) {
  const { login, register } = useSession();
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const action = mode === 'login' ? 'Đăng nhập' : 'Đăng ký';
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (busy) return;
    const form = event.currentTarget;
    const data = new FormData(form);
    const email = String(data.get('email') ?? '');
    const password = String(data.get('password') ?? '');
    setBusy(true);
    setError(null);
    try {
      if (mode === 'login') await login(email, password);
      else await register(email, password);
      form.reset();
      navigate(safeReturnTo(params.get('returnTo')), { replace: true });
    } catch (reason) {
      const code = reason instanceof ApiError ? reason.code : '';
      setError(code === 'invalid_credentials' ? 'Email hoặc mật khẩu chưa đúng.' : code === 'account_exists' ? 'Email này đã có tài khoản.' : code === 'validation_error' ? 'Kiểm tra lại email và mật khẩu.' : 'Chưa kết nối được. Hãy thử lại.');
    } finally {
      setBusy(false);
    }
  }
  const back = params.get('returnTo');
  const switchLink = `${mode === 'login' ? '/register' : '/login'}${back ? `?returnTo=${encodeURIComponent(back)}` : ''}`;
  return <><form onSubmit={event => void submit(event)} className="auth-form">
    <label>Email<input name="email" type="email" autoComplete="email" required /></label>
    <label>Mật khẩu<input name="password" type="password" autoComplete={mode === 'login' ? 'current-password' : 'new-password'} required minLength={6} /></label>
    <button className="button button-primary" type="submit" disabled={busy}>{busy ? 'Đang xử lý…' : action}</button>
    {error && <p role="alert">{error}</p>}
  </form><p>{mode === 'login' ? 'Chưa có tài khoản?' : 'Đã có tài khoản?'} <Link to={switchLink}>{mode === 'login' ? 'Đăng ký' : 'Đăng nhập'}</Link></p></>;
}
