import { useEffect, useRef } from 'react';
import type { BoardProps } from './Board';
import { BoardGrid } from './BoardGrid';
import './board.css';

export function ZoomableBoard(props: BoardProps) {
  const viewport = useRef<HTMLDivElement>(null);
  const lastStep = props.moves.reduce((latest, move) => Math.max(latest, move.step), 0);
  const shownStep = useRef(lastStep);
  useEffect(() => {
    const element = viewport.current;
    const focused = element?.querySelector<HTMLElement>('[tabindex="0"]');
    if (element && focused) {
      element.scrollLeft = Math.max(0, focused.offsetLeft - element.clientWidth / 2 + 22);
      element.scrollTop = Math.max(0, focused.offsetTop - element.clientHeight / 2 + 22);
    }
  }, []);
  // Follow new moves (the bot's reply lands wherever it wants): centre the last move if it is out of view.
  useEffect(() => {
    if (lastStep === shownStep.current) return;
    shownStep.current = lastStep;
    const element = viewport.current;
    const cell = element?.querySelector<HTMLElement>('[data-last-move]');
    if (!element || !cell) return;
    const view = element.getBoundingClientRect(), target = cell.getBoundingClientRect();
    const inView = target.left >= view.left && target.right <= view.right && target.top >= view.top && target.bottom <= view.bottom;
    if (inView) return;
    const reduced = typeof window.matchMedia === 'function' && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    element.scrollTo({
      left: element.scrollLeft + (target.left + target.width / 2) - (view.left + view.width / 2),
      top: element.scrollTop + (target.top + target.height / 2) - (view.top + view.height / 2),
      behavior: reduced ? 'auto' : 'smooth',
    });
  }, [lastStep]);
  return <div className="gomoku-zoom-viewport" ref={viewport} role="region" aria-label="Bàn cờ phóng to, cuộn để di chuyển">
    <BoardGrid {...props} autoFocus />
  </div>;
}
