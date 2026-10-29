import type { ReactNode } from 'react';

export function GameWorkspace({ main, top, bottom, variant }: {
  main: ReactNode;
  top: ReactNode;
  bottom: ReactNode;
  variant: 'entry' | 'match';
}) {
  return <div className={`game-workspace game-workspace--${variant}`}>
    <div className="game-workspace-main">{main}</div>
    <aside className="game-workspace-side" aria-label="Thông tin bàn chơi">
      {top}
      {bottom}
    </aside>
  </div>;
}
