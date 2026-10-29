import './versusIntro.css';

export const versusIntroDurationMs = 1650;

export function VersusIntro({ playing = true }: { playing?: boolean }) {
  return <div className={`versus-intro${playing ? ' versus-intro--playing' : ''}`} aria-hidden="true">
    <div className="versus-intro-panel versus-intro-panel--human" />
    <div className="versus-intro-panel versus-intro-panel--bot" />
    <svg className="versus-intro-seam versus-intro-seam--desktop" viewBox="0 0 1000 1000" preserveAspectRatio="none"><polyline points="500,0 510,120 490,250 520,390 490,510 510,640 480,770 510,890 500,1000" /></svg>
    <svg className="versus-intro-seam versus-intro-seam--mobile" viewBox="0 0 1000 1000" preserveAspectRatio="none"><polyline points="0,500 160,510 280,480 400,510 520,490 640,520 760,490 880,510 1000,500" /></svg>
    <span className="versus-intro-flash" />
    <span className="versus-intro-title">VS</span>
  </div>;
}
