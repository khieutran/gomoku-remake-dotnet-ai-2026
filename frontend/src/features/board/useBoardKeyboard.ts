import { useRef, useState, type KeyboardEvent } from 'react';

export function useBoardKeyboard(size: number, initialRow: number, initialColumn: number) {
  const [focused, setFocused] = useState({ row: initialRow, column: initialColumn });
  const cells = useRef(new Map<string, HTMLButtonElement>());
  function onKeyDown(event: KeyboardEvent<HTMLButtonElement>, row: number, column: number, activate: () => void) {
    const directions: Record<string, [number, number]> = {
      ArrowUp: [-1, 0], ArrowDown: [1, 0], ArrowLeft: [0, -1], ArrowRight: [0, 1],
    };
    const direction = directions[event.key];
    if (direction) {
      event.preventDefault();
      const next = { row: Math.max(0, Math.min(size - 1, row + direction[0])), column: Math.max(0, Math.min(size - 1, column + direction[1])) };
      setFocused(next);
      cells.current.get(`${next.row}:${next.column}`)?.focus();
    } else if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      if (!event.repeat) activate();
    }
  }
  return { focused, setFocused, cells, onKeyDown };
}
