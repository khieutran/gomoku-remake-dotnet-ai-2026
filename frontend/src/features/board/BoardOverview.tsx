import type { BoardProps } from './Board';
import { BoardGrid } from './BoardGrid';
import './board.css';

export function BoardOverview(props: BoardProps) {
  return <BoardGrid {...props} interactive={false} overview />;
}
