import type { MatchDto } from '../../shared/api/contracts';

export function MatchStatus({ snapshot }: { snapshot: MatchDto }) {
  const { currentGame: game } = snapshot;
  const status = game.turnStatus === 'human' ? 'Lượt của bạn (X)'
    : game.turnStatus === 'ai_pending' ? 'Bot đang suy nghĩ…'
      : game.turnStatus === 'ai_retrying' ? 'Bot đang thử lại. Bàn cờ sẽ tự cập nhật.'
        : game.status === 'XWon' ? 'Bạn thắng ván này!'
          : game.status === 'OWon' ? 'Bot thắng ván này.' : 'Ván hòa.';
  return <section aria-label="Trạng thái ván đấu"><h2>Ván {game.number}</h2><p role="status">{status}</p><p>Điểm: X {snapshot.xScore} – O {snapshot.oScore}</p></section>;
}
