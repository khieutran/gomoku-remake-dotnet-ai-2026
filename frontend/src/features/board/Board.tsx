import { useEffect, useRef, useState } from 'react';
import { BoardGrid } from './BoardGrid';
import { BoardOverview } from './BoardOverview';
import { ZoomableBoard } from './ZoomableBoard';
import './board.css';

export interface BoardMove { step: number; row: number; column: number; stone: 'X' | 'O' }
export interface BoardProps { size: number; moves: readonly BoardMove[]; interactive: boolean; onMove?: (row: number, column: number) => void }

export function Board(props: BoardProps) {
  const container = useRef<HTMLDivElement>(null);
  const restoreZoomFocus = useRef(false);
  const [width, setWidth] = useState(0);
  const [zoomed, setZoomed] = useState(false);
  useEffect(() => {
    const element = container.current;
    if (!element) return;
    const observer = new ResizeObserver(entries => setWidth(entries[0]?.contentRect.width ?? 0));
    observer.observe(element);
    return () => observer.disconnect();
  }, []);
  // Reserve the grid border so every desktop cell reaches the target size.
  const compact = (width - 4) / props.size < 40;
  function closeZoom() {
    restoreZoomFocus.current = true;
    setZoomed(false);
  }
  return <div className="gomoku-board" ref={container}>
    {compact && zoomed ? <>
      <button type="button" className="gomoku-zoom-button" onClick={closeZoom}>Thu bàn cờ</button>
      <p className="gomoku-board-help">Vuốt hoặc cuộn để di chuyển. Dùng phím mũi tên, Enter hoặc Space để đặt quân.</p>
      <ZoomableBoard {...props} />
    </> : compact ? <>
      <BoardOverview {...props} />
      <p className="gomoku-board-help">Bàn tổng quan. Phóng to để chọn ô chính xác.</p>
    </> : <BoardGrid {...props} />}
    {compact && !zoomed && <button type="button" className="gomoku-zoom-button" ref={element => { if (element && restoreZoomFocus.current) { element.focus(); restoreZoomFocus.current = false; } }} onClick={() => setZoomed(true)}>Phóng bàn cờ</button>}
  </div>;
}
