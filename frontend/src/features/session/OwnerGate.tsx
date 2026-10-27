import { useState, type ReactNode } from 'react';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { useSession } from './sessionContext';

function returnPath(pathname: string, search: string) {
  return `${pathname}${search}`;
}

export function GuestStartButton({ children = 'Chơi khách' }: { children?: ReactNode }) {
  const { chooseGuest } = useSession();
  const navigate = useNavigate();
  const [error, setError] = useState(false);
  const [busy, setBusy] = useState(false);
  async function start() {
    if (busy) return;
    setBusy(true);
    setError(false);
    try {
      await chooseGuest();
      navigate('/play?scope=guest');
    } catch {
      setError(true);
    } finally {
      setBusy(false);
    }
  }
  return <><button type="button" className="button button-primary" disabled={busy} onClick={() => void start()}>{busy ? 'Đang mở phiên…' : children}</button>{error && <p role="alert">Chưa mở được phiên khách. Hãy thử lại.</p>}</>;
}

export function OwnerGate({ scope, children }: { scope: 'account' | 'guest'; children: ReactNode }) {
  const { session, ready, error, chooseGuest } = useSession();
  const location = useLocation();
  const [busy, setBusy] = useState(false);
  const [guestError, setGuestError] = useState(false);
  if (!ready) return <p role="status">Đang kiểm tra phiên…</p>;
  if (error) return <p role="alert">{error}</p>;
  if (session.scope === scope) return <>{children}</>;
  if (scope === 'account') {
    const back = encodeURIComponent(returnPath(location.pathname, location.search));
    return <p>Đăng nhập để xem ván của tài khoản. <Link to={`/login?returnTo=${back}`}>Đăng nhập để tiếp tục</Link></p>;
  }
  async function startGuest() {
    if (busy) return;
    setBusy(true);
    setGuestError(false);
    try { await chooseGuest(); } catch { setGuestError(true); } finally { setBusy(false); }
  }
  return <><p>Ván khách được lưu lâu dài và chỉ phiên khách hiện tại có quyền xem lại. Ván này không chuyển vào tài khoản khi đăng nhập.</p><button type="button" className="button button-primary" disabled={busy} onClick={() => void startGuest()}>{busy ? 'Đang mở phiên…' : 'Bắt đầu chơi khách'}</button>{guestError && <p role="alert">Chưa mở được phiên khách. Hãy thử lại.</p>}</>;
}
