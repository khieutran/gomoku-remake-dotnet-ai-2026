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
  const { pathname } = useLocation();
  const { session, logout } = useSession();
  const [logoutError, setLogoutError] = useState(false);
  const [menuOpenFor, setMenuOpenFor] = useState<string | null>(null);
  const menuOpen = menuOpenFor === pathname;
  const menuButton = useRef<HTMLButtonElement>(null);
  const menu = useRef<HTMLElement>(null);
  useEffect(() => {
    if (!menuOpen) return;
    menu.current?.querySelector<HTMLElement>('a, button')?.focus();
    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') {
        setMenuOpenFor(null);
        menuButton.current?.focus();
      }
    }
    function handlePointerDown(event: PointerEvent) {
      if (!(event.target instanceof Node) || menu.current?.contains(event.target) || menuButton.current?.contains(event.target)) return;
      setMenuOpenFor(null);
    }
    document.addEventListener('keydown', handleKeyDown);
    document.addEventListener('pointerdown', handlePointerDown);
    return () => {
      document.removeEventListener('keydown', handleKeyDown);
      document.removeEventListener('pointerdown', handlePointerDown);
    };
  }, [menuOpen]);
  async function signOut() {
    setLogoutError(false);
    try {
      await logout();
      navigate('/', { replace: true });
    } catch {
      setLogoutError(true);
    }
  }
  return <><header className="site-header"><Link to="/" className="brand" aria-label="Gomoku trang chủ"><span className="brand-mark" aria-hidden="true">X O</span><span>gomoku</span></Link><button ref={menuButton} type="button" className="site-menu-toggle" aria-expanded={menuOpen} aria-controls="site-navigation" onClick={() => setMenuOpenFor(openFor => openFor === pathname ? null : pathname)}><svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" aria-hidden="true"><path d="M4 6h16M4 12h16M4 18h16" /></svg><span>Menu</span></button><nav ref={menu} id="site-navigation" className={menuOpen ? 'site-nav--open' : ''} aria-label="Điều hướng chính"><NavLink to="/history" onClick={() => setMenuOpenFor(null)}>Lịch sử</NavLink><NavLink to="/leaderboard" onClick={() => setMenuOpenFor(null)}>Xếp hạng</NavLink>{session.scope === 'account' ? <button type="button" className="nav-action" onClick={() => { setMenuOpenFor(null); void signOut(); }}>Đăng xuất</button> : <NavLink to="/login" onClick={() => setMenuOpenFor(null)}>Tài khoản</NavLink>}</nav></header>{logoutError && <p role="alert">Chưa đăng xuất được. Hãy thử lại.</p>}</>;
}

export default function App() {
  const main = useRef<HTMLElement>(null);
  return <div className="app-shell"><a className="skip-link" href="#main">Đến nội dung</a><RouteFocus main={main} /><SiteHeader /><main ref={main} id="main" tabIndex={-1}><AppRoutes /></main></div>;
}
