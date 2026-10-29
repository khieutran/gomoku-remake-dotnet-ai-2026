import { lazy, memo, Suspense, useState } from 'react';
import { Link, Route, Routes, useParams, useSearchParams } from 'react-router-dom';
import { Window } from '../shared/ui/Window';
import { GameWorkspace } from '../shared/ui/GameWorkspace';
import { AuthForms } from '../features/session/AuthForms';
import { OwnerGate } from '../features/session/OwnerGate';
import { useSession } from '../features/session/sessionContext';
import { MatchPage } from '../features/match/MatchPage';
import { Board } from '../features/board/Board';

const LeaderboardPage = lazy(() => import('../features/leaderboard/LeaderboardPage').then(module => ({ default: module.LeaderboardPage })));
const HistoryPage = lazy(() => import('../features/history/HistoryPage').then(module => ({ default: module.HistoryPage })));
const ReplayPage = lazy(() => import('../features/history/ReplayPage').then(module => ({ default: module.ReplayPage })));

function Page({ title, children, className = '' }: { title: string; children: React.ReactNode; className?: string }) {
  return <Window title={title} className={className}><h1 tabIndex={-1}>{title}</h1>{children}</Window>;
}
const EntryBoard = memo(function EntryBoard({ columns }: { columns: 20 | 30 }) {
  return <Board size={20} columns={columns} moves={[]} interactive={false} preview />;
});
function EntryPage() {
  const { session, chooseGuest } = useSession();
  const [params] = useSearchParams();
  const [step, setStep] = useState<'choice' | 'identity' | 'setup'>(params.get('start') === 'setup' ? 'setup' : 'choice');
  const [chosenScope, setChosenScope] = useState<'guest' | 'account' | null>(null);
  const playScope = chosenScope ?? (session.scope === 'account' ? 'account' : 'guest');
  const [guestBusy, setGuestBusy] = useState(false);
  const [guestError, setGuestError] = useState(false);
  const [stone, setStone] = useState<'X' | 'O'>('X');
  const [first, setFirst] = useState(true);
  const boardColumns = typeof window !== 'undefined' && window.matchMedia?.('(max-width: 700px)').matches ? 20 : 30;
  const setup = `&stone=${stone}&first=${first}&boardSize=20&boardColumns=${boardColumns}`;
  async function startGuest() {
    if (guestBusy) return;
    setGuestBusy(true);
    setGuestError(false);
    try {
      await chooseGuest();
      setChosenScope('guest');
      setStep('setup');
    } catch {
      setGuestError(true);
    } finally {
      setGuestBusy(false);
    }
  }
  return <GameWorkspace variant="entry"
    main={<Window title="Bàn cờ" className="workspace-main-window entry-main-window">
      <div className="entry-board-stage">
        <EntryBoard columns={boardColumns} />
        <section className="start-board-overlay" aria-label="Chọn cách chơi">
          <div className="start-menu">
            <h1 tabIndex={-1}>{step === 'choice' ? 'Một nước nữa nhé' : step === 'identity' ? 'Chọn cách chơi' : 'Sẵn sàng vào trận'}</h1>
            {step === 'choice' && <div className="entry-actions">
              {session.scope !== 'none' && <Link className="button button-primary" to={`/play?scope=${session.scope}`}>Chơi tiếp</Link>}
              <button type="button" className={`button${session.scope === 'none' ? ' button-primary' : ''}`} onClick={() => setStep('identity')}>Chơi mới</button>
            </div>}
            {step === 'identity' && <div className="entry-actions">
              {session.scope === 'account' && <button type="button" className="button button-primary" onClick={() => { setChosenScope('account'); setStep('setup'); }}>Chơi với tài khoản</button>}
              <button type="button" className={`button${session.scope !== 'account' ? ' button-primary' : ''}`} disabled={guestBusy} onClick={() => void startGuest()}>{guestBusy ? 'Đang mở phiên…' : 'Chơi khách'}</button>
              <Link className="button" to="/login?returnTo=%2F%3Fstart%3Dsetup">Đăng nhập</Link>
              <Link className="start-register" to="/register?returnTo=%2F%3Fstart%3Dsetup">Chưa có tài khoản? Đăng ký</Link>
              <button type="button" className="start-back" onClick={() => setStep('choice')}>Quay lại</button>
              {guestError && <p role="alert">Chưa mở được phiên khách. Hãy thử lại.</p>}
            </div>}
            {step === 'setup' && <>
              <p className="start-board-size">Bàn cờ {boardColumns === 30 ? '20 × 30' : '20 × 20'}</p>
              <fieldset className="start-choice"><legend>Bạn cầm quân</legend><label><input type="radio" name="stone" checked={stone === 'X'} onChange={() => setStone('X')} /> X</label><label><input type="radio" name="stone" checked={stone === 'O'} onChange={() => setStone('O')} /> O</label></fieldset>
              <fieldset className="start-choice"><legend>Lượt mở</legend><label><input type="radio" name="first" checked={first} onChange={() => setFirst(true)} /> Đánh trước</label><label><input type="radio" name="first" checked={!first} onChange={() => setFirst(false)} /> Đánh sau</label></fieldset>
              <div className="entry-actions"><Link className="button button-primary" to={`/play?scope=${playScope}${setup}`}>Bắt đầu trận</Link><button type="button" className="start-back" onClick={() => setStep('identity')}>Quay lại</button></div>
            </>}
          </div>
        </section>
      </div>
      <details className="entry-mobile-note"><summary>Ván khách được lưu thế nào?</summary><p>Ván khách được lưu lâu dài để cải thiện AI. Chỉ phiên khách này có quyền xem lại; đăng nhập sau không chuyển ván khách vào tài khoản.</p></details>
    </Window>}
    top={<Window title="Cách chơi"><div className="entry-stones" aria-hidden="true"><span>{stone}</span><span>{stone === 'X' ? 'O' : 'X'}</span></div><p>Bạn cầm {stone}, bot cầm {stone === 'X' ? 'O' : 'X'}. {first ? 'Bạn đánh trước.' : 'Bot đánh trước.'} Tạo đúng năm quân liên tiếp để thắng.</p></Window>}
    bottom={<Window title="Lưu ván"><p className="guest-note">Ván khách được lưu lâu dài để cải thiện AI. Chỉ phiên khách này có quyền xem lại; đăng nhập sau không chuyển ván khách vào tài khoản.</p><Link className="button" to="/history">Xem lịch sử</Link></Window>}
  />;
}
function PlayEntry() {
  const [params] = useSearchParams();
  const scope = params.get('scope') === 'guest' ? 'guest' : 'account';
  return <PlayRouteContent scope={scope} />;
}
function PlayRouteContent({ scope, matchId }: { scope: 'guest' | 'account'; matchId?: string }) {
  const { session } = useSession();
  if (session.scope === scope) return <MatchPage scope={scope} matchId={matchId} />;
  return <GameWorkspace variant="match"
    main={<Window title="Ván đấu" className="workspace-main-window"><h1 tabIndex={-1}>Đấu với bot</h1><OwnerGate scope={scope}><MatchPage scope={scope} matchId={matchId} /></OwnerGate></Window>}
    top={<Window title="Cách chơi"><div className="entry-stones" aria-hidden="true"><span>X</span><span>O</span></div><p>Bạn cầm X, bot cầm O.</p></Window>}
    bottom={<Window title="Lưu ván"><p>Ván đấu sẽ được lưu để bạn trở lại chơi tiếp.</p></Window>}
  />;
}
function PlayMatch() {
  const [params] = useSearchParams();
  const { matchId } = useParams();
  const { session } = useSession();
  const scope = params.get('scope') === 'guest' ? 'guest' : params.get('scope') === 'account' ? 'account' : session.scope === 'guest' ? 'guest' : 'account';
  return <PlayRouteContent scope={scope} matchId={matchId} />;
}
function HistoryRoute() {
  const [params] = useSearchParams();
  const { session } = useSession();
  const scope = params.get('scope') === 'guest' ? 'guest' : params.get('scope') === 'account' ? 'account' : session.scope === 'guest' ? 'guest' : 'account';
  return <Window title="Lịch sử ván đấu" className="history-window"><HistoryPage scope={scope} /></Window>;
}
function ReplayRoute() {
  const [params] = useSearchParams();
  const { gameId } = useParams();
  const { session } = useSession();
  const scope = params.get('scope') === 'guest' ? 'guest' : params.get('scope') === 'account' ? 'account' : session.scope === 'guest' ? 'guest' : 'account';
  return <ReplayPage scope={scope} gameId={gameId ?? ''} />;
}
export function AppRoutes() {
  return <Suspense fallback={<p role="status">Đang tải trang…</p>}><Routes>
    <Route path="/" element={<EntryPage />} />
    <Route path="/play" element={<PlayEntry />} />
    <Route path="/play/:matchId" element={<PlayMatch />} />
    <Route path="/login" element={<Page title="Đăng nhập" className="auth-window"><AuthForms mode="login" /></Page>} />
    <Route path="/register" element={<Page title="Đăng ký" className="auth-window"><AuthForms mode="register" /></Page>} />
    <Route path="/history" element={<HistoryRoute />} />
    <Route path="/history/:gameId" element={<ReplayRoute />} />
    <Route path="/leaderboard" element={<LeaderboardPage />} />
    <Route path="*" element={<Page title="Không tìm thấy trang"><Link to="/">Về trang chọn cách chơi</Link></Page>} />
  </Routes></Suspense>;
}
