import { useState, type ReactNode } from 'react';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { useSession } from './sessionContext';

function returnPath(pathname: string, search: string) {
  return `${pathname}${search}`;
}

export function GuestStartButton({ children = 'Chơi khách', to = '/play?scope=guest' }: { children?: ReactNode; to?: string }) {
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
      navigate(to);
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
  return <><p>Phiên khách giữ 30 ngày trên thiết bị này và chỉ phiên đó xem lại được ván của mình. Đăng nhập sau không chuyển ván khách vào tài khoản.</p><button type="button" className="button button-primary" disabled={busy} onClick={() => void startGuest()}>{busy ? 'Đang mở phiên…' : 'Bắt đầu chơi khách'}</button>{guestError && <p role="alert">Chưa mở được phiên khách. Hãy thử lại.</p>}</>;
}
