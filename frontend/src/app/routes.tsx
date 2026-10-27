import { Link, Route, Routes } from 'react-router-dom';
import { Window } from '../shared/ui/Window';

function Page({ title, children }: { title: string; children: React.ReactNode }) {
  return <Window title={title}><h1 tabIndex={-1}>{title}</h1>{children}</Window>;
}
function EntryPage() {
  return <div className="entry-layout"><Page title="Một nước nữa nhé"><p className="intro">Đấu trí cùng bot, lưu từng ván và trở lại chơi tiếp.</p><div className="entry-actions"><Link className="button button-primary" to="/play?scope=guest">Chơi khách</Link><Link className="button" to="/login">Đăng nhập</Link><Link className="text-link" to="/register">Đăng ký</Link></div><p className="guest-note">Ván khách được lưu lâu dài để cải thiện AI. Chỉ phiên khách này có quyền xem lại; đăng nhập sau không chuyển ván khách vào tài khoản.</p></Page><Window title="Bàn cờ của bạn"><div className="entry-stones" aria-hidden="true"><span>X</span><span>O</span></div><p>Bạn cầm X, bot cầm O. Mỗi nước đi mở ra một cơ hội mới.</p><p>Chọn chơi khách hoặc đăng nhập để bắt đầu.</p></Window></div>;
}
export function AppRoutes() {
  return <Routes>
    <Route path="/" element={<EntryPage />} />
    <Route path="/play" element={<Page title="Bắt đầu chơi"><p>Chọn phiên khách hoặc đăng nhập để tìm ván đang chơi của bạn.</p><Link className="button" to="/">Chọn cách chơi</Link></Page>} />
    <Route path="/play/:matchId" element={<Page title="Ván đang chơi"><p>Ván đấu sẽ xuất hiện tại đây khi kết nối phiên chơi.</p><Link to="/">Chọn cách chơi</Link></Page>} />
    <Route path="/login" element={<Page title="Đăng nhập"><p>Đăng nhập tài khoản để chơi tiếp và xem lại các ván đã lưu.</p><Link to="/register">Chưa có tài khoản? Đăng ký</Link></Page>} />
    <Route path="/register" element={<Page title="Đăng ký"><p>Tạo tài khoản để lưu ván đấu và tham gia bảng xếp hạng.</p><Link to="/login">Đã có tài khoản? Đăng nhập</Link></Page>} />
    <Route path="/history" element={<Page title="Lịch sử ván đấu"><p>Các ván đã kết thúc và ván đang chơi sẽ được tải theo phiên của bạn.</p><Link to="/">Chọn cách chơi</Link></Page>} />
    <Route path="/history/:gameId" element={<Page title="Phát lại ván đấu"><p>Xem lại từng nước đi của ván đã kết thúc trong phiên của bạn.</p><Link to="/history">Về lịch sử</Link></Page>} />
    <Route path="/leaderboard" element={<Page title="Bảng xếp hạng"><p>Điểm và vị trí tài khoản sẽ được lấy từ bảng xếp hạng.</p><p>Khách có thể xem bảng, nhưng không có vị trí riêng.</p></Page>} />
    <Route path="*" element={<Page title="Không tìm thấy trang"><Link to="/">Về trang chọn cách chơi</Link></Page>} />
  </Routes>;
}
