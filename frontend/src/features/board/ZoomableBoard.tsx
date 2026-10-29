import { useEffect, useRef } from 'react';
import type { BoardProps } from './Board';
import { BoardGrid } from './BoardGrid';
import './board.css';

export function ZoomableBoard(props: BoardProps) {
  const viewport = useRef<HTMLDivElement>(null);
  useEffect(() => {
    const element = viewport.current;
    const focused = element?.querySelector<HTMLElement>('[tabindex="0"]');
    if (element && focused) {
      element.scrollLeft = Math.max(0, focused.offsetLeft - element.clientWidth / 2 + 22);
      element.scrollTop = Math.max(0, focused.offsetTop - element.clientHeight / 2 + 22);
    }
  }, []);
  return <div className="gomoku-zoom-viewport" ref={viewport} role="region" aria-label="Bàn cờ phóng to, cuộn để di chuyển">
    <BoardGrid {...props} autoFocus />
  </div>;
}
