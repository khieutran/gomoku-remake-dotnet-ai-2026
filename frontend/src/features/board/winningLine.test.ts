import { expect, it } from 'vitest';
import type { MoveDto } from '../../shared/api/contracts';
import { findWinningLine } from './winningLine';

const line = (stone: 'X' | 'O', cells: [number, number][], startStep = 1): MoveDto[] =>
  cells.map(([row, column], index) => ({ step: startStep + index, row, column, stone }));

it('finds an exact horizontal five and names the reason', () => {
  const moves = line('X', [[7, 4], [7, 5], [7, 6], [7, 7], [7, 8]]);
  expect(findWinningLine(moves, 20, 'XWon')).toEqual({
    winner: 'X', direction: 'ngang', cells: [[7, 4], [7, 5], [7, 6], [7, 7], [7, 8]],
  });
});

it('finds vertical and diagonal five for O', () => {
  expect(findWinningLine(line('O', [[3, 10], [4, 10], [5, 10], [6, 10], [7, 10]]), 20, 'OWon')?.direction).toBe('dọc');
  expect(findWinningLine(line('O', [[3, 3], [4, 4], [5, 5], [6, 6], [7, 7]]), 20, 'OWon')?.direction).toBe('chéo xuống');
  expect(findWinningLine(line('O', [[7, 3], [6, 4], [5, 5], [4, 6], [3, 7]]), 20, 'OWon')?.direction).toBe('chéo lên');
});

it('does not highlight a five blocked by opponent stones at both ends', () => {
  const moves = [...line('O', [[7, 3], [7, 9]]), ...line('X', [[7, 4], [7, 5], [7, 6], [7, 7], [7, 8]], 3)];
  expect(findWinningLine(moves, 20, 'XWon')).toBeNull();
});

it('treats the board edge as open, but rejects a run of six', () => {
  const edge = [...line('O', [[0, 5]]), ...line('X', [[0, 0], [0, 1], [0, 2], [0, 3], [0, 4]], 2)];
  expect(findWinningLine(edge, 20, 'XWon')?.cells).toEqual([[0, 0], [0, 1], [0, 2], [0, 3], [0, 4]]);
  expect(findWinningLine(line('X', [[7, 4], [7, 5], [7, 6], [7, 7], [7, 8], [7, 9]]), 20, 'XWon')).toBeNull();
});

it('never invents a winner while the server says the game is unfinished or drawn', () => {
  const moves = line('X', [[7, 4], [7, 5], [7, 6], [7, 7], [7, 8]]);
  expect(findWinningLine(moves, 20, 'InProgress')).toBeNull();
  expect(findWinningLine(moves, 20, 'Draw')).toBeNull();
});
