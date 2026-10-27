import { Link, Route, Routes, useParams, useSearchParams } from 'react-router-dom';
import { Window } from '../shared/ui/Window';
import { AuthForms } from '../features/session/AuthForms';
import { GuestStartButton, OwnerGate } from '../features/session/OwnerGate';
import { useSession } from '../features/session/sessionContext';
import { LeaderboardPage } from '../features/leaderboard/LeaderboardPage';
import { HistoryPage } from '../features/history/HistoryPage';
import { ReplayPage } from '../features/history/ReplayPage';
import { MatchPage } from '../features/match/MatchPage';

function Page({ title, children }: { title: string; children: React.ReactNode }) {
  return <Window title={title}><h1 tabIndex={-1}>{title}</h1>{children}</Window>;
}
function EntryPage() {
  const { session } = useSession();
  return <div className="entry-layout"><Page title="Một nước nữa nhé"><p className="intro">Đấu trí cùng bot, lưu từng ván và trở lại chơi tiếp.</p><div className="entry-actions">{session.scope === 'account' && <Link className="button button-primary" to="/play?scope=account">Chơi tiếp</Link>}{session.scope === 'guest' && <Link className="button button-primary" to="/play?scope=guest">Tiếp tục ván khách</Link>}<GuestStartButton />{session.scope !== 'account' && <><Link className="button" to="/login">Đăng nhập</Link><Link className="text-link" to="/register">Đăng ký</Link></>}</div><p className="guest-note">Ván khách được lưu lâu dài để cải thiện AI. Chỉ phiên khách này có quyền xem lại; đăng nhập sau không chuyển ván khách vào tài khoản.</p></Page><Window title="Bàn cờ của bạn"><div className="entry-stones" aria-hidden="true"><span>X</span><span>O</span></div><p>Bạn cầm X, bot cầm O. Mỗi nước đi mở ra một cơ hội mới.</p><p>Chọn chơi khách hoặc đăng nhập để bắt đầu.</p></Window></div>;
}
function PlayEntry() {
  const [params] = useSearchParams();
  const scope = params.get('scope') === 'guest' ? 'guest' : 'account';
  return <Page title="Bắt đầu chơi"><OwnerGate scope={scope}><MatchPage scope={scope} /></OwnerGate></Page>;
}
function PlayMatch() {
  const [params] = useSearchParams();
  const { matchId } = useParams();
  const { session } = useSession();
  const scope = params.get('scope') === 'guest' ? 'guest' : params.get('scope') === 'account' ? 'account' : session.scope === 'guest' ? 'guest' : 'account';
  return <Page title="Ván đang chơi"><OwnerGate scope={scope}><MatchPage scope={scope} matchId={matchId} /></OwnerGate></Page>;
}
function HistoryRoute() {
  const [params] = useSearchParams();
  const { session } = useSession();
  const scope = params.get('scope') === 'guest' ? 'guest' : params.get('scope') === 'account' ? 'account' : session.scope === 'guest' ? 'guest' : 'account';
  return <Window title="Lịch sử ván đấu"><HistoryPage scope={scope} /></Window>;
}
function ReplayRoute() {
  const [params] = useSearchParams();
  const { gameId } = useParams();
  const { session } = useSession();
  const scope = params.get('scope') === 'guest' ? 'guest' : params.get('scope') === 'account' ? 'account' : session.scope === 'guest' ? 'guest' : 'account';
  return <Window title="Phát lại ván đấu"><ReplayPage scope={scope} gameId={gameId ?? ''} /></Window>;
}
export function AppRoutes() {
  return <Routes>
    <Route path="/" element={<EntryPage />} />
    <Route path="/play" element={<PlayEntry />} />
    <Route path="/play/:matchId" element={<PlayMatch />} />
    <Route path="/login" element={<Page title="Đăng nhập"><AuthForms mode="login" /></Page>} />
    <Route path="/register" element={<Page title="Đăng ký"><AuthForms mode="register" /></Page>} />
    <Route path="/history" element={<HistoryRoute />} />
    <Route path="/history/:gameId" element={<ReplayRoute />} />
    <Route path="/leaderboard" element={<LeaderboardPage />} />
    <Route path="*" element={<Page title="Không tìm thấy trang"><Link to="/">Về trang chọn cách chơi</Link></Page>} />
  </Routes>;
}
