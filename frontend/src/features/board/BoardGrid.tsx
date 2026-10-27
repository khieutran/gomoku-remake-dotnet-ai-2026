import { useEffect, useState, type CSSProperties } from 'react';
import type { BoardProps } from './Board';
import { useBoardKeyboard } from './useBoardKeyboard';

export function BoardGrid({ size, moves, interactive, onMove, overview = false, autoFocus = false }: BoardProps & { overview?: boolean; autoFocus?: boolean }) {
  const last = moves.reduce<(typeof moves)[number] | undefined>((latest, move) => !latest || move.step > latest.step ? move : latest, undefined);
  const [initial] = useState({ row: last?.row ?? Math.floor(size / 2), column: last?.column ?? Math.floor(size / 2) });
  const { focused, setFocused, cells, onKeyDown } = useBoardKeyboard(size, initial.row, initial.column);
  const stones = new Map(moves.map(move => [`${move.row}:${move.column}`, move]));
  useEffect(() => {
    if (autoFocus) cells.current.get(`${initial.row}:${initial.column}`)?.focus();
  }, [autoFocus, cells, initial]);
  return <div className={`gomoku-grid${overview ? ' gomoku-grid--overview' : ''}`} style={{ '--board-size': size } as CSSProperties} role="grid" aria-label={`Bàn cờ ${size} nhân ${size}`} aria-rowcount={size} aria-colcount={size} aria-readonly={!interactive || overview}>
    {Array.from({ length: size }, (_, row) => <div className="gomoku-row" role="row" key={row}>
      {Array.from({ length: size }, (_, column) => {
        const key = `${row}:${column}`;
        const move = stones.get(key);
        const isLast = Boolean(move && move.step === last?.step);
        const blocked = overview || !interactive || Boolean(move);
        const label = `Hàng ${row + 1}, cột ${column + 1}, ${move ? `quân ${move.stone}` : 'ô trống'}${isLast ? ', nước cuối' : ''}`;
        const activate = () => { if (!blocked) onMove?.(row, column); };
        const className = `gomoku-cell${move ? ` gomoku-cell--${move.stone.toLowerCase()}` : ''}`;
        return overview ? <div key={column} className={className} role="gridcell" aria-label={label} aria-rowindex={row + 1} aria-colindex={column + 1} data-last-move={isLast || undefined}>{move?.stone}</div> :
          <button type="button" key={column} ref={element => { if (element) cells.current.set(key, element); else cells.current.delete(key); }} className={className} role="gridcell" aria-label={label} aria-rowindex={row + 1} aria-colindex={column + 1} aria-disabled={blocked} data-last-move={isLast || undefined}
            tabIndex={focused.row === row && focused.column === column ? 0 : -1} onFocus={() => setFocused({ row, column })} onClick={activate} onKeyDown={event => onKeyDown(event, row, column, activate)}>{move?.stone}</button>;
      })}
    </div>)}
  </div>;
}
