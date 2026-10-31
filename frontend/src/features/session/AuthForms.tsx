import { useRef, useState, type FormEvent } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { ApiError } from '../../shared/api/errors';
import { useSession } from './sessionContext';

type Field = 'displayName' | 'email' | 'password';
type FieldErrors = Partial<Record<Field, string>>;

// Mirrors the API rules in AuthEndpoints: 8+ character password, 2-24 character display name.
const minPassword = 8;
const maxPassword = 128;
const fieldOrder: Field[] = ['displayName', 'email', 'password'];
const messages = {
  displayName: 'Tên hiển thị cần từ 2 đến 24 ký tự, gồm chữ, số, khoảng trắng hoặc . _ -',
  emailMissing: 'Hãy nhập email.',
  emailInvalid: 'Email chưa đúng định dạng, ví dụ ten@gmail.com.',
  emailTaken: 'Email này đã có tài khoản. Hãy đăng nhập.',
  passwordMissing: 'Hãy nhập mật khẩu.',
  passwordShort: `Mật khẩu cần ít nhất ${minPassword} ký tự.`,
  passwordLong: `Mật khẩu tối đa ${maxPassword} ký tự.`,
};

function safeReturnTo(raw: string | null): string {
  if (!raw || !raw.startsWith('/') || raw.startsWith('//') || raw.includes('\\')) return '/play?scope=account';
  return raw;
}

const normalizeName = (value: string) => value.trim().split(/\s+/).join(' ');

function validate(mode: 'login' | 'register', displayName: string, email: string, password: string): FieldErrors {
  const errors: FieldErrors = {};
  if (mode === 'register') {
    const name = normalizeName(displayName);
    if (name.length < 2 || name.length > 24 || !/^[\p{L}\p{N} ._-]+$/u.test(name)) errors.displayName = messages.displayName;
  }
  if (!email.trim()) errors.email = messages.emailMissing;
  else if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email.trim())) errors.email = messages.emailInvalid;
  if (!password) errors.password = messages.passwordMissing;
  else if (mode === 'register' && password.length < minPassword) errors.password = messages.passwordShort;
  else if (mode === 'register' && password.length > maxPassword) errors.password = messages.passwordLong;
  return errors;
}

// Field problems go next to the field; everything else is one message for the whole form.
function serverErrors(reason: unknown): { fields: FieldErrors; form: string | null } {
  if (!(reason instanceof ApiError)) return { fields: {}, form: 'Không kết nối được với máy chủ. Hãy kiểm tra mạng rồi thử lại.' };
  const details = reason.details ?? [];
  switch (reason.code) {
    case 'account_exists': return { fields: { email: messages.emailTaken }, form: null };
    case 'validation_error': {
      const fields: FieldErrors = {};
      if (details.includes('display_name_invalid')) fields.displayName = messages.displayName;
      if (details.includes('email_invalid')) fields.email = messages.emailInvalid;
      if (details.includes('password_too_long')) fields.password = messages.passwordLong;
      else if (details.some(code => code === 'password_too_short' || code.startsWith('Password'))) fields.password = messages.passwordShort;
      return Object.keys(fields).length ? { fields, form: null } : { fields, form: 'Thông tin chưa hợp lệ. Hãy kiểm tra lại các ô.' };
    }
    case 'invalid_credentials': return { fields: {}, form: 'Email hoặc mật khẩu chưa đúng.' };
    case 'account_locked': return { fields: {}, form: 'Tài khoản đang tạm khoá vì nhập sai nhiều lần. Hãy thử lại sau vài phút.' };
    case 'rate_limit_exceeded': return { fields: {}, form: 'Bạn đã thử quá nhiều lần. Hãy đợi một lúc rồi thử lại.' };
    case 'storage_unavailable': return { fields: {}, form: 'Máy chủ đang tạm gián đoạn. Hãy thử lại sau.' };
    case 'csrf_invalid': return { fields: {}, form: 'Phiên xác thực đã hết hạn. Hãy tải lại trang rồi thử lại.' };
    default: return { fields: {}, form: `Không thực hiện được yêu cầu (${reason.code}). Hãy thử lại.` };
  }
}

