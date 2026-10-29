import type { MatchDto } from '../../shared/api/contracts';

export function MatchStatus({ snapshot }: { snapshot: MatchDto }) {
  const { currentGame: game } = snapshot;
  const status = game.turnStatus === 'human' ? `Lượt của bạn · ${snapshot.humanStone}`
    : game.turnStatus === 'ai_pending' ? 'Bot đang suy nghĩ…'
      : game.turnStatus === 'ai_retrying' ? 'Bot đang thử lại…'
        : game.status === `${snapshot.humanStone}Won` ? 'Bạn thắng'
          : game.status === 'Draw' ? 'Ván hòa' : 'Bot thắng';
  return <section className="match-status-panel" aria-label="Trạng thái ván đấu">
    <h2>Ván {game.number}</h2>
    <p role="status" aria-live="polite" aria-atomic="true">{status}</p>
  </section>;
}
