import { useEffect, useRef, useState, type ReactNode } from 'react';
import { createPortal } from 'react-dom';
import { BoardGrid } from './BoardGrid';
import { BoardOverview } from './BoardOverview';
import { ZoomableBoard } from './ZoomableBoard';
import './board.css';

export interface BoardMove { step: number; row: number; column: number; stone: 'X' | 'O' }
export interface BoardProps { size: number; columns?: number; moves: readonly BoardMove[]; interactive: boolean; onMove?: (row: number, column: number) => void; winningCells?: readonly [row: number, column: number][]; preview?: boolean; zoomControlTarget?: Element | null }

export function Board(props: BoardProps) {
  const container = useRef<HTMLDivElement>(null);
  const restoreZoomFocus = useRef(false);
  const [width, setWidth] = useState(0);
  const [zoomed, setZoomed] = useState(false);
  useEffect(() => {
    const element = container.current;
    if (!element || props.preview || typeof ResizeObserver === 'undefined') return;
    const observer = new ResizeObserver(entries => setWidth(entries[0]?.contentRect.width ?? 0));
    observer.observe(element);
    return () => observer.disconnect();
  }, [props.preview]);
  // Small mouse targets remain playable; coarse pointers use the zoomed 44px cells.
  const coarsePointer = typeof window.matchMedia === 'function' && window.matchMedia('(pointer: coarse)').matches;
  const compact = (width - 4) / (props.columns ?? props.size) < (coarsePointer ? 40 : 28);
  function closeZoom() {
    restoreZoomFocus.current = true;
    setZoomed(false);
  }
  function placeZoomControl(button: ReactNode) {
    return props.zoomControlTarget ? createPortal(button, props.zoomControlTarget) : button;
  }
  return <div className={`gomoku-board${(props.columns ?? props.size) > props.size ? ' gomoku-board--wide' : ''}`} ref={container}>
    {props.preview ? <BoardOverview {...props} /> : compact && zoomed ? <>
      {placeZoomControl(<button type="button" className="gomoku-zoom-button" onClick={closeZoom}>Toàn cảnh</button>)}
      <p className="gomoku-board-help">Vuốt hoặc cuộn để di chuyển. Dùng phím mũi tên, Enter hoặc Space để đặt quân.</p>
      <ZoomableBoard {...props} />
    </> : compact ? <>
      <BoardOverview {...props} />
      <p className="gomoku-board-help sr-only">Bàn tổng quan. Phóng to để chọn ô chính xác.</p>
    </> : <BoardGrid {...props} />}
    {compact && !zoomed && !props.preview && placeZoomControl(<button type="button" className="gomoku-zoom-button" ref={element => { if (element && restoreZoomFocus.current) { element.focus(); restoreZoomFocus.current = false; } }} onClick={() => setZoomed(true)}>Phóng bàn cờ</button>)}
  </div>;
}
