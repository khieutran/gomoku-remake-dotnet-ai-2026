import { useEffect, useMemo, useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import type { OwnerIdentity, OwnerScope } from '../../shared/api/contracts';
import { ApiError } from '../../shared/api/errors';
import { queryKeys } from '../../shared/api/queryKeys';
import { Board } from '../board/Board';
import { findWinningLine } from '../board/winningLine';
import { OwnerGate } from '../session/OwnerGate';
import { useSession } from '../session/sessionContext';
import { GameWorkspace } from '../../shared/ui/GameWorkspace';
import { Window } from '../../shared/ui/Window';
import { getHistoryGame } from './historyApi';
import '../match/match.css';

function ReplayShell({ message, alert = false }: { message: string; alert?: boolean }) {
  return <GameWorkspace variant="match"
    main={<Window title="Ván đấu" className="workspace-main-window match-window"><section className="match-page" aria-label="Phát lại"><div className="match-board-stage replay-board-placeholder"><p role={alert ? 'alert' : 'status'}>{message}</p></div></section></Window>}
    top={<Window title="Trạng thái"><p>Phát lại ván đấu</p></Window>}
    bottom={<Window title="Điều khiển"><Link className="button" to="/history">Về lịch sử</Link></Window>}
  />;
}

function ReplayContent({ scope, gameId, identity }: { scope: OwnerScope; gameId: string; identity: OwnerIdentity }) {
  const { requireAccount } = useSession();
  const queryClient = useQueryClient();
  const [locked, setLocked] = useState(false);
  const [step, setStep] = useState(0);
  const [boardHeaderActions, setBoardHeaderActions] = useState<HTMLDivElement | null>(null);
  const game = useQuery({ queryKey: queryKeys.historyGame(identity, gameId), queryFn: ({ signal }) => getHistoryGame(scope, gameId, signal), retry: false, enabled: !locked });
  const expired = game.error instanceof ApiError && game.error.status === 401;
  useEffect(() => {
    if (!expired || locked) return;
    void queryClient.cancelQueries({ queryKey: queryKeys.owner(identity) }).then(() => {
      setLocked(true);
      queryClient.removeQueries({ queryKey: queryKeys.owner(identity) });
      if (scope === 'account') void requireAccount();
    });
  }, [expired, locked, queryClient, identity, scope, requireAccount]);
  const moves = useMemo(() => [...(game.data?.moves ?? [])].sort((a, b) => a.step - b.step), [game.data]);
  if (expired || locked) return <ReplayShell alert message={scope === 'guest'
    ? 'Không thể truy cập phiên khách cũ. Hãy bắt đầu phiên khách mới để chơi tiếp.'
    : 'Phiên tài khoản đã hết hạn. Đăng nhập lại để xem lịch sử.'} />;
  if (game.isPending || game.isFetching) return <ReplayShell message="Đang tải ván đấu…" />;
  if (game.error) return <ReplayShell alert message="Chưa tải được ván đấu. Hãy thử lại." />;
  const shown = moves.slice(0, step);
  const winningLine = step === moves.length ? findWinningLine(moves, game.data.boardSize, game.data.status, game.data.boardColumns ?? game.data.boardSize) : null;
  return <GameWorkspace variant="match"
    main={<Window title="Ván đấu" className="workspace-main-window match-window" headerActionsRef={setBoardHeaderActions}><section className="match-page" aria-label="Phát lại"><div className="match-board-stage"><Board size={game.data.boardSize} columns={game.data.boardColumns ?? game.data.boardSize} moves={shown} winningCells={winningLine?.cells} interactive={false} zoomControlTarget={boardHeaderActions} /></div></section></Window>}
    top={<Window title="Trạng thái"><div className="replay-status"><span>Ván {game.data.number} · {game.data.status}</span><span aria-live="polite">Bước {step} / {moves.length}</span></div></Window>}
    bottom={<Window title="Điều khiển"><div className="replay-actions">
      <button type="button" className="button" disabled={step === 0} onClick={() => setStep(value => value - 1)}>Bước trước</button>
      <button type="button" className="button" disabled={step >= moves.length} onClick={() => setStep(value => value + 1)}>Bước sau</button>
      <Link className="button" to={`/play/${game.data.matchId}?scope=${scope}`}>Về bàn chơi</Link>
    </div></Window>}
  />;
}

export function ReplayPage({ scope, gameId }: { scope: OwnerScope; gameId: string }) {
  const { session } = useSession();
  return <><h1 className="sr-only" tabIndex={-1}>Phát lại ván đấu</h1>
    <OwnerGate scope={scope}>{session.scope === scope && <ReplayContent key={`${scope}:${session.ownerId}:${gameId}`} scope={scope} gameId={gameId} identity={{ scope, ownerId: session.ownerId }} />}</OwnerGate>
  </>;
}
