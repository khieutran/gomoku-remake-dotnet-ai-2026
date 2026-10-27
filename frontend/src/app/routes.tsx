import { Link, Route, Routes, useSearchParams } from 'react-router-dom';
import { Window } from '../shared/ui/Window';
import { AuthForms } from '../features/session/AuthForms';
import { GuestStartButton, OwnerGate } from '../features/session/OwnerGate';
import { useSession } from '../features/session/sessionContext';

function Page({ title, children }: { title: string; children: React.ReactNode }) {
  return <Window title={title}><h1 tabIndex={-1}>{title}</h1>{children}</Window>;
}
function EntryPage() {
  const { session } = useSession();
  return <div className="entry-layout"><Page title="Một nước nữa nhé"><p className="intro">Đấu trí cùng bot, lưu từng ván và trở lại chơi tiếp.</p><div className="entry-actions">{session.scope === 'account' && <Link className="button button-primary" to="/play?scope=account">Chơi tiếp</Link>}{session.scope === 'guest' && <Link className="button button-primary" to="/play?scope=guest">Tiếp tục ván khách</Link>}<GuestStartButton />{session.scope !== 'account' && <><Link className="button" to="/login">Đăng nhập</Link><Link className="text-link" to="/register">Đăng ký</Link></>}</div><p className="guest-note">Ván khách được lưu lâu dài để cải thiện AI. Chỉ phiên khách này có quyền xem lại; đăng nhập sau không chuyển ván khách vào tài khoản.</p></Page><Window title="Bàn cờ của bạn"><div className="entry-stones" aria-hidden="true"><span>X</span><span>O</span></div><p>Bạn cầm X, bot cầm O. Mỗi nước đi mở ra một cơ hội mới.</p><p>Chọn chơi khách hoặc đăng nhập để bắt đầu.</p></Window></div>;
}
function PlayEntry() {
  const [params] = useSearchParams();
  const { session } = useSession();
  const scope = params.get('scope') === 'guest' ? 'guest' : 'account';
  return <Page title="Bắt đầu chơi"><OwnerGate scope={scope}><p>{session.scope === 'guest' ? 'Phiên khách đã sẵn sàng.' : 'Tài khoản đã sẵn sàng.'} Ván đang chơi sẽ được tìm tại đây.</p></OwnerGate></Page>;
}
function PlayMatch() {
  const [params] = useSearchParams();
  const { session } = useSession();
  const scope = params.get('scope') === 'guest' ? 'guest' : params.get('scope') === 'account' ? 'account' : session.scope === 'guest' ? 'guest' : 'account';
  return <Page title="Ván đang chơi"><OwnerGate scope={scope}><p>Ván đấu sẽ xuất hiện tại đây khi kết nối phiên chơi.</p></OwnerGate></Page>;
}
export function AppRoutes() {
  return <Routes>
    <Route path="/" element={<EntryPage />} />
    <Route path="/play" element={<PlayEntry />} />
    <Route path="/play/:matchId" element={<PlayMatch />} />
    <Route path="/login" element={<Page title="Đăng nhập"><AuthForms mode="login" /></Page>} />
    <Route path="/register" element={<Page title="Đăng ký"><AuthForms mode="register" /></Page>} />
    <Route path="/history" element={<Page title="Lịch sử ván đấu"><p>Các ván đã kết thúc và ván đang chơi sẽ được tải theo phiên của bạn.</p><Link to="/">Chọn cách chơi</Link></Page>} />
    <Route path="/history/:gameId" element={<Page title="Phát lại ván đấu"><p>Xem lại từng nước đi của ván đã kết thúc trong phiên của bạn.</p><Link to="/history">Về lịch sử</Link></Page>} />
    <Route path="/leaderboard" element={<Page title="Bảng xếp hạng"><p>Điểm và vị trí tài khoản sẽ được lấy từ bảng xếp hạng.</p><p>Khách có thể xem bảng, nhưng không có vị trí riêng.</p></Page>} />
    <Route path="*" element={<Page title="Không tìm thấy trang"><Link to="/">Về trang chọn cách chơi</Link></Page>} />
  </Routes>;
}
