import './leaderboard.css';

export function MatchScorePanel({ xScore, oScore }: { xScore: number; oScore: number }) {
  return <section className="match-score-panel" aria-label="Điểm trận">
    <h2>Điểm trận</h2>
    <dl className="match-score-values">
      <div><dt>Bạn <span aria-hidden="true">X</span></dt><dd>{xScore}</dd></div>
      <div><dt>Bot <span aria-hidden="true">O</span></dt><dd>{oScore}</dd></div>
    </dl>
  </section>;
}
