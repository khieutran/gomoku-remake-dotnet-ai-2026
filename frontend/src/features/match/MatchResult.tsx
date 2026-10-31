import { useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import type { MatchDto, OwnerScope } from '../../shared/api/contracts';
import { pickFinishScene } from './finishScenes';

const cutsceneTimeoutMs = 6500;
// Matches the .match-result-overlay--leaving fade in match.css.
const cutsceneFadeMs = 350;
// The bot gloats after a win; rotate by game number so a losing streak does not repeat one line.
const botTaunts = ['Chơi tiếp không ní, thấy hơi gà', 'Gỡ được ván nào chưa ní?', 'Năm quân thôi mà, khó vậy sao?'];

// Remember finished games whose cutscene already played, so returning from replay goes straight to the result.
const seenKey = 'gomoku.finishCutsceneSeen';
function cutsceneSeen(gameId: string) {
  try { return (JSON.parse(localStorage.getItem(seenKey) ?? '[]') as string[]).includes(gameId); } catch { return false; }
}
function markCutsceneSeen(gameId: string) {
  try {
    const seen = (JSON.parse(localStorage.getItem(seenKey) ?? '[]') as string[]).filter(id => id !== gameId);
    localStorage.setItem(seenKey, JSON.stringify([...seen, gameId].slice(-50)));
  } catch { /* storage unavailable: the cutscene may replay, nothing else breaks */ }
}

function matches(query: string) {
  return typeof window !== 'undefined' && typeof window.matchMedia === 'function' && window.matchMedia(query).matches;
}

export function MatchResult({ game, humanStone, scope, busy, onNext }: {
  game: MatchDto['currentGame']; humanStone: 'X' | 'O'; scope: OwnerScope;
  busy: boolean; onNext: () => void;
}) {
  const [scene] = useState(() => pickFinishScene(game.status, humanStone));
  // Phone boards are square or taller; the wide cut would play letterboxed at a third of the board height.
  const [src] = useState(() => scene && (matches('(max-width: 700px)') ? scene.tall : scene.wide));
  // cutscene -> leaving (video fades out over the already visible card) -> result.
  const [phase, setPhase] = useState<'cutscene' | 'leaving' | 'result'>(() =>
    scene !== null && !matches('(prefers-reduced-motion: reduce)') && !cutsceneSeen(game.id) ? 'cutscene' : 'result');
  const videoRef = useRef<HTMLVideoElement>(null);
  const finish = () => setPhase(current => current === 'cutscene' ? 'leaving' : current);
  useEffect(() => {
    if (phase !== 'cutscene') return;
    markCutsceneSeen(game.id);
    let active = true;
    const timeout = window.setTimeout(() => setPhase('leaving'), cutsceneTimeoutMs);
    try {
      const play = videoRef.current?.play();
      if (play) void play.catch(() => { if (active) setPhase('leaving'); });
    } catch {
      queueMicrotask(() => { if (active) setPhase('leaving'); });
    }
    return () => { active = false; window.clearTimeout(timeout); };
  }, [phase, game.id]);
  useEffect(() => {
    if (phase !== 'leaving') return;
    const timeout = window.setTimeout(() => setPhase('result'), cutsceneFadeMs);
    return () => window.clearTimeout(timeout);
  }, [phase]);

  const draw = game.status === 'Draw';
  const botWon = !draw && game.status !== `${humanStone}Won`;
  const title = draw ? 'Ván hòa' : botWon ? 'Bot thắng!' : 'Bạn thắng!';

  const cutscene = scene && phase !== 'result' && <div aria-hidden={phase === 'leaving'}
    className={`match-result-overlay match-result-overlay--cutscene match-result-overlay--${phase === 'cutscene' ? 'entering' : 'leaving'}`}>
    <video ref={videoRef} src={src ?? undefined} aria-label={`Hoạt cảnh kết thúc: ${scene.label}`}
      autoPlay muted playsInline preload="auto" onEnded={finish} onError={finish} />
    {phase === 'cutscene' && <button type="button" className="button match-result-skip" onClick={finish}>Bỏ qua hoạt cảnh</button>}
  </div>;
  if (phase === 'cutscene') return cutscene;

  return <>{cutscene}<div className="match-result-overlay">
    <section className={`match-result-card${botWon ? ' match-result-card--bot' : ''}`} role="region" aria-label="Kết quả ván">
      {draw
        ? <span className="match-result-mark" aria-hidden="true">X = O</span>
        : <img className="match-result-icon" src={botWon ? '/images/sprites/robot.png' : '/images/sprites/knight.png'} alt="" aria-hidden="true" />}
      <h2>{title}</h2>
      {botWon && <p className="match-result-taunt">“{botTaunts[(game.number - 1) % botTaunts.length]}”</p>}
      {!botWon && !draw && <p className="match-result-taunt">“Bot quá gà, chưa đủ tuổi!”</p>}
      {draw && <p>Bàn cờ đã kín. Không bên nào tạo được đường thắng.</p>}
      <div className="match-result-actions">
        <button type="button" className="button button-primary" disabled={busy} onClick={onNext}>Chơi tiếp</button>
        <Link className="button" to={`/history/${game.id}?scope=${scope}`}>Xem lại</Link>
      </div>
    </section>
  </div></>;
}