export function AuthForms({ mode }: { mode: 'login' | 'register' }) {
  const { login, register } = useSession();
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const [busy, setBusy] = useState(false);
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [showPassword, setShowPassword] = useState(false);
  const inputs = useRef<Partial<Record<Field, HTMLInputElement | null>>>({});
  const action = mode === 'login' ? 'Đăng nhập' : 'Đăng ký';

  function showErrors(errors: FieldErrors) {
    setFieldErrors(errors);
    const first = fieldOrder.find(field => errors[field]);
    if (first) inputs.current[first]?.focus();
  }
  function clearError(field: Field) {
    setFieldErrors(current => current[field] ? { ...current, [field]: undefined } : current);
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (busy) return;
    const form = event.currentTarget;
    const data = new FormData(form);
    const displayName = String(data.get('displayName') ?? '');
    const email = String(data.get('email') ?? '').trim();
    const password = String(data.get('password') ?? '');
    setFormError(null);
    const invalid = validate(mode, displayName, email, password);
    if (Object.keys(invalid).length) {
      showErrors(invalid);
      return;
    }
    setFieldErrors({});
    setBusy(true);
    try {
      if (mode === 'login') await login(email, password);
      else await register(email, password, normalizeName(displayName));
      form.reset();
      navigate(safeReturnTo(params.get('returnTo')), { replace: true });
    } catch (reason) {
      const { fields, form: message } = serverErrors(reason);
      showErrors(fields);
      setFormError(message);
    } finally {
      setBusy(false);
    }
  }

  const describedBy = (field: Field, hint?: string) =>
    [hint, fieldErrors[field] ? `auth-${field}-error` : undefined].filter(Boolean).join(' ') || undefined;
  const fieldError = (field: Field) => fieldErrors[field] &&
    <p id={`auth-${field}-error`} className="auth-field-error">{fieldErrors[field]}</p>;
  const back = params.get('returnTo');
  const switchLink = `${mode === 'login' ? '/register' : '/login'}${back ? `?returnTo=${encodeURIComponent(back)}` : ''}`;
  return <><form onSubmit={event => void submit(event)} className="auth-form" noValidate>
    {mode === 'register' && <div className="auth-field">
      <label htmlFor="auth-displayName">Tên hiển thị</label>
      <input id="auth-displayName" name="displayName" autoComplete="nickname" maxLength={40} ref={element => { inputs.current.displayName = element; }}
        aria-invalid={Boolean(fieldErrors.displayName)} aria-describedby={describedBy('displayName', 'auth-displayName-hint')} onChange={() => clearError('displayName')} />
      <p id="auth-displayName-hint" className="auth-hint">Tên này hiện trên bảng xếp hạng.</p>
      {fieldError('displayName')}
    </div>}
    <div className="auth-field">
      <label htmlFor="auth-email">Email</label>
      <input id="auth-email" name="email" type="email" inputMode="email" autoComplete="email" ref={element => { inputs.current.email = element; }}
        aria-invalid={Boolean(fieldErrors.email)} aria-describedby={describedBy('email')} onChange={() => clearError('email')} />
      {fieldError('email')}
    </div>
    <div className="auth-field">
      <label htmlFor="auth-password">Mật khẩu</label>
      <div className="auth-password-field">
        <input id="auth-password" name="password" type={showPassword ? 'text' : 'password'} autoComplete={mode === 'login' ? 'current-password' : 'new-password'}
          ref={element => { inputs.current.password = element; }} aria-invalid={Boolean(fieldErrors.password)}
          aria-describedby={describedBy('password', mode === 'register' ? 'auth-password-hint' : undefined)} onChange={() => clearError('password')} />
        <button type="button" className="auth-password-toggle" aria-label={showPassword ? 'Ẩn mật khẩu' : 'Hiện mật khẩu'} aria-pressed={showPassword} onClick={() => setShowPassword(visible => !visible)}>
          <svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
            <path d="M2 12s3.5-6 10-6 10 6 10 6-3.5 6-10 6S2 12 2 12Z" />
            <circle cx="12" cy="12" r="3" />
            {!showPassword && <path d="M3 3 21 21" />}
          </svg>
        </button>
      </div>
      {mode === 'register' && <p id="auth-password-hint" className="auth-hint">Tối thiểu {minPassword} ký tự.</p>}
      {fieldError('password')}
    </div>
    <button className="button button-primary" type="submit" disabled={busy}>{busy ? 'Đang xử lý…' : action}</button>
    {formError && <p role="alert" className="auth-form-error">{formError}</p>}
  </form><p>{mode === 'login' ? 'Chưa có tài khoản?' : 'Đã có tài khoản?'} <Link to={switchLink}>{mode === 'login' ? 'Đăng ký' : 'Đăng nhập'}</Link></p></>;
}
