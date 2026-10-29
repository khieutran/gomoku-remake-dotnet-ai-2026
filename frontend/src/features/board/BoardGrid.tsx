import { useEffect, useState, type CSSProperties } from 'react';
import type { BoardProps } from './Board';
import { useBoardKeyboard } from './useBoardKeyboard';

export function BoardGrid({ size, columns = size, moves, interactive, onMove, winningCells = [], overview = false, autoFocus = false }: BoardProps & { overview?: boolean; autoFocus?: boolean }) {
  const last = moves.reduce<(typeof moves)[number] | undefined>((latest, move) => !latest || move.step > latest.step ? move : latest, undefined);
  const [initial] = useState({ row: last?.row ?? Math.floor(size / 2), column: last?.column ?? Math.floor(columns / 2) });
  const { focused, setFocused, cells, onKeyDown } = useBoardKeyboard(size, columns, initial.row, initial.column);
  const stones = new Map(moves.map(move => [`${move.row}:${move.column}`, move]));
  const winningOrder = new Map(winningCells.map(([row, column], index) => [`${row}:${column}`, index]));
  useEffect(() => {
    if (autoFocus) cells.current.get(`${initial.row}:${initial.column}`)?.focus();
  }, [autoFocus, cells, initial]);
  return <div className={`gomoku-grid${overview ? ' gomoku-grid--overview' : ''}`} style={{ '--board-rows': size, '--board-columns': columns } as CSSProperties} role="grid" aria-label={`Bàn cờ ${size} nhân ${columns}`} aria-rowcount={size} aria-colcount={columns} aria-readonly={!interactive || overview}>
    {Array.from({ length: size }, (_, row) => <div className="gomoku-row" role="row" key={row}>
      {Array.from({ length: columns }, (_, column) => {
        const key = `${row}:${column}`;
        const move = stones.get(key);
        const winOrder = winningOrder.get(key);
        const isLast = Boolean(move && move.step === last?.step);
        const blocked = overview || !interactive || Boolean(move);
        const label = `Hàng ${row + 1}, cột ${column + 1}, ${move ? `quân ${move.stone}` : 'ô trống'}${isLast ? ', nước cuối' : ''}${winOrder !== undefined ? ', đường thắng' : ''}`;
        const activate = () => { if (!blocked) onMove?.(row, column); };
        const className = `gomoku-cell${move ? ` gomoku-cell--${move.stone.toLowerCase()}` : ''}`;
        const winProps = winOrder === undefined ? {} : { 'data-winning-cell': winOrder + 1, style: { '--win-order': winOrder } as CSSProperties };
        return overview ? <div key={column} className={className} role="gridcell" aria-label={label} aria-rowindex={row + 1} aria-colindex={column + 1} data-last-move={isLast || undefined} {...winProps}>{move?.stone}</div> :
          <button type="button" key={column} ref={element => { if (element) cells.current.set(key, element); else cells.current.delete(key); }} className={className} role="gridcell" aria-label={label} aria-rowindex={row + 1} aria-colindex={column + 1} aria-disabled={blocked} data-last-move={isLast || undefined}
            tabIndex={focused.row === row && focused.column === column ? 0 : -1} onFocus={() => setFocused({ row, column })} onClick={activate} onKeyDown={event => onKeyDown(event, row, column, activate)} {...winProps}>{move?.stone}</button>;
      })}
    </div>)}
  </div>;
}
