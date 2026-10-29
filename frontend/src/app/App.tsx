import { useEffect, useRef, useState, type RefObject } from 'react';
import { Link, NavLink, useLocation, useNavigate } from 'react-router-dom';
import { AppRoutes } from './routes';
import { useSession } from '../features/session/sessionContext';

function RouteFocus({ main }: { main: RefObject<HTMLElement | null> }) {
  const { pathname } = useLocation();
  const previousPath = useRef(pathname);
  useEffect(() => {
    if (previousPath.current !== pathname) main.current?.querySelector('h1')?.focus();
    previousPath.current = pathname;
  }, [pathname, main]);
  return null;
}

function SiteHeader() {
  const navigate = useNavigate();
  const { session, logout } = useSession();
  const [logoutError, setLogoutError] = useState(false);
  async function signOut() {
    setLogoutError(false);
    try {
      await logout();
      navigate('/', { replace: true });
    } catch {
      setLogoutError(true);
    }
  }
  return <><header className="site-header"><Link to="/" className="brand" aria-label="Gomoku trang chủ"><span className="brand-mark" aria-hidden="true">X O</span><span>gomoku</span></Link><nav aria-label="Điều hướng chính"><NavLink to="/history">Lịch sử</NavLink><NavLink to="/leaderboard">Xếp hạng</NavLink>{session.scope === 'account' ? <button type="button" className="nav-action" onClick={() => void signOut()}>Đăng xuất</button> : <NavLink to="/login">Tài khoản</NavLink>}</nav></header>{logoutError && <p role="alert">Chưa đăng xuất được. Hãy thử lại.</p>}</>;
}

export default function App() {
  const main = useRef<HTMLElement>(null);
  return <div className="app-shell"><a className="skip-link" href="#main">Đến nội dung</a><RouteFocus main={main} /><SiteHeader /><main ref={main} id="main" tabIndex={-1}><AppRoutes /></main></div>;
}
