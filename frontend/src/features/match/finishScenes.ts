import type { MatchDto } from '../../shared/api/contracts';

// Rendered by scripts/render-finish-cutscenes.sh: `wide` fits the desktop board, `tall` the phone board.
export type FinishScene = { wide: string; tall: string; label: string };

const scene = (name: string, label: string): FinishScene =>
  ({ wide: `/videos/finish/${name}-wide.webm`, tall: `/videos/finish/${name}-tall.webm`, label });

export const finishScenes: Readonly<Record<'human' | 'bot', readonly FinishScene[]>> = {
  human: [scene('sword', 'Hiệp sĩ dùng kiếm hạ robot'), scene('arrow', 'Hiệp sĩ bắn cung hạ robot')],
  bot: [scene('fire', 'Robot phun lửa vào hiệp sĩ'), scene('electric', 'Robot biến hiệp sĩ thành gà')],
};

export function pickFinishScene(
  status: MatchDto['currentGame']['status'],
  humanStone: 'X' | 'O',
  random: () => number = Math.random,
): FinishScene | null {
  if (status !== 'XWon' && status !== 'OWon') return null;
  const winner = status[0];
  const scenes = finishScenes[winner === humanStone ? 'human' : 'bot'];
  const index = Math.max(0, Math.min(scenes.length - 1, Math.floor(random() * scenes.length)));
  return scenes[index];
}
