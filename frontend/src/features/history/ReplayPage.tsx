import { useEffect, useMemo, useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import type { OwnerIdentity, OwnerScope } from '../../shared/api/contracts';
import { ApiError } from '../../shared/api/errors';
import { queryKeys } from '../../shared/api/queryKeys';
import { Board } from '../board/Board';
import { OwnerGate } from '../session/OwnerGate';
import { useSession } from '../session/sessionContext';
import { getHistoryGame } from './historyApi';

function ReplayContent({ scope, gameId, identity }: { scope: OwnerScope; gameId: string; identity: OwnerIdentity }) {
  const { requireAccount } = useSession();
  const queryClient = useQueryClient();
  const [locked, setLocked] = useState(false);
  const [step, setStep] = useState(0);
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
  if (expired || locked) return scope === 'guest'
    ? <p role="alert">Không thể truy cập phiên khách cũ. Hãy bắt đầu phiên khách mới để chơi tiếp.</p>
    : <p role="alert">Phiên tài khoản đã hết hạn. Đăng nhập lại để xem lịch sử.</p>;
  if (game.isPending || game.isFetching) return <p role="status">Đang tải ván đấu…</p>;
  if (game.error) return <p role="alert">Chưa tải được ván đấu. Hãy thử lại.</p>;
  const shown = moves.slice(0, step);
  return <section aria-label="Phát lại">
    <p>Ván {game.data.number} · {game.data.status}</p>
    <p aria-live="polite">Bước {step} / {moves.length}</p>
    <Board size={game.data.boardSize} moves={shown} interactive={false} />
    <div className="history-controls">
      <button type="button" className="button" disabled={step === 0} onClick={() => setStep(value => value - 1)}>Bước trước</button>
      <button type="button" className="button" disabled={step >= moves.length} onClick={() => setStep(value => value + 1)}>Bước sau</button>
    </div>
  </section>;
}

export function ReplayPage({ scope, gameId }: { scope: OwnerScope; gameId: string }) {
  const { session } = useSession();
  return <div className="page-shell"><h1 tabIndex={-1}>Phát lại ván đấu</h1>
    <OwnerGate scope={scope}>{session.scope === scope && <ReplayContent key={`${scope}:${session.ownerId}:${gameId}`} scope={scope} gameId={gameId} identity={{ scope, ownerId: session.ownerId }} />}</OwnerGate>
  </div>;
}
