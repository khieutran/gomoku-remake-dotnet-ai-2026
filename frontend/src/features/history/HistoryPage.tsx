import { useEffect, useState } from 'react';
import { useInfiniteQuery, useQueryClient } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import type { HistorySummaryDto, OwnerIdentity, OwnerScope } from '../../shared/api/contracts';
import { ApiError } from '../../shared/api/errors';
import { queryKeys } from '../../shared/api/queryKeys';
import { OwnerGate } from '../session/OwnerGate';
import { useSession } from '../session/sessionContext';
import { ActiveMatches } from './ActiveMatches';
import { listActive, listHistory } from './historyApi';
import './history.css';

const limit = 20;
const unauthorized = (error: unknown) => error instanceof ApiError && error.status === 401;
const dateTime = new Intl.DateTimeFormat('vi-VN', { dateStyle: 'short', timeStyle: 'short' });

function resultFor(game: HistorySummaryDto) {
  if (game.status === 'Draw') return { text: 'Hòa', tone: 'draw' };
  if (!game.humanStone) return { text: game.status === 'XWon' ? 'X thắng' : 'O thắng', tone: 'unknown' };
  return game.status === `${game.humanStone}Won`
    ? { text: 'Bạn thắng', tone: 'win' }
    : { text: 'Bạn thua', tone: 'loss' };
}

function HistoryContent({ scope, identity }: { scope: OwnerScope; identity: OwnerIdentity }) {
  const queryClient = useQueryClient();
  const { requireAccount, session } = useSession();
  const [locked, setLocked] = useState(false);
  const active = useInfiniteQuery({
    queryKey: queryKeys.active(identity, null, limit), initialPageParam: null as string | null,
    queryFn: ({ pageParam, signal }) => listActive(scope, pageParam, limit, signal),
    getNextPageParam: page => page.nextCursor ?? undefined,
    retry: false, enabled: !locked,
  });
  const history = useInfiniteQuery({
    queryKey: queryKeys.history(identity, null, limit), initialPageParam: null as string | null,
    queryFn: ({ pageParam, signal }) => listHistory(scope, pageParam, limit, signal),
    getNextPageParam: page => page.nextCursor ?? undefined,
    retry: false, enabled: !locked,
  });
  const expired = unauthorized(active.error) || unauthorized(history.error);
  useEffect(() => {
    if (!expired || locked) return;
    void queryClient.cancelQueries({ queryKey: queryKeys.owner(identity) }).then(() => {
      setLocked(true);
      queryClient.removeQueries({ queryKey: queryKeys.owner(identity) });
      if (scope === 'account') void requireAccount();
    });
  }, [expired, locked, queryClient, identity, scope, requireAccount]);

  if (expired || locked) return scope === 'guest'
    ? <p role="alert">Không thể truy cập phiên khách cũ. Hãy bắt đầu phiên khách mới để chơi tiếp.</p>
    : <p role="alert">Phiên tài khoản đã hết hạn. Đăng nhập lại để xem lịch sử.</p>;
  if (active.isPending || history.isPending ||
    (active.isFetching && !active.isFetchingNextPage) ||
    (history.isFetching && !history.isFetchingNextPage)) return <p role="status">Đang tải lịch sử…</p>;
  if (active.error || history.error) return <p role="alert">Chưa tải được lịch sử. Hãy thử lại.</p>;
  const activeItems = active.data.pages.flatMap(page => page.items);
  const historyItems = history.data.pages.flatMap(page => page.items);
  return <>
    <section className="history-player" aria-label="Người chơi">
      <span className="history-player-mark" aria-hidden="true">X O</span>
      <div><span className="history-player-label">Người chơi</span>
        <strong>{scope === 'account' && session.scope === 'account' ? session.email ?? 'Tài khoản của bạn' : 'Khách'}</strong>
        <small>{scope === 'account' ? 'Lịch sử gắn với tài khoản của bạn' : 'Lịch sử của phiên khách hiện tại'}</small>
      </div>
    </section>
    <ActiveMatches scope={scope} items={activeItems} hasMore={Boolean(active.hasNextPage)} loadingMore={active.isFetchingNextPage} onMore={() => void active.fetchNextPage()} />
    <section aria-label="Ván đã xong" className="history-section">
      <h2>Ván đã xong</h2>
      {historyItems.length ? <div className="history-table-wrap"><table className="history-table" aria-label="Kết quả các ván đã xong">
        <thead><tr><th scope="col">Ván đấu</th><th scope="col">Kết quả</th><th scope="col" className="history-table-moves">Số nước</th></tr></thead>
        <tbody>{historyItems.map((game, index) => {
          const result = resultFor(game);
          return <tr key={game.gameId}>
            <td><Link to={`/history/${game.gameId}?scope=${scope}`}>Ván {index + 1}</Link><small>{dateTime.format(new Date(game.finishedAt))} · {game.boardSize}×{game.boardColumns ?? game.boardSize} · {game.moveCount} nước</small></td>
            <td><span className={`history-result history-result--${result.tone}`}>{result.text}</span></td>
            <td className="history-table-moves">{game.moveCount}</td>
          </tr>;
        })}</tbody>
      </table></div> : <p>Chưa có ván đã xong.</p>}
      {history.hasNextPage && <button type="button" className="button" disabled={history.isFetchingNextPage} onClick={() => void history.fetchNextPage()}>{history.isFetchingNextPage ? 'Đang tải…' : 'Xem thêm'}</button>}
    </section>
  </>;
}

export function HistoryPage({ scope }: { scope: OwnerScope }) {
  const { session } = useSession();
  return <div className="page-shell history-page"><h1 tabIndex={-1}>Lịch sử ván đấu</h1>
    <OwnerGate scope={scope}>{session.scope === scope && <HistoryContent key={`${scope}:${session.ownerId}`} scope={scope} identity={{ scope, ownerId: session.ownerId }} />}</OwnerGate>
  </div>;
}
