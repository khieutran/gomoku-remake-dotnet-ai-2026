import { useContext, useEffect, useRef } from 'react';
import { Link } from 'react-router-dom';
import type { OwnerIdentity, OwnerScope } from '../../shared/api/contracts';
import { SessionContext } from '../session/sessionContext';
import { Board } from '../board/Board';
import { MatchStatus } from './MatchStatus';
import { MatchScorePanel } from '../leaderboard/MatchScorePanel';
import { useMatchFlow } from './useMatchFlow';

export function MatchPage({ scope, matchId, identity: suppliedIdentity }: { scope: OwnerScope; matchId?: string; identity?: OwnerIdentity }) {
  const session = useContext(SessionContext);
  const notifiedSession = useRef(false);
  const identity = suppliedIdentity ?? (session?.session.scope === scope ? { scope, ownerId: session.session.ownerId } : null);
  const flow = useMatchFlow(identity, matchId);
  const snapshot = flow.snapshot;
  useEffect(() => {
    if (flow.unauthorized && scope === 'account' && !notifiedSession.current) {
      notifiedSession.current = true;
      void session?.requireAccount();
    }
    if (!flow.unauthorized) notifiedSession.current = false;
  }, [flow.unauthorized, scope, session]);
  if (flow.loading) return <p role="status">Đang tải ván đấu…</p>;
  if (flow.unauthorized) {
    const returnTo = matchId ? `/play/${matchId}?scope=${scope}` : `/play?scope=${scope}`;
    return <p role="alert">{flow.error} <Link to={scope === 'account' ? `/login?returnTo=${encodeURIComponent(returnTo)}` : '/play?scope=guest'}>Mở lại phiên</Link></p>;
  }
  if (!snapshot) return <div><p role="alert">{flow.error ?? 'Không mở được ván đấu.'}</p><button type="button" onClick={flow.recover}>Thử lại</button></div>;
  const moves = flow.pendingMove ? [...snapshot.currentGame.moves, flow.pendingMove] : snapshot.currentGame.moves;
  return <section className="match-page" aria-label="Bàn chơi">
    <MatchStatus snapshot={snapshot} />
    <MatchScorePanel xScore={snapshot.xScore} oScore={snapshot.oScore} />
    {flow.error && <p role="alert">{flow.error}</p>}
    <Board size={snapshot.boardSize} moves={moves} interactive={snapshot.currentGame.turnStatus === 'human' && !flow.busy && !flow.pendingMove && !flow.canRetry} onMove={flow.play} />
    <div className="match-actions">
      {flow.canRetry && <button type="button" disabled={flow.busy} onClick={flow.recover}>Thử lại nước đi</button>}
      {snapshot.currentGame.turnStatus === 'finished' && <button type="button" disabled={flow.busy} onClick={flow.nextGame}>Ván tiếp theo</button>}
      <Link to="/">Rời bàn cờ</Link>
    </div>
  </section>;
}
