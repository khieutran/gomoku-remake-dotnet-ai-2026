import './leaderboard.css';
import type { ReactNode } from 'react';

export function MatchScorePanel({ xScore, oScore, humanStone = 'X', children }: { xScore: number; oScore: number; humanStone?: 'X' | 'O'; children?: ReactNode }) {
  const humanScore = humanStone === 'X' ? xScore : oScore;
  const aiScore = humanStone === 'X' ? oScore : xScore;
  const aiStone = humanStone === 'X' ? 'O' : 'X';
  return <section className="match-score-panel" aria-label="Điểm trận">
    <dl className="match-score-values">
      <div><dt>Bạn <span aria-hidden="true">{humanStone}</span></dt><dd>{humanScore}</dd></div>
      <div><dt>Bot <span aria-hidden="true">{aiStone}</span></dt><dd>{aiScore}</dd></div>
    </dl>
    {children}
  </section>;
}
