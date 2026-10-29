import { useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import type { MatchDto, OwnerScope } from '../../shared/api/contracts';
import type { WinningLine } from '../board/winningLine';
import { pickFinishScene } from './finishScenes';

const cutsceneTimeoutMs = 6500;

function prefersReducedMotion() {
  return typeof window !== 'undefined' && typeof window.matchMedia === 'function' &&
    window.matchMedia('(prefers-reduced-motion: reduce)').matches;
}

export function MatchResult({ game, humanStone, scope, line, busy, onNext }: {
  game: MatchDto['currentGame']; humanStone: 'X' | 'O'; scope: OwnerScope; line: WinningLine | null;
  busy: boolean; onNext: () => void;
}) {
  const [scene] = useState(() => pickFinishScene(game.status, humanStone));
  const [playing, setPlaying] = useState(() => scene !== null && !prefersReducedMotion());
  const videoRef = useRef<HTMLVideoElement>(null);
  useEffect(() => {
    if (!playing) return;
    let active = true;
    const timeout = window.setTimeout(() => setPlaying(false), cutsceneTimeoutMs);
    try {
      const play = videoRef.current?.play();
      if (play) void play.catch(() => { if (active) setPlaying(false); });
    } catch {
      queueMicrotask(() => { if (active) setPlaying(false); });
    }
    return () => { active = false; window.clearTimeout(timeout); };
  }, [playing]);

  const botWon = (game.status === 'XWon' || game.status === 'OWon') && game.status !== `${humanStone}Won`;
  const title = game.status === 'Draw' ? 'Ván hòa' : botWon ? 'BOT VICTORY' : 'Bạn thắng';
  const direction = line?.direction === 'ngang' ? 'hàng ngang'
    : line?.direction === 'dọc' ? 'hàng dọc' : line ? `đường ${line.direction}` : null;
  const reason = game.status === 'Draw' ? 'Bàn cờ đã kín. Không bên nào tạo được đường thắng.'
    : direction ? `5 quân liên tiếp theo ${direction}, không bị chặn cả hai đầu.`
      : 'Máy chủ đã xác nhận kết quả ván.';

  if (playing && scene) return <div className="match-result-overlay match-result-overlay--cutscene">
    <video ref={videoRef} src={scene.src} aria-label={`Hoạt cảnh kết thúc: ${scene.label}`}
      autoPlay muted playsInline preload="auto" onEnded={() => setPlaying(false)} onError={() => setPlaying(false)} />
    <button type="button" className="button match-result-skip" onClick={() => setPlaying(false)}>Bỏ qua hoạt cảnh</button>
  </div>;

  return <div className="match-result-overlay">
    <section className={`match-result-card${botWon ? ' match-result-card--bot' : ''}`} role="region" aria-label="Kết quả ván">
      {botWon
        ? <img className="match-result-robot-icon" src="/images/finish-cutscenes/robot.png" alt="" aria-hidden="true" />
        : <span className="match-result-mark" aria-hidden="true">{game.status === 'Draw' ? 'X = O' : 'XXXXX'}</span>}
      <h2>{title}</h2>
      {botWon && <p className="match-result-taunt">“Chơi tiếp không ní, thấy hơi gà”</p>}
      <p className={botWon ? 'match-result-reason' : undefined}>{reason}</p>
      <div className="match-result-actions">
        <button type="button" className="button button-primary" disabled={busy} onClick={onNext}>Chơi tiếp</button>
        <Link className="button" to={`/history/${game.id}?scope=${scope}`}>Xem lại</Link>
      </div>
    </section>
  </div>;
}
