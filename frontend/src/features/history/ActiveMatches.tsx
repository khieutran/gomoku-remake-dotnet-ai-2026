import { Link } from 'react-router-dom';
import type { ActiveMatchPageDto, OwnerScope } from '../../shared/api/contracts';

export function ActiveMatches({ scope, items, hasMore, loadingMore, onMore }: {
  scope: OwnerScope;
  items: ActiveMatchPageDto['items'];
  hasMore: boolean;
  loadingMore: boolean;
  onMore: () => void;
}) {
  return <section aria-label="Ván dở" className="history-section">
    <h2>Ván dở</h2>
    {items.length ? <ol>{items.map((item, index) => <li key={item.matchId}>
      <Link to={`/play/${item.matchId}?scope=${scope}`}>Ván dở {index + 1}</Link>
    </li>)}</ol> : <p>Chưa có ván dở.</p>}
    {hasMore && <button type="button" className="button" disabled={loadingMore} onClick={onMore}>{loadingMore ? 'Đang tải…' : 'Xem thêm'}</button>}
  </section>;
}
