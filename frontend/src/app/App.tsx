import { useEffect, useRef, useState } from 'react';
import { Link, NavLink, useLocation, useNavigate } from 'react-router-dom';
import { AppRoutes } from './routes';
import { useSession } from '../features/session/sessionContext';
export default function App() {
  const location = useLocation();
  const navigate = useNavigate();
  const { session, logout } = useSession();
  const [logoutError, setLogoutError] = useState(false);
  const previousPath = useRef(location.pathname);
  const main = useRef<HTMLElement>(null);
  async function signOut() {
    setLogoutError(false);
    try {
      await logout();
      navigate('/', { replace: true });
    } catch {
      setLogoutError(true);
    }
  }
  useEffect(() => {
    if (previousPath.current !== location.pathname) main.current?.querySelector('h1')?.focus();
    previousPath.current = location.pathname;
  }, [location.pathname]);
  return <div className="app-shell"><a className="skip-link" href="#main">Đến nội dung</a><header className="site-header"><Link to="/" className="brand" aria-label="Gomoku trang chủ"><span className="brand-mark" aria-hidden="true">X O</span><span>gomoku</span></Link><nav aria-label="Điều hướng chính"><NavLink to="/history">Lịch sử</NavLink><NavLink to="/leaderboard">Xếp hạng</NavLink>{session.scope === 'account' ? <button type="button" className="nav-action" onClick={() => void signOut()}>Đăng xuất</button> : <NavLink to="/login">Tài khoản</NavLink>}</nav></header>{logoutError && <p role="alert">Chưa đăng xuất được. Hãy thử lại.</p>}<main ref={main} id="main" tabIndex={-1}><AppRoutes /></main><footer>Mỗi nước đi đều được lưu trong phiên của bạn.</footer></div>;
}
