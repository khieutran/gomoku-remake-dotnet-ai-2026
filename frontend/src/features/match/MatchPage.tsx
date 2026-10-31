import { useContext, useEffect, useRef, useState } from 'react';
import { Link, useLocation, useNavigate, useSearchParams } from 'react-router-dom';
import type { OwnerIdentity, OwnerScope } from '../../shared/api/contracts';
import { Window } from '../../shared/ui/Window';
import { GameWorkspace } from '../../shared/ui/GameWorkspace';
import { SessionContext } from '../session/sessionContext';
import { Board } from '../board/Board';
import { findWinningLine } from '../board/winningLine';
import { MatchStatus } from './MatchStatus';
import { MatchResult } from './MatchResult';
import { VersusIntro, versusIntroDurationMs } from './VersusIntro';
import { MatchScorePanel } from '../leaderboard/MatchScorePanel';
import { useMatchFlow } from './useMatchFlow';
import { finishFormat, prefetchFinishScenes } from './finishScenes';
import './match.css';

function createdMatchIdFrom(state: unknown): string | undefined {
  const value = (state as { createdMatchId?: unknown } | null)?.createdMatchId;
  return typeof value === 'string' ? value : undefined;
}

export function MatchPage({ scope, matchId, identity: suppliedIdentity }: { scope: OwnerScope; matchId?: string; identity?: OwnerIdentity }) {
  const [params] = useSearchParams();
  const location = useLocation();
  const navigate = useNavigate();
  // A reload keeps history state, so a match created from setup params resumes instead of being recreated.
  const [resumeId] = useState(() => matchId ? undefined : createdMatchIdFrom(location.state));
  const resolvedMatchId = matchId ?? resumeId;
  const [boardHeaderActions, setBoardHeaderActions] = useState<HTMLDivElement | null>(null);
  const stone = params.get('stone');
  const first = params.get('first');
  const defaultSize = 20;
  // Frozen on mount: rotating a phone across the breakpoint must not change the setup and create another match.
  const [defaultColumns] = useState<20 | 30>(() => {
    const requested = params.get('boardColumns');
    if (requested === '20' || requested === '30') return Number(requested) as 20 | 30;
    return typeof window !== 'undefined' && window.matchMedia?.('(max-width: 700px)').matches ? 20 : 30;
  });
  const setup = !matchId && (stone === 'X' || stone === 'O') && (first === 'true' || first === 'false')
    ? { humanStone: stone as 'X' | 'O', humanFirst: first === 'true',
      boardSize: defaultSize as 20, boardColumns: defaultColumns } : undefined;
  const session = useContext(SessionContext);
  const notifiedSession = useRef(false);
  const identity = suppliedIdentity ?? (session?.session.scope === scope ? { scope, ownerId: session.session.ownerId } : null);
  const flow = useMatchFlow(identity, resolvedMatchId, setup,
    { humanStone: 'X', humanFirst: true, boardSize: defaultSize, boardColumns: defaultColumns });
  const snapshot = flow.snapshot;
  const snapshotId = snapshot?.matchId;
  const [introDoneFor, setIntroDoneFor] = useState<string | null>(null);
  const introVisible = !resolvedMatchId && snapshotId !== undefined && introDoneFor !== snapshotId;
  useEffect(() => {
    if (!introVisible || !snapshotId) return;
    const reducedMotion = typeof window.matchMedia === 'function' &&
      window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    const timer = window.setTimeout(() => setIntroDoneFor(snapshotId),
      reducedMotion ? 0 : versusIntroDurationMs);
    return () => window.clearTimeout(timer);
  }, [introVisible, snapshotId]);
  useEffect(() => {
    if (matchId || !snapshotId || createdMatchIdFrom(location.state) === snapshotId) return;
    const state = typeof location.state === 'object' && location.state !== null ? location.state : {};
    navigate({ pathname: location.pathname, search: location.search, hash: location.hash },
      { replace: true, state: { ...state, createdMatchId: snapshotId } });
  }, [matchId, snapshotId, location, navigate]);
  useEffect(() => { if (snapshotId) prefetchFinishScenes(finishFormat()); }, [snapshotId]);
  const game = snapshot?.currentGame;
  const gameId = game?.id;
  const gameTurnStatus = game?.turnStatus;
  const [revealedGameId, setRevealedGameId] = useState<string | null>(null);
  useEffect(() => {
    if (!gameId || gameTurnStatus !== 'finished') return;
    const reducedMotion = typeof window.matchMedia === 'function' &&
      window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    const timer = window.setTimeout(() => setRevealedGameId(gameId), reducedMotion ? 0 : 650);
    return () => window.clearTimeout(timer);
  }, [gameId, gameTurnStatus]);
  useEffect(() => {
    if (flow.unauthorized && scope === 'account' && !notifiedSession.current) {
      notifiedSession.current = true;
      void session?.requireAccount();
    }
    if (!flow.unauthorized) notifiedSession.current = false;
  }, [flow.unauthorized, scope, session]);
  if (flow.loading) return <GameWorkspace variant="match"
    main={<Window title="Ván đấu" className="workspace-main-window match-window" headerActionsRef={setBoardHeaderActions}><h1 className="sr-only" tabIndex={-1}>Đấu với bot</h1><section className="match-page" aria-label="Bàn chơi"><div className="match-board-stage"><Board size={setup?.boardSize ?? defaultSize} columns={setup?.boardColumns ?? defaultColumns} moves={[]} interactive={false} zoomControlTarget={boardHeaderActions} />{!resolvedMatchId && <VersusIntro playing={false} />}{resolvedMatchId && <div className="match-result-overlay"><p role="status">Đang tải ván đấu…</p></div>}</div></section></Window>}
    top={<Window title="Trạng thái"><p>Đang mở ván…</p></Window>}
    bottom={<Window title="Bảng điểm"><p>Đang tải điểm trận…</p></Window>}
  />;
  if (flow.unauthorized) {
    const returnTo = matchId ? `/play/${matchId}?scope=${scope}` : `/play?scope=${scope}`;
    return <p role="alert">{flow.error} <Link to={scope === 'account' ? `/login?returnTo=${encodeURIComponent(returnTo)}` : '/play?scope=guest'}>Mở lại phiên</Link></p>;
  }
  if (!snapshot) return <div><p role="alert">{flow.error ?? 'Không mở được ván đấu.'}</p><button type="button" onClick={flow.recover}>Thử lại</button></div>;
  const currentGame = snapshot.currentGame;
  const moves = flow.pendingMove ? [...snapshot.currentGame.moves, flow.pendingMove] : snapshot.currentGame.moves;
  const winningLine = currentGame.turnStatus === 'finished' ? findWinningLine(currentGame.moves, snapshot.boardSize, currentGame.status, snapshot.boardColumns) : null;
  return <GameWorkspace variant="match"
    main={<Window title="Ván đấu" className="workspace-main-window match-window" headerActionsRef={setBoardHeaderActions}>
      <h1 className="sr-only" tabIndex={-1}>Đấu với bot</h1>
      <section className="match-page" aria-label="Bàn chơi"><div className="match-board-stage">
        <Board size={snapshot.boardSize} columns={snapshot.boardColumns} moves={moves} winningCells={winningLine?.cells} interactive={!introVisible && currentGame.turnStatus === 'human' && !flow.busy && !flow.pendingMove && !flow.canRetry} onMove={flow.play} zoomControlTarget={boardHeaderActions} />
        {introVisible && <VersusIntro />}
        {currentGame.turnStatus === 'finished' && revealedGameId === currentGame.id &&
      <MatchResult key={currentGame.id} game={currentGame} humanStone={snapshot.humanStone} scope={scope} busy={flow.busy} onNext={flow.nextGame} />}
      </div></section>
    </Window>}
    top={<Window title="Trạng thái"><MatchStatus snapshot={snapshot} />{flow.error && <p role="alert">{flow.error}</p>}</Window>}
    bottom={<Window title="Bảng điểm"><MatchScorePanel xScore={snapshot.xScore} oScore={snapshot.oScore} humanStone={snapshot.humanStone}>
        <div className="match-actions">
          {flow.canRetry && <button type="button" className="button button-primary" disabled={flow.busy} onClick={flow.recover}>Thử lại nước đi</button>}
          <Link className="button" to="/">Rời bàn cờ</Link>
        </div>
      </MatchScorePanel></Window>}
  />;
}
