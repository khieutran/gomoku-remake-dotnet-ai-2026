import { useEffect, useRef } from 'react';
import { Link, NavLink, useLocation } from 'react-router-dom';
import { AppRoutes } from './routes';
export default function App() {
  const location = useLocation();
  const previousPath = useRef(location.pathname);
  const main = useRef<HTMLElement>(null);
  useEffect(() => {
    if (previousPath.current !== location.pathname) main.current?.querySelector('h1')?.focus();
    previousPath.current = location.pathname;
  }, [location.pathname]);
  return <div className="app-shell"><a className="skip-link" href="#main">Đến nội dung</a><header className="site-header"><Link to="/" className="brand" aria-label="Gomoku trang chủ"><span className="brand-mark" aria-hidden="true">X O</span><span>gomoku</span></Link><nav aria-label="Điều hướng chính"><NavLink to="/history">Lịch sử</NavLink><NavLink to="/leaderboard">Xếp hạng</NavLink><NavLink to="/login">Tài khoản</NavLink></nav></header><main ref={main} id="main" tabIndex={-1}><AppRoutes /></main><footer>Mỗi nước đi đều được lưu trong phiên của bạn.</footer></div>;
}
