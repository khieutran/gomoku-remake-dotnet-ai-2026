import type { MatchDto, MoveDto } from '../../shared/api/contracts';

type GameStatus = MatchDto['currentGame']['status'];
type Cell = [row: number, column: number];
type Direction = 'ngang' | 'dọc' | 'chéo xuống' | 'chéo lên';

export type WinningLine = { winner: 'X' | 'O'; direction: Direction; cells: Cell[] };

const directions: { row: number; column: number; name: Direction }[] = [
  { row: 0, column: 1, name: 'ngang' },
  { row: 1, column: 0, name: 'dọc' },
  { row: 1, column: 1, name: 'chéo xuống' },
  { row: 1, column: -1, name: 'chéo lên' },
];

export function findWinningLine(moves: readonly MoveDto[], size: number, status: GameStatus, columns = size): WinningLine | null {
  const winner = status === 'XWon' ? 'X' : status === 'OWon' ? 'O' : null;
  if (!winner) return null;
  const last = moves.filter(move => move.stone === winner).reduce<MoveDto | null>(
    (latest, move) => !latest || move.step > latest.step ? move : latest, null);
  if (!last) return null;

  const stones = new Map(moves.map(move => [`${move.row}:${move.column}`, move.stone]));
  const inBoard = (row: number, column: number) => row >= 0 && row < size && column >= 0 && column < columns;
  const stoneAt = (row: number, column: number) => inBoard(row, column) ? stones.get(`${row}:${column}`) : undefined;

  for (const direction of directions) {
    const count = (step: number) => {
      let total = 0;
      while (stoneAt(last.row + (total + 1) * direction.row * step,
        last.column + (total + 1) * direction.column * step) === winner) total++;
      return total;
    };
    const backward = count(-1);
    const forward = count(1);
    if (backward + 1 + forward !== 5) continue;

    const opponent = winner === 'X' ? 'O' : 'X';
    const before = stoneAt(last.row - (backward + 1) * direction.row,
      last.column - (backward + 1) * direction.column);
    const after = stoneAt(last.row + (forward + 1) * direction.row,
      last.column + (forward + 1) * direction.column);
    if (before === opponent && after === opponent) continue;

    const cells: Cell[] = Array.from({ length: 5 }, (_, index) => [
      last.row + (index - backward) * direction.row,
      last.column + (index - backward) * direction.column,
    ]);
    return { winner, direction: direction.name, cells };
  }
  return null;
}
