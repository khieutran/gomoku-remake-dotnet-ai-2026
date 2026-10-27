import { useEffect } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { ApiError } from '../../shared/api/errors';
import { queryKeys } from '../../shared/api/queryKeys';
import { Window } from '../../shared/ui/Window';
import { useSession } from '../session/sessionContext';
import { leaderboardApi } from './leaderboardApi';
import './leaderboard.css';

export function LeaderboardPage() {
  const { session, ready, requireAccount } = useSession();
  const isAccount = ready && session.scope === 'account';
  const top = useQuery({ queryKey: queryKeys.leaderboard(), queryFn: ({ signal }) => leaderboardApi.top(signal), retry: false });
  const mine = useQuery({
    queryKey: queryKeys.rankingMe(isAccount ? { scope: 'account', ownerId: session.ownerId } : { scope: 'account', ownerId: '' }),
    queryFn: ({ signal }) => leaderboardApi.me(signal),
    enabled: isAccount,
    retry: false,
  });
  const needsLogin = mine.error instanceof ApiError && mine.error.status === 401;

  useEffect(() => {
    if (needsLogin && session.scope === 'account') void requireAccount();
  }, [needsLogin, requireAccount, session]);

  return <div className="leaderboard-layout">
    <Window title="Bảng xếp hạng">
      <h1 tabIndex={-1}>Bảng xếp hạng</h1>
      <p>Điểm tài khoản do máy chủ ghi sau mỗi ván đã kết thúc.</p>
      {top.isPending && <p role="status">Đang tải bảng xếp hạng…</p>}
      {top.isError && <p role="alert">Chưa tải được bảng xếp hạng. Hãy thử lại sau.</p>}
      {top.data && (top.data.items.length === 0 ? <p>Chưa có tài khoản nào trên bảng xếp hạng.</p> :
        <div className="leaderboard-table-scroll"><table aria-label="Bảng xếp hạng">
          <thead><tr><th scope="col">Hạng</th><th scope="col">Tài khoản</th><th scope="col">Điểm</th></tr></thead>
          <tbody>{top.data.items.map(entry => <tr key={entry.accountId}><td>{entry.rank}</td><td className="leaderboard-id">{entry.accountId}</td><td>{entry.points}</td></tr>)}</tbody>
        </table></div>)}
    </Window>
    <Window title="Vị trí của bạn" className="leaderboard-personal">
      {isAccount && !needsLogin ? <div>
        <h2>Vị trí của bạn</h2>
        {mine.isFetching && <p role="status">Đang tải vị trí…</p>}
        {mine.isError && <p role="alert">Chưa tải được vị trí của bạn. Hãy thử lại sau.</p>}
        {mine.data && !mine.isFetching && !mine.isError && <dl className="leaderboard-stats"><div><dt>Hạng</dt><dd>{mine.data.rank ?? 'Chưa xếp hạng'}</dd></div><div><dt>Điểm</dt><dd>{mine.data.points}</dd></div></dl>}
      </div> : <p>Chỉ tài khoản có vị trí riêng. <Link to="/login">Đăng nhập để xem vị trí</Link>.</p>}
    </Window>
  </div>;
}
