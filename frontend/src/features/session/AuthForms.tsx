import { useState, type FormEvent } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { ApiError } from '../../shared/api/errors';
import { useSession } from './sessionContext';

function safeReturnTo(raw: string | null): string {
  if (!raw || !raw.startsWith('/') || raw.startsWith('//') || raw.includes('\\')) return '/play?scope=account';
  return raw;
}

const passwordHint = 'Mật khẩu cần ít nhất 6 ký tự, gồm chữ hoa, chữ thường, số và ký tự đặc biệt.';

function registrationPasswordIsValid(password: string): boolean {
  return password.length >= 6 && /[A-Z]/.test(password) && /[a-z]/.test(password) && /[0-9]/.test(password) && /[^a-zA-Z0-9]/.test(password);
}

function authErrorMessage(reason: unknown, mode: 'login' | 'register'): string {
  if (!(reason instanceof ApiError)) return 'Không kết nối được với máy chủ. Hãy kiểm tra mạng rồi thử lại.';
  switch (reason.code) {
    case 'invalid_credentials': return 'Email hoặc mật khẩu chưa đúng.';
    case 'account_exists': return 'Email này đã có tài khoản. Hãy đăng nhập.';
    case 'validation_error': return mode === 'register' && reason.details?.some(detail => detail.startsWith('Password'))
      ? passwordHint : 'Email hoặc mật khẩu chưa hợp lệ. Hãy kiểm tra lại.';
    case 'rate_limit_exceeded': return 'Bạn đã thử quá nhiều lần. Hãy đợi một lúc rồi thử lại.';
    case 'storage_unavailable': return 'Máy chủ đang tạm gián đoạn. Hãy thử lại sau.';
    case 'csrf_invalid': return 'Phiên xác thực đã hết hạn. Hãy tải lại trang rồi thử lại.';
    default: return `Không thực hiện được yêu cầu (${reason.code}). Hãy thử lại.`;
  }
}

export function AuthForms({ mode }: { mode: 'login' | 'register' }) {
  const { login, register } = useSession();
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [showPassword, setShowPassword] = useState(false);
  const action = mode === 'login' ? 'Đăng nhập' : 'Đăng ký';
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (busy) return;
    const form = event.currentTarget;
    const data = new FormData(form);
    const email = String(data.get('email') ?? '');
    const password = String(data.get('password') ?? '');
    if (mode === 'register' && !registrationPasswordIsValid(password)) {
      setError(passwordHint);
      return;
    }
    setBusy(true);
    setError(null);
    try {
      if (mode === 'login') await login(email, password);
      else await register(email, password);
      form.reset();
      navigate(safeReturnTo(params.get('returnTo')), { replace: true });
    } catch (reason) {
      setError(authErrorMessage(reason, mode));
    } finally {
      setBusy(false);
    }
  }
  const back = params.get('returnTo');
  const switchLink = `${mode === 'login' ? '/register' : '/login'}${back ? `?returnTo=${encodeURIComponent(back)}` : ''}`;
  return <><form onSubmit={event => void submit(event)} className="auth-form">
    <label>Email<input name="email" type="email" autoComplete="email" required /></label>
    <label htmlFor="auth-password">Mật khẩu</label>
    <div className="auth-password-field">
      <input id="auth-password" name="password" type={showPassword ? 'text' : 'password'} autoComplete={mode === 'login' ? 'current-password' : 'new-password'} required minLength={6} aria-describedby={mode === 'register' ? 'auth-password-hint' : undefined} />
      <button type="button" className="auth-password-toggle" aria-label={showPassword ? 'Ẩn mật khẩu' : 'Hiện mật khẩu'} aria-pressed={showPassword} onClick={() => setShowPassword(visible => !visible)}>
        <svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
          <path d="M2 12s3.5-6 10-6 10 6 10 6-3.5 6-10 6S2 12 2 12Z" />
          <circle cx="12" cy="12" r="3" />
          {!showPassword && <path d="M3 3 21 21" />}
        </svg>
      </button>
    </div>
    {mode === 'register' && <p id="auth-password-hint" className="auth-password-hint">{passwordHint}</p>}
    <button className="button button-primary" type="submit" disabled={busy}>{busy ? 'Đang xử lý…' : action}</button>
    {error && <p role="alert">{error}</p>}
  </form><p>{mode === 'login' ? 'Chưa có tài khoản?' : 'Đã có tài khoản?'} <Link to={switchLink}>{mode === 'login' ? 'Đăng ký' : 'Đăng nhập'}</Link></p></>;
}
